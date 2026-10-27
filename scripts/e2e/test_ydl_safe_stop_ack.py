"""MP-YDL SafeStop ACK contract tests.

A SafeStop may only be accepted after every local safety frame send succeeds.
A failed send keeps the latch active, returns a stable error code, and must not
start the estimated-completion path.
"""
import ctypes
import json
import os
import time
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
IOUI_BIN = ROOT.parent / "IOUI" / "Binaries" / "Win64" / "Debug"
RUNTIME_ROOT = IOUI_BIN / "ExternalLibraries"
DLL_PATH = RUNTIME_ROOT / "IOUI-Win64-MP-YDL.dll"
DEVICE_INDEX = 0

pytestmark = pytest.mark.skipif(
    os.name != "nt" or not DLL_PATH.exists(),
    reason="Windows MP-YDL build is required",
)


def _load_plugin(responses, events):
    os.add_dll_directory(str(RUNTIME_ROOT))
    dll = ctypes.WinDLL(str(DLL_PATH))
    uint8 = ctypes.c_ubyte
    byte_ptr = ctypes.POINTER(ctypes.c_ubyte)
    short_ptr = ctypes.POINTER(ctypes.c_short)
    dispatcher_type = ctypes.WINFUNCTYPE(
        None, uint8, ctypes.c_char_p, byte_ptr, ctypes.c_uint, ctypes.c_void_p
    )

    dll.Initialize.restype = ctypes.c_void_p
    dll.SetPluginRuntimeRoot.argtypes = [ctypes.c_char_p]
    dll.SetPluginRuntimeRoot.restype = ctypes.c_int
    dll.OpenDevice.argtypes = [uint8]
    dll.OpenDevice.restype = ctypes.c_int
    dll.CloseDevice.argtypes = [uint8]
    dll.CloseDevice.restype = ctypes.c_int
    dll.SetDeviceDO.argtypes = [uint8, short_ptr]
    dll.SetDeviceDO.restype = ctypes.c_int
    dll.WritePluginChannel.argtypes = [uint8, ctypes.c_char_p, byte_ptr, ctypes.c_uint]
    dll.WritePluginChannel.restype = ctypes.c_int
    dll.SetPluginChannelDispatcher.argtypes = [uint8, dispatcher_type, ctypes.c_void_p]
    dll.SetPluginChannelDispatcher.restype = ctypes.c_int

    @dispatcher_type
    def dispatcher(_index, channel, data, size, _user_data):
        if not data or size == 0:
            return
        raw = ctypes.string_at(data, size).decode("utf-8", "replace")
        if channel == b"_rpc.res":
            responses.append(json.loads(raw))
        elif channel == b"_event":
            events.append(json.loads(raw))

    assert dll.Initialize()
    assert dll.SetPluginChannelDispatcher(DEVICE_INDEX, dispatcher, None) == 1
    return dll, dispatcher


def _send_rpc(dll, request_id, topic, payload):
    envelope = {"requestId": request_id, "topic": topic, "payload": payload}
    raw = json.dumps(envelope, separators=(",", ":")).encode("utf-8")
    buf = (ctypes.c_ubyte * len(raw)).from_buffer_copy(raw)
    assert dll.WritePluginChannel(DEVICE_INDEX, b"_rpc.req", buf, len(raw)) == 1
    return raw


def _request_stop(dll, request_id, generation, settle_ms=250, **extra):
    payload = {
        "generation": generation,
        "estimatedSettleMs": settle_ms,
        "reason": "ack_test",
    }
    payload.update(extra)
    return _send_rpc(dll, request_id, "plugin.safety.stop", payload)


def _safe_states(events, generation):
    states = []
    for event in events:
        payload = event.get("payload")
        if event.get("name") == "plugin.safety.stateChanged" and isinstance(payload, dict):
            if payload.get("generation") == generation:
                states.append(payload)
    return states


def test_safe_stop_ack_requires_all_frames_sent(tmp_path):
    responses = []
    events = []
    dll, dispatcher = _load_plugin(responses, events)
    try:
        assert dll.SetPluginRuntimeRoot(str(RUNTIME_ROOT).encode("utf-8")) == 1
        assert dll.OpenDevice(DEVICE_INDEX) == 1
        _request_stop(dll, "safe-ack-success", 1)
        deadline = time.monotonic() + 1.5
        while time.monotonic() < deadline:
            if any(r.get("requestId") == "safe-ack-success" for r in responses) and _safe_states(events, 1):
                break
            time.sleep(0.02)

        success = next(r for r in responses if r.get("requestId") == "safe-ack-success")
        assert success["ok"] is True
        assert success["payload"]["accepted"] is True
        assert success["payload"]["completionMode"] == "estimated"
        assert success["payload"]["confirmed"] is False
        states = _safe_states(events, 1)
        assert len(states) == 1
        assert states[0]["state"] == "safeStopped"
        assert states[0]["estimated"] is True
        assert states[0]["confirmed"] is False
    finally:
        dll.CloseDevice(DEVICE_INDEX)
        dispatcher = None
        dll = None

    # IPv6 remote through an IPv4 socket makes every safety-frame send fail while
    # still allowing the device to initialize. This exercises partial batch failure
    # rather than the already-closed-device path.
    config_dir = tmp_path / "Config" / "MP-YDL"
    config_dir.mkdir(parents=True)
    (config_dir / "config.xml").write_text(
        '<MPRoot><YDLDevice safeStopSettleMs="250">'
        '<UDP ip="::1" port="30120" localPort="30121" />'
        "</YDLDevice></MPRoot>",
        encoding="utf-8",
    )

    responses.clear()
    events.clear()
    dll, dispatcher = _load_plugin(responses, events)
    try:
        assert dll.SetPluginRuntimeRoot(str(tmp_path).encode("utf-8")) == 1
        assert dll.OpenDevice(DEVICE_INDEX) == 1
        _request_stop(dll, "safe-ack-failure", 2)
        deadline = time.monotonic() + 1.0
        while time.monotonic() < deadline:
            if any(r.get("requestId") == "safe-ack-failure" for r in responses):
                break
            time.sleep(0.02)

        failure = next(r for r in responses if r.get("requestId") == "safe-ack-failure")
        assert failure["ok"] is False
        assert failure["errorCode"] == "safe_frames_failed"
        assert "latch remains active" in failure["errorMessage"]
        time.sleep(0.4)
        assert _safe_states(events, 2) == [], "failed SafeStop must not emit safeStopped"
    finally:
        dll.CloseDevice(DEVICE_INDEX)
        dispatcher = None
        dll = None