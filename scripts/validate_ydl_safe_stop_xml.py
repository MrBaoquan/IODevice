#!/usr/bin/env python3
"""Static gate for MP-YDL SafeStop orchestration living in UNIPlayer project XML."""
from __future__ import annotations

import hashlib
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

WORKSPACE = Path(__file__).resolve().parents[2]
UNITY = WORKSPACE / "UNIPlayer"
DEMO = UNITY / "Projects/ydl/StreamingAssets/Configs/Profiles/track-cinema.xml"
DEMO_RUNTIME = UNITY / "Assets/StreamingAssets/Configs/Profiles/track-cinema.xml"
MAIN = UNITY / "Projects/ydl/StreamingAssets/Configs/Main.xml"
MAIN_RUNTIME = UNITY / "Assets/StreamingAssets/Configs/Main.xml"


def fail(message: str) -> None:
    print(f"FAIL: {message}")
    raise SystemExit(1)


def read(path: Path) -> str:
    if not path.exists():
        fail(f"missing file: {path}")
    return path.read_text(encoding="utf-8-sig")


def read_composition(path: Path) -> str:
    """Read an UNIPlayer XML plus its Include graph for composed-rule checks."""
    streaming_root = None
    for parent in (path.resolve(), *path.resolve().parents):
        if parent.name == "StreamingAssets":
            streaming_root = parent
            break
    if streaming_root is None:
        fail(f"cannot infer StreamingAssets root: {path}")

    chunks: list[str] = []
    visited: set[Path] = set()

    def visit(current: Path) -> None:
        current = current.resolve()
        if current in visited:
            fail(f"include cycle detected at {current}")
        if not current.exists():
            fail(f"missing include: {current}")
        visited.add(current)
        text = read(current)
        chunks.append(text)
        root = ET.fromstring(text)
        for node in root.findall(".//Include"):
            raw = (node.get("path") or "").strip()
            if not raw:
                fail(f"empty Include path in {current}")
            visit((streaming_root / raw.replace("\\", "/")).resolve())

    visit(path)
    return "\n".join(chunks)

def action(xml: str, name: str) -> str:
    match = re.search(
        rf'<Action\s+name="{re.escape(name)}"[^>]*>(.*?)</Action>',
        xml,
        flags=re.S,
    )
    if not match:
        fail(f"missing Action {name}")
    return match.group(1)


def ordered(text: str, first: str, second: str, label: str) -> None:
    a = text.find(first)
    b = text.find(second)
    if a < 0 or b < 0 or a >= b:
        fail(f"{label}: expected {first!r} before {second!r}")


for path in (DEMO, MAIN):
    ET.parse(path)
    print(f"xml-parse OK {path.relative_to(WORKSPACE)}")

for src, runtime in ((DEMO, DEMO_RUNTIME), (MAIN, MAIN_RUNTIME)):
    src_hash = hashlib.sha256(src.read_bytes()).hexdigest()
    runtime_hash = hashlib.sha256(runtime.read_bytes()).hexdigest()
    if src_hash != runtime_hash:
        fail(f"runtime copy differs: {runtime}")
    print(f"runtime-copy OK {runtime.relative_to(WORKSPACE)}")
YDL_MODULE = UNITY / "Projects/ydl/StreamingAssets/Configs/Modules/business/ydl.xml"
YDL_MODULE_RUNTIME = UNITY / "Assets/StreamingAssets/Configs/Modules/business/ydl.xml"
if YDL_MODULE.exists() or YDL_MODULE_RUNTIME.exists():
    if not YDL_MODULE.exists() or not YDL_MODULE_RUNTIME.exists():
        fail("YDL module exists in only one runtime copy")
    if hashlib.sha256(YDL_MODULE.read_bytes()).hexdigest() != hashlib.sha256(YDL_MODULE_RUNTIME.read_bytes()).hexdigest():
        fail("YDL module runtime copies differ")
    print(f"runtime-copy OK {YDL_MODULE_RUNTIME.relative_to(WORKSPACE)}")

