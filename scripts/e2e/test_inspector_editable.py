"""
UIA 端到端验证: "选中关键帧后检查器必须可编辑" (bug #2 的回归保护)。

被测的失效模式
--------------
检查器由**播放头**驱动, 而"选中关键帧"曾经不移动播放头, 于是面板停在
"播放头不在关键帧上"的禁用态 —— 用户看到的就是"选了关键帧却改不了参数"。

为什么用 UIA 而不是截图/点击注入
--------------------------------
实测 (见 README): 时间轴是自绘控件, UIA 看不到其内部(所以看不到关键帧);
但**检查器的 `ValueInput` / `ValueSlider` 是原生控件, UIA 能读到 enabled 状态**。
于是可以把"不可编辑"这个症状直接做成断言 —— 不需要注入任何点击。

测试如何建立前置条件 (不依赖人工摆放播放头)
-------------------------------------------
1) 用走带时间框精确设到影片首个片段起点之前 → 断言禁用 (负面基线);
2) 设到首个片段起点 (含首关键帧) → 检查器出现关键帧列表;
3) 点列表项 → 走 `SelectKeyframe` + 播放头同步 → 断言可编辑。
第 2/3 步利用了本用例的 fixture 事实: showcase 影片首个片段从 32s 开始。

用法
----
    # 1) 启动应用
    Binaries\\Win64\\Debug\\IOStudio.exe --motion <含关键帧的 .motion>
    # 2) 跑断言
    C:\\Python311\\python.exe -m pytest scripts\\e2e\\test_inspector_editable.py -v
"""
import os
import re
import time
from pathlib import Path

import pytest
from pywinauto import Desktop

WINDOW_KEYWORD = os.environ.get("IOSTUDIO_WINDOW_KEYWORD", "showcase")
WINDOW_TIMEOUT = int(os.environ.get("IOSTUDIO_WINDOW_TIMEOUT", "25"))

VALUE_INPUT_ID = "ValueInput"
VALUE_SLIDER_ID = "ValueSlider"
PANEL_ID = "KfPropertyPanel"

# 影片路径: 用于从文件里**推导**首片段时间, 避免把常量写死后随文件/编辑漂移。
#
# 注意: 必须指向**已跟踪的源文件**, 不能用部署目录的副本 ——
# 部署副本会被应用"自动保存"改写 (例如双击加帧会扩展片段范围),
# 一旦指向它, 派生常量就会随测试自身的副作用漂移, 断言忽真忽假。
MOTION_PATH = Path(
    os.environ.get(
        "IOSTUDIO_MOTION",
        str(
            Path(__file__).resolve().parents[2]
            / "IOStudio"
            / "Config"
            / "Motion"
            / "showcase_10min.motion"
        ),
    )
)


def _first_clip_span(path=MOTION_PATH):
    """
    从 .motion 里取第一条片段的时间范围 (毫秒)。

    测试常量必须来自**文件本身**, 否则一旦影片被编辑过(例如双击加帧改变了片段范围),
    写死的常量就会指向错误位置, 导致断言忽真忽假。
    """
    try:
        text = path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return None
    m = re.search(r'"clips"\s*:\s*\[\s*\{[^}]*?"start_ms"\s*:\s*(\d+)[^}]*?"end_ms"\s*:\s*(\d+)', text)
    if not m:
        return None
    return float(m.group(1)), float(m.group(2))


_SPAN = _first_clip_span()
if _SPAN is None:
    # 文件不可读时退回环境变量/默认 (仅用于无法读取文件的场景)
    FIRST_CLIP_MS = float(os.environ.get("IOSTUDIO_FIRST_CLIP_MS", "27400"))
    CLIP_END_MS = float(os.environ.get("IOSTUDIO_CLIP_END_MS", "30400"))
else:
    FIRST_CLIP_MS, CLIP_END_MS = _SPAN

# 首片段之前 (必为空窗 -> 禁用态)
GAP_MS = max(0.0, FIRST_CLIP_MS - 2000.0)
# 片段内、且刻意远离首尾关键帧的中点 (首/末关键帧通常贴片段两端)
MID_CLIP_MS = (FIRST_CLIP_MS + CLIP_END_MS) / 2.0


