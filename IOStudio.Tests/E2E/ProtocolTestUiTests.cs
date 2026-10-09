using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using IOStudio.Services.ProtocolTest;
using IOStudio.Tests.E2E;
using IOStudio.ViewModels;
using Xunit;

namespace IOStudio.Tests.E2E
{
    /// <summary>
    /// 协议测试台 (通用调试台) 的**确定性**部分验收。
    ///
    /// <para><b>能力边界 (实测, 2026-10-08)</b>: 本文件<b>不</b>断言
    /// "后台收帧 → Dispatcher.UIThread.Post → 绑定集合"这条链路 ——
    /// <c>HeadlessUnitTestSession.Dispatch</c> 只执行<b>调用期间入队</b>的任务,
    /// 线程池 Post 回来的回调常不执行 (探针实测: UI 线程内 Post=1, 线程池 Post=0), 会 flaky。
    /// <para><b>并发限制</b>: 本类与其它 headless 会话测试类必须在同一 xunit collection 里
    /// 串行跑 —— <c>Dispatcher.UIThread</c> 是进程级的, 两个会话并行会互相派发到对方线程
    /// ("Call from invalid thread")。</para>
    /// </summary>
    [Collection("AvaloniaHeadless")]
    public class ProtocolTestUiTests : IDisposable
    {
        private readonly HeadlessUnitTestSession _session;

        public ProtocolTestUiTests()
        {
            _session = HeadlessUnitTestSession.StartNew(typeof(HeadlessApp));
        }

        public void Dispose() => _session.Dispose();

        private static ProtocolSessionViewModel NewSession(string transport = "udp", bool listener = true) =>
            new(
                "t1",
                ProtocolUiOptions.Transports.First(t => t.Transport == transport && t.IsListener == listener)
            );

        /// <summary>在 UI 线程上追加日志 (AppendEntry 会 Post, 需再泵一次才会落地)。</summary>
        private async Task AppendAsync(ProtocolSessionViewModel s, ProtocolLogEntry e)
        {
            await _session.Dispatch(() => { s.AppendEntry(e); return true; }, CancellationToken.None);
            await _session.Dispatch(() => true, CancellationToken.None);
        }

        // ─────────── 选项/可见性 ───────────

        [Fact]
        public void Options_Cover_Required_Transports_And_FrameModes()
        {
            var transports = ProtocolUiOptions.Transports.Select(t => t.Transport).Distinct().ToList();
            Assert.Contains("udp", transports);
            Assert.Contains("tcp-client", transports);
            Assert.Contains("tcp-server", transports);
            Assert.Contains("websocket-client", transports);
            Assert.Contains("websocket-server", transports);
            Assert.Contains("serial", transports);

            Assert.Equal(
                new[] { "none", "delimiter", "length-prefix", "fixed" },
                ProtocolUiOptions.FrameModes.Select(f => f.Mode)
            );
        }

        [Fact]
        public void Session_Field_Visibility_Follows_Transport_And_Frame()
        {
            var s = NewSession("tcp-server");
            Assert.False(s.ShowHost); // 服务端不需要目标主机
            Assert.True(s.ShowPort);
            Assert.False(s.ShowSerial);

            s.Transport = ProtocolUiOptions.Transports.First(t => t.Transport == "tcp-client");
            Assert.True(s.ShowHost);

            s.Transport = ProtocolUiOptions.Transports.First(t => t.Transport == "serial");
            Assert.False(s.ShowPort); // 串口没有端口概念
            Assert.True(s.ShowSerial);

            s.FrameMode = ProtocolUiOptions.FrameModes.First(f => f.Mode == "delimiter");
            Assert.True(s.ShowDelimiter);
            Assert.False(s.ShowPrefixBytes);

            s.FrameMode = ProtocolUiOptions.FrameModes.First(f => f.Mode == "length-prefix");
            Assert.True(s.ShowPrefixBytes);

            s.FrameMode = ProtocolUiOptions.FrameModes.First(f => f.Mode == "fixed");
            Assert.True(s.ShowFixedSize);
        }

        [Fact]
        public void Peers_Panel_Only_For_MultiClient_Server()
        {
            var s = NewSession("tcp-server");
            Assert.True(s.ShowPeers);

            s.Transport = ProtocolUiOptions.Transports.First(t => t.Transport == "tcp-client");
            Assert.False(s.ShowPeers);

            s.Transport = ProtocolUiOptions.Transports.First(t => t.Transport == "udp" && t.IsListener);
            Assert.False(s.ShowPeers);
        }

        [Fact]
        public void Virtual_Session_Hides_Connection_Controls()
        {
            var s = NewSession();
            Assert.True(s.ShowControls);

            // 「场景运行」这类虚拟会话只承载日志, 不应给出会去 bind 端口的启停按钮
            s.IsVirtual = true;
            Assert.False(s.ShowControls);
        }