xml = read_composition(MAIN)
main = read_composition(MAIN)

required_tokens = [
    'trigger="YDL-0:_event@channel"',
    'name="HandleYdlPluginEvent"',
    'name="RequestYdlSafeStop"',
    'name="RaiseYdlSafeFault"',
    "plugin.safety.stop",
    "plugin.safety.stateChanged",
    "plugin.safety.clear",
    "_ydl_safe_generation",
    "_ydl_safe_fault",
    "_ydl_safe_stop_rate",
    "_ydl_safe_value",
    "_ydl_safe_estimated_ms",
    "safeStopRate",
    "safeValue",
    "confirmed",
]
for token in required_tokens:
    if token not in xml:
        fail(f"missing XML safety token: {token}")

safe_stop = action(xml, "RequestYdlSafeStop")
ordered(safe_stop, "StopVideo()", "plugin.safety.stop", "safe-stop motion end")
ordered(safe_stop, "plugin.safety.stop", "ydl_safe_completion_timer.Restart", "safe-stop sequence")
for action_name in (
    "HandlePlaybackStop",
    "HandlePlaybackLoad",
    "HandleEmergencyStop",
    "HandleEnterIdle",
    "HandleVideoFinished",
    "HandleIdleTimeout",
):
    body = action(xml, action_name)
    if "PauseVideo()" in body:
        fail(f"{action_name}: SafeStop boundary must not use PauseVideo")
    if "RequestYdlSafeStop()" not in body:
        fail(f"{action_name}: missing RequestYdlSafeStop")
ordered(action(xml, "HandlePlaybackStop"), "RequestYdlSafeStop()", "ShowIdlePage()", "playback.stop")
ordered(action(xml, "HandlePlaybackLoad"), "RequestYdlSafeStop()", "LoadFilmVideo", "playback.load")
ordered(action(xml, "HandleEnterIdle"), "RequestYdlSafeStop()", "Back2Idle()", "enterIdle")
ordered(action(xml, "HandleVideoFinished"), "RequestYdlSafeStop()", "Back2Idle()", "natural_end")
fault_guard = re.search(
    r"if\(_ydl_link_loss_latched \|\| _ydl_safe_fault,.*?exitcall\(\)\s*\).*?if\(_ydl_motion_active",
    safe_stop,
    re.S,
)
if fault_guard is None:
    fail("SafeStop must not auto-retry or auto-clear after a latched fault")
if "StopVideo()" not in action(xml, "HandleYdlPluginEvent"):
    fail("link-loss handling must end/unload the motion slot")
idle_event = action(xml, "HandleEnterIdleEvent")
ordered(idle_event, "StopVideo()", "RequestYdlSafeStop()", "enterIdle event")

start_requested = action(xml, "StartRequestedMedia")
for token in (
    "start gate activeVideo=",
    "duplicate start ignored for active video=",
    "duplicate start while paused resumes active video=",
    "set(pb.state, 'stopped')",
):
    if token not in start_requested:
        fail(f"StartRequestedMedia idempotent/switch token missing: {token}")
if start_requested.rfind("set(pb.state, 'stopped')") > start_requested.rfind("Playback_StartRequestedMedia()"):
    fail("film switch must clear stale playing state before restart")

video_started = action(xml, "HandleVideoStarted")
if "coalesce(pb.filmId, trigger, '')" not in video_started:
    fail("video session identity must use filmId instead of the playable trigger")

enter_idle_control = action(xml, "HandleEnterIdle")
for token in (
    "set(pb.state, 'idle')",
    "set(pb.timeMs, 0)",
    "set(pb.requestedTrigger, '')",
    "media.playback.state.changed",
):
    if token not in enter_idle_control:
        fail(f"HandleEnterIdle stale-state cleanup missing: {token}")

completion = action(xml, "UpdateYdlSafeCompletion")
for token in ("plugin.safety.clear", "_ydl_safe_settled == false"):
    if token not in completion:
        fail(f"missing completion token: {token}")
