"""MP-YDL SafeStop dynamic completion-time tests."""
import ctypes
import time

import pytest

from test_ydl_safe_stop_ack import (
    DEVICE_INDEX,
    RUNTIME_ROOT,
    _load_plugin,
    _request_stop,
    _safe_states,
    _send_rpc,
)

DO_COUNT = 66


def _set_height(dll, value):
    outputs = (ctypes.c_short * DO_COUNT)()
    outputs[6] = value
    assert dll.SetDeviceDO(DEVICE_INDEX, outputs) == 1
    time.sleep(0.15)


def _wait_for(predicate, timeout=2.0):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if predicate():
            return
        time.sleep(0.02)
    raise AssertionError("timed out waiting for native plugin state")


def _clear(dll, responses, generation):
    _send_rpc(dll, f"clear-{generation}", "plugin.safety.clear", {"generation": generation})
    _wait_for(lambda: any(r.get("requestId") == f"clear-{generation}" for r in responses))
    response = next(r for r in responses if r.get("requestId") == f"clear-{generation}")
    assert response["ok"] is True


@pytest.mark.parametrize(
    "height,expected_ramp_ms",
    [(200, 100), (500, 250), (1000, 500)],
)
def test_completion_time_tracks_height_and_safe_rate(height, expected_ramp_ms, tmp_path):
    responses = []
    events = []
    dll, dispatcher = _load_plugin(responses, events)
    try:
        assert dll.SetPluginRuntimeRoot(str(RUNTIME_ROOT).encode("utf-8")) == 1
        assert dll.OpenDevice(DEVICE_INDEX) == 1
        _set_height(dll, height)

        request_id = "rate-1"
        started = time.monotonic()
        _request_stop(
            dll,
            request_id,
            1,
            settle_ms=-1,
            safeStopRate=2.0,
            safeValue=0.0,
        )
        _wait_for(
            lambda: any(r.get("requestId") == request_id for r in responses)
            and _safe_states(events, 1),
            timeout=3.0,
        )
        elapsed_ms = (time.monotonic() - started) * 1000.0
        response = next(r for r in responses if r.get("requestId") == request_id)
        payload = response["payload"]
        assert payload["accepted"] is True
        assert payload["rampMs"] == expected_ramp_ms
        assert payload["estimatedMs"] == expected_ramp_ms + 250
        assert elapsed_ms >= expected_ramp_ms + 250 - 1, (
            f"SafeStop completed too early: elapsed={elapsed_ms:.1f}ms estimated={expected_ramp_ms + 250}ms"
        )
        state = _safe_states(events, 1)[0]
        assert state["state"] == "safeStopped"
        assert state["estimatedMs"] == expected_ramp_ms + 250
        assert state["rampMs"] == expected_ramp_ms
        _clear(dll, responses, 1)
    finally:
        dll.CloseDevice(DEVICE_INDEX)
        dispatcher = None
        dll = None
