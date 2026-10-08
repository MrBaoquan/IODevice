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
        public void EncodeSend_Handles_Text_And_Hex()
        {
            Assert.Equal(new byte[] { 0x50, 0x49 }, ProtocolSessionViewModel.EncodeSend("PI", "UTF-8"));
            Assert.Equal(new byte[] { 0x00, 0xFF, 0x10 }, ProtocolSessionViewModel.EncodeSend("00 FF 10", "HEX"));
            Assert.Equal(new byte[] { 0xAB, 0xCD }, ProtocolSessionViewModel.EncodeSend("AB-CD", "HEX"));
            Assert.Empty(ProtocolSessionViewModel.EncodeSend("", "HEX"));
        }

        // ─────────── 自动应答规则 ───────────

        [Fact]
        public void AutoReplyRule_Matches_By_Mode()
        {
            var contains = new AutoReplyRuleViewModel { MatchType = "包含", MatchValue = "AT" };
            Assert.True(contains.Matches("AT+GMR"));
            Assert.False(contains.Matches("OK"));

            var regex = new AutoReplyRuleViewModel { MatchType = "正则", MatchValue = "^PING\\d+$" };
            Assert.True(regex.Matches("PING42"));
            Assert.False(regex.Matches("PINGx"));

            var starts = new AutoReplyRuleViewModel { MatchType = "开头是", MatchValue = "GET" };
            Assert.True(starts.Matches("GET /x"));
            Assert.False(starts.Matches("POST"));

            // 正则写错不能把接收循环搞崩
            var broken = new AutoReplyRuleViewModel { MatchType = "正则", MatchValue = "([" };
            Assert.False(broken.Matches("anything"));
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

            var stopped = await _session.Dispatch(
                () =>
                {
                    s.StopCommand.Execute().Subscribe();
                    return !s.IsRunning;
                },
                CancellationToken.None
            );
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
