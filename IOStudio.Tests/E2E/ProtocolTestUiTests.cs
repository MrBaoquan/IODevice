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
            Assert.Equal("", e.Text);          // 二进制不该被当文本渲染成乱码
            Assert.Equal("00FF10AB", e.Hex);
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
            Assert.Contains("超时", fail.Text);
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
                () => string.Join("|", s.Messages.Select(m => m.Text)),
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
    }
}
