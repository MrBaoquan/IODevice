"""影动力播控联动验收 —— 游戏状态宣告 (DO[64..68]) 与播控端回发回读 (DI[64..73])。

原厂协议 §5.2 把游戏状态同步标注为"必需": 游戏端发 `Ready` 表示就绪, 播控端据此进入控制态。
旧实现只在 `OpenDevice` 里自动发一次 Ready, 之后没有任何落点 —— 作者既无法在内容里决定
何时宣告就绪, 也无法在丢包后补发。

本用例守四件事:
  1. **上升沿语义**: 持续高电平不重发; 拉低再拉高 = 重发一次 (手动补发机制)。
  2. **四个状态 + GamePara 请求**都能按通道打出对应报文。
  3. **回读是真实入站**, 且**不是自环** —— 这是我们自己发的状态不能点亮 DI。
     自环是这个改动最容易犯的错: `currentGameState` 收发两头都写, 拿它做 DI 会让
     编排层永远看到"平台已回复"。所以回读必须走只由入站写的 `platformGameState_`。
  4. GamePara 应答被解析 (DeviceID 等不再静默丢弃)。

前置: 不要有其它 IOStudio 实例在跑 (会占用 30121)。
用法: python -m pytest scripts/e2e/test_ydl_game_state.py -v
"""
import json
import os
import socket
import subprocess
import tempfile
import threading
import time
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
DEPLOY = Path(
    os.environ.get("IOSTUDIO_DEPLOY_DIR", str(ROOT / "Binaries" / "Win64" / "Debug"))
)
EXE = DEPLOY / "IOStudio.exe"
PLUGIN_LOG = DEPLOY / "ExternalLibraries" / "Logs" / "MP-YDL.log"

GAME_PORT = 30121
PLATFORM_PORT = 30120
PLAY_MS = 9000
INBOUND_START_S = 4.5     # 留出干净的"只发不收"窗口用于自环断言 (探针在 1s/3s 已发过 Ready)
CAPTURE_PAD = 4.0

DEVICE_ID = 4242


def _probe_json():
    """生成本用例的 .motion 夹具。

    自带夹具而不是引用仓库里的固定文件: 这两条时序 (Ready 的拉低→再拉高、Playing 早于入站)
    就是断言本身, 放在测试里才不会被无关的夹具改动悄悄改掉。
    """
    def track(tid, role, ch, label, kfs):
        return {
            "id": tid, "device": "YDL-0", "oaction": "", "output_type": "oaxis",
            "oaxis_channel": ch, "label": label, "color": "#4FC3F7", "value_type": "float",
            "muted": False, "enabled": True, "locked": False, "group": "验证", "role": role,
            "show_in_curve": True,
            "clips": [{"id": "c_" + tid, "start_ms": 0, "end_ms": PLAY_MS, "keyframes": kfs}],
        }

    return {
        "version": "3.0", "name": "ydl_game_state_probe",
        "duration_ms": PLAY_MS, "fps": 60,
        # 每通道**只允许一条轨道** —— 两条轨道写同一通道会互相覆盖, 时序就不再可控。
        "tracks": [
            # Ready: 1s / 3s 各打一次脉冲 (验证"写一次发一次"), 6s 拉高后**保持**到结束
            # (验证保持住的通道不逐帧重发)
            track("trk_ready", "game_ready", "OAxis_64", "游戏就绪", [
                {"time_ms": 0, "value": 0.0, "interpolation": "step"},
                {"time_ms": 1000, "value": 1.0, "interpolation": "step"},
                {"time_ms": 1200, "value": 0.0, "interpolation": "step"},
                {"time_ms": 3000, "value": 1.0, "interpolation": "step"},
                {"time_ms": 3200, "value": 0.0, "interpolation": "step"},
                {"time_ms": 6000, "value": 1.0, "interpolation": "step"}]),
            # GameOver 在 5s 打一次脉冲
            track("trk_over", "game_over", "OAxis_65", "游戏结束", [
                {"time_ms": 0, "value": 0.0, "interpolation": "step"},
                {"time_ms": 5000, "value": 1.0, "interpolation": "step"},
                {"time_ms": 5200, "value": 0.0, "interpolation": "step"}]),
        ],
        "action_instances": [], "role_track_bindings": {},
        "embedded_presets": [],
        "groups": [{"name": "验证", "color": "#64748B", "collapsed": False}],
        "events": [], "markers": [], "event_lanes": [], "marker_lanes": [],
    }


def _netmsg(cmd, para_obj=None):
    msg = {"Cmd": cmd}
    if para_obj is not None:
        msg["Para"] = para_obj
    return json.dumps(msg, ensure_ascii=False, separators=(",", ":")).encode("utf-8")