        // ─────────── 日志语义 ───────────

        [Fact]
        public void Received_Frame_Is_Marked_When_Assertion_Fails()
        {
            var bad = ProtocolLogEntry.Frame("收", "srv", Encoding.UTF8.GetBytes("ACTUAL"), matched: false);
            Assert.True(bad.IsBad);
            Assert.True(bad.IsReceived);
            Assert.Contains("✗", bad.Direction);

            var ok = ProtocolLogEntry.Frame("收", "srv", Encoding.UTF8.GetBytes("ACTUAL"), matched: true);
            Assert.False(ok.IsBad);
            Assert.DoesNotContain("✗", ok.Direction);
        }

        [Fact]
        public void Binary_Frame_Leaves_Text_Empty_And_Keeps_Hex()
        {
            var e = ProtocolLogEntry.Frame("收", "srv", new byte[] { 0x00, 0xFF, 0x10, 0xAB }, null);
            Assert.Equal("", e.ContentText);   // 二进制不该被当文本渲染成乱码
            Assert.Equal("00FF10AB", e.HexColumn);
        }

        [Fact]
        public void AutoReply_Frame_Is_Distinguishable_From_Manual_Send()
        {
            var e = ProtocolLogEntry.AutoReply(Encoding.UTF8.GetBytes("OK"));
            Assert.True(e.IsAuto);
            Assert.True(e.IsSent);
            Assert.Equal("自动", e.Direction);
        }

        [Fact]
        public void Step_Result_Row_Carries_Verdict_And_Reason()
        {
            var pass = ProtocolLogEntry.Step(new ProtocolTestStepResult
            {
                Index = 2, Op = "expect", Via = "srv", Ok = true, ElapsedMs = 3,
            });
            Assert.True(pass.IsPass);
            Assert.Contains("PASS", pass.Direction);

            var fail = ProtocolLogEntry.Step(new ProtocolTestStepResult
            {
                Index = 3, Op = "expect", Via = "srv", Ok = false, Reason = "超时",
            });
            Assert.True(fail.IsBad);
            Assert.Contains("超时", fail.ContentText);
        }

        // ─────────── 自由收发: 编解码 ───────────

        [Fact]
        public void EncodeSend_Handles_Text_Hex_And_Gbk()
        {
            Assert.Equal(new byte[] { 0x50, 0x49 }, ProtocolEncoding.Encode("PI", "UTF-8"));
            Assert.Equal(new byte[] { 0x00, 0xFF, 0x10 }, ProtocolEncoding.Encode("00 FF 10", "HEX"));
            Assert.Equal(new byte[] { 0xAB, 0xCD }, ProtocolEncoding.Encode("AB-CD", "HEX"));
            Assert.Empty(ProtocolEncoding.Encode("", "HEX"));
            Assert.Equal(new byte[] { 0x41 }, ProtocolEncoding.Encode("A", "ASCII"));

            // GBK: 国产设备刚需。中文字节数必须与 UTF-8 不同, 否则说明没走 GBK。
            var gbk = ProtocolEncoding.Encode("中文", "GBK");
            var utf8 = ProtocolEncoding.Encode("中文", "UTF-8");
            Assert.Equal(4, gbk.Length);       // GBK: 每汉字 2 字节
            Assert.Equal(6, utf8.Length);      // UTF-8: 每汉字 3 字节
            Assert.Equal("中文", ProtocolEncoding.Get("GBK").GetString(gbk));
        }

        [Fact]
        public void NewLine_Option_Maps_To_Expected_Suffix()
        {
            Assert.Equal("", ProtocolUiOptions.NewLineToText("无"));
            Assert.Equal("\r\n", ProtocolUiOptions.NewLineToText("\\r\\n"));
            Assert.Equal("\n", ProtocolUiOptions.NewLineToText("\\n"));
        }

        // ─────────── 自动应答规则 ───────────

