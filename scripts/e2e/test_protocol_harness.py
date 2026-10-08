"""
协议测试台验收。

两部分:
  A. loopback 自测 —— 证明"测试台自身"正确 (无需任何外部对端, 可进 CI)。
     UDP / TCP 各自起监听 + 连接, 往返并断言。
  B. MP-YDL 真实链路验收 —— 用测试台作为"播控端"绑定 30120,
     在 IOStudio 真实播放影动力编排时, 断言插件产出的报文符合协议。
     这是"结合 YDL 的验收": 测试台必须能在真实链路上给出可判定的结论。

前置: 不要有其它 IOStudio 实例在跑 (插件会占 30121, 并往 30120 发心跳造成干扰)。

用法:
    python -m pytest scripts/e2e/test_protocol_harness.py -v
"""
import json
import os
import subprocess
import time
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
DEPLOY = Path(
    os.environ.get("IOSTUDIO_DEPLOY_DIR", str(ROOT / "Binaries" / "Win64" / "Debug"))
)
EXE = DEPLOY / "IOStudio.exe"

SRC_SCENARIOS = ROOT / "IOStudio" / "Config" / "ProtocolTests"
MOTION = DEPLOY / "Config" / "Motion" / "mp_ydl_game_demo.motion"

# 所有 loopback-*.json 都自动纳入门禁 —— 新增自测场景无需改测试代码。
LOOPBACK_SCENARIOS = sorted(p.name for p in SRC_SCENARIOS.glob("loopback-*.json"))

GAME_PORT = 30121
PLATFORM_PORT = 30120
PLAY_MS = int(os.environ.get("YDL_PLAY_MS", "30000"))


def _udp_port_owners(port):
    """netstat 查 UDP 端口占用 (Windows 下 bind 探测会误判, netstat 才权威)。"""
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


def _require_exe():
    if not EXE.exists():
        pytest.skip(f"未找到 {EXE} (设置 IOSTUDIO_DEPLOY_DIR)")


def _run_scenario(name, report_path):
    """用 CLI 跑一个协议测试场景, 返回 (退出码, 报告 dict 或 None, stdout)。"""
    scenario = SRC_SCENARIOS / name
    assert scenario.exists(), f"场景不存在: {scenario}"
    r = subprocess.run(
        [str(EXE), "--protocol-test", str(scenario), "--report", str(report_path)],
        cwd=str(DEPLOY),
        capture_output=True,
        text=True,
        timeout=120,
    )
    report = None
    if report_path.exists():
        try:
            # utf-8-sig: 容忍可能的 BOM (报告本身不带 BOM, 这里是防御)
            report = json.loads(report_path.read_text(encoding="utf-8-sig"))
        except Exception:
            report = None
    return r.returncode, report, (r.stdout or "") + (r.stderr or "")


# ───────────────────────── A. loopback 自测 ─────────────────────────

@pytest.mark.parametrize("name", LOOPBACK_SCENARIOS)
def test_loopback_scenario_passes(name, tmp_path):
    """
    loopback 场景必须全通过: 起监听 + 连接 + 往返 + 各类断言。
    这是测试台自身正确性的门禁 —— 不依赖任何外部设备。
    """
    _require_exe()
    report_path = tmp_path / (name + ".report.json")
    code, report, output = _run_scenario(name, report_path)

    assert report is not None, f"未生成报告:\n{output}"
    assert code == 0, (
        f"{name} 未通过 (exit={code}):\n{output}\n"
        + json.dumps(report, ensure_ascii=False, indent=2)
    )
    assert report["ok"] is True
    assert report["summary"]["failed"] == 0
    assert report["summary"]["passed"] > 0


def test_loopback_scenario_fails_when_expectation_is_wrong(tmp_path):
    """
    反向验证: 断言不成立时, 测试台必须**报失败且退出码为 2**。
    否则"全绿"就没有意义 (假绿风险)。
    """
    _require_exe()
    bad = tmp_path / "bad.json"
    bad.write_text(
        json.dumps({
            "name": "负例: 期望不该出现的内容",
            "timeout_ms": 800,
            "steps": [
                {"op": "listen", "as": "srv", "transport": "udp", "port": 0},
                {"op": "connect", "as": "cli", "transport": "udp",
                 "host": "127.0.0.1", "port_from": "srv"},
                {"op": "send", "via": "cli", "encoding": "utf8", "data": "PING"},
                {"op": "expect", "via": "srv",
                 "match": {"type": "equals", "value": "TOTALLY-DIFFERENT"}},
            ],
        }),
        encoding="utf-8",
    )
    report_path = tmp_path / "bad.report.json"
    r = subprocess.run(
        [str(EXE), "--protocol-test", str(bad), "--report", str(report_path)],
        cwd=str(DEPLOY), capture_output=True, text=True, timeout=120,
    )
    assert r.returncode == 2, f"负例应退出码 2, 实际 {r.returncode}"
    report = json.loads(report_path.read_text(encoding="utf-8-sig"))
    assert report["ok"] is False
    assert report["summary"]["failed"] == 1
    # 失败原因必须可读 (供 AI/人定位)
    reason = report["steps"][-1].get("reason") or ""
    assert reason, "失败步骤必须带 reason"