# ─────────────────────────── 基础设施 ───────────────────────────

def _find_window(keyword=WINDOW_KEYWORD, timeout=WINDOW_TIMEOUT):
    # 用 Win32 按标题找句柄, 避免枚举整个桌面 (某些环境下极慢, 实测 212s)
    from uia_win import find_window as _fw

    return _fw(keyword, timeout)


def _by_id(win, auto_id):
    """按 AutomationId 找第一个后代控件。"""
    for d in win.descendants():
        if (getattr(d.element_info, "automation_id", "") or "") == auto_id:
            return d
    return None


def _list_items(win):
    """关键帧列表项 (ListItem)。"""
    return [
        d
        for d in win.descendants()
        if d.element_info.control_type == "ListItem" and d.is_visible()
    ]


def _wait_until(fn, timeout=6.0, interval=0.2):
    """轮询条件 —— UIA 状态刷新有延迟, 不要用固定 sleep 做同步。"""
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            if fn():
                return True
        except Exception:
            pass
        time.sleep(interval)
    return False


def _open_time_editor(win):
    """
    打开走带时间编辑框; 返回控件或 None。

    先把窗口置前: 若同一进程另有窗口(如设备调试工作台)抢占了焦点,
    双击事件会因为窗口未处于前台而丢失 —— 这属于环境干扰, 不是被测缺陷。
    """
    try:
        win.set_focus()
    except Exception:
        pass
    time.sleep(0.15)

    box = _by_id(win, "CurrentTimeEditBox")
    if box is not None and box.is_visible():
        return box

    disp = _by_id(win, "CurrentTimeDisplayText")
    if disp is None:
        return None

    # 先单击让控件获得焦点, 再双击 —— Avalonia 的 DoubleTapped 需要两次点击且聚焦稳定
    for _ in range(3):
        try:
            disp.click_input()
            time.sleep(0.15)
            disp.double_click_input()
        except Exception:
            pass
        time.sleep(0.6)
        box = _by_id(win, "CurrentTimeEditBox")
        if box is not None and box.is_visible():
            return box

    return None


def _set_playhead(win, ms, tolerance_ms=1.0, snap_tolerance_ms=700.0):
    """
    用走带时间框精确设置播放头 (双击 → 输入秒数 → 回车)。

    两点注意:
    1. 双击开关偶发不生效 (UIA 焦点竞争), 故重试 —— 测试侧稳健性, 不应造成假失败。
    2. **`SeekTo` 会吸附到附近关键帧**, 所以只要落在吸附半径内即算成功;
       目标时间必须刻意远离关键帧, 否则会被吸走 (这不是缺陷, 是设计行为)。
    """
    for _ in range(3):
        box = _open_time_editor(win)
        if box is None:
            time.sleep(0.5)
            continue

        try:
            box.set_edit_text(f"{ms / 1000.0:.3f}")
            time.sleep(0.2)
            box.type_keys("{ENTER}")
        except Exception:
            time.sleep(0.5)
            continue

        if _wait_until(lambda: abs(_current_ms(win) - ms) < snap_tolerance_ms, timeout=4.0):
            return True

        time.sleep(0.5)

    return False


def _current_ms(win):
    t = _by_id(win, "CurrentTimeDisplayText")
    if t is None:
        return -1.0
    text = t.element_info.name or ""
    # 格式 mm:ss.fff
    try:
        mm, rest = text.split(":")
        return (int(mm) * 60 + float(rest)) * 1000.0
    except Exception:
        return -1.0


@pytest.fixture(scope="module")
def win():
    w = _find_window()
    if w is None:
        pytest.skip(
            f"未找到标题含 {WINDOW_KEYWORD!r} 的可见 IOStudio 窗口。请先启动 "
            "--motion <含关键帧的影片>, 必要时设置 IOSTUDIO_WINDOW_KEYWORD。"
        )
    return w


