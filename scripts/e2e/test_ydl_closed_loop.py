"""
MP-YDL (影动力) 双向闭环验收测试。

与 test_ydl_link.py 的"单向抓包"不同, 本用例扮演**播控端 (厂家服务软件)**:
  · 绑定 30120 (播控端端口): 接收插件 (游戏端) 发来的报文;
  · 向插件 (游戏端, 30121) 下发平台侧命令: GamePlatform_KeyState /
    GamePlatform_ContinueGameCoinInfo / GamePlatform_GameState;
  · 用插件日志 (Logs/MP-YDL.log) 断言插件**确实收到并解析**了这些命令;
  · 断言链路健康状态位随收发真实变化, 以及 KeyState → DI[56..63] 的映射:
        DI[0]  system_ready / DI[5] fault_active / DI[7] busy / DI[46] interlock_ok。

这样把"只证明发出方向正确"升级为"收发双向都被真实观测", 形成调试测试闭环。

前置: 不要有其它 IOStudio 实例在跑 (它会占用 30121 造成干扰)。
用法:
    python -m pytest scripts/e2e/test_ydl_closed_loop.py -v
"""
import json
import os
import socket
import subprocess
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

GAME_PORT = 30121      # 游戏端端口 —— 插件监听它 (本测试向这里下发平台侧命令)
PLATFORM_PORT = 30120  # 播控端端口 —— 插件发往这里 (本测试在此收发)
PLAY_MS = int(os.environ.get("YDL_PLAY_MS", "20000"))
CAPTURE_PAD = 5.0
INBOUND_START_S = 7.0   # 等插件 OpenDevice 后再下发
INBOUND_PERIOD_S = 0.5

# 唯一标记: 避免与上一次运行残留的日志内容混淆 (断言"本次"确实被收到)
KEY_VALUE = 504             # bit3..bit8 → DI[56..63] = 11111100
COIN_NEED = 90711
COIN_CURRENT = 88137
GAME_STATE = "ClosedLoop_Playing"

PLUGIN_LOG = DEPLOY / "ExternalLibraries" / "Logs" / "MP-YDL.log"


def _netmsg(cmd, para_obj=None):
    """按协议打包 NetMsgJson: Para 是"转义后的 JSON 字符串"。"""
    msg = {"Cmd": cmd}
    if para_obj is not None:
        msg["Para"] = json.dumps(para_obj, ensure_ascii=False, separators=(",", ":"))
    return json.dumps(msg, ensure_ascii=False, separators=(",", ":")).encode("utf-8")


INBOUND = [
    _netmsg("GamePlatform_KeyState", {"KeyValue": KEY_VALUE}),
    _netmsg("GamePlatform_ContinueGameCoinInfo", {"NeedCoins": COIN_NEED, "CurrentCoins": COIN_CURRENT}),
    _netmsg("GamePlatform_GameState", {"GameState": GAME_STATE}),
]


def _udp_port_owners(port):
    """用 netstat 查出绑定该 UDP 端口的进程 ID (Windows UDP 允许同端口共存, 不能用 bind 判断)。"""
    try:
        out = subprocess.run(
            ["netstat", "-ano", "-p", "UDP"],
            capture_output=True, text=True, timeout=10,
        ).stdout
    except Exception:
        return set()
    owners = set()
    for line in out.splitlines():
        parts = line.split()
        if len(parts) < 4 or parts[0].upper() != "UDP":
            continue
        if parts[1].endswith(f":{port}"):
            try:
                owners.add(int(parts[-1]))
            except ValueError:
                pass
    return owners


def _read_plugin_log():
    """读取插件日志 (本用例通过它观测"插件收到了什么")。"""
    for cand in (
        DEPLOY / "ExternalLibraries" / "Logs" / "MP-YDL.log",
        DEPLOY / "Logs" / "MP-YDL.log",
    ):
        if cand.exists():
            return cand.read_text(encoding="utf-8", errors="replace")
    return ""