        [Fact]
        public void AutoReplyRule_Matches_Text_And_Hex()
        {
            var contains = new AutoReplyRuleViewModel { MatchType = "包含", MatchValue = "AT" };
            Assert.True(contains.Matches(Encoding.UTF8.GetBytes("AT+GMR")));
            Assert.False(contains.Matches(Encoding.UTF8.GetBytes("OK")));

            var regex = new AutoReplyRuleViewModel { MatchType = "正则", MatchValue = "^PING\\d+$" };
            Assert.True(regex.Matches(Encoding.UTF8.GetBytes("PING42")));
            Assert.False(regex.Matches(Encoding.UTF8.GetBytes("PINGx")));

            var starts = new AutoReplyRuleViewModel { MatchType = "开头是", MatchValue = "GET" };
            Assert.True(starts.Matches(Encoding.UTF8.GetBytes("GET /x")));
            Assert.False(starts.Matches(Encoding.UTF8.GetBytes("POST")));

            // 二进制协议: 没有可读文本, 必须能按 HEX 匹配
            var hexRule = new AutoReplyRuleViewModel { MatchType = "包含", MatchValue = "AA BB" };
            Assert.True(hexRule.Matches(new byte[] { 0x01, 0xAA, 0xBB, 0x02 }));
            Assert.False(hexRule.Matches(new byte[] { 0x01, 0xAA, 0xCC, 0x02 }));

            // 正则写错不能把接收循环搞崩
            var broken = new AutoReplyRuleViewModel { MatchType = "正则", MatchValue = "([" };
            Assert.False(broken.Matches(Encoding.UTF8.GetBytes("anything")));
        }

        [Fact]
        public async Task QuickCommand_Sends_Configured_Payload()
        {
            var s = NewSession();
            var port = await _session.Dispatch(
                () =>
                {
                    using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                    return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
                },
                CancellationToken.None
            );

            await _session.Dispatch(
                () =>
                {
                    s.Port = port;
                    s.FrameMode = ProtocolUiOptions.FrameModes.First(f => f.Mode == "none");
                    s.QuickCommands.Add(new QuickCommandViewModel(s)
                    {
                        Text = "AT",
                        Encoding = "UTF-8",
                        NewLine = "\\r\\n", // 现场指令常以 CRLF 结尾
                    });
                    s.StartCommand.Execute().Subscribe();
                    return true;
                },
                CancellationToken.None
            );

            var started = false;
            for (int i = 0; i < 60 && !started; i++)
            {
                started = await _session.Dispatch(() => s.IsRunning, CancellationToken.None);
                if (!started)
                    await Task.Delay(50);
            }
            Assert.True(started, "UDP 监听未启动");

            using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var target = new IPEndPoint(IPAddress.Loopback, port);

            // 先让监听端知道"回给谁" (UDP 监听端未收到过数据时无法确定目的地址)
            await client.SendAsync(Encoding.UTF8.GetBytes("HELLO"), target);

            // 触发快捷指令, 直到客户端收到 (避免时序竞争)
            byte[]? got = null;
            for (int i = 0; i < 20 && got is null; i++)
            {
                await _session.Dispatch(
                    () =>
                    {
                        s.QuickCommands[0].SendCommand.Execute().Subscribe();
                        return true;
                    },
                    CancellationToken.None
                );

                var recv = client.ReceiveAsync();
                var done = await Task.WhenAny(recv, Task.Delay(300));
                if (done == recv)
                    got = (await recv).Buffer;
            }

            Assert.NotNull(got);
            Assert.Equal("AT\r\n", Encoding.UTF8.GetString(got!)); // 新行必须被带上

            await _session.Dispatch(
                () =>
                {
                    s.StopCommand.Execute().Subscribe();
                    return true;
                },
                CancellationToken.None
            );
        }

        [Fact]
        public void Workspace_RoundTrips_Session_Config()
        {
            var path = Path.Combine(Path.GetTempPath(), $"ptws-{Guid.NewGuid():N}.json");
            try
            {
                var s = NewSession("tcp-server");
                s.Label = "服务端A";
                s.Port = 12345;
                s.SendNewLine = "\\r\\n";
                s.SendEncoding = "GBK";
                s.LoopSend = true;
                s.LoopIntervalMs = 250;
                s.Rules.Add(new AutoReplyRuleViewModel { MatchType = "包含", MatchValue = "PING", ReplyText = "PONG" });
                s.QuickCommands.Add(new QuickCommandViewModel(s) { Text = "AT", NewLine = "\\n", PeriodicEnabled = true });

                var ws = new ProtocolWorkspace();
                ws.Sessions.Add(s.Snapshot());
                ws.Save(path);

                var loaded = ProtocolWorkspace.Load(path);
                Assert.Single(loaded.Sessions);

                var restored = NewSession("udp");
                restored.Restore(loaded.Sessions[0]);
                Assert.Equal("服务端A", restored.Label);
                Assert.Equal("tcp-server", restored.Transport.Transport);
                Assert.Equal(12345, restored.Port);
                Assert.Equal("\\r\\n", restored.SendNewLine);
                Assert.Equal("GBK", restored.SendEncoding);
                Assert.True(restored.LoopSend);
                Assert.Equal(250, restored.LoopIntervalMs);
                Assert.Single(restored.Rules);
                Assert.Equal("PONG", restored.Rules[0].ReplyText);
                Assert.Single(restored.QuickCommands);
                Assert.True(restored.QuickCommands[0].PeriodicEnabled);
            }
            finally
            {
                try { File.Delete(path); } catch { /* ignore */ }
            }
        }

