"""
时间轴「双击添加关键帧」端到端验证 (窗口消息注入)。

为什么不能走 UIA
----------------
时间轴 `TrackClipControl` 是**自绘控件**: UIA 看不到它的内部, 而且实测
**UIA 的合成点击也送不到它** (在画布上 `click_input` 后播放头不动)。
因此这条链路只能用真实的窗口消息注入 (PostMessage) —— 它能进 Avalonia 的输入管线。

本机环境注意
------------
本机交互桌面**不渲染** (全屏截图为白屏), 但 PostMessage 仍可达并被处理,
所以本套件不依赖截图, 全程以**应用日志**为行为证据。

判定依据 (应用自身写出的日志)
-----------------------------
复现双击时会成对出现:
    [Action] 双击添加关键帧 | <轨道> @ <ms>     ← 手势送达 TrackClipControl
    [Action] 执行 | 添加关键帧                   ← 真正落到模型 (经撤销栈)
只看第二条无法区分"手势没送到"与"被片段/锁定规则拒绝"; 两者都出现才是完整成功。

用法
----
    # 应用需以含关键帧的影片启动 (日志会写到 Binaries\\Win64\\Debug\\Logs)
    C:\\Python311\\python.exe -m pytest scripts\\e2e\\test_timeline_double_click.py -v
"""
import ctypes
import os
import re
import time
from ctypes import wintypes
from pathlib import Path

import pytest

WINDOW_KEYWORD = os.environ.get("IOSTUDIO_WINDOW_KEYWORD", "showcase")
# 应用部署目录 (含 Logs/); 默认按仓库结构推断
DEPLOY_DIR = Path(
    os.environ.get(
        "IOSTUDIO_DEPLOY_DIR",
        str(Path(__file__).resolve().parents[2] / "Binaries" / "Win64" / "Debug"),
    )
)
LOG_DIR = DEPLOY_DIR / "Logs"

# 坐标 (客户区): 时间轴首条轨道行 y≈314 (窗口 1456x939 实测), x 取一个落在片段内的位置
TARGET_X = int(os.environ.get("IOSTUDIO_CLICK_X", "560"))
TARGET_Y = int(os.environ.get("IOSTUDIO_CLICK_Y", "314"))

# ── Win32 ──
user32 = ctypes.WinDLL("user32", use_last_error=True)

WM_MOUSEMOVE = 0x0200
WM_LBUTTONDOWN = 0x0201
WM_LBUTTONUP = 0x0202
SW_RESTORE = 9

user32.EnumWindows.argtypes = [ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM), wintypes.LPARAM]
user32.EnumWindows.restype = wintypes.BOOL
user32.IsWindowVisible.argtypes = [wintypes.HWND]
user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
user32.GetWindowTextLengthW.argtypes = [wintypes.HWND]
user32.ShowWindow.argtypes = [wintypes.HWND, ctypes.c_int]
user32.GetClientRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
user32.ClientToScreen.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.POINT)]
user32.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]


user32.IsWindow.argtypes = [wintypes.HWND]
user32.IsWindow.restype = wintypes.BOOL


def _find_window(keyword, timeout=25):
    """按标题关键字找可见顶层窗口句柄。"""
    found = []
    deadline = time.time() + timeout
    while time.time() < deadline:
        found.clear()

        @ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
        def _cb(hwnd, _lparam):
            if not user32.IsWindowVisible(hwnd):
                return True
            n = user32.GetWindowTextLengthW(hwnd)
            if n <= 0:
                return True
            buf = ctypes.create_unicode_buffer(n + 1)
            user32.GetWindowTextW(hwnd, buf, n + 1)
            if keyword.lower() in buf.value.lower():
                found.append(hwnd)
            return True

        user32.EnumWindows(_cb, 0)
        if found:
            return found[0]
        time.sleep(0.5)
    return None


def _lparam(x, y):
    return (y << 16) | (x & 0xFFFF)


def _double_click(hwnd, x, y):
    """在客户区坐标 (x,y) 处投递一次双击。"""
    user32.ShowWindow(hwnd, SW_RESTORE)
    time.sleep(0.3)
    user32.PostMessageW(hwnd, WM_MOUSEMOVE, 0, _lparam(x, y))
    time.sleep(0.12)
    for _ in range(2):
        user32.PostMessageW(hwnd, WM_LBUTTONDOWN, 1, _lparam(x, y))
        user32.PostMessageW(hwnd, WM_LBUTTONUP, 0, _lparam(x, y))
        time.sleep(0.07)


def _today_log():
    """当天的 UI 日志文件 (NLog ${shortdate} 为 yyyy-MM-dd)。"""
    return LOG_DIR / f"IOStudio-{time.strftime('%Y-%m-%d')}.log"


def _read_log():
    p = _today_log()
    if not p.exists():
        return ""
    try:
        return p.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return ""


def _wait_for(pattern, since_len, timeout=8.0):
    """等待日志中(相对于 since_len 之后)出现匹配。"""
    deadline = time.time() + timeout
    last = ""
    while time.time() < deadline:
        text = _read_log()[since_len:]
        m = re.search(pattern, text)
        if m:
            return m
        last = text
        time.sleep(0.25)
    return None


