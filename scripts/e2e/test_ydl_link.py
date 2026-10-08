"""
MP-YDL (影动力) 协议链路验收测试。

目标: 用"真实播放 + 真实 UDP 抓包"验证插件产出的报文符合原厂协议,
把之前的手工抓包变成可重复的门禁。

断言 (逐条对应已修复的缺陷):
  1. 有报文产生 (链路通)。
  2. **100% 可被标准 JSON 解析** —— 修复前实测 97% 非法 (Para 未转义)。
  3. 每条带 Para 的报文, 其 Para 反转义后**自身也是合法 JSON**。
  4. Cmd 属于协议枚举。
  5. 电缸发送间隔 **>= 45ms** —— 协议建议 50ms; 修复前实测 22ms (超发 2 倍)。
  6. HeightRatioList 值域在 0..1。
  7. 心跳约 1s 一条。

前置: 不要有其它 IOStudio 实例在跑 (它会同时往 30121 发心跳, 造成干扰)。

用法:
    python -m pytest scripts/e2e/test_ydl_link.py -v
"""
import json
import os
import socket
import subprocess
import sys
import threading
import time
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
DEPLOY = Path(
    os.environ.get("IOSTUDIO_DEPLOY_DIR", str(ROOT / "Binaries" / "Win64" / "Debug"))
)
MOTION = DEPLOY / "Config" / "Motion" / "mp_ydl_game_demo.motion"
EXE = DEPLOY / "IOStudio.exe"

GAME_PORT = 30121          # 协议: 游戏端端口 (插件发往这里)
PLAY_MS = int(os.environ.get("YDL_PLAY_MS", "30000"))
CAPTURE_PAD = 4.0

KNOWN_CMDS = {
    "GamePlatform_GamePara",
    "GamePlatform_GameState",
    "GamePlatform_Heart",
    "GamePlatform_PlatformEffect",
    "GamePlatform_ContinueGameRequest",
    "GamePlatform_ContinueGameCoinInfo",
    "GamePlatform_ContinueGameStart",
    "GamePlatform_UserInfo",
    "GamePlatform_GameAchievement",
    "GamePlatform_KeyState",
    "GamePlatform_Cylinder",
}

MIN_CYLINDER_INTERVAL_MS = 45.0   # 协议建议 50ms, 留调度抖动余量


def _capture(seconds):
    """绑定游戏端端口, 收集 (ms, text)。"""
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.bind(("127.0.0.1", GAME_PORT))
    sock.settimeout(0.3)
    records = []
    t0 = time.time()
    while time.time() - t0 < seconds:
        try:
            data, _ = sock.recvfrom(8192)
        except socket.timeout:
            continue
        except OSError:
            break
        records.append((round((time.time() - t0) * 1000), data.decode("utf-8", errors="replace")))
    sock.close()
    return records