        // ─────────── 日志过滤 / 导出 ───────────

        [Fact]
        public async Task Log_Filter_Keeps_Only_Matching_Entries()
        {
            var s = NewSession();
            await AppendAsync(s, ProtocolLogEntry.Frame("收", "", Encoding.UTF8.GetBytes("PING"), null));
            await AppendAsync(s, ProtocolLogEntry.Frame("发", "", Encoding.UTF8.GetBytes("PONG"), null));

            Assert.Equal(2, await _session.Dispatch(() => s.Messages.Count, CancellationToken.None));

            // 过滤: 只有含 PING 的那条可见
            await _session.Dispatch(() => { s.FilterText = "PING"; return true; }, CancellationToken.None);
            var visible = await _session.Dispatch(
                () => string.Join("|", s.Messages.Select(m => m.ContentText)),
                CancellationToken.None
            );
            Assert.Equal("PING", visible);

            // 清空过滤后两条都回来 (证明过滤不改原始数据)
            await _session.Dispatch(() => { s.FilterText = ""; return true; }, CancellationToken.None);
            Assert.Equal(2, await _session.Dispatch(() => s.Messages.Count, CancellationToken.None));
        }

        [Fact]
        public async Task Log_Filter_Also_Matches_Hex()
        {
            var s = NewSession();
            await AppendAsync(s, ProtocolLogEntry.Frame("收", "", new byte[] { 0xDE, 0xAD }, null));

            await _session.Dispatch(() => { s.FilterText = "DEAD"; return true; }, CancellationToken.None);
            Assert.Equal(1, await _session.Dispatch(() => s.Messages.Count, CancellationToken.None));
        }

        [Fact]
        public async Task Export_Text_Contains_Header_And_Rows()
        {
            var s = NewSession();
            s.Label = "会话A";
            await AppendAsync(s, ProtocolLogEntry.Frame("收", "", Encoding.UTF8.GetBytes("HELLO"), null));

            var text = await _session.Dispatch(() => s.ExportText(), CancellationToken.None);
            Assert.Contains("会话A", text);
            Assert.Contains("HELLO", text);
            Assert.Contains("HEX", text);
        }

        // ─────────── 会话启停 ───────────

        [Fact]
        public async Task Session_Can_Start_And_Stop_Udp_Listener()
        {
            var s = NewSession();

            var port = await _session.Dispatch(
                () =>
                {
                    using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                    return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
                },
                CancellationToken.None
            );

            var started = await _session.Dispatch(
                () =>
                {
                    s.Port = port;
                    s.FrameMode = ProtocolUiOptions.FrameModes.First(f => f.Mode == "none");
                    s.StartCommand.Execute().Subscribe();
                    return s.IsRunning;
                },
                CancellationToken.None
            );
            Assert.True(started, "UDP 监听未启动");

            // 停止现在是"取消 → 等循环真正退出 → 释放", 异步完成, 必须轮询而非同步读
            await _session.Dispatch(
                () =>
                {
                    s.StopCommand.Execute().Subscribe();
                    return true;
                },
                CancellationToken.None
            );

            var stopped = false;
            for (int i = 0; i < 60 && !stopped; i++)
            {
                stopped = await _session.Dispatch(() => !s.IsRunning, CancellationToken.None);
                if (!stopped)
                    await Task.Delay(50);
            }
            Assert.True(stopped, "UDP 监听未停止 (端口会被一直占着)");
        }

        [Fact]
        public void Checksums_Match_Standard_Test_Vectors()
        {
            // CRC 写错极难靠肉眼发现, 必须对标准 "123456789" 检查值
            var check = Encoding.ASCII.GetBytes("123456789");

            Assert.Equal(0x4B37, Checksums.Crc16Modbus(check));   // CRC-16/MODBUS
            Assert.Equal(0x29B1, Checksums.Crc16Ccitt(check));    // CRC-16/CCITT-FALSE
            Assert.Equal(0xCBF43926u, Checksums.Crc32(check));    // CRC-32/IEEE

            Assert.Equal(6, Checksums.Sum8(new byte[] { 1, 2, 3 }));
            Assert.Equal(0, Checksums.Xor8(new byte[] { 1, 2, 3 }));

            // 字节序: Modbus 惯例低字节在前
            var le = Checksums.Compute("CRC16-Modbus", check, littleEndian: true);
            var be = Checksums.Compute("CRC16-Modbus", check, littleEndian: false);
            Assert.Equal(new byte[] { 0x37, 0x4B }, le);
            Assert.Equal(new byte[] { 0x4B, 0x37 }, be);

            Assert.Empty(Checksums.Compute("无", check));
        }