@pytest.fixture(scope="module")
def hwnd():
    h = _find_window(WINDOW_KEYWORD)
    if h is None:
        pytest.skip(f"未找到标题含 {WINDOW_KEYWORD!r} 的可见 IOStudio 窗口。")
    return h


def _assert_alive(hwnd):
    """句柄可能在测试之间失效 (应用重启/关闭) —— 直接给出可读的失败原因。"""
    if not user32.IsWindow(hwnd):
        pytest.fail(
            "目标窗口句柄已失效 (应用可能被重启或关闭)。请保持 IOStudio 运行后重跑。"
        )


@pytest.fixture(scope="module")
def log_ready():
    """日志必须可用 —— 否则本套件没有判定依据。"""
    if not LOG_DIR.exists():
        pytest.skip(f"日志目录不存在: {LOG_DIR} (设置 IOSTUDIO_DEPLOY_DIR)")
    if not _today_log().exists():
        pytest.skip(f"今天的日志不存在: {_today_log()}")
    return True


# ─────────────────────────── 断言 ───────────────────────────

def test_window_is_addressable(hwnd):
    """前置: 能找到并操作目标窗口 (恢复显示 + 拿到客户区尺寸)。"""
    rc = wintypes.RECT()
    assert user32.GetClientRect(hwnd, ctypes.byref(rc)), "GetClientRect 失败"
    w, h = rc.right - rc.left, rc.bottom - rc.top
    assert w > 400 and h > 300, f"客户区异常: {w}x{h}"
    # 点击目标必须落在客户区内
    assert 0 <= TARGET_X < w, f"TARGET_X={TARGET_X} 超出客户区宽度 {w}"
    assert 0 <= TARGET_Y < h, f"TARGET_Y={TARGET_Y} 超出客户区高度 {h}"


def test_double_click_on_timeline_adds_keyframe(hwnd, log_ready):
    """
    核心: 在时间轴轨道行上双击, 必须同时出现
      - [Action] 双击添加关键帧  (手势送达了自绘控件)
      - [Action] 执行 | 添加关键帧 (真正落到模型)
    缺前者 = 输入没送到; 缺后者 = 被片段/锁定规则拒绝。两者都要有。
    """
    _assert_alive(hwnd)
    start = len(_read_log())
    _double_click(hwnd, TARGET_X, TARGET_Y)

    got_request = _wait_for(r"\[Action\] 双击添加关键帧", start, timeout=6.0)
    assert got_request is not None, (
        f"双击后日志未出现『双击添加关键帧』—— 输入没送到 TrackClipControl。"
        f"(点击位置 客户区({TARGET_X},{TARGET_Y}); 日志 {_today_log()})"
    )

    # 请求之后应紧跟"执行"（同一事件里同步发生, 给稍长窗口）
    got_exec = _wait_for(r"\[Action\] 执行 \| 添加关键帧", start, timeout=6.0)
    assert got_exec is not None, (
        f"手势已送达(『{got_request.group(0)}』)但未产生『执行 | 添加关键帧』—— "
        "被片段范围或轨道锁定规则拒绝了。"
    )


def test_double_click_logs_target_track_and_time(hwnd, log_ready):
    """
    请求日志应带上"落在哪条轨道、什么时间", 便于排查
    —— 这是可观测性本身的一条约束。
    """
    _assert_alive(hwnd)
    start = len(_read_log())

    # 换一个横向位置, 避免与上一条测试落在同一时刻 (重复落点可能被去重/合并)
    x = TARGET_X + 90
    rc = wintypes.RECT()
    user32.GetClientRect(hwnd, ctypes.byref(rc))
    if x >= rc.right:
        x = TARGET_X - 90
    _double_click(hwnd, x, TARGET_Y)

    m = _wait_for(r"\[Action\] 双击添加关键帧 \| (.+?) @ (\d+)ms", start, timeout=6.0)
    assert m is not None, (
        f"未记录到带轨道/时间的双击日志 (点击位置 客户区({x},{TARGET_Y}))"
    )

    track_label, ms = m.group(1).strip(), int(m.group(2))
    assert track_label, "日志缺少轨道名"
    assert ms >= 0, f"落点时间异常: {ms}"


def test_single_click_does_not_add_keyframe(hwnd, log_ready):
    """
    边界: 单击**不得**触发加帧 (避免误操作)。
    """
    _assert_alive(hwnd)
    start = len(_read_log())
    user32.PostMessageW(hwnd, WM_MOUSEMOVE, 0, _lparam(TARGET_X, TARGET_Y))
    time.sleep(0.12)
    user32.PostMessageW(hwnd, WM_LBUTTONDOWN, 1, _lparam(TARGET_X, TARGET_Y))
    user32.PostMessageW(hwnd, WM_LBUTTONUP, 0, _lparam(TARGET_X, TARGET_Y))
    time.sleep(2.0)

    after = _read_log()[start:]
    assert "双击添加关键帧" not in after, (
        "单击触发了『双击添加关键帧』—— 双击判定过于宽松"
    )
