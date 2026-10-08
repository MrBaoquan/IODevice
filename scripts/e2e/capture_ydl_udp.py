"""抓取 MP-YDL (影动力) 插件发往游戏端的 UDP 报文, 用于审查协议接入的真实行为。

注意: 该插件当前产出的 Para 未做 JSON 转义 (非法 JSON), 因此不能用 json.loads 分类,
这里用正则宽松提取 Cmd, 同时单独统计"能否被标准 JSON 解析"。

用法:
    python capture_ydl_udp.py <seconds> <out.txt>
"""
import collections
import json
import re
import socket
import sys
import time

PORT = 30121
CMD_RE = re.compile(r'"Cmd"\s*:\s*"([^"]+)"')
PARA_RE = re.compile(r'"Para"\s*:\s*"(.+?)"\s*\}\s*$')


def classify(text):
    """返回 (cmd, para, json_ok)。"""
    try:
        obj = json.loads(text)
        return obj.get("Cmd", "<no Cmd>"), obj.get("Para"), True
    except Exception:
        m = CMD_RE.search(text)
        cmd = m.group(1) if m else "<no Cmd>"
        p = PARA_RE.search(text)
        return cmd, (p.group(1) if p else None), False


def main():
    seconds = float(sys.argv[1]) if len(sys.argv) > 1 else 30.0
    out = sys.argv[2] if len(sys.argv) > 2 else "ydl_udp_capture.txt"

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.bind(("127.0.0.1", PORT))
    sock.settimeout(0.4)

    records = []
    t0 = time.time()
    deadline = t0 + seconds
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    print(f"listening on 127.0.0.1:{PORT} for {seconds}s ...", flush=True)

    while time.time() < deadline:
        try:
            data, _ = sock.recvfrom(8192)
        except socket.timeout:
            continue
        except OSError:
            break
        records.append((round((time.time() - t0) * 1000), data.decode("utf-8", errors="replace")))
    sock.close()

    by_cmd = collections.OrderedDict()
    bad = 0
    for ms, text in records:
        cmd, para, ok = classify(text)
        if not ok:
            bad += 1
        by_cmd.setdefault(cmd, []).append((ms, para, text, ok))

    L = []
    L.append(f"抓包时长 {seconds}s, 收到报文 {len(records)} 条")
    L.append(f"标准 JSON 解析失败: {bad} 条 ({bad * 100 // max(1, len(records))}%)")
    L.append("")
    L.append("=== 按 Cmd 归类 ===")
    for cmd, items in by_cmd.items():
        okc = sum(1 for it in items if it[3])
        L.append(f"{cmd:<40} x{len(items):<6} 合法JSON={okc:<5} 首次@{items[0][0]}ms")
    L.append("")
    L.append("=== 每类样例 ===")
    for cmd, items in by_cmd.items():
        L.append(f"--- {cmd} (共 {len(items)}) ---")
        for ms, para, text, ok in items[:4]:
            L.append(f"  @{ms}ms  jsonOK={ok}  {text[:240]}")
    L.append("")
    L.append("=== Cylinder 发送间隔 ===")
    cyl = [ms for ms, _, _, _ in by_cmd.get("GamePlatform_Cylinder", [])]
    if len(cyl) > 2:
        span = cyl[-1] - cyl[0]
        L.append(f"  条数={len(cyl)} 跨度={span}ms 平均间隔={span / (len(cyl) - 1):.1f}ms (协议建议 50ms)")
    L.append("")
    L.append("=== 首批 40 条原始报文 ===")
    for ms, text in records[:40]:
        L.append(f"  @{ms}ms  {text[:240]}")

    with open(out, "w", encoding="utf-8") as f:
        f.write("\n".join(L))
    print(f"captured={len(records)} invalid_json={bad} -> {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