        [Fact]
        public void HexDump_Formats_Offset_Hex_Ascii()
        {
            var data = Encoding.ASCII.GetBytes("Hello, hexdump! 0123456789");
            var lines = HexDump.Format(data).Split('\n');

            // 26 字节 → 16 + 10, 两行; 偏移按 16 递增
            Assert.Equal(2, lines.Length);
            Assert.StartsWith("0000  48 65 6C 6C 6F 2C 20 68", lines[0]);
            Assert.EndsWith("Hello, hexdump! ", lines[0]); // 第 16 字节是空格, 属可打印
            Assert.StartsWith("0010  30 31 32 33 34 35 36 37", lines[1]);
            Assert.EndsWith("0123456789", lines[1]);

            // 不可打印字节以 '.' 呈现; 末尾不留换行
            var four = HexDump.Format(new byte[] { 0, 1, 0xFF, 0x41 });
            Assert.StartsWith("0000  00 01 FF 41", four);
            Assert.EndsWith("...A", four);
            Assert.DoesNotContain("\n", four);

            Assert.Equal("", HexDump.Format(Array.Empty<byte>()));
            Assert.Equal("", HexDump.Format(null));
        }

        [Fact]
        public void Log_Entry_Timing_Text_Formats()
        {
            var e = ProtocolLogEntry.Frame("发", "", new byte[] { 1 }, null);
            Assert.Equal("", e.TimingText);                 // 无时序信息时为空

            e.DeltaMs = 1000;
            Assert.Equal("1000", e.TimingText);

            e.RttMs = 12;
            Assert.Equal("1000 / 12", e.TimingText);

            var rx = ProtocolLogEntry.Frame("收", "", new byte[] { 1 }, null);
            rx.RttMs = 7;
            Assert.Equal("– / 7", rx.TimingText);
            Assert.Equal("7", rx.RttText);
        }

        [Fact]
        public void ContentMode_Switch_Updates_Existing_Rows()
        {
            var e = ProtocolLogEntry.Frame("收", "", Encoding.ASCII.GetBytes("AB"), null);

            e.DisplayMode = ProtocolContentMode.TextAndHex;
            Assert.Equal("AB", e.ContentText);
            Assert.Equal("4142", e.HexColumn);

            e.DisplayMode = ProtocolContentMode.HexOnly;
            Assert.Equal("", e.ContentText);
            Assert.Equal("4142", e.HexColumn);

            e.DisplayMode = ProtocolContentMode.Dump;
            Assert.Contains("0000  41 42", e.ContentText);
            Assert.Equal("", e.HexColumn);

            // 过滤/导出用原始表示, 与显示模式无关
            Assert.Equal("AB", e.PlainText);
            Assert.Equal("4142", e.HexText);
        }