if "_ydl_safe_timer_ticks < 33" in completion:
    fail("SafeStop completion must not wait for a fixed 33-tick interval")

ydl_rate_match = re.search(r"set\(_ydl_safe_stop_rate,\s*([0-9.]+)\)", xml)
if ydl_rate_match is None:
    fail("YDL XML must declare _ydl_safe_stop_rate")
ydl_rate = float(ydl_rate_match.group(1))
io_device_config = UNITY / "Assets/Plugins/IOToolkit/Config/IODevice.xml"
io_root = ET.parse(io_device_config).getroot()
ydl_device = next((node for node in io_root.iter("Device") if node.get("Name") == "YDL-0"), None)
if ydl_device is None:
    fail("YDL-0 device is missing from runtime IODevice.xml")
properties = ydl_device.find("Properties")
if properties is None:
    fail("YDL-0 Properties node is missing from runtime IODevice.xml")
cylinder_key = next((node for node in properties.iter("Key") if node.get("Name") in {"OAxis_06", "OAxis_6"}), None)
if cylinder_key is None:
    fail("OAxis_6 safety configuration is missing from runtime IODevice.xml")
configured_rate = float(cylinder_key.get("SafeStopRate") or "0")
if abs(configured_rate - ydl_rate) > 1e-6:
    fail(
        "SafeStopRate drift between ydl.xml and IODevice.xml: "
        f"xml={ydl_rate}, io={configured_rate}"
    )
if (cylinder_key.get("SafeStopPolicy") or "").lower() != "controlled":
    fail("OAxis_6 must use Controlled SafeStop policy")

function_lib = (UNITY / "Assets/Develop/Scripts/Core/Functions/FunctionLibrary.cs").read_text(encoding="utf-8-sig")
video_player = (UNITY / "Assets/Develop/Scripts/UIs/VideoPlayerUI.cs").read_text(encoding="utf-8-sig")
if '"StopVideo"' not in function_lib or 'Managements.UI.Get<VideoPlayerUI>().Stop();' not in function_lib:
    fail("generic XML StopVideo primitive is missing")
if "motionCoordinator.Stop();" not in video_player:
    fail("VideoPlayerUI.Stop must end/unload the motion slot")

for name, channel in (("GameReady", "OAxis_64"), ("GameOver", "OAxis_65")):
    if not re.search(rf'<OAction\s+device="YDL-0"\s+name="{name}"[^>]*ensureKeys="{channel}"', xml):
        fail(f"missing YDL game command declaration: {name} -> {channel}")
game_session = action(xml, "StartYdlGameSession")
for token in ("_ydl_game_session_active", "_ydl_game_session_seq", "PulseYdlGameCommand('GameReady')"):
    if token not in game_session:
        fail(f"GameReady session rule missing: {token}")
game_over = action(xml, "CompleteYdlGameSessionIfPending")
for token in ("PulseYdlGameCommand('GameOver')", "_ydl_game_over_sent_seq", "_ydl_game_session_active"):
    if token not in game_over:
        fail(f"GameOver session rule missing: {token}")
if "CompleteYdlGameSessionIfPending()" not in safe_stop:
    fail("SafeStop entry must flush pending GameOver")
if "CompleteYdlGameSessionIfPending()" not in action(xml, "UpdateYdlSafeCompletion"):
    fail("SafeStop completion must flush pending GameOver after clear")
for lifecycle in (
    "HandlePlaybackStop", "HandlePlaybackLoad", "HandleEmergencyStop",
    "HandleEnterIdle", "HandleIdleTimeout", "HandleEnterIdleEvent", "HandleVideoFinished",
):
    if "EndYdlGameSession()" not in action(xml, lifecycle):
        fail(f"{lifecycle}: terminal transition must end the YDL game session")
started = action(xml, "HandleVideoStarted")
if "Ydl_RequestMediaSession('video', startedMediaId)" not in started:
    fail("HandleVideoStarted must enter the unified media-session arbiter")
