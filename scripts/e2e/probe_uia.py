"""
IOStudio UIA 可见性结论 (实测, 2026-09-30)。

目的: 在铺开 pywinauto E2E 之前, 用事实界定 UIA 能覆盖的范围。
本文件是一份探针(不是测试套件骨架) —— 它做两件事:
  1) 断言已知的可行能力 (回归保护): 关键控件确实可通过 AutomationId 定位;
  2) 断言已知的盲区 (能力边界): 时间轴自绘控件不在 UIA 树里。

为什么要有"盲区断言": 一旦 Avalonia 或本项目给时间轴补上自动化暴露,
这些断言会失败, 提醒我们及时把"双击加帧/关键帧拖拽"纳入 UIA 自动化,
而不是继续依赖窗口消息注入。

用法:
    python probe_uia.py                 # 打印可见性报告(人工查看)
    pytest probe_uia.py -v              # 跑下面两组断言
"""
import os
import sys
import time

import pytest
from pywinauto import Desktop

WINDOW_KEYWORD = os.environ.get("IOSTUDIO_WINDOW_KEYWORD", "showcase")
WINDOW_TIMEOUT = int(os.environ.get("IOSTUDIO_WINDOW_TIMEOUT", "20"))

# 已验证: 这些控件可通过 AutomationId 稳定定位 (检查器 / 走带 / 菜单)
KNOWN_VISIBLE_IDS = [
    "KfPropertyPanel",   # 检查器面板
    "ValueInput",        # 数值输入框 —— "选关键帧后改不了参数" 的关键控件
    "ValueSlider",       # 数值滑块
    "MarkerNameInput",
    "PlayheadEventDataInput",
    "CurrentTimeDisplayText",
    "DurationDisplayText",
    "SpeedComboBox",
    "ZoomPercentText",
    "MenuBar",
    "MainMenu",
    "AutoKeyToggle",
]

# 已验证: 时间轴/曲线等自绘控件<b>不</b>暴露到 UIA
KNOWN_BLIND_SPOTS = [
    "TrackClipControl",
    "CurveEditorControl",
    "TimeRulerControl",
]


def _find_window(keyword=WINDOW_KEYWORD, timeout=WINDOW_TIMEOUT):
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


@pytest.fixture(scope="module")
def win():
    w = _find_window()
    if w is None:
        pytest.skip(
            f"未找到标题含 {WINDOW_KEYWORD!r} 的可见 IOStudio 窗口; "
            "请先启动: IOStudio.exe --motion <file> 并设置 IOSTUDIO_WINDOW_KEYWORD"
        )
    return w


def _all_descendants(win):
    return win.descendants()


def _auto_ids(win):
    ids = set()
    for d in _all_descendants(win):
        aid = getattr(d.element_info, "automation_id", None)
        if aid:
            ids.add(aid)
    return ids


@pytest.mark.parametrize("auto_id", KNOWN_VISIBLE_IDS)
def test_inspector_and_transport_controls_are_locatable(win, auto_id):
    """检查器/走带控件必须能通过 AutomationId 定位 —— pywinauto 路线的前提。"""
    ids = _auto_ids(win)
    assert auto_id in ids, (
        f"AutomationId {auto_id!r} 不在 UIA 树中。"
        f"可用 Id 数量={len(ids)}; 检查器是否仍命名为该 Id。"
    )


@pytest.mark.parametrize("class_name", KNOWN_BLIND_SPOTS)
def test_timeline_selfdrawn_controls_are_not_exposed(win, class_name):
    """
    时间轴自绘控件当前不在 UIA 树里。
    若此断言失败, 说明上游已补上自动化暴露 —— 应把"双击加帧/关键帧交互"
    从窗口消息注入迁移到 UIA 自动化。
    """
    wrapper_types = set()
    names = set()
    for d in _all_descendants(win):
        info = d.element_info
        wrapper_types.add(getattr(info, "control_type", "") or "")
        names.add(getattr(info, "class_name", "") or "")

    exposed = any(class_name.lower() in n.lower() for n in names | wrapper_types)
    assert not exposed, (
        f"{class_name} 现在已经暴露到 UIA 了 —— 请更新本文件, "
        "并将时间轴交互验证迁移到 UIA 路线。"
    )


def _report():
    win = _find_window()
    if win is None:
        print(f"未找到标题含 {WINDOW_KEYWORD!r} 的可见窗口")
        return 1

    ds = _all_descendants(win)
    ids = _auto_ids(win)
    print(f"窗口: {win.window_text()}  类名: {win.class_name()}")
    print(f"UIA 后代元素: {len(ds)}  带 AutomationId: {len(ids)}")

    print("\n按类型统计:")
    by_type = {}
    for d in ds:
        t = d.element_info.control_type
        by_type[t] = by_type.get(t, 0) + 1
    for t, n in sorted(by_type.items(), key=lambda kv: -kv[1]):
        print(f"  {t:<18} {n}")

    print("\n已知可见 Id 命中情况:")
    for aid in KNOWN_VISIBLE_IDS:
        print(f"  {'OK ' if aid in ids else 'MISS'} {aid}")

    print("\n已知盲区(应不可见):")
    names = {getattr(d.element_info, "class_name", "") or "" for d in ds}
    for c in KNOWN_BLIND_SPOTS:
        seen = any(c.lower() in n.lower() for n in names)
        print(f"  {'EXPOSED(!)' if seen else 'absent    '} {c}")

    return 0


if __name__ == "__main__":
    sys.exit(_report())