def _udp_port_owners(port):
    """netstat 查出绑定该 UDP 端口的进程 (Windows 的 UDP 允许同端口共存)。"""
    try:
        out = subprocess.run(
            ["netstat", "-ano", "-p", "UDP"], capture_output=True, text=True, timeout=10
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


@pytest.fixture(autouse=True)
def _no_other_sender():
    """预检: 30120 必须没有别的监听者。

    上一个 e2e 套件 (如 closed_loop) 的实例可能还没退干净, 仍在往 30120 发包;
    那些报文会被本用例收进来, 表现为"Ready 发了 N 次, 疑似逐帧重发"这类**误导性**失败。
    宁可在这里明确报"端口被占", 也不要给出一个看起来像真 bug 的结论。
    """
    owners = _udp_port_owners(PLATFORM_PORT)
    if owners:
        pytest.fail(
            f"端口 {PLATFORM_PORT} 已被进程 {sorted(owners)} 占用 —— 本用例需要独占该端口。"
        )


@pytest.fixture(scope="module")
def session():
    if not EXE.exists():
        pytest.skip(f"未找到 {EXE} (设置 IOSTUDIO_DEPLOY_DIR)")

    motion = Path(tempfile.gettempdir()) / "ydl_game_state_probe.motion"
    motion.write_text(json.dumps(_probe_json(), ensure_ascii=False, indent=2), encoding="utf-8")

    log_before_lines = (
        len(PLUGIN_LOG.read_text(encoding="utf-8", errors="replace").splitlines())
        if PLUGIN_LOG.exists() else 0
    )

    result = {"records": [], "recv": [], "inboundSentAt": None}
    stop = threading.Event()

    def peer():
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        try:
            sock.bind(("127.0.0.1", PLATFORM_PORT))
        except OSError as exc:
            result["peerError"] = f"绑定 {PLATFORM_PORT} 失败: {exc!r}"
            return
        sock.settimeout(0.2)
        t0 = time.time()
        while time.time() - t0 < PLAY_MS / 1000.0 + CAPTURE_PAD and not stop.is_set():
            now = time.time() - t0
            if now >= INBOUND_START_S:
                if result["inboundSentAt"] is None:
                    result["inboundSentAt"] = now
                try:
                    sock.sendto(
                        _netmsg("GamePlatform_GameState", {"GameState": "Playing"}),
                        ("127.0.0.1", GAME_PORT),
                    )
                    sock.sendto(
                        _netmsg(
                            "GamePlatform_GamePara",
                            {
                                "Language": 0,
                                "DifficultyLevel": 3,
                                "GameLevel": 2,
                                "PlayerVSMode": 1,
                                "DeviceID": DEVICE_ID,
                            },
                        ),
                        ("127.0.0.1", GAME_PORT),
                    )
                except OSError:
                    pass
            try:
                data, _ = sock.recvfrom(16384)
            except socket.timeout:
                continue
            except OSError:
                continue
            text = data.decode("utf-8", errors="replace")
            result["records"].append((round(now * 1000), text))
            try:
                result["recv"].append((now, json.loads(text)))
            except Exception:
                pass
        sock.close()

    th = threading.Thread(target=peer, daemon=True)
    th.start()
    time.sleep(1.0)

    proc = subprocess.Popen(
        [
            str(EXE), "--motion", str(motion), "--play", "--native-dispatch",
            "--live-output", "--headless", "--exit-after", str(PLAY_MS),
        ],
        cwd=str(DEPLOY), stdout=subprocess.PIPE, stderr=subprocess.PIPE,
    )
    try:
        proc.wait(timeout=PLAY_MS / 1000.0 + 60)
    except subprocess.TimeoutExpired:
        proc.kill()
    finally:
        stop.set()
        th.join(timeout=CAPTURE_PAD + 10)

    if result.get("peerError"):
        pytest.fail(result["peerError"])
    if not result["records"]:
        pytest.skip("未捕获到任何 UDP 报文 (插件未加载?)")

    full_lines = (
        PLUGIN_LOG.read_text(encoding="utf-8", errors="replace").splitlines()
        if PLUGIN_LOG.exists() else []
    )
    result["log"] = chr(10).join(full_lines[log_before_lines:])  # 只留本次运行的日志
    return result


def _states(session):
    return [
        o["Para"]["GameState"]
        for _, o in session["recv"]
        if o.get("Cmd") == "GamePlatform_GameState"
        and isinstance(o.get("Para"), dict)
    ]


def _di_lines(session):
    return [ln for ln in session["log"].splitlines() if "host DI read" in ln]


# ---------- 出站: 四个状态 + GamePara 请求 ----------

def test_two_channels_reach_the_platform(session):
    """两个通道 (Ready / GameOver) 各按写入发出一一对应的报文。"""
    states = _states(session)
    # Ready 会多一次: OpenDevice 的自动就绪 (覆盖"内容还没开播"的窗口)
    assert "Ready" in states, f"没有发出 Ready: {states}"
    assert "GameOver" in states, f"没有发出 GameOver: {states}"
    # 只用两个通道: 不该出现 Playing/GameClose 的通道触发
    assert "Playing" not in states, f"不该有 Playing (本设计只做 Ready/GameOver): {states}"
    assert "GameClose" not in states, f"不该有 GameClose: {states}"


def test_ready_can_be_fired_repeatedly(session):
    """探针在 1s 与 3s 各打一次 Ready 脉冲 —— 两次都要发出去。

    这就是"调用一次 setdo 就输出一次": 不需要先拉低再拉高, 写一次发一条。
    """
    times = [ms for ms, text in session["records"] if _is_state(text, "Ready")]
    assert len(times) >= 3, (
        f"Ready 少于三次 (自动 1 次 + 两次通道脉冲): {times} —— 重发机制失效"
    )
    gaps = [b - a for a, b in zip(times, times[1:])]
    assert max(gaps) > 1000, f"两次通道触发挤在一起, 看不到独立脉冲 (gaps={gaps})"


def test_held_channel_does_not_flood(session):
    """保持住的通道**不能**每帧重发。

    宿主每帧强制全量下发 DO (FlushDO @60fps)。若触发判据写成"非 0 即发"、
    或把比较状态也一起复位, 探针在 6s 拉高的那条会从 6s 到 9s 打出 ~180 条。
    """
    states = _states(session)
    # 1s/3s 两次脉冲 + 6s 那次保持 = 通道触发 3 次, 加 OpenDevice 自动 1 次
    assert states.count("Ready") <= 5, (
        f"Ready 发了 {states.count('Ready')} 次 —— 保持住的通道在逐帧重发: {states}"
    )
    assert states.count("GameOver") <= 2, f"GameOver 重复发送: {states}"


# ---------- 入站回读 ----------

def test_platform_reply_is_reflected_in_di(session):
    lines = _di_lines(session)
    assert lines, "插件没有输出 DI 行"
    assert any("platform=Playing" in ln for ln in lines), (
        "播控端回发的 GameState=Playing 没有反映到 DI。"
        f"最后几行: {lines[-3:]}"
    )


def test_platform_params_are_received(session):
    lines = _di_lines(session)
    assert any("param_recv=1" in ln for ln in lines), (
        "GamePara 应答没有被解析 (param_received 未置位)。"
        f"最后几行: {lines[-3:]}"
    )


# ---------- 自环回归锁 (本改动最容易犯的错) ----------

def test_sending_state_does_not_light_the_input(session):
    """我们自己发的状态**不能**被回读成"平台已回复"。

    做法: 入站从 INBOUND_START_S 才开始, 而之前我们**已经**发过 Ready 了 (探针在 1s/3s)。
    那一刻起 DI 的 platform= 必须仍是 NULL —— 否则说明回读取了 `currentGameState`
    (收发两头都写), 形成自环, 编排层会永远误判"平台已就绪"。
    """
    lines = _di_lines(session)
    before = [ln for ln in lines if "platform=" in ln and not ln.endswith("platform=NULL param_recv=0")]
    null_lines = [ln for ln in lines if "platform=NULL" in ln]
    assert null_lines, (
        "入站开始前没有任何 platform=NULL 行 —— 无法证明回读不是自环。"
        f"本次 DI 行: {lines[:3]}"
    )
    assert before, "入站确实生效了 (需要至少一行 platform=非 NULL)"

    # 第一条非 NULL 必须出现在**首次入站之后**
    first_nonnull_ts = before[0].split(" host DI read")[0]
    first_null_ts = null_lines[0].split(" host DI read")[0]
    assert first_null_ts < first_nonnull_ts, (
        "platform 在收到播控端回发之前就已经不是 NULL —— 这是自环: "
        "回读取了本端发出的状态, 而不是入站状态。"
    )


def test_no_invalid_json_outbound(session):
    bad = []
    for _, text in session["records"]:
        try:
            json.loads(text)
        except Exception:
            bad.append(text[:160])
    assert bad == [], f"{len(bad)} 条出站报文非法 JSON: {bad[:2]}"


def _is_state(text, state):
    try:
        o = json.loads(text)
    except Exception:
        return False
    return (
        o.get("Cmd") == "GamePlatform_GameState"
        and isinstance(o.get("Para"), dict)
        and o["Para"].get("GameState") == state
    )
