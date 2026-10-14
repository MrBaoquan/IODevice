"""轨道开关位是否真的门控设备输出 (enabled / muted / solo)。

**为什么要这层**: 这三个位曾经只在宿主侧生效 (IOStudio 的实时值预览), 而 native 派发层
只认 `muted`。于是出现最坏的一种错位 —— **界面看着对**（被禁用的轨道在面板上不再跳数），
**设备却照发**。实测: `enabled=false` 的单条轨道仍发出 104 条 Cylinder。

三个开关都必须在这一层断言, 因为它们对输出是对等的"不参与", 但实现路径不同:
  · enabled=false → 轨道完全不参与
  · muted=true    → 同上
  · solo          → 只要存在"启用且独奏"的轨道, 其余一律不参与

断言设计: 每个用例都要求**心跳仍在**(证明链路活着), 再看目标报文是否消失 ——
否则"什么都没收到"会掩盖真正的失败原因, 变成假绿。
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

PLATFORM_PORT = 30120       # 插件发往这里 (本用例在此接收)
# 播放时长只需覆盖"设备打开 + 若干次跳变"。设备打开实测在 4.8~8s 之间浮动,
# 所以跳变用**方波**(全程反复), 不做任何"某时刻发生某事"的时序假设。
PLAYBACK_MS = 16000
CAPTURE_PAD = 4.0


def _kf(t, v):
    return {"time_ms": t, "value": v, "interpolation": "step"}


def _track(tid, role, channel, label, keyframes, **flags):
    track = {
        "id": tid,
        "device": "YDL-0",
        "oaction": "",
        "output_type": "oaxis",
        "oaxis_channel": channel,
        "label": label,
        "color": "#4FC3F7",
        "value_type": "float",
        "muted": False,
        "enabled": True,
        "locked": False,
        "group": "验证",
        "role": role,
        "show_in_curve": True,
        "neutral_value": 0,
        "clips": [{"id": f"c_{tid}", "start_ms": 0, "end_ms": PLAYBACK_MS,
                   "keyframes": keyframes}],
    }
    track.update(flags)
    return track


def _timeline(tracks, name):
    return {
        "version": "3.0", "name": name, "duration_ms": PLAYBACK_MS, "fps": 60,
        "tracks": tracks,
        "action_instances": [], "role_track_bindings": {},
        "embedded_presets": [],
        "groups": [{"name": "验证", "color": "#64748B", "collapsed": False}],
        "events": [], "markers": [], "event_lanes": [], "marker_lanes": [],
    }


def _square(period=1200, upto=PLAYBACK_MS, low=0.0, high=1.0):
    """全程反复的方波 —— 不论设备何时就绪, 之后都必然还有跳变。

    刻意不用"某时刻跳一次"的写法: 设备就绪时刻会在 4.8~8s 之间浮动, 固定时刻的跳变
    有时落在就绪之前, 于是什么都没被观察到, 断言会以"功能坏了"的样子假失败。
    """
    kfs = [_kf(0, low)]
    t = 400
    v = high
    while t < upto:
        kfs.append(_kf(t, v))
        v = low if v >= high else high
        t += period
    kfs.append(_kf(upto, low))
    return kfs


_HEAVE_ON = _square()
_WATER_ON = _square()


def _run_case(tmpdir, doc):
    """跑一次播放并返回 (cmds, 原始行)。"""
    motion = Path(tmpdir) / f"{doc['name']}.motion"
    motion.write_text(json.dumps(doc, ensure_ascii=False, indent=2), encoding="utf-8")

    records = []
    stop = threading.Event()
    error = {}

    def peer():
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        try:
            sock.bind(("127.0.0.1", PLATFORM_PORT))
        except OSError as exc:
            error["peer"] = f"绑定 {PLATFORM_PORT} 失败: {exc!r}"
            return
        sock.settimeout(0.2)
        t0 = time.time()
        while time.time() - t0 < PLAYBACK_MS / 1000.0 + CAPTURE_PAD and not stop.is_set():
            try:
                data, _ = sock.recvfrom(16384)
            except socket.timeout:
                continue
            except OSError:
                continue
            records.append(data.decode("utf-8", errors="replace"))
        sock.close()

    th = threading.Thread(target=peer, daemon=True)
    th.start()
    time.sleep(1.0)

    proc = subprocess.Popen(
        [str(EXE), "--motion", str(motion), "--play", "--native-dispatch",
         "--live-output", "--headless", "--exit-after", str(PLAYBACK_MS)],
        cwd=str(DEPLOY), stdout=subprocess.PIPE, stderr=subprocess.PIPE,
    )
    try:
        proc.wait(timeout=PLAYBACK_MS / 1000.0 + 60)
    except subprocess.TimeoutExpired:
        proc.kill()
    finally:
        stop.set()
        th.join(timeout=CAPTURE_PAD + 10)

    if error.get("peer"):
        pytest.fail(error["peer"])
    return records


def _cmds(records):
    out = []
    for text in records:
        try:
            out.append(json.loads(text).get("Cmd"))
        except Exception:
            pass
    return out


@pytest.fixture(scope="module")
def tmpdir():
    with tempfile.TemporaryDirectory(prefix="trackgate_") as d:
        yield d


def _udp_port_owners(port):
    """用 netstat 查出绑定该 UDP 端口的进程 (Windows 的 UDP 允许同端口共存, 不能用 bind 判断)。"""
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

    别的 IOStudio 实例只要还在播放, 就会往 30120 发它自己的报文, 被本用例的监听收进来,
    表现为"被禁用的轨道仍在输出"这类**误导性**断言失败 —— 排查方向会被彻底带偏。
    宁可在这里明确报出"端口被占", 也不要给出一个看起来像真 bug 的结论。
    """
    owners = _udp_port_owners(PLATFORM_PORT)
    if owners:
        pytest.fail(
            f"端口 {PLATFORM_PORT} 已被进程 {sorted(owners)} 占用 —— "
            "是否有其它 IOStudio / 抓包脚本在跑? 本用例需要独占该端口。"
        )