        [Fact]
        public async Task Send_Timing_Is_Recorded()
        {
            var s = NewSession();
            var port = await _session.Dispatch(
                () =>
                {
                    using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                    return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
                },
                CancellationToken.None
            );

            await _session.Dispatch(
                () =>
                {
                    s.Port = port;
                    s.FrameMode = ProtocolUiOptions.FrameModes.First(f => f.Mode == "none");
                    s.StartCommand.Execute().Subscribe();
                    return true;
                },
                CancellationToken.None
            );

            var started = false;
            for (int i = 0; i < 60 && !started; i++)
            {
                started = await _session.Dispatch(() => s.IsRunning, CancellationToken.None);
                if (!started)
                    await Task.Delay(50);
            }
            Assert.True(started, "UDP 监听未启动");

            // 先让监听端知道"发给谁"
            using (var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                await peer.SendAsync(Encoding.UTF8.GetBytes("HELLO"), new IPEndPoint(IPAddress.Loopback, port));

            await Task.Delay(300);
            await _session.Dispatch(() => true, CancellationToken.None); // 泵一次, 让收帧日志落地

            // 间隔发两次
            await _session.Dispatch(() => { s.SendCommand.Execute().Subscribe(); return true; }, CancellationToken.None);
            await Task.Delay(180);
            await _session.Dispatch(() => { s.SendCommand.Execute().Subscribe(); return true; }, CancellationToken.None);
            await Task.Delay(200);
            await _session.Dispatch(() => true, CancellationToken.None);

            var deltas = await _session.Dispatch(
                () => s.Messages.Where(m => m.IsSent).Select(m => m.DeltaMs).ToList(),
                CancellationToken.None
            );

            Assert.True(deltas.Count >= 2, $"发送未被记录: {deltas.Count}");
            // 第二条发送必须记录与第一条的间隔, 且量级正确 (≥100ms)
            Assert.NotNull(deltas[1]);
            Assert.True(deltas[1] >= 100, $"Δt 不合理: {deltas[1]}");

            await _session.Dispatch(() => { s.StopCommand.Execute().Subscribe(); return true; }, CancellationToken.None);
        }

        [Fact]
        public async Task SendFile_Delivers_Raw_Bytes_In_Chunks()
        {
            var s = NewSession();
            var port = await _session.Dispatch(
                () =>
                {
                    using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                    return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
                },
                CancellationToken.None
            );

            var payload = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09 };
            var path = Path.Combine(Path.GetTempPath(), $"ptfile-{Guid.NewGuid():N}.bin");
            File.WriteAllBytes(path, payload);

            try
            {
                await _session.Dispatch(
                    () =>
                    {
                        s.Port = port;
                        s.FrameMode = ProtocolUiOptions.FrameModes.First(f => f.Mode == "none");
                        s.StartCommand.Execute().Subscribe();
                        return true;
                    },
                    CancellationToken.None
                );

                var started = false;
                for (int i = 0; i < 60 && !started; i++)
                {
                    started = await _session.Dispatch(() => s.IsRunning, CancellationToken.None);
                    if (!started)
                        await Task.Delay(50);
                }
                Assert.True(started, "UDP 监听未启动");

                using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                var clientPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;

                // UDP 监听端没有对端地址, 默认要等"收到第一帧"才学会回发目标 —— 而那次学习
                // 是通过后台线程 Post 回 UI 再更新统计/日志的, headless 下这条链路不可靠
                // (本类文档已注明), 会让用例 flaky。这里直接显式指定对端, 用例只测
                // "文件分块 + 原始字节送达" 本身 (对端学习由真实进程里的协议场景覆盖)。
                await _session.Dispatch(
                    () =>
                    {
                        s.SelectedPeer = $"127.0.0.1:{clientPort}";
                        return true;
                    },
                    CancellationToken.None
                );

                await _session.Dispatch(
                    () =>
                    {
                        s.FilePath = path;
                        s.FileChunkSize = 4;      // 4 字节/包 → 4 + 4 + 2
                        s.FileChunkIntervalMs = 0;
                        s.SendFileCommand.Execute().Subscribe();
                        return true;
                    },
                    CancellationToken.None
                );

                // 发送循环本身不占 UI 线程 (VM 里全 ConfigureAwait(false)), 所以这里不需要
                // 靠泵 dispatcher 来推进发送; 保留一次泵是为了让 ReactiveCommand 的
                // canExecute (跟着 IsRunning 走) 有机会结算, 然后边收边等。
                var received = new System.Collections.Generic.List<byte>();
                var deadline = DateTime.UtcNow.AddSeconds(8);
                while (received.Count < payload.Length && DateTime.UtcNow < deadline)
                {
                    await _session.Dispatch(() => true, CancellationToken.None);

                    while (client.Available > 0)
                        received.AddRange((await client.ReceiveAsync()).Buffer);

                    if (received.Count < payload.Length)
                        await Task.Delay(20);
                }

                var diag = await _session.Dispatch(
                    () =>
                        $"running={s.IsRunning} status={s.StatusText} peer={s.SelectedPeer} "
                        + "msgs="
                        + string.Join(" | ", s.Messages.Select(m => m.Direction + ":" + m.ContentText)),
                    CancellationToken.None
                );

                Assert.True(
                    received.Count == payload.Length,
                    $"期望 {payload.Length} 字节, 实收 {received.Count} 字节. {diag}"
                );
                Assert.Equal(payload, received.ToArray());
            }
            finally
            {
                try { File.Delete(path); } catch { /* ignore */ }
                await _session.Dispatch(
                    () =>
                    {
                        s.StopCommand.Execute().Subscribe();
                        return true;
                    },
                    CancellationToken.None
                );
            }
        }

        [Fact]
        public void Main_Vm_Starts_With_One_Selected_Session()
        {
            // 打开窗口就能收: 默认已有一路会话并处于选中态
            var vm = new ProtocolTestViewModel();
            Assert.NotEmpty(vm.Sessions);
            Assert.NotNull(vm.SelectedSession);
            Assert.True(vm.HasSelection);
        }

