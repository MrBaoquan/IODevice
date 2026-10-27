"""Native SafeStop policy test for the C ABI."""
import ctypes
import json
import os
import threading
import time
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
BIN_DIR = ROOT / "Binaries" / "Win64" / "Debug"
DLL_PATH = BIN_DIR / "IODevice_C_Wrapper.dll"
DEVICE = "SafeStopTest"

pytestmark = pytest.mark.skipif(
    os.name != "nt" or not DLL_PATH.exists(),
    reason="Windows native IODevice build is required",
)

def _window():
    user32 = ctypes.WinDLL("user32", use_last_error=True)
    user32.CreateWindowExW.argtypes = [ctypes.c_uint, ctypes.c_wchar_p, ctypes.c_wchar_p, ctypes.c_uint, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p]
    user32.CreateWindowExW.restype = ctypes.c_void_p
    hwnd = user32.CreateWindowExW(0, "STATIC", "SafeStopNativeTest", 0, 0, 0, 100, 100, 0, 0, 0, 0)
    assert hwnd
    return user32, hwnd

@pytest.fixture()
def native(tmp_path):
    config = tmp_path / "IODevice.SafeStop.xml"
    config.write_text('''<?xml version="1.0"?>
<IORoot>
  <Device Name="SafeStopTest" Type="External" DllName="Simulator" Index="1">
    <Properties>
      <Key Name="OAxis_00" Min="0" Max="1" SlewRate="100" SafeValue="0" SafeStopRate="0.5" SafeStopPolicy="Controlled" />
      <Key Name="OAxis_01" Min="0" Max="1" SlewRate="0" SafeValue="0" SafeStopPolicy="Immediate" />
    </Properties>
  </Device>
</IORoot>
''', encoding="utf-8")
    user32, hwnd = _window()
    os.add_dll_directory(str(BIN_DIR))
    dll = ctypes.WinDLL(str(DLL_PATH))
    bstr = ctypes.c_wchar_p
    signatures = [
        ("SetIORuntimeRoot", [bstr], ctypes.c_int), ("SetPluginConfigRoot", [bstr], ctypes.c_int),
        ("SetIOConfigPath", [bstr], ctypes.c_int), ("Load", [], ctypes.c_int), ("Unload", [], ctypes.c_int),
        ("IsValid", [bstr], ctypes.c_bool), ("GetDOSingle", [bstr, bstr], ctypes.c_float),
        ("SetDOSingleImmediate", [bstr, bstr, ctypes.c_float], ctypes.c_int),
        ("SetDOSingleEx", [bstr, bstr, ctypes.c_float, ctypes.c_int], ctypes.c_int),
        ("DOImmediate", [bstr], ctypes.c_int), ("EnterSafeState", [], ctypes.c_int),
        ("MotionLoadSlotFromJson", [bstr, bstr, ctypes.c_int, ctypes.c_int], ctypes.c_int),
        ("MotionPlaySlot", [bstr], ctypes.c_int), ("MotionUnloadAll", [], None),
    ]
    for name, args, result in signatures:
        fn = getattr(dll, name); fn.argtypes = args; fn.restype = result
    assert dll.SetIORuntimeRoot(str(BIN_DIR)) == 1
    assert dll.SetPluginConfigRoot(str(BIN_DIR / "Config")) == 1
    assert dll.SetIOConfigPath(str(config)) == 1
    assert dll.Load() == 0
    assert dll.IsValid(DEVICE)
    try:
        yield dll
    finally:
        dll.MotionUnloadAll(); dll.Unload(); user32.DestroyWindow(hwnd)

def _active_motion_json():
    return json.dumps({
        "version":"3.0", "name":"safe_stop_policy", "duration_ms":4000, "fps":60,
        "tracks":[
            {"id":"analog","device":DEVICE,"oaction":"","output_type":"oaxis","oaxis_channel":"OAxis_00","label":"analog","color":"#fff","value_type":"float","muted":False,"enabled":True,"locked":False,"engineering_range":1,
             "clips":[{"id":"a","start_ms":0,"end_ms":4000,"keyframes":[{"time_ms":0,"value":1.0,"interpolation":"step"}]}]},
            {"id":"discrete","device":DEVICE,"oaction":"","output_type":"oaxis","oaxis_channel":"OAxis_01","label":"discrete","color":"#fff","value_type":"bool","muted":False,"enabled":True,"locked":False,"engineering_range":1,
             "clips":[{"id":"d","start_ms":0,"end_ms":4000,"keyframes":[{"time_ms":0,"value":1.0,"interpolation":"step"}]}]},
        ], "groups":[], "events":[], "markers":[],
    }, ensure_ascii=False)