def test_link_is_alive_in_every_case(tmpdir):
    """基线: 心跳必须在。后续"没有目标报文"的断言靠这条才有意义。"""
    records = _run_case(tmpdir, _timeline(
        [_track("t_heave", "heave", "OAxis_6", "升降", _HEAVE_ON, engineering_range=1000)],
        "gate_baseline",
    ))
    cmds = _cmds(records)
    assert "GamePlatform_Heart" in cmds, "没收到心跳 —— 链路没起来, 后面的断言都不可信"
    assert "GamePlatform_Cylinder" in cmds, "启用状态下的电缸轨道居然没有输出"


def test_disabled_track_does_not_drive_the_device(tmpdir):
    """禁用轨道: 设备不应收到任何该轨道的输出。

    回归锁 —— 修复前 `enabled` 根本不被 native 解析, 这条会看到 ~100 条 Cylinder。
    """
    records = _run_case(tmpdir, _timeline(
        [_track("t_heave", "heave", "OAxis_6", "升降", _HEAVE_ON,
                enabled=False, engineering_range=1000)],
        "gate_disabled",
    ))
    cmds = _cmds(records)
    assert "GamePlatform_Heart" in cmds, "链路没起来"
    assert "GamePlatform_Cylinder" not in cmds, (
        "被禁用的轨道仍在驱动设备 —— enabled 没有被派发层尊重"
    )


def test_muted_track_does_not_drive_the_device(tmpdir):
    """静音轨道: 同上 (这条本来就通过, 用来钉住行为不回归)。"""
    records = _run_case(tmpdir, _timeline(
        [_track("t_heave", "heave", "OAxis_6", "升降", _HEAVE_ON,
                muted=True, engineering_range=1000)],
        "gate_muted",
    ))
    cmds = _cmds(records)
    assert "GamePlatform_Heart" in cmds, "链路没起来"
    assert "GamePlatform_Cylinder" not in cmds, "被静音的轨道仍在驱动设备"


def test_solo_suppresses_other_tracks(tmpdir):
    """独奏: 非独奏轨道的输出必须消失, 而独奏轨道仍在发。"""
    records = _run_case(tmpdir, _timeline(
        [
            _track("t_heave", "heave", "OAxis_6", "升降", _HEAVE_ON,
                   engineering_range=1000),
            _track("t_water", "water", "OAxis_16", "喷水", _WATER_ON, solo=True),
        ],
        "gate_solo",
    ))
    cmds = _cmds(records)
    assert "GamePlatform_Heart" in cmds, "链路没起来"
    assert "GamePlatform_Cylinder" not in cmds, (
        "有独奏轨道时, 非独奏的电缸轨道仍在输出 —— solo 没有被派发层尊重"
    )
    assert "GamePlatform_PlatformEffect" in cmds, "独奏轨道自己反倒没输出"