        /// <summary>
        /// 自动应答端到端: 外部发 PING → 会话自动回 PONG。
        ///
        /// <para>这条路径 (收帧 → 匹配规则 → 发送) 不经过 UI 线程, 因此可以在 headless 里
        /// 确定性验证 —— 它正是"没有真实设备也能把链路跑通"的核心能力。</para>
        /// </summary>
        [Fact]
        public async Task AutoReply_Simulates_Peer_Over_Udp()
        {
            var s = NewSession();

            var port = await _session.Dispatch(
                () =>
                {
                    using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                    return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
                },
                CancellationToken.None
            );

            await _session.Dispatch(
                () =>
                {
                    s.Port = port;
                    s.FrameMode = ProtocolUiOptions.FrameModes.First(f => f.Mode == "none");
                    s.Rules.Add(new AutoReplyRuleViewModel
                    {
                        MatchType = "包含",
                        MatchValue = "PING",
                        ReplyText = "PONG",
                        ReplyEncoding = "UTF-8",
                    });
                    s.StartCommand.Execute().Subscribe();
                    return true;
                },
                CancellationToken.None
            );

            var started = false;
            for (int i = 0; i < 60 && !started; i++)
            {
                started = await _session.Dispatch(() => s.IsRunning, CancellationToken.None);
                if (!started)
                    await Task.Delay(50);
            }
            Assert.True(started, "UDP 监听未启动");

            using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            await client.SendAsync(Encoding.UTF8.GetBytes("PING"), new IPEndPoint(IPAddress.Loopback, port));

            var recvTask = client.ReceiveAsync();
            var done = await Task.WhenAny(recvTask, Task.Delay(5000));
            Assert.True(done == recvTask, "未收到自动应答 (规则未命中或回包地址不对)");

            var recv = await recvTask;
            Assert.Equal("PONG", Encoding.UTF8.GetString(recv.Buffer));

            await _session.Dispatch(
                () =>
                {
                    s.StopCommand.Execute().Subscribe();
                    return true;
                },
                CancellationToken.None
            );
        }

        // ─────────── 批次 A: 读与发顺手 ───────────

        [Fact]
        public async Task Invalid_Hex_Is_Reported_Inline()
        {
            var s = NewSession();
            var r = await _session.Dispatch(
                () =>
                {
                    s.SendEncoding = "HEX";
                    s.SendText = "AB CD"; // 空格应被容忍
                    var valid = (s.SendTextError is null, s.HasSendTextError);

                    s.SendText = "ABC"; // 奇数位 → 非法
                    var odd = s.SendTextError ?? "";

                    s.SendText = "ZZ"; // 非十六进制字符 → 非法
                    var nonHex = s.SendTextError ?? "";

                    s.SendEncoding = "UTF-8";
                    s.SendText = "hello";
                    var back = s.HasSendTextError;
                    return (valid, odd, nonHex, back);
                },
                CancellationToken.None
            );

            Assert.True(r.valid.Item1);
            Assert.False(r.valid.Item2);
            Assert.Contains("HEX", r.odd);
            Assert.Contains("HEX", r.nonHex);
            Assert.False(r.back);
        }

        [Fact]
        public async Task Send_Preview_Counts_Bytes_Including_NewLine()
        {
            var s = NewSession();
            var r = await _session.Dispatch(
                () =>
                {
                    s.SendEncoding = "UTF-8";
                    s.SendText = "PING";
                    s.SendNewLine = "\\r\\n";
                    var withNewLine = s.SendPreviewText;

                    s.SendNewLine = "无";
                    var plain = s.SendPreviewText;

                    s.SendText = "";
                    var empty = s.SendPreviewText;
                    return (withNewLine, plain, empty);
                },
                CancellationToken.None
            );

            Assert.Equal("将发送 6 字节", r.withNewLine);
            Assert.Equal("将发送 4 字节", r.plain);
            Assert.Equal("", r.empty);
        }

        [Fact]
        public async Task Pause_Then_Resume_Reveals_Buffered_Frames()
        {
            var s = NewSession();
            await AppendAsync(s, ProtocolLogEntry.Frame("收", "", Encoding.UTF8.GetBytes("A"), null));
            Assert.Single(s.Messages);

            await _session.Dispatch(() => { s.PauseDisplay = true; return true; }, CancellationToken.None);

            // 暂停期间到达的帧不应立刻显示…
            await AppendAsync(s, ProtocolLogEntry.Frame("收", "", Encoding.UTF8.GetBytes("B"), null));
            Assert.Single(s.Messages);

            // …但恢复后必须补回来 (此前会永久丢失)
            await _session.Dispatch(() => { s.PauseDisplay = false; return true; }, CancellationToken.None);
            Assert.Equal(2, s.Messages.Count);
        }