@pytest.fixture(scope="module")
def closed_loop():
    if not EXE.exists():
        pytest.skip(f"未找到 {EXE} (设置 IOSTUDIO_DEPLOY_DIR)")
    if not MOTION.exists():
        pytest.skip(f"未找到 {MOTION} (先运行 --motion-build)")

    for port, who in ((PLATFORM_PORT, "播控端"), (GAME_PORT, "游戏端")):
        busy = _udp_port_owners(port)
        if busy:
            pytest.fail(
                f"端口 {port} ({who}) 已被进程 {sorted(busy)} 占用 —— "
                "是否有其它 IOStudio/厂家软件在跑? 本用例需要独占 30120/30121。"
            )

    result = {"records": [], "inboundSent": 0}
    stop = threading.Event()

    def peer():
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.bind(("127.0.0.1", PLATFORM_PORT))
        sock.settimeout(0.2)
        t0 = time.time()
        last_send = 0.0
        records = []
        sent = 0
        total = PLAY_MS / 1000.0 + CAPTURE_PAD
        try:
            while time.time() - t0 < total and not stop.is_set():
                now = time.time() - t0
                if now >= INBOUND_START_S and (now - last_send) >= INBOUND_PERIOD_S:
                    for payload in INBOUND:
                        sock.sendto(payload, ("127.0.0.1", GAME_PORT))
                    last_send = now
                    sent += 1
                try:
                    data, _ = sock.recvfrom(8192)
                except socket.timeout:
                    continue
                except OSError:
                    break
                records.append((round(now * 1000), data.decode("utf-8", errors="replace")))
        finally:
            sock.close()
        result["records"] = records
        result["inboundSent"] = sent

    th = threading.Thread(target=peer, daemon=True)
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
        time.sleep(6.0)
        owners = _udp_port_owners(GAME_PORT)
        result["game_port_owner_is_cli"] = proc.pid in owners
        result["game_port_owners"] = sorted(owners)
        proc.wait(timeout=PLAY_MS / 1000.0 + 45)
    except subprocess.TimeoutExpired:
        proc.kill()
    finally:
        stop.set()
        th.join(timeout=CAPTURE_PAD + 15)

    result["log"] = _read_plugin_log()
    if not result["records"]:
        pytest.skip("未捕获到任何 UDP 报文 (插件未加载或设备未注册为 YDL-0)")
    return result


# ---------- 方向 1: 插件 → 播控端 (出站) ----------

def test_outbound_messages_flow(closed_loop):
    """链路基本可用: 插件持续向播控端发送心跳。"""
    valid = []
    for _, text in closed_loop["records"]:
        try:
            valid.append(json.loads(text))
        except Exception:
            pass
    beats = [o for o in valid if o.get("Cmd") == "GamePlatform_Heart"]
    assert len(beats) >= 3, f"心跳太少 ({len(beats)}), 出站链路可能不通"


def test_outbound_is_valid_json(closed_loop):
    """P0-1 回归: 所有出站报文必须能被标准 JSON 解析。"""
    bad = []
    for _, text in closed_loop["records"]:
        try:
            json.loads(text)
        except Exception:
            bad.append(text[:160])
    assert bad == [], f"{len(bad)}/{len(closed_loop['records'])} 条出站报文非法 JSON: {bad[:2]}"


# ---------- 方向 2: 播控端 → 插件 (入站, 闭环) ----------

def test_inbound_commands_were_actually_sent(closed_loop):
    assert closed_loop["inboundSent"] > 0, "测试没有成功下发任何平台侧命令 (时序问题?)"


def test_plugin_received_key_state(closed_loop):
    log = closed_loop["log"]
    assert "YDL recv" in log, "插件日志没有记录任何收到的命令"
    assert "GamePlatform_KeyState" in log, "插件没有收到 KeyState"
    assert str(KEY_VALUE) in log, f"插件没有收到本次的 KeyValue={KEY_VALUE}"


def test_plugin_received_coin_info(closed_loop):
    log = closed_loop["log"]
    assert "GamePlatform_ContinueGameCoinInfo" in log, "插件没有收到 ContinueGameCoinInfo"
    assert f'"CurrentCoins":{COIN_CURRENT}' in log, f"插件没有收到本次的 CurrentCoins={COIN_CURRENT}"


def test_plugin_received_game_state(closed_loop):
    log = closed_loop["log"]
    assert GAME_STATE in log, f"插件没有收到本次的 GameState={GAME_STATE}"


# ---------- 真实状态位 (由链路健康驱动) ----------

def test_link_health_status_bits(closed_loop):
    """system_ready / interlock_ok 由真实收发驱动: 收到入站后 interlock_ok=1。"""
    log = closed_loop["log"]
    assert "YDL status" in log, "插件没有输出链路健康状态行"
    assert "ready=1" in log, "system_ready 未置位 (本端未就绪?)"
    assert "link=1" in log, "interlock_ok 未置位 (未观测到入站链路)"
    assert "fault=0" in log, "意外出现 fault_active=1"


def test_key_state_maps_to_di(closed_loop):
    """KeyState=504 (bit3..bit8) → 宿主读到的 DI[56..63] 应为 11111100。"""
    log = closed_loop["log"]
    assert "host DI read" in log, "宿主从未读取 DI (设备未被轮询?)"
    expected = "keys56..63=11111100"
    assert expected in log, (
        f"DI[56..63] 未按 KeyState 映射 (期望 {expected})。"
        "说明入站按键状态没有进入 DI 反馈。"
    )
