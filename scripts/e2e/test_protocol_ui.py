"""
协议测试台 UI 的端到端验收 (真实桌面 + UIA)。

为什么必须单独做这个:
    协议测试台的价值在于"人在 UI 侧看到收到的报文"。这条链路
    (后台收帧 → Dispatcher.UIThread.Post → 绑定集合) 出错时, CLI 门禁
    依然全绿, 只有 UI 是空的 —— 而 Headless 会话下无法可靠断言
    (Dispatch 只泵调用期间入队的任务, 线程池 Post 回来的回调常常不执行, 会 flaky)。
    所以这里用真实桌面驱动: 打开窗口 → 开始监听 → 外部发 UDP → 读列表断言。

用法:
    1) 启动应用:  Binaries\\Win64\\Debug\\IOStudio.exe        (纯 GUI)
    2) 跑断言:    C:\\Python311\\python.exe -m pytest scripts\\e2e\\test_protocol_ui.py -v
    未启动时本用例会自动拉起应用。
"""
import os
import socket
import subprocess
import sys
import time
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent))
from uia_win import find_window  # noqa: E402
from pywinauto import Desktop  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
DEPLOY = Path(
    os.environ.get("IOSTUDIO_DEPLOY_DIR", str(ROOT / "Binaries" / "Win64" / "Debug"))
)
EXE = DEPLOY / "IOStudio.exe"

MAIN_KEYWORD = "IOStudio"
TEST_WINDOW_TITLE = "协议测试台"
WINDOW_TIMEOUT = int(os.environ.get("IOSTUDIO_WINDOW_TIMEOUT", "40"))

PAYLOAD = "PING-FROM-UI-TEST"


# ─────────────────────────── 基础设施 ───────────────────────────

def _by_id(win, auto_id):
    for d in win.descendants():
        if (getattr(d.element_info, "automation_id", "") or "") == auto_id:
            return d
    return None


def _menu_items(win):
    return [
        d
        for d in win.descendants()
        if d.element_info.control_type == "MenuItem"
    ]


def _wait_until(fn, timeout=8.0, interval=0.25):
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            if fn():
                return True
        except Exception:
            pass
        time.sleep(interval)
    return False


def _free_udp_port():
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.close()
    return port


def _open_protocol_window(main):
    """通过「工作区 → 协议测试台」菜单打开窗口。

    失败重试: 连跑其它 UI 用例时, 残留窗口会抢焦点导致菜单未展开 ——
    属环境干扰而非被测缺陷, 测试侧必须重试而不是直接失败。
    """
    for _ in range(4):
        main.set_focus()
        time.sleep(0.5)

        tools = None
        for d in _menu_items(main):
            if "工作区" in (d.element_info.name or ""):
                tools = d
                break
        assert tools is not None, "未找到「工作区」菜单"

        try:
            tools.click_input()
        except Exception:
            pass
        time.sleep(0.8)

        item = None
        for d in _menu_items(main):
            if (d.element_info.name or "") == TEST_WINDOW_TITLE:
                item = d
                break

        if item is not None:
            item.click_input()
            time.sleep(1.2)
            return

        # 菜单没展开: 先把焦点收回来再试
        try:
            main.set_focus()
        except Exception:
            pass
        time.sleep(0.6)

    raise AssertionError("「工作区」菜单里没有「协议测试台」 (重试 4 次仍未展开)")


def _row_texts(win):
    """把消息列表每行拼成一个字符串 (ListItem 自身 name 可能为空, 取其后代文本)。"""
    rows = []
    for d in win.descendants():
        if d.element_info.control_type != "ListItem":
            continue
        parts = [d.element_info.name or ""]
        try:
            for c in d.descendants():
                if c.element_info.control_type == "Text":
                    parts.append(c.element_info.name or "")
        except Exception:
            pass
        rows.append(" ".join(p for p in parts if p))
    return rows