        [Fact]
        public async Task Direction_Filter_Separates_Rx_Tx_And_Errors()
        {
            var s = NewSession();
            await AppendAsync(s, ProtocolLogEntry.Frame("收", "", Encoding.UTF8.GetBytes("R"), null));
            await AppendAsync(s, ProtocolLogEntry.Frame("发", "", Encoding.UTF8.GetBytes("T"), null));
            await AppendAsync(s, ProtocolLogEntry.Frame("收", "", Encoding.UTF8.GetBytes("X"), matched: false));

            var counts = await _session.Dispatch(
                () =>
                {
                    s.FilterDirection = "收";
                    var rx = s.Messages.Count;
                    s.FilterDirection = "发";
                    var tx = s.Messages.Count;
                    s.FilterDirection = "异常";
                    var bad = s.Messages.Count;
                    s.FilterDirection = "全部";
                    var all = s.Messages.Count;
                    return (rx, tx, bad, all);
                },
                CancellationToken.None
            );

            Assert.Equal(2, counts.rx); // 收到 R + 失败 X
            Assert.Equal(1, counts.tx);
            Assert.Equal(1, counts.bad);
            Assert.Equal(3, counts.all);
        }

        [Fact]
        public async Task Copy_Entry_Writes_Text_And_Hex_To_Clipboard()
        {
            var s = NewSession();
            string? captured = null;
            s.ClipboardWriter = t =>
            {
                captured = t;
                return Task.CompletedTask;
            };

            var e = ProtocolLogEntry.Frame("收", "", Encoding.UTF8.GetBytes("HELLO"), null);
            await AppendAsync(s, e);

            await _session.Dispatch(
                () =>
                {
                    s.CopyEntryTextCommand.Execute(e).Subscribe();
                    return true;
                },
                CancellationToken.None
            );
            await Task.Delay(30);
            Assert.Equal("HELLO", captured);

            await _session.Dispatch(
                () =>
                {
                    s.CopyEntryHexCommand.Execute(e).Subscribe();
                    return true;
                },
                CancellationToken.None
            );
            await Task.Delay(30);
            Assert.Equal("48454C4C4F", captured);
        }

        [Fact]
        public async Task Binary_Frame_Copy_Falls_Back_To_Hexdump()
        {
            var s = NewSession();
            string? captured = null;
            s.ClipboardWriter = t =>
            {
                captured = t;
                return Task.CompletedTask;
            };

            var e = ProtocolLogEntry.Frame("收", "", new byte[] { 0x00, 0xFF }, null);
            await AppendAsync(s, e);

            await _session.Dispatch(
                () =>
                {
                    s.CopyEntryTextCommand.Execute(e).Subscribe();
                    return true;
                },
                CancellationToken.None
            );
            await Task.Delay(30);
            Assert.Contains("00 FF", captured); // 二进制帧没有文本列 → 退回转储
        }

        [Fact]
        public async Task Send_History_Recalls_Previous_Input()
        {
            var s = NewSession();
            var r = await _session.Dispatch(
                () =>
                {
                    // 无端点: 记录历史仍应发生 (发送本身早退)
                    s.SendText = "AAA";
                    s.SendCommand.Execute().Subscribe();
                    s.SendText = "BBB";
                    s.SendCommand.Execute().Subscribe();

                    var p1 = s.HistoryPrev();
                    var afterFirst = s.SendText;
                    s.HistoryPrev();
                    var afterSecond = s.SendText;
                    s.HistoryNext();
                    var afterNext = s.SendText;
                    return (p1, afterFirst, afterSecond, afterNext);
                },
                CancellationToken.None
            );

            Assert.True(r.p1);
            Assert.Equal("BBB", r.afterFirst);
            Assert.Equal("AAA", r.afterSecond);
            Assert.Equal("BBB", r.afterNext);
        }

        [Fact]
        public async Task Inactive_Session_Badges_Unseen_Then_Clears()
        {
            var s = NewSession();
            s.IsActive = false;

            await AppendAsync(s, ProtocolLogEntry.Frame("收", "", Encoding.UTF8.GetBytes("A"), null));
            Assert.True(s.HasUnseen);
            Assert.Equal("1", s.BadgeText);

            s.MarkSeen();
            Assert.False(s.HasUnseen);

            await AppendAsync(s, ProtocolLogEntry.Frame("收", "", Encoding.UTF8.GetBytes("X"), matched: false));
            Assert.True(s.HasErrorSinceSeen);
            Assert.Equal("!", s.BadgeText);

            // 当前会话不做未读累计
            s.IsActive = true;
            s.MarkSeen();
            await AppendAsync(s, ProtocolLogEntry.Frame("收", "", Encoding.UTF8.GetBytes("B"), null));
            Assert.False(s.HasUnseen);
        }

        [Fact]
        public void Selected_Entry_Provides_Hexdump_For_Detail_Pane()
        {
            var e = ProtocolLogEntry.Frame("收", "", new byte[] { 0x00, 0xFF, 0x10, 0xAB }, null);
            Assert.Contains("00 FF 10 AB", e.HexDumpText);
            Assert.True(e.CanResend);

            var info = ProtocolLogEntry.Info("就绪");
            Assert.False(info.CanResend);
            Assert.Equal("", info.HexDumpText);
        }
    }
}
