"""MP-YDL SafeStop clear generation tests."""
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


def _wait_for(predicate, timeout=3.0):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if predicate():
            return
        time.sleep(0.02)
    raise AssertionError("timed out waiting for plugin response/event")


def _set_height(dll, value):
    outputs = (ctypes.c_short * DO_COUNT)()
    outputs[6] = value
    assert dll.SetDeviceDO(DEVICE_INDEX, outputs) == 1
    time.sleep(0.15)


def _clear_result(dll, responses, request_id, generation):
    _send_rpc(dll, request_id, "plugin.safety.clear", {"generation": generation})
    _wait_for(lambda: any(r.get("requestId") == request_id for r in responses))
    return next(r for r in responses if r.get("requestId") == request_id)


def test_clear_requires_current_completed_generation():
    responses = []
    events = []
    dll, dispatcher = _load_plugin(responses, events)
    try:
        assert dll.SetPluginRuntimeRoot(str(RUNTIME_ROOT).encode("utf-8")) == 1
        assert dll.OpenDevice(DEVICE_INDEX) == 1
        _set_height(dll, 1000)

        _request_stop(dll, "gen-stop-1", 1, settle_ms=-1, safeStopRate=2.0, safeValue=0.0)

        early = _clear_result(dll, responses, "gen-clear-early", 1)
        assert early["ok"] is False
        assert early["errorCode"] == "safe_stop_in_progress"

        _wait_for(lambda: _safe_states(events, 1))
        assert _safe_states(events, 1)[0]["state"] == "safeStopped"

        wrong = _clear_result(dll, responses, "gen-clear-wrong", 2)
        assert wrong["ok"] is False
        assert wrong["errorCode"] == "generation_mismatch"

        current = _clear_result(dll, responses, "gen-clear-current", 1)
        assert current["ok"] is True
        assert current["payload"]["alreadyCleared"] is False

        repeated = _clear_result(dll, responses, "gen-clear-repeat", 1)
        assert repeated["ok"] is True
        assert repeated["payload"]["alreadyCleared"] is True

        _set_height(dll, 500)
        _request_stop(dll, "gen-stop-2", 2, settle_ms=-1, safeStopRate=2.0, safeValue=0.0)
        _wait_for(lambda: _safe_states(events, 2))
        assert _safe_states(events, 2)[0]["state"] == "safeStopped"

        stale = _clear_result(dll, responses, "gen-clear-stale", 1)
        assert stale["ok"] is False
        assert stale["errorCode"] == "generation_mismatch"

        final = _clear_result(dll, responses, "gen-clear-final", 2)
        assert final["ok"] is True
        assert final["payload"]["alreadyCleared"] is False
    finally:
        dll.CloseDevice(DEVICE_INDEX)
        dispatcher = None
        dll = None