def _session_count(win):
    """会话列表里的会话数 (只数会话列表的 ListItem, 别把消息列表算进来)。"""
    lst = _by_id(win, "ProtocolSessionList")
    if lst is None:
        return -1
    return sum(
        1 for d in lst.descendants() if d.element_info.control_type == "ListItem"
    )


def _set_port(win, port):
    """给端口输入框设值 (TextBox: Avalonia 的 TextBox.Text 绑定在输入时即时提交)。"""
    ctrl = _by_id(win, "ProtocolPort")
    assert ctrl is not None, "未找到端口控件 (ProtocolPort)"

    try:
        ctrl.set_edit_text(str(port))
    except Exception:
        ctrl.set_focus()
        time.sleep(0.2)
        ctrl.type_keys("^a")
        ctrl.type_keys(str(port))
    time.sleep(0.3)


# ─────────────────────────── 用例 ───────────────────────────

@pytest.fixture(scope="module")
def app():
    if not EXE.exists():
        pytest.skip(f"未找到 {EXE}")

    # 清掉上次保存的工作区: 否则"默认会话"会带着上次的传输/端口, 断言不再确定
    ws = DEPLOY / "Config" / "ProtocolTestWorkspace.json"
    try:
        ws.unlink()
    except OSError:
        pass

    # 等前序用例(如 YDL 播放)的进程真正退出: 残留窗口会抢焦点
    time.sleep(2.0)

    proc = subprocess.Popen([str(EXE)], cwd=str(DEPLOY))
    main = find_window(MAIN_KEYWORD, timeout=WINDOW_TIMEOUT)
    if main is None:
        proc.kill()
        pytest.fail("主窗口未出现 (应用启动失败?)")

    time.sleep(2.0)  # 等设备加载完, 避免与窗口打开抢焦点
    yield {"proc": proc, "main": main}

    try:
        proc.kill()
    except Exception:
        pass


def test_menu_opens_protocol_window_and_shows_received_datagram(app):
    """
    端到端: 菜单打开窗口 → 设端口 → 开始监听 → 外部发 UDP → 列表出现该报文。
    """
    main = app["main"]
    _open_protocol_window(main)

    win = find_window(TEST_WINDOW_TITLE, timeout=15)
    assert win is not None, "协议测试台窗口未打开"

    port = _free_udp_port()
    _set_port(win, port)

    # 传输默认就是「UDP 监听」(列表第一项), 组帧默认「无组帧」
    start = _by_id(win, "ProtocolStart")
    assert start is not None, "未找到开始按钮 (ProtocolStart)"
    start.click_input()
    time.sleep(1.0)

    # 反复发送: 避免"监听尚未就绪"的时序竞争 (UDP 无握手, 早发的会被丢弃)
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        def received():
            rows = _row_texts(win)
            return any(PAYLOAD in r for r in rows)

        ok = False
        deadline = time.time() + 10.0
        while time.time() < deadline:
            sock.sendto(PAYLOAD.encode("utf-8"), ("127.0.0.1", port))
            if _wait_until(received, timeout=1.0, interval=0.25):
                ok = True
                break
    finally:
        sock.close()

    rows = _row_texts(win)
    assert ok, f"UI 列表未出现收到的报文 {PAYLOAD!r}; 当前列表: {rows}"

    # 该行必须被标为「收」且带 HEX (证明是真正解析出的一帧, 而不是回显/占位)
    line = next(r for r in rows if PAYLOAD in r)
    assert "收" in line, f"收到的行未标记方向: {line!r}"
    assert PAYLOAD.encode("utf-8").hex().upper() in line.upper(), f"该行缺少 HEX: {line!r}"

    stop = _by_id(win, "ProtocolStop")
    if stop is not None:
        stop.click_input()


def test_protocol_window_exposes_send_controls(app):
    """窗口必须同时具备发送能力 (现场常需要回一条报文试探对端)。"""
    win = find_window(TEST_WINDOW_TITLE, timeout=10)
    if win is None:
        pytest.skip("协议测试台窗口未打开 (依赖上一个用例)")
    for auto_id in ("ProtocolSendText", "ProtocolSend", "ProtocolSendEncoding", "ProtocolMessages"):
        assert _by_id(win, auto_id) is not None, f"缺少控件 {auto_id}"


