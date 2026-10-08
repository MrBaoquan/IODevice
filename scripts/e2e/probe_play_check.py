"""验证: 播放时间轴时, Simulator 的 DO 是否真的随时间变化。

避免用 -c 内联 (中文经 PowerShell 传递会被破坏), 改为独立脚本文件。
"""
import json
import sys
import time
import urllib.request

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
from pywinauto import Desktop  # noqa: E402

BASE = "http://127.0.0.1:18080"


def gj(path, timeout=3):
    with urllib.request.urlopen(BASE + path, timeout=timeout) as r:
        return json.loads(r.read().decode("utf-8"))


def find_editor():
    for w in Desktop(backend="uia").windows():
        try:
            title = w.window_text() or ""
        except Exception:
            continue
        if "showcase" in title.lower():
            return w
    return None


def by_id(win, aid):
    for d in win.descendants():
        if (getattr(d.element_info, "automation_id", "") or "") == aid:
            return d
    return None


def playhead(win):
    t = by_id(win, "CurrentTimeDisplayText")
    return t.element_info.name if t else "?"


def main():
    win = find_editor()
    if win is None:
        print("未找到编辑器窗口")
        return 1
    print("窗口:", win.window_text())

    try:
        win.set_focus()
        time.sleep(0.3)
    except Exception as e:
        print("set_focus 失败:", type(e).__name__)

    play = by_id(win, "TransportPlay")
    pause = by_id(win, "TransportPause")
    playing = bool(pause and pause.is_visible())
    print(f"播放中(初始)={playing}  播放头={playhead(win)}")

    if not playing:
        if play is None:
            print("找不到播放按钮")
            return 2
        play.click_input()
        time.sleep(0.6)

    print("   time       pitch  roll    x    speed")
    combos = set()
    for _ in range(12):
        t = playhead(win)
        do = gj("/api/instances/1/io").get("do", [])
        combos.add((do[1], do[3], do[4], do[8]))
        print(f"  {t}  {do[1]:>5} {do[3]:>5} {do[4]:>5} {do[8]:>6}")
        time.sleep(0.5)

    p2 = by_id(win, "TransportPause")
    if p2 is not None and p2.is_visible():
        p2.click_input()
        print("-> 已暂停")

    print(f"不同取值组合数 = {len(combos)}  (>1 = 确实在变化)")
    print(f"结束播放头 = {playhead(win)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