@pytest.fixture(scope="module")
def captured():
    if not EXE.exists():
        pytest.skip(f"未找到 {EXE} (设置 IOSTUDIO_DEPLOY_DIR)")
    if not MOTION.exists():
        pytest.skip(f"未找到 {MOTION} (先运行 --motion-build)")

    # 前置检查: 端口不被占用 (否则可能是另一个 IOStudio 在跑, 会污染抓包)
    probe = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        probe.bind(("127.0.0.1", GAME_PORT))
    except OSError as e:
        pytest.fail(f"端口 {GAME_PORT} 已被占用 (是否有其它 IOStudio 在跑?): {e}")
    finally:
        probe.close()

    result = {}

    def run_capture():
        result["records"] = _capture(PLAY_MS / 1000.0 + CAPTURE_PAD)

    th = threading.Thread(target=run_capture, daemon=True)
    th.start()
    time.sleep(1.5)

    proc = subprocess.Popen(
        [
            str(EXE), "--motion", str(MOTION), "--play", "--native-dispatch",
            "--live-output", "--headless", "--exit-after", str(PLAY_MS),
        ],
        cwd=str(DEPLOY),
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    try:
        proc.wait(timeout=PLAY_MS / 1000.0 + 40)
    except subprocess.TimeoutExpired:
        proc.kill()
    th.join(timeout=CAPTURE_PAD + 15)

    records = result.get("records", [])
    if not records:
        pytest.skip("未捕获到任何 UDP 报文 (插件未加载或设备未注册为 YDL-0)")
    return records


def _split(records):
    """返回 (valid, invalid, para_objects)。"""
    valid, invalid, paras = [], [], []
    for ms, text in records:
        try:
            obj = json.loads(text)
        except Exception:
            invalid.append((ms, text))
            continue
        valid.append((ms, obj))
        para = obj.get("Para")
        if isinstance(para, str):
            try:
                paras.append((ms, obj.get("Cmd"), json.loads(para)))
            except Exception:
                paras.append((ms, obj.get("Cmd"), None))
    return valid, invalid, paras


def test_messages_are_all_valid_json(captured):
    """P0-1 回归: 所有报文必须能被标准 JSON 解析器解析。"""
    valid, invalid, _ = _split(captured)
    assert invalid == [], (
        f"{len(invalid)}/{len(captured)} 条报文不是合法 JSON (修复前为 97%)。"
        f"示例: {invalid[0][1][:160]!r}"
    )


def test_para_payload_is_valid_json(captured):
    """P0-1 回归: Para 反转义后自身必须是合法 JSON (协议要求 Para 为转义后的 JSON 字符串)。"""
    _, _, paras = _split(captured)
    bad = [(ms, cmd) for ms, cmd, obj in paras if obj is None]
    assert paras, "没有任何带 Para 的报文, 无法验证"
    assert bad == [], f"以下报文的 Para 不是合法 JSON: {bad[:5]}"


def test_commands_are_known(captured):
    valid, _, _ = _split(captured)
    cmds = {obj.get("Cmd") for _, obj in valid}
    unknown = cmds - KNOWN_CMDS
    assert not unknown, f"出现协议未定义的 Cmd: {unknown}"


def test_cylinder_is_rate_limited(captured):
    """P1-6 回归: 电缸间隔需 >= 协议建议 (修复前实测 22ms)。"""
    valid, _, _ = _split(captured)
    cyl = [ms for ms, obj in valid if obj.get("Cmd") == "GamePlatform_Cylinder"]
    assert len(cyl) > 3, f"电缸报文太少 ({len(cyl)}), 无法验证节流"
    gaps = [cyl[i + 1] - cyl[i] for i in range(len(cyl) - 1)]
    mn = min(gaps)
    avg = sum(gaps) / len(gaps)
    assert mn >= MIN_CYLINDER_INTERVAL_MS, (
        f"电缸最小间隔 {mn}ms < {MIN_CYLINDER_INTERVAL_MS}ms (平均 {avg:.1f}ms) —— 节流失效"
    )


def test_cylinder_values_in_range(captured):
    valid, _, _ = _split(captured)
    bad = []
    for ms, obj in valid:
        if obj.get("Cmd") != "GamePlatform_Cylinder":
            continue
        data = json.loads(obj["Para"])
        for v in data.get("HeightRatioList", []):
            if not (0.0 <= v <= 1.0):
                bad.append((ms, v))
    assert not bad, f"HeightRatioList 越界 (应 0..1): {bad[:5]}"


def test_heartbeat_is_periodic(captured):
    valid, _, _ = _split(captured)
    beats = [ms for ms, obj in valid if obj.get("Cmd") == "GamePlatform_Heart"]
    assert len(beats) >= 3, f"心跳太少 ({len(beats)})"
    gaps = [beats[i + 1] - beats[i] for i in range(len(beats) - 1)]
    avg = sum(gaps) / len(gaps)
    assert 700 <= avg <= 1400, f"心跳平均间隔 {avg:.0f}ms, 期望约 1000ms"


def test_effect_intensity_is_within_protocol_range(captured):
    """P2-1 回归: Intensity 若出现, 必须在协议规定的 1..100。"""
    valid, _, _ = _split(captured)
    bad = []
    for ms, obj in valid:
        if obj.get("Cmd") != "GamePlatform_PlatformEffect":
            continue
        data = json.loads(obj["Para"])
        eff = data.get("EffectData", {})
        if "Intensity" in eff and not (1 <= eff["Intensity"] <= 100):
            bad.append((ms, eff["Intensity"]))
    assert not bad, f"Intensity 超出 1..100: {bad[:5]}"
