"""诊断: 播放时间轴时, Simulator 实例是否收到运动数据。

要点:
- 走带可能是"已在播放"状态 (此时 Play 按钮隐藏, Pause 可见), 不能盲点 Play。
- 采样 /api/instances/{n}/io, 报告 DO 运动通道的**峰值**, 全 0 即"没派发到"。
- 顺带打印实例列表, 用于确认设备清理结果 (应只剩一个 Simulator)。
"""
import json
import sys
import time
import urllib.request

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
from pywinauto import Desktop  # noqa: E402

BASE = "http://127.0.0.1:18080"
MOTION_DO = {
    0: "duration", 1: "pitch", 2: "yaw", 3: "roll",
    4: "x", 5: "y", 6: "z", 7: "angle",
    8: "speed", 9: "reset", 10: "estop", 11: "enable",
}


def gj(path, timeout=3):
    with urllib.request.urlopen(BASE + path, timeout=timeout) as r:
        return json.loads(r.read().decode("utf-8"))


def find_window(keyword="showcase", timeout=20):
    deadline = time.time() + timeout
    while time.time() < deadline:
        for w in Desktop(backend="uia").windows():
            try:
                if keyword.lower() in w.window_text().lower() and w.is_visible():
                    return w
            except Exception:
                continue
        time.sleep(0.5)
    return None


def by_id(win, aid):
    for d in win.descendants():
        if (getattr(d.element_info, "automation_id", "") or "") == aid:
            return d
    return None


def list_instances():
    try:
        r = gj("/api/instances")
        out = []
        for inst in r.get("instances", []):
            out.append(inst.get("device_index"))
        return out
    except Exception as e:
        return f"ERR {e}"


def sample(seconds=6.0, step=0.4):
    peak = {}
    n = 0
    t0 = time.time()
    while time.time() - t0 < seconds:
        try:
            s = gj("/api/instances/1/io")
            do = s.get("do", [])
            for i in MOTION_DO:
                if i < len(do):
                    peak[i] = max(peak.get(i, 0), abs(do[i]))
            n += 1
        except Exception:
            pass
        time.sleep(step)
    return peak, n


def main():
    print("实例列表 device_index:", list_instances())

    win = find_window()
    if not win:
        print("未找到编辑器窗口")
        return 1

    play = by_id(win, "TransportPlay")
    pause = by_id(win, "TransportPause")
    playing = pause is not None and pause.is_visible()
    print(f"当前是否播放中: {playing}")

    if not playing:
        if play is None:
            print("找不到播放按钮")
            return 2
        print("点击播放 ...")
        play.click_input()
        time.sleep(0.5)

    peak, n = sample(6.0)
    print(f"采样 {n} 次, 运动通道峰值:")
    nz = {MOTION_DO[i]: peak.get(i, 0) for i in MOTION_DO if peak.get(i, 0)}
    if nz:
        for k, v in nz.items():
            print(f"   {k:<9} = {v}")
    else:
        print("   (全部为 0 —— 运动数据未到达 Simulator)")

    # 停止播放, 避免继续跑
    pause = by_id(win, "TransportPause")
    if pause is not None and pause.is_visible():
        pause.click_input()
        print("已暂停")
    return 0


if __name__ == "__main__":
    sys.exit(main())