if "StartYdlGameSession()" in started:
    fail("HandleVideoStarted must not bypass GameOver -> SafeStop -> Ready arbitration")
request_session = action(xml, "Ydl_RequestMediaSession")
if "Ydl_TryStartDesiredMediaSession()" not in request_session:
    fail("media-session requests must be serialized through the arbiter")
arbiter = action(xml, "Ydl_TryStartDesiredMediaSession")
for token in ("RequestYdlSafeStop()", "CompleteYdlGameSessionIfPending()", "StartYdlGameSession(startKind, startId)"):
    if token not in arbiter and token not in safe_stop and token not in completion:
        fail(f"media-session arbitration token missing: {token}")
if "StartYdlGameSession(startKind, startId)" not in arbiter:
    fail("arbiter must be the only path that starts a new media session")
mode = action(xml, "Ydl_ConfigureMode")
for token in ("'normal'", "'custom'", "Ydl_ApplyNormalMode()", "Ydl_ApplyCustomMode()", "_ydl_safe_in_progress"):
    if token not in mode:
        fail(f"mode-switch safety token missing: {token}")
custom = action(xml, "Ydl_ApplyCustomMode")
for token in ("set(idlePage.motion, '')", "idlePage.StopMotion()", "_ydl_idle_motion_original"):
    if token not in custom:
        fail(f"custom idle suppression token missing: {token}")
normal = action(xml, "Ydl_ApplyNormalMode")
for token in ("set(idlePage.motion, _ydl_idle_motion_original)", "idlePage.RestartMedia()", "Ydl_RequestMediaSession('idle', idlePage.name)"):
    if token not in normal:
        fail(f"normal idle restore token missing: {token}")
for idle_action in ("HandleIdlePageStarted", "HandleIdlePageStopped", "HandleIdlePageFinished"):
    body = action(xml, idle_action)
    if "if(_ydl_mode == 'custom'" not in body:
        fail(f"{idle_action}: missing custom-mode early guard")
stopped_idle = action(xml, "HandleIdlePageStopped")
ordered(stopped_idle, "if(_ydl_mode == 'custom'", "RequestYdlSafeStop()", "custom idle stop must not request SafeStop")
enter_idle = action(xml, "HandleEnterIdle")
if "_ydl_active_media_kind != 'idle'" not in enter_idle or "Ydl_RequestMediaSession('idle', idlePage.name)" not in enter_idle:
    fail("enterIdle must be idempotent and restore the Idle session")
playback_stop = action(xml, "HandlePlaybackStop")
if "local(wasVideoSession, _ydl_active_media_kind == 'video')" not in playback_stop:
    fail("playback.stop must distinguish video -> idle from repeated stop")
if "Ydl_RequestMediaSession('idle', idlePage.name)" not in playback_stop:
    fail("playback.stop must restore the Idle session after leaving video")
initializer = action(xml, "Ydl_Initialize")
for token in ("Ydl_ResetState()", "_ydl_state_prepared"):
    if token not in initializer:
        fail(f"Ydl_Initialize fallback token missing: {token}")
if "AbortYdlGameSessionOnFault()" not in action(xml, "HandleYdlPluginEvent"):
    fail("link-loss handling must abort the local game session")
function_lib = (UNITY / "Assets/Develop/Scripts/Core/Functions/FunctionLibrary.cs").read_text(encoding="utf-8-sig")
if '"DOImmediate"' not in function_lib:
    fail("generic XML DOImmediate primitive is missing")
pulse = action(xml, "PulseYdlGameCommand")
for token in ("SetDO('YDL-0', actionName, 1)", "DOImmediate('YDL-0')", "SetDO('YDL-0', actionName, 0)"):
    if token not in pulse:
        fail(f"YDL game command pulse missing: {token}")