# ─────────────────────────── 断言 ───────────────────────────

def test_inspector_value_controls_exist(win):
    """前提: 检查器数值控件确实在 UIA 树里 (定位能力)。"""
    assert _by_id(win, PANEL_ID) is not None, f"缺少 {PANEL_ID}"
    assert _by_id(win, VALUE_INPUT_ID) is not None, f"缺少 {VALUE_INPUT_ID}"
    assert _by_id(win, VALUE_SLIDER_ID) is not None, f"缺少 {VALUE_SLIDER_ID}"


def test_value_controls_are_disabled_when_playhead_not_on_keyframe(win):
    """
    负面基线: 播放头落在首片段之前的空窗时, 数值控件必须禁用。
    没有这条, 后面"选中后变可编辑"就失去判别力。
    """
    assert _set_playhead(win, GAP_MS), f"无法把播放头设到 {GAP_MS}ms"

    assert _wait_until(
        lambda: not _by_id(win, VALUE_INPUT_ID).is_enabled(), timeout=5.0
    ), (
        f"播放头在 {GAP_MS}ms (首片段之前), 但数值框仍可编辑 —— "
        "禁用逻辑失效, 后续断言将失去意义。"
    )
    assert not _by_id(win, VALUE_SLIDER_ID).is_enabled()


def test_selecting_keyframe_makes_value_editable(win):
    """
    核心回归: 播放头落在关键帧上时, 数值控件必须可编辑。

    覆盖 bug #2 的完整因果:
      播放头在关键帧上 -> 检查器识别 -> 脱离"不在关键帧上"的禁用态。
    """
    # 先制造负面状态 (同时也是"变可编辑不是因为本来就可编辑"的保证)
    assert _set_playhead(win, GAP_MS), f"无法把播放头设到 {GAP_MS}ms"
    assert _wait_until(
        lambda: not _by_id(win, VALUE_INPUT_ID).is_enabled(), timeout=5.0
    ), "前置的禁用态未建立"

    # 移到首片段起点 —— 该处存在首关键帧
    assert _set_playhead(win, FIRST_CLIP_MS), f"无法把播放头设到 {FIRST_CLIP_MS}ms"

    became_editable = _wait_until(
        lambda: _by_id(win, VALUE_INPUT_ID).is_enabled()
        and _by_id(win, VALUE_SLIDER_ID).is_enabled(),
        timeout=8.0,
    )

    items = len(_list_items(win))
    assert became_editable, (
        "播放头落在首关键帧上后检查器仍未变为可编辑 —— "
        f"ValueInput.enabled={_by_id(win, VALUE_INPUT_ID).is_enabled()}, "
        f"ValueSlider.enabled={_by_id(win, VALUE_SLIDER_ID).is_enabled()}, "
        f"关键帧列表项={items}, 当前时间={_current_ms(win)}ms。"
    )


def test_inspector_recognizes_clip_membership_off_keyframe(win):
    """
    回归: 播放头落在片段内但**不在关键帧上**时, 检查器必须仍然认得"在片段内"。

    曾经的缺陷: clipIdx 只在恰好命中关键帧时才赋值, 于是在片段中间时
    ClipIndex<0, 面板误报"播放头不在片段内", 关键帧列表也不显示 ——
    用户因此根本无法从列表里点选关键帧。
    """
    assert _set_playhead(win, MID_CLIP_MS), f"无法把播放头设到片段内 {MID_CLIP_MS}ms"

    # 片段内 -> 应识别到片段 (表现为关键帧列表非空 / 不再显示"不在片段内")
    assert _wait_until(lambda: len(_list_items(win)) > 0, timeout=6.0), (
        f"播放头在 {_current_ms(win):.0f}ms (片段 [{FIRST_CLIP_MS:.0f},35000] 内), "
        "但检查器没有关键帧列表 —— 说明它误判为'播放头不在片段内'。"
    )

    # 并且此时应仍为禁用 (不在关键帧上) —— 与"可编辑"互斥, 说明判定是精确的
    assert not _by_id(win, VALUE_INPUT_ID).is_enabled(), (
        "播放头不在关键帧上却可编辑 —— 判定过于宽松"
    )


