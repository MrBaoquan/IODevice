"""UIA 窗口定位助手。

为什么不直接用 `Desktop(backend='uia').windows()`:
  该调用会枚举**所有**顶层窗口的 UIA 树。某些环境里存在巨型 UIA 窗口
  (实测本机 21 个窗口耗时 212 秒), 会让每个用例莫名其妙地慢 1~2 个数量级。

做法: 先用 Win32 `EnumWindows` 按标题定位 HWND (毫秒级), 再包成 UIA wrapper。
窗口内部的 `descendants()` 依然很快 (实测 ~350 元素 / 400ms)。
"""
import ctypes
import time
from ctypes import wintypes

from pywinauto import Desktop

user32 = ctypes.WinDLL("user32", use_last_error=True)

user32.EnumWindows.argtypes = [
    ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM),
    wintypes.LPARAM,
]
user32.EnumWindows.restype = wintypes.BOOL
user32.IsWindowVisible.argtypes = [wintypes.HWND]
user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
user32.GetWindowTextLengthW.argtypes = [wintypes.HWND]
user32.IsWindow.argtypes = [wintypes.HWND]


def find_handles(keyword):
    """按标题子串返回所有可见顶层窗口句柄。"""
    found = []
    kw = keyword.lower()

    @ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    def _cb(hwnd, _lparam):
        if not user32.IsWindowVisible(hwnd):
            return True
        n = user32.GetWindowTextLengthW(hwnd)
        if n <= 0:
            return True
        buf = ctypes.create_unicode_buffer(n + 1)
        user32.GetWindowTextW(hwnd, buf, n + 1)
        if kw in buf.value.lower():
            found.append(hwnd)
        return True

    user32.EnumWindows(_cb, 0)
    return found


def find_window(keyword, timeout=25):
    """等待并返回 UIA wrapper (失败返回 None)。"""
    deadline = time.time() + timeout
    while time.time() < deadline:
        for h in find_handles(keyword):
            try:
                return Desktop(backend="uia").window(handle=h)
            except Exception:
                continue
        time.sleep(0.5)
    return None