def test_loopback_scenarios_exist():
    """门禁必须真的有场景可跑, 否则"全绿"是空集。"""
    assert LOOPBACK_SCENARIOS, f"未找到任何 loopback 场景: {SRC_SCENARIOS}"
    # 至少覆盖 UDP / TCP / WebSocket 与三种组帧
    joined = " ".join(LOOPBACK_SCENARIOS)
    assert "udp" in joined and "tcp" in joined and "websocket" in joined, joined


# ───────────────────────── C. 串口 (需真实/虚拟串口) ─────────────────────────

def test_serial_scenario(tmp_path):
    """
    串口往返。需要 IOUI_TEST_SERIAL 指向一个真实/虚拟串口, 且该串口有回环
    或回显设备 (否则收不到数据)。未配置则跳过 —— 串口依赖物理环境, 不适合硬门禁。
    """
    _require_exe()
    if not os.environ.get("IOUI_TEST_SERIAL"):
        pytest.skip("未设置 IOUI_TEST_SERIAL (例如 COM3), 跳过串口用例")
    report_path = tmp_path / "serial.report.json"
    code, report, output = _run_scenario("serial-echo.json", report_path)
    assert report is not None, f"未生成报告:\n{output}"
    assert code == 0, (
        f"串口场景未通过 (exit={code}):\n{output}\n"
        + json.dumps(report, ensure_ascii=False, indent=2)
    )


# ───────────────────────── B. MP-YDL 真实链路验收 ─────────────────────────

@pytest.fixture(scope="module")
def ydl_harness_result(tmp_path_factory):
    _require_exe()
    if not MOTION.exists():
        pytest.skip(f"未找到 {MOTION} (先运行 --motion-build)")

    busy = _udp_port_owners(PLATFORM_PORT) | _udp_port_owners(GAME_PORT)
    if busy:
        pytest.fail(
            f"端口 {PLATFORM_PORT}/{GAME_PORT} 已被进程 {sorted(busy)} 占用 —— "
            "本用例需要独占这两个端口, 请先关闭其它 IOStudio/厂家软件。"
        )

    # 1) 真实播放: 插件以"游戏端"身份监听 30121, 把命令发往 30120
    player = subprocess.Popen(
        [
            str(EXE), "--motion", str(MOTION), "--play", "--native-dispatch",
            "--live-output", "--headless", "--exit-after", str(PLAY_MS),
        ],
        cwd=str(DEPLOY),
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )

    # 播放端必须真的占住 30121 —— 否则插件绑定失败, 电缸线程不会启动, 表现为
    # "收不到 Cylinder"这种难以定位的现象。这里把环境问题变成明确失败。
    owners = set()
    for _ in range(40):
        owners = _udp_port_owners(GAME_PORT)
        if player.pid in owners:
            break
        time.sleep(0.25)
    if player.pid not in owners:
        player.kill()
        pytest.fail(
            f"播放端未占住 {GAME_PORT} (当前占用者={sorted(owners)}) —— "
            "可能有其它 IOStudio 实例残留; 这会让插件绑定失败、不产生电缸报文。"
        )

    try:
        time.sleep(1.0)  # 让播放先跑起来, 开始产生报文

        # 2) 测试台作为"播控端": 绑定 30120 并断言收到的报文
        report_path = tmp_path_factory.mktemp("pt") / "ydl.report.json"
        code, report, output = _run_scenario("ydl-platform-side.json", report_path)
    finally:
        player.kill()
        try:
            player.wait(timeout=10)
        except Exception:
            pass

    return {"code": code, "report": report, "output": output}


def test_ydl_link_passes_protocol_harness(ydl_harness_result):
    """
    测试台在真实影动力链路上必须判定通过 (exit 0 / report.ok)。
    这同时验证了两件事: 测试台可用 + 插件报文符合协议。
    """
    r = ydl_harness_result
    assert r["report"] is not None, f"未生成报告:\n{r['output']}"
    assert r["code"] == 0, (
        f"YDL 链路未通过协议测试台 (exit={r['code']}):\n{r['output']}\n"
        + json.dumps(r["report"], ensure_ascii=False, indent=2)
    )
    assert r["report"]["ok"] is True
    assert r["report"]["summary"]["failed"] == 0


def test_ydl_harness_observed_expected_commands(ydl_harness_result):
    """
    报告里必须能看到电缸(Cylinder)与环境特效(PlatformEffect) ——
    否则"通过"可能是空场景假绿。
    """
    r = ydl_harness_result
    report = r["report"]
    assert report is not None, r["output"]
    texts = " ".join(
        (s.get("received_text") or "") for s in report["steps"]
    )
    assert "GamePlatform_Cylinder" in texts, f"未观察到 Cylinder 报文:\n{texts[:500]}"
    assert "HeightRatioList" in texts, "Cylinder 报文的 Para 应含 HeightRatioList"
    assert "GamePlatform_PlatformEffect" in texts, f"未观察到 PlatformEffect:\n{texts[:500]}"
