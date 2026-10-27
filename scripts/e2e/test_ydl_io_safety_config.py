"""Static/persistence contract checks for YDL SafeStop channel configuration."""
from pathlib import Path
import json
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
WORKSPACE = ROOT.parent

CONFIG_PATHS = [
    ROOT / "IOStudio" / "Config" / "IODevice.YDL.xml",
    ROOT / "Binaries" / "Win64" / "Debug" / "Config" / "IODevice.YDL.xml",
    WORKSPACE / "UNIPlayer" / "Assets" / "Plugins" / "IOToolkit" / "Config" / "IODevice.xml",
]

CONTROLLED_KEY = "OAxis_06"
MOTION_PATH = WORKSPACE / "UNIPlayer" / "Assets" / "StreamingAssets" / "Motions" / "mp_ydl_game_demo.motion"
DISCRETE_KEYS = ["OAxis_16", "OAxis_18", "OAxis_20", "OAxis_22", "OAxis_24", "OAxis_48", "OAxis_49", "OAxis_64", "OAxis_65"]


def _keys(path):
    root = ET.parse(path).getroot()
    device = next((node for node in root.iter("Device") if node.get("Name") == "YDL-0"), None)
    assert device is not None, f"YDL-0 device missing from {path}"
    result = {}
    properties = device.find("Properties")
    assert properties is not None, f"Properties missing from YDL-0 in {path}"
    for node in properties.iter("Key"):
        name = node.get("Name")
        if name:
            result[name] = dict(node.attrib)
    return result


def test_runtime_configs_keep_channel_safety_contract():
    for path in CONFIG_PATHS:
        assert path.exists(), f"missing runtime config: {path}"
        keys = _keys(path)
        assert CONTROLLED_KEY in keys, f"{path}: missing canonical {CONTROLLED_KEY}"
        controlled = keys[CONTROLLED_KEY]
        assert controlled["Min"] == "0"
        assert controlled["Max"] == "1000"
        assert float(controlled["SlewRate"]) == 0.5
        assert float(controlled["SafeValue"]) == 0.0
        assert float(controlled["SafeStopRate"]) == 0.5
        assert controlled["SafeStopPolicy"] == "Controlled"

        for key in DISCRETE_KEYS:
            assert key in keys, f"{path}: missing {key}"
            assert float(keys[key]["SafeValue"]) == 0.0
            assert keys[key]["SafeStopPolicy"] == "Immediate"

        motion = json.loads(MOTION_PATH.read_text(encoding="utf-8-sig"))
        motion_channels = {track.get("oaxis_channel") for track in motion.get("tracks", []) if track.get("oaxis_channel")}
        assert motion_channels, "motion file has no output channels"
        missing = motion_channels - set(keys)
        assert not missing, f"{path}: motion channels missing from Properties/Key: {sorted(missing)}"


def test_unity_and_iostudio_serializers_declare_safety_attributes():
    unity = (WORKSPACE / "UNIPlayer" / "Assets" / "Develop" / "Scripts" / "Core" / "IO" / "IOConfigManager.cs").read_text(encoding="utf-8-sig")
    studio = (ROOT / "IOStudio" / "ViewModels" / "IONodeConfig.cs").read_text(encoding="utf-8-sig")
    for token in ('[XmlAttribute("SlewRate")', '[XmlAttribute("SafeValue")', '[XmlAttribute("SafeStopRate")', '[XmlAttribute("SafeStopPolicy")'):
        assert token in unity, f"UNIPlayer serializer is missing {token}"
        assert token in studio, f"IOStudio serializer is missing {token}"


def test_unity_focus_loss_uses_native_safe_state_path():
    toolkit = (WORKSPACE / "UNIPlayer" / "Assets" / "Plugins" / "IOToolkit" / "Runtime" / "IOToolkit.cs").read_text(encoding="utf-8-sig")
    assert "private void OnApplicationFocus(bool hasFocus)" in toolkit
    assert "if (!hasFocus)" in toolkit
    assert "EnterSafeStateSafely();" in toolkit
    assert "IODeviceController.EnterSafeState();" in toolkit