def test_enter_safe_state_collects_active_channels_through_policy(native):
    assert native.MotionLoadSlotFromJson("safe", _active_motion_json(), 0, 0) == 0
    assert native.MotionPlaySlot("safe") == 0
    assert native.SetDOSingleImmediate(DEVICE, "OAxis_00", 1.0) == 1
    assert native.SetDOSingleImmediate(DEVICE, "OAxis_01", 1.0) == 1

    assert native.EnterSafeState() == 0
    native.DOImmediate(DEVICE)
    assert native.GetDOSingle(DEVICE, "OAxis_00") > 0.9, "EnterSafeState hardcoded or jumped to another value"
    assert native.GetDOSingle(DEVICE, "OAxis_01") == pytest.approx(0.0), "discrete output was not immediately safe"

def test_safe_stop_mode_uses_channel_policy(native):
    assert native.SetDOSingleImmediate(DEVICE, "OAxis_00", 1.0) == 1
    assert native.SetDOSingleImmediate(DEVICE, "OAxis_01", 1.0) == 1
    assert native.GetDOSingle(DEVICE, "OAxis_00") == pytest.approx(1.0)
    assert native.GetDOSingle(DEVICE, "OAxis_01") == pytest.approx(1.0)

    # EOutputWriteMode::SafeStop = 2
    assert native.SetDOSingleEx(DEVICE, "OAxis_00", 0.0, 2) == 1
    assert native.SetDOSingleEx(DEVICE, "OAxis_01", 0.0, 2) == 1
    native.DOImmediate(DEVICE)
    assert native.GetDOSingle(DEVICE, "OAxis_00") > 0.9, "controlled channel jumped immediately"
    assert native.GetDOSingle(DEVICE, "OAxis_01") == pytest.approx(0.0), "discrete channel was not immediate"

    time.sleep(1.0)
    native.DOImmediate(DEVICE)
    mid = native.GetDOSingle(DEVICE, "OAxis_00")
    assert 0.2 < mid < 0.8, mid

    time.sleep(1.6)
    native.DOImmediate(DEVICE)
    assert native.GetDOSingle(DEVICE, "OAxis_00") == pytest.approx(0.0, abs=0.02)



@pytest.mark.parametrize("initial", [0.2, 0.5, 1.0])
def test_controlled_safe_stop_tracks_travel_rate(native, initial):
    assert native.SetDOSingleImmediate(DEVICE, "OAxis_00", initial) == 1
    native.DOImmediate(DEVICE)
    assert native.GetDOSingle(DEVICE, "OAxis_00") == pytest.approx(initial)

    started = time.monotonic()
    assert native.SetDOSingleEx(DEVICE, "OAxis_00", 0.0, 2) == 1
    native.DOImmediate(DEVICE)
    assert native.GetDOSingle(DEVICE, "OAxis_00") > initial - 0.02

    expected_seconds = initial / 0.5
    deadline = started + expected_seconds + 1.0
    value = initial
    while time.monotonic() < deadline:
        native.DOImmediate(DEVICE)
        value = native.GetDOSingle(DEVICE, "OAxis_00")
        if value <= 0.01:
            break
        time.sleep(0.02)

    assert value <= 0.01, f"did not reach neutral from {initial}: {value}"
    assert time.monotonic() - started >= expected_seconds - 0.15


def test_enter_safe_state_is_reentrant_without_deadlock(native):
    assert native.MotionLoadSlotFromJson("safe", _active_motion_json(), 0, 0) == 0
    assert native.MotionPlaySlot("safe") == 0
    barrier = threading.Barrier(4)
    results = []
    errors = []

    def worker():
        try:
            barrier.wait(timeout=2)
            for _ in range(10):
                results.append(native.EnterSafeState())
        except BaseException as exc:  # pragma: no cover - diagnostic path
            errors.append(exc)

    threads = [threading.Thread(target=worker) for _ in range(4)]
    for thread in threads:
        thread.start()
    for thread in threads:
        thread.join(timeout=5)

    assert all(not thread.is_alive() for thread in threads), "EnterSafeState deadlocked"
    assert errors == []
    assert len(results) == 40
    assert all(result == 0 for result in results)