def test_new_session_adds_parallel_session(app):
    """通用调试台的核心: 可并行开多路会话 (TCP/UDP/WebSocket/串口 各自独立)。"""
    win = find_window(TEST_WINDOW_TITLE, timeout=10)
    if win is None:
        pytest.skip("协议测试台窗口未打开 (依赖前面的用例)")

    before = _session_count(win)
    assert before >= 1, f"会话列表未渲染: {before}"

    new_btn = _by_id(win, "ProtocolNewSession")
    assert new_btn is not None, "未找到「新建会话」按钮"
    new_btn.click_input()

    assert _wait_until(lambda: _session_count(win) == before + 1, timeout=6.0), (
        f"新建会话后会话数未增加: before={before} after={_session_count(win)}"
    )


def test_protocol_single_transport_selector(app):
    """批次 B「IA 收口」: 传输类型只保留**一个**下拉 (连接带里的 ProtocolTransport)。

    此前工具条上还有一个「新建会话的传输」下拉 (ProtocolNewTransport), 与连接带
    的「传输」并列可见, 用户分不清该用哪个。新建会话现在改为沿用当前会话的类型,
    工具条不再需要专用下拉 —— 这里把它锁死, 防止回归。
    """
    win = find_window(TEST_WINDOW_TITLE, timeout=10)
    if win is None:
        pytest.skip("协议测试台窗口未打开 (依赖前面的用例)")

    assert _by_id(win, "ProtocolTransport") is not None, "连接带缺少「传输」下拉"
    assert _by_id(win, "ProtocolNewTransport") is None, (
        "工具条的重复「传输」下拉 (ProtocolNewTransport) 不应再存在"
    )


def test_protocol_batch_a_affordances(app):
    """批次 A「读与发顺手」: 行详情面板 + 方向过滤 + 发送字节预览。

    这条路径的价值在"人把收到的帧看懂 / 回得出去": 选中一行必须能拿到
    可复制的文本与 HEX、能从这一帧重发; 发送前必须能看出会发多少字节。
    Headless 侧已断言 VM 语义, 这里只验真实控件确实按预期出现/联动
    (自动化树里不出现 = 用户根本点不到)。
    """
    win = find_window(TEST_WINDOW_TITLE, timeout=10)
    if win is None:
        pytest.skip("协议测试台窗口未打开 (依赖前面的用例)")

    # 方向过滤 / 发送框清空 / 字节预览 —— 三个新控件的存在性
    for auto_id in ("ProtocolFilterDirection", "ProtocolClearSend", "ProtocolSendStatus"):
        assert _by_id(win, auto_id) is not None, f"缺少批次 A 控件 {auto_id}"

    # 选中一行日志 → 底部详情面板出现 (未选中时这些按钮不在自动化树里)
    lst = _by_id(win, "ProtocolMessages")
    assert lst is not None, "未找到日志列表 (ProtocolMessages)"
    rows = [
        d for d in lst.descendants() if d.element_info.control_type == "ListItem"
    ]
    assert rows, "日志列表为空 (依赖第一个用例先收到报文)"
    rows[-1].click_input()
    time.sleep(0.5)

    assert _wait_until(
        lambda: _by_id(win, "ProtocolCopyText") is not None, timeout=4.0
    ), "选中日志行后未出现详情面板 (ProtocolCopyText)"
    assert _by_id(win, "ProtocolCopyHex") is not None, "详情面板缺少「复制 HEX」"

    # 发送框输入 → 字节预览实时更新
    send = _by_id(win, "ProtocolSendText")
    assert send is not None, "未找到发送框 (ProtocolSendText)"
    send.set_edit_text("PING")
    time.sleep(0.4)

    status = _by_id(win, "ProtocolSendStatus")
    text = (status.element_info.name or "") if status is not None else ""
    assert "字节" in text, f"字节预览未更新, 当前: {text!r}"
