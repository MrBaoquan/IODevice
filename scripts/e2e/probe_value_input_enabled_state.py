"""
证明 UIA 不只是"看得见", 而且"能操作" —— 针对本次修复的检查器数值框。

若 ValueInput 可写, 那么"选中关键帧 -> 检查器可编辑"这条链路的
最后一步(把值写进去)就能用 UIA 自动化, 不必依赖窗口消息注入。
"""
import os
import sys
import time

from pywinauto import Desktop

WINDOW_KEYWORD = os.environ.get("IOSTUDIO_WINDOW_KEYWORD", "showcase")


def find_window(keyword=WINDOW_KEYWORD, timeout=20):
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


def main():
    win = find_window()
    if win is None:
        print("窗口未找到")
        return 1

    target = None
    for d in win.descendants():
        if getattr(d.element_info, "automation_id", None) == "ValueInput":
            target = d
            break
    if target is None:
        print("ValueInput 不在 UIA 树中")
        return 2
    edit = target

    info = edit.element_info
    print(f"ValueInput found: control_type={info.control_type}")
    print(f"  enabled     = {edit.is_enabled()}")
    try:
        print(f"  read-only   = {edit.is_read_only()}")
    except Exception as e:
        print(f"  read-only   = 读取失败: {type(e).__name__}")

    # 读当前值 (ValuePattern)
    try:
        original = edit.get_value()
        print(f"  current     = {original!r}")
    except Exception as e:
        print(f"  current     = 读取失败: {type(e).__name__}: {e}")
        original = None

    # 尝试写入 (ValuePattern.SetValue) —— 这是 pywinauto 能否驱动它的关键
    probe_value = "0.512"
    writable = False
    try:
        edit.set_edit_text(probe_value)
        time.sleep(0.4)
        after = edit.get_value()
        print(f"  after write = {after!r}")
        writable = after == probe_value or str(after).startswith("0.51")
    except Exception as e:
        print(f"  写入失败: {type(e).__name__}: {e}")

    print()
    print("结论: ValueInput " + ("可被 UIA 读写 (pywinauto 可驱动)" if writable else "不可写 (需真实键盘/窗口消息)"))
    if original is not None:
        try:
            edit.set_edit_text(original)
            print(f"已还原为 {original!r}")
        except Exception:
            pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