def _click_effective_keyframe_list_item(win):
    """
    点检查器关键帧列表里一个**会真正生效**的项。

    关键: 列表中被标为"当前项"(IsCurrent) 的那一条再点一次是**无操作**
    (播放头已在其上, 面板无需切换)。所以不能盲点 items[0] ——
    若恰好命中当前项, 就会误判为"点选无效"。
    做法: 从播放头位于片段中间(无当前项)开始, 依次尝试前几项, 直到面板变为可编辑。
    """
    items = _list_items(win)
    if not items:
        return False

    for i in range(min(len(items), 3)):
        items[i].click_input()
        if _wait_until(lambda: _by_id(win, VALUE_INPUT_ID).is_enabled(), timeout=4.0):
            return True
    return False


def test_clicking_keyframe_list_item_selects_that_keyframe(win):
    """
    应用自身的"选中关键帧"入口 (检查器关键帧列表点击) 必须:
      1) 把播放头移到该关键帧 (列表项自带绝对时间);
      2) 使检查器变为可编辑。
    """
    assert _set_playhead(win, MID_CLIP_MS), f"无法把播放头设到 {MID_CLIP_MS}ms"

    if not _list_items(win):
        pytest.fail(
            f"{MID_CLIP_MS}ms 处检查器没有关键帧列表项 —— "
            "见 test_inspector_recognizes_clip_membership_off_keyframe。"
        )

    clicked = _click_effective_keyframe_list_item(win)

    assert clicked and _by_id(win, VALUE_INPUT_ID).is_enabled(), (
        "点击关键帧列表项后数值控件未变为可编辑 —— "
        f"ValueInput.enabled={_by_id(win, VALUE_INPUT_ID).is_enabled()}, "
        f"ValueSlider.enabled={_by_id(win, VALUE_SLIDER_ID).is_enabled()}, "
        f"当前时间={_current_ms(win):.0f}ms。"
    )


def test_value_input_accepts_write_after_selection(win):
    """
    端到端最后一环: 可编辑不只是"变绿", 要真的能写进去。
    """
    assert _set_playhead(win, MID_CLIP_MS), f"无法把播放头设到 {MID_CLIP_MS}ms"

    if not _list_items(win):
        pytest.fail("检查器没有关键帧列表项, 无法点选")

    if not _click_effective_keyframe_list_item(win):
        pytest.fail("点选关键帧后数值框仍不可编辑 —— 先修上一条测试")

    edit = _by_id(win, VALUE_INPUT_ID)
    original = edit.get_value()
    probe = "0.457"
    try:
        edit.set_edit_text(probe)
        write_ok = _wait_until(
            lambda: str(_by_id(win, VALUE_INPUT_ID).get_value()).startswith("0.45"),
            timeout=5.0,
        )
    finally:
        # 还原, 避免污染后续断言/用户现场
        try:
            edit.set_edit_text(original)
        except Exception:
            pass

    assert write_ok, f"写入后读回不一致 (原值={original!r})"


def test_typed_time_is_not_rewritten_by_snap(win):
    """
    回归: 走带时间框输入的是**精确**时间, 不应被吸附改写。

    曾经的缺陷: 提交走 ViewModel.SeekTo, 而吸附半径是"10 像素"换算的时间
    (缩小时可达 1 秒以上), 于是输入 33.500 会被吸到 32.600, 用户无法定位到
    片段中间的任意时间点。
    """
    # 取一个远离所有关键帧的片段内时刻 (首片段关键帧: 32000/32600/34400/35000)
    target = MID_CLIP_MS
    assert _set_playhead(win, target), f"无法把播放头设到 {target}ms"

    actual = _current_ms(win)
    assert abs(actual - target) <= 1.0, (
        f"输入 {target:.0f}ms 后实际为 {actual:.0f}ms —— "
        "被吸附改写了 (精确输入不应受吸附影响)。"
    )