motion_path = UNITY / "Assets/StreamingAssets/Motions/mp_ydl_game_demo.motion"
if motion_path.exists():
    import json
    motion = json.loads(motion_path.read_text(encoding="utf-8-sig"))
    for event in motion.get("events", []):
        if event.get("event_name") == "ydl.gameState":
            try:
                payload = json.loads(event.get("event_data", "{}"))
            except Exception:
                payload = {}
            if payload.get("state") == "GameOver" or payload.get("GameState") == "GameOver":
                fail("motion file must not own lifecycle GameOver")

if '_ydl_capabilities_obj.safety.supportsSafeStop' not in action(xml, "Ydl_Initialize"):
    fail("capability discovery missing in Ydl_Initialize")
if 'Ydl_Initialize()' not in action(xml, "Main"):
    fail("Main must invoke Ydl_Initialize")
if 'onVideoFinished=""' not in main or 'onLongTimeNoInteractive="OnIdleTimeout();"' not in main:
    fail("Main.xml lifecycle hooks are not routed through XML safety actions")
if "onBeforePagesShow" in xml or "onAppReady" in xml:
    fail("legacy GlobalEvent lifecycle hooks must not be used")
configure = action(xml, "Ydl_ConfigureProfile")
for token in ("Ydl_ResetState()", "Ydl_ConfigureMode('normal')"):
    if token not in configure:
        fail(f"Ydl_ConfigureProfile token missing: {token}")
if '<Lifecycle onConfigure="Ydl_ConfigureProfile()"' not in xml or 'onReady="InitializeState()"' not in xml:
    fail("YDL Profile must use explicit Lifecycle onConfigure/onReady stages")

idle_page_source = (UNITY / "Assets/Develop/Scripts/Core/UNIML/CoreTypes.cs").read_text(encoding="utf-8-sig")
idle_ui_source = (UNITY / "Assets/Develop/Scripts/UIs/IdleUI.cs").read_text(encoding="utf-8-sig")
motion_coordinator_source = (UNITY / "Assets/Develop/Scripts/Video/VideoMotionCoordinator.cs").read_text(encoding="utf-8-sig")
for token in ('public class IdlePage', '[XmlAttribute("motion")]', 'onStarted', 'onStopped', 'onFinished', 'MotionFileExists'):
    if token not in idle_page_source:
        fail(f"IdlePage media contract missing: {token}")
if "Begin(IdlePage pageInfo" not in motion_coordinator_source:
    fail("VideoMotionCoordinator does not support IdlePage")
if "motionCoordinator.Begin(info" not in idle_ui_source or "motionCoordinator.Stop();" not in idle_ui_source:
    fail("IdleUI does not start/stop the idle-page motion slot")
for token in ('onEnterIdle="OnIdleEntered();"', 'onStopped="OnIdlePageStopped();"', 'onLongTimeNoInteractive="OnIdleTimeout();"'):
    if token not in main:
        fail(f"Main.xml idle lifecycle hook missing: {token}")
if 'onLeaveIdle="OnIdleLeft();"' in main:
    fail("Main.xml must not invoke idle stop twice (onLeaveIdle + onStopped)")
for action_name in ("OnIdleEntered", "OnIdlePageStarted", "OnIdlePageStopped", "OnIdlePageFinished", "OnIdleTimeout"):
    action(xml, action_name)
completion_body = action(xml, "UpdateYdlSafeCompletion")
if "idlePage.IsMotionPlaying" not in completion_body:
    fail("SafeStop clear must preserve active idle-page motion state")

forbidden = ("RequestSafeStop", "MotionSafeStopClient", "StopYdlSafely", "EnsureSafeForNextPlayback")
for path in list((UNITY / "Assets").rglob("*.cs")) + list((WORKSPACE / "IOToolkit_Unity_Plugin").rglob("*.cs")):
    text = path.read_text(encoding="utf-8-sig", errors="ignore")
    for token in forbidden:
        if token in text:
            fail(f"C# SafeStop coupling remains in {path}: {token}")

print("PASS: UNIPlayer SafeStop policy is XML-owned; C# contains no YDL SafeStop state machine")
