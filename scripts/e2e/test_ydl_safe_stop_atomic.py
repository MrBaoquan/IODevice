"""MP-YDL SafeStop output-barrier integration test.

The test loads the real plugin DLL, captures its UDP output, races ordinary DO
writes against plugin.safety.stop, and proves no stale cylinder/effect command
can appear after the first safe cylinder frame.
"""
import ctypes
import json
import os
import socket
import threading
import time
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
IOUI_BIN = ROOT.parent / "IOUI" / "Binaries" / "Win64" / "Debug"
DLL_PATH = IOUI_BIN / "ExternalLibraries" / "IOUI-Win64-MP-YDL.dll"
PLATFORM_PORT = 30120
GAME_PORT = 30121
DEVICE_INDEX = 0
DO_COUNT = 66

pytestmark = pytest.mark.skipif(
    os.name != "nt" or not DLL_PATH.exists(),
    reason="Windows MP-YDL build is required",
)


def _port_is_free(port):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        sock.bind(("127.0.0.1", port))
        return True
    except OSError:
        return False
    finally:
        sock.close()


def _start_capture(records, stop_event):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.bind(("127.0.0.1", PLATFORM_PORT))
    sock.settimeout(0.05)
    t0 = time.monotonic()
    try:
        while not stop_event.is_set():
            try:
                data, _ = sock.recvfrom(8192)
            except socket.timeout:
                continue
            records.append((int((time.monotonic() - t0) * 1000), data.decode("utf-8", "replace")))
    finally:
        sock.close()


def _load_plugin(rpc_responses):
    os.add_dll_directory(str(IOUI_BIN / "ExternalLibraries"))
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
        if channel != b"_rpc.res" or not data or size == 0:
            return
        raw = ctypes.string_at(data, size).decode("utf-8", "replace")
        rpc_responses.append(json.loads(raw))

    assert dll.Initialize()
    assert dll.SetPluginRuntimeRoot(str(IOUI_BIN / "ExternalLibraries").encode("utf-8")) == 1
    assert dll.SetPluginChannelDispatcher(DEVICE_INDEX, dispatcher, None) == 1
    assert dll.OpenDevice(DEVICE_INDEX) == 1
    return dll, dispatcher


def _send_rpc(dll, request_id, topic, payload):
    envelope = {
        "requestId": request_id,
        "topic": topic,
        "payload": payload,
    }
    raw = json.dumps(envelope, separators=(",", ":")).encode("utf-8")
    buf = (ctypes.c_ubyte * len(raw)).from_buffer_copy(raw)
    return dll.WritePluginChannel(DEVICE_INDEX, b"_rpc.req", buf, len(raw))


def _parse(records):
    parsed = []
    for ms, raw in records:
        try:
            parsed.append((ms, json.loads(raw)))
        except json.JSONDecodeError:
            continue
    return parsed


def test_safe_stop_blocks_stale_output_after_safe_frame():
    assert _port_is_free(PLATFORM_PORT), f"UDP {PLATFORM_PORT} is already in use"
    assert _port_is_free(GAME_PORT), f"UDP {GAME_PORT} is already in use"

    records = []
    rpc_responses = []
    stop_capture = threading.Event()
    capture = threading.Thread(target=_start_capture, args=(records, stop_capture), daemon=True)
    capture.start()
    time.sleep(0.1)

    dll, dispatcher = _load_plugin(rpc_responses)
    writer_stop = threading.Event()
    statuses = []

    def write_outputs():
        values = (ctypes.c_short * DO_COUNT)()
        phase = 0
        while not writer_stop.is_set():
            values[6] = 1000 if phase % 2 == 0 else 850
            values[16] = 1
            values[18] = 1
            values[20] = 1
            values[22] = 1
            statuses.append(dll.SetDeviceDO(DEVICE_INDEX, values))
            phase += 1
            time.sleep(0.008)

    writer = threading.Thread(target=write_outputs, daemon=True)
    writer.start()
    try:
        time.sleep(0.35)
        assert _send_rpc(
            dll,
            "safe-stop-atomic-1",
            "plugin.safety.stop",
            {"generation": 1, "estimatedSettleMs": 300, "safeStopRate": 10.0, "safeValue": 0, "reason": "atomic_test"},
        ) == 1
        time.sleep(0.65)
        assert any(r.get("requestId") == "safe-stop-atomic-1" and r.get("ok") for r in rpc_responses)
    finally:
        writer_stop.set()
        writer.join(timeout=2)
        stop_capture.set()
        capture.join(timeout=2)
        dll.CloseDevice(DEVICE_INDEX)
        dispatcher = None
        dll = None

    parsed = _parse(records)
    parsed.sort(key=lambda item: item[0])
    cylinders = []
    for ms, obj in parsed:
        if obj.get("Cmd") != "GamePlatform_Cylinder":
            continue
        para = obj.get("Para") or {}
        heights = para.get("HeightRatioList") if isinstance(para, dict) else None
        if heights:
            cylinders.append((ms, heights))

    assert len(cylinders) >= 2, f"not enough cylinder frames: {cylinders}"
    safe_index = next(
        (i for i, (_ms, heights) in enumerate(cylinders) if all(abs(v) < 1e-6 for v in heights)),
        None,
    )
    assert safe_index is not None, "SafeStop safe cylinder frame was not emitted"
    safe_ms = cylinders[safe_index][0]

    stale_after_safe = []
    for ms, obj in parsed:
        if ms < safe_ms:
            continue
        cmd = obj.get("Cmd")
        para = obj.get("Para") or {}
        if cmd == "GamePlatform_Cylinder":
            heights = para.get("HeightRatioList", []) if isinstance(para, dict) else []
            if any(abs(float(v)) > 1e-6 for v in heights):
                stale_after_safe.append((ms, cmd, para))
        elif cmd == "GamePlatform_PlatformEffect":
            effect = para.get("EffectData", {}) if isinstance(para, dict) else {}
            if effect.get("IsOpen") is True:
                stale_after_safe.append((ms, cmd, para))
        elif cmd == "GamePlatform_GameState":
            if para.get("GameState") == "Ready":
                stale_after_safe.append((ms, cmd, para))

    assert stale_after_safe == [], f"stale output escaped SafeStop barrier: {stale_after_safe[:5]}"
    assert statuses and all(status == 1 for status in statuses), "ordinary SetDeviceDO calls crashed/failed"