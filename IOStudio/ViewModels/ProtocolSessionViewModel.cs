using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using IOStudio.Services.ProtocolTest;
using ReactiveUI;

namespace IOStudio.ViewModels
{
    /// <summary>
    /// 一个独立的协议会话 (一路连接)。
    ///
    /// <para>通用调试台的基本单位: 多路会话并行, 每路独立配置/收发/统计/规则。
    /// 对标 SSCOM / Hercules 这类工具, 但支持 TCP/UDP/WebSocket/串口 且可脚本化。</para>
    ///
    /// <para>线程模型: 收帧在线程池上跑, 每条日志与统计都 Post 回 UI 线程再改集合 ——
    /// 后台线程直接改 ObservableCollection 会崩。</para>
    /// </summary>
    public sealed class ProtocolSessionViewModel : ViewModelBase, IDisposable
    {
        /// <summary>日志上限: 调试台会长时间开着, 不设上限必然吃光内存。</summary>
        public const int MaxLogEntries = 5000;

        /// <summary>自动应答限速: 防止两端互相应答形成无限回声。</summary>
        private const int MaxAutoRepliesPerSecond = 20;

        public string Id { get; }

        /// <summary>虚拟会话 (如"场景运行"): 没有真实端点, 只承载日志, 不显示启停/发送控件。</summary>
        private bool _isVirtual;
        public bool IsVirtual
        {
            get => _isVirtual;
            set
            {
                this.RaiseAndSetIfChanged(ref _isVirtual, value);
                this.RaisePropertyChanged(nameof(ShowControls));
            }
        }

        public bool ShowControls => !IsVirtual;

        /// <summary>从任意线程追加一条日志 (内部会切回 UI 线程)。</summary>
        public void AppendEntry(ProtocolLogEntry entry) =>
            Dispatcher.UIThread.Post(() => AddEntry(entry));

        public ProtocolSessionViewModel(string id, ProtocolTransportOption transport)
        {
            Id = id;
            _transport = transport;
            _frameMode = ProtocolUiOptions.FrameModes[0];

            StartCommand = ReactiveCommand.CreateFromTask(
                StartAsync,
                this.WhenAnyValue(x => x.IsRunning, r => !r)
            );
            StopCommand = ReactiveCommand.CreateFromTask(
                StopAsync,
                this.WhenAnyValue(x => x.IsRunning)
            );
            SendCommand = ReactiveCommand.CreateFromTask(
                SendAsync,
                this.WhenAnyValue(x => x.IsRunning)
            );
            AddRuleCommand = ReactiveCommand.Create(() =>
                Rules.Add(new AutoReplyRuleViewModel(r => Rules.Remove(r))));
            RemoveRuleCommand = ReactiveCommand.Create<AutoReplyRuleViewModel>(r => Rules.Remove(r));
            ClearLogCommand = ReactiveCommand.Create(ClearLog);
        }

        // ───────────── 标识与配置 ─────────────

        private string _label = "";
        public string Label
        {
            get => _label;
            set => this.RaiseAndSetIfChanged(ref _label, value);
        }

        private ProtocolTransportOption _transport;
        public ProtocolTransportOption Transport
        {
            get => _transport;
            set
            {
                this.RaiseAndSetIfChanged(ref _transport, value);
                RaiseFieldVisibility();
            }
        }

        private ProtocolFrameOption _frameMode;
        public ProtocolFrameOption FrameMode
        {
            get => _frameMode;
            set
            {
                this.RaiseAndSetIfChanged(ref _frameMode, value);
                RaiseFieldVisibility();
            }
        }

        private string _host = "127.0.0.1";
        public string Host
        {
            get => _host;
            set => this.RaiseAndSetIfChanged(ref _host, value);
        }

        private int _port = 9000;
        public int Port
        {
            get => _port;
            set => this.RaiseAndSetIfChanged(ref _port, value);
        }

        private string _serialDevice = "COM3";
        public string SerialDevice
        {
            get => _serialDevice;
            set => this.RaiseAndSetIfChanged(ref _serialDevice, value);
        }

        private int _baud = 115200;
        public int Baud
        {
            get => _baud;
            set => this.RaiseAndSetIfChanged(ref _baud, value);
        }

        private string _delimiter = "\\r\\n";
        public string Delimiter
        {
            get => _delimiter;
            set => this.RaiseAndSetIfChanged(ref _delimiter, value);
        }

        private int _prefixBytes = 4;
        public int PrefixBytes
        {
            get => _prefixBytes;
            set => this.RaiseAndSetIfChanged(ref _prefixBytes, value);
        }

        private int _fixedSize = 8;
        public int FixedSize
        {
            get => _fixedSize;
            set => this.RaiseAndSetIfChanged(ref _fixedSize, value);
        }

        public bool ShowHost => Transport.NeedsHost;
        public bool ShowPort => Transport.Transport != "serial";
        public bool ShowSerial => Transport.Transport == "serial";
        public bool ShowDelimiter => FrameMode.Mode == "delimiter";
        public bool ShowPrefixBytes => FrameMode.Mode == "length-prefix";
        public bool ShowFixedSize => FrameMode.Mode == "fixed";

        private void RaiseFieldVisibility()
        {
            this.RaisePropertyChanged(nameof(ShowHost));
            this.RaisePropertyChanged(nameof(ShowPort));
            this.RaisePropertyChanged(nameof(ShowSerial));
            this.RaisePropertyChanged(nameof(ShowDelimiter));
            this.RaisePropertyChanged(nameof(ShowPrefixBytes));
            this.RaisePropertyChanged(nameof(ShowFixedSize));
        }

        // ───────────── 运行态与统计 ─────────────

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isRunning, value);
                this.RaisePropertyChanged(nameof(StatusText));
            }
        }

        private string _statusText = "未启动";
        public string StatusText
        {
            get => _statusText;
            private set => this.RaiseAndSetIfChanged(ref _statusText, value);
        }

        private long _rxCount, _txCount, _rxBytes, _txBytes;

        /// <summary>收发统计 —— 调试时最常问"到底有没有在收"。</summary>
        public string StatsText =>
            $"收 {_rxCount} 条 / {_rxBytes} B    发 {_txCount} 条 / {_txBytes} B";

        private void RaiseStats() => this.RaisePropertyChanged(nameof(StatsText));

        // ───────────── 日志 (带过滤) ─────────────

        private readonly List<ProtocolLogEntry> _all = new();

        /// <summary>UI 绑定的集合 (只含通过过滤的条目)。</summary>
        public ObservableCollection<ProtocolLogEntry> Messages { get; } = new();

        private string _filterText = "";
        public string FilterText
        {
            get => _filterText;
            set
            {
                this.RaiseAndSetIfChanged(ref _filterText, value);
                RebuildVisible();
            }
        }

        private bool MatchesFilter(ProtocolLogEntry e) =>
            string.IsNullOrWhiteSpace(_filterText)
            || (e.Text?.Contains(_filterText, StringComparison.OrdinalIgnoreCase) ?? false)
            || (e.Hex?.Contains(_filterText, StringComparison.OrdinalIgnoreCase) ?? false)
            || (e.Direction?.Contains(_filterText, StringComparison.OrdinalIgnoreCase) ?? false);

        private void AddEntry(ProtocolLogEntry entry)
        {
            _all.Add(entry);
            if (_all.Count > MaxLogEntries)
                _all.RemoveRange(0, _all.Count - MaxLogEntries);

            if (MatchesFilter(entry))
            {
                Messages.Add(entry);
                // 可见集合同样封顶 (过滤为空时两者等长)
                if (Messages.Count > MaxLogEntries)
                    Messages.RemoveAt(0);
            }
        }

        private void RebuildVisible()
        {
            Messages.Clear();
            foreach (var e in _all)
            {
                if (MatchesFilter(e))
                    Messages.Add(e);
            }
        }

        public void ClearLog()
        {
            _all.Clear();
            Messages.Clear();
        }

        /// <summary>导出当前可见日志 (文本)。</summary>
        public string ExportText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# 协议会话 {Label} ({Transport.Label})  导出 {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"# {StatsText}");
            sb.AppendLine("# 时间\t方向\t字节\t内容\tHEX");
            foreach (var e in Messages)
                sb.AppendLine($"{e.Time}\t{e.Direction}\t{e.Size}\t{e.Text}\t{e.Hex}");
            return sb.ToString();
        }

        // ───────────── 发送 ─────────────

        private string _sendText = "{\"Cmd\":\"Ping\"}";
        public string SendText
        {
            get => _sendText;
            set => this.RaiseAndSetIfChanged(ref _sendText, value);
        }

        private string _sendEncoding = "UTF-8";
        public string SendEncoding
        {
            get => _sendEncoding;
            set => this.RaiseAndSetIfChanged(ref _sendEncoding, value);
        }

        // ───────────── 定时发送 ─────────────

        private bool _periodicEnabled;
        public bool PeriodicEnabled
        {
            get => _periodicEnabled;
            set => this.RaiseAndSetIfChanged(ref _periodicEnabled, value);
        }

        private int _periodicIntervalMs = 1000;
        public int PeriodicIntervalMs
        {
            get => _periodicIntervalMs;
            set => this.RaiseAndSetIfChanged(ref _periodicIntervalMs, value);
        }

        // ───────────── 自动应答 ─────────────

        public ObservableCollection<AutoReplyRuleViewModel> Rules { get; } = new();

        public ReactiveCommand<Unit, Unit> AddRuleCommand { get; }
        public ReactiveCommand<AutoReplyRuleViewModel, Unit> RemoveRuleCommand { get; }

        // ───────────── 命令 ─────────────

        public ReactiveCommand<Unit, Unit> StartCommand { get; }
        public ReactiveCommand<Unit, Unit> StopCommand { get; }
        public ReactiveCommand<Unit, Unit> SendCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearLogCommand { get; }

        private IProtocolEndpoint? _endpoint;
        private CancellationTokenSource? _cts;

        private FrameCodec BuildCodec()
        {
            var spec = FrameMode.Mode switch
            {
                "delimiter" => new FrameSpec
                {
                    Mode = "delimiter",
                    Delimiter = Unescape(Delimiter),
                    DelimiterEncoding = "utf8",
                },
                "length-prefix" => new FrameSpec
                {
                    Mode = "length-prefix",
                    PrefixBytes = PrefixBytes,
                    Endian = "big",
                },
                "fixed" => new FrameSpec { Mode = "fixed", Size = FixedSize },
                _ => null,
            };
            return ProtocolEndpointFactory.BuildCodec(spec);
        }

        /// <summary>把用户输入的 \r\n 等转义还原成真实字符。</summary>
        private static string Unescape(string s) =>
            (s ?? "").Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\\", "\\");

        private async Task StartAsync()
        {
            try
            {
                _cts = new CancellationTokenSource();
                _endpoint = ProtocolEndpointFactory.Create(
                    Transport.Transport,
                    Transport.IsListener,
                    BuildCodec(),
                    SerialDevice,
                    Baud
                );
                await _endpoint.OpenAsync(Host, Port, _cts.Token).ConfigureAwait(true);

                IsRunning = true;
                var actual = Transport.Transport == "serial"
                    ? SerialDevice
                    : _endpoint.Port.ToString(CultureInfo.InvariantCulture);
                StatusText = $"已启动 · {actual}";
                AddEntry(ProtocolLogEntry.Info($"已启动 {Transport.Label} @ {actual}"));

                _ = ReceiveLoopAsync(_endpoint, _cts.Token);
                if (PeriodicEnabled)
                    _ = PeriodicLoopAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                StatusText = "启动失败: " + ex.Message;
                AddEntry(ProtocolLogEntry.Info("启动失败: " + ex.Message));
                await StopAsync().ConfigureAwait(true);
            }
        }

        private async Task StopAsync()
        {
            try
            {
                _cts?.Cancel();
            }
            catch
            {
                /* ignore */
            }

            var ep = _endpoint;
            _endpoint = null;
            try
            {
                ep?.Dispose();
            }
            catch
            {
                /* ignore */
            }

            _cts?.Dispose();
            _cts = null;
            IsRunning = false;
            StatusText = "已停止";
            AddEntry(ProtocolLogEntry.Info("已停止"));
        }

        private async Task ReceiveLoopAsync(IProtocolEndpoint ep, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                byte[]? data;
                try
                {
                    data = await ep.ReceiveAsync(TimeSpan.FromMilliseconds(300), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    var msg = ex.Message;
                    Dispatcher.UIThread.Post(() =>
                    {
                        AddEntry(ProtocolLogEntry.Info("接收结束: " + msg));
                        if (IsRunning)
                            StatusText = "接收中断: " + msg;
                    });
                    break;
                }

                if (data is null)
                    continue;

                var frame = data;
                Dispatcher.UIThread.Post(() =>
                {
                    _rxCount++;
                    _rxBytes += frame.Length;
                    RaiseStats();
                    AddEntry(ProtocolLogEntry.Frame("收", "", frame, null));
                });

                await TryAutoReplyAsync(frame, ct).ConfigureAwait(false);
            }
        }

        private async Task PeriodicLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(Math.Max(50, PeriodicIntervalMs), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (!PeriodicEnabled)
                    continue;

                var ep = _endpoint;
                if (ep is null)
                    continue;

                try
                {
                    var bytes = EncodeSend(SendText, SendEncoding);
                    await ep.SendAsync(bytes, ct).ConfigureAwait(false);
                    Dispatcher.UIThread.Post(() =>
                    {
                        _txCount++;
                        _txBytes += bytes.Length;
                        RaiseStats();
                        AddEntry(ProtocolLogEntry.Frame("发", "定时", bytes, null));
                    });
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    var msg = ex.Message;
                    Dispatcher.UIThread.Post(() => AddEntry(ProtocolLogEntry.Info("定时发送失败: " + msg)));
                    break;
                }
            }
        }

        private async Task SendAsync()
        {
            var ep = _endpoint;
            if (ep is null)
                return;
            try
            {
                var bytes = EncodeSend(SendText, SendEncoding);
                await ep.SendAsync(bytes, _cts?.Token ?? CancellationToken.None).ConfigureAwait(true);
                _txCount++;
                _txBytes += bytes.Length;
                RaiseStats();
                AddEntry(ProtocolLogEntry.Frame("发", "", bytes, null));
            }
            catch (Exception ex)
            {
                AddEntry(ProtocolLogEntry.Info("发送失败: " + ex.Message));
            }
        }

        // ── 自动应答 (模拟对端) ──

        private int _autoRepliesThisSecond;
        private long _autoReplyWindowStartTicks;

        private async Task TryAutoReplyAsync(byte[] frame, CancellationToken ct)
        {
            if (Rules.Count == 0)
                return;

            var text = Encoding.UTF8.GetString(frame);
            foreach (var rule in Rules.ToList())
            {
                if (!rule.Enabled)
                    continue;
                if (!rule.Matches(text))
                    continue;

                // 限速: 两端都开自动应答时, 否则会无限互相回声把链路打满
                var now = Environment.TickCount64;
                if (now - _autoReplyWindowStartTicks > 1000)
                {
                    _autoReplyWindowStartTicks = now;
                    _autoRepliesThisSecond = 0;
                }
                if (++_autoRepliesThisSecond > MaxAutoRepliesPerSecond)
                {
                    Dispatcher.UIThread.Post(() =>
                        AddEntry(ProtocolLogEntry.Info("自动应答已限速 (疑似两端互相回声), 本秒不再应答"))
                    );
                    return;
                }

                var ep = _endpoint;
                if (ep is null)
                    return;
                try
                {
                    var bytes = EncodeSend(rule.ReplyText, rule.ReplyEncoding);
                    await ep.SendAsync(bytes, ct).ConfigureAwait(false);
                    Dispatcher.UIThread.Post(() =>
                    {
                        _txCount++;
                        _txBytes += bytes.Length;
                        RaiseStats();
                        AddEntry(ProtocolLogEntry.AutoReply(bytes));
                    });
                }
                catch (Exception ex)
                {
                    var msg = ex.Message;
                    Dispatcher.UIThread.Post(() => AddEntry(ProtocolLogEntry.Info("自动应答失败: " + msg)));
                }
                return; // 只按第一条命中的规则应答
            }
        }

        public static byte[] EncodeSend(string text, string encoding)
        {
            if (string.Equals(encoding, "HEX", StringComparison.OrdinalIgnoreCase))
            {
                var cleaned = (text ?? "").Replace(" ", "").Replace("-", "").Replace("\r", "").Replace("\n", "");
                return cleaned.Length == 0 ? Array.Empty<byte>() : Convert.FromHexString(cleaned);
            }
            return Encoding.UTF8.GetBytes(text ?? "");
        }

        public void Dispose()
        {
            try
            {
                _cts?.Cancel();
                _endpoint?.Dispose();
                _cts?.Dispose();
            }
            catch
            {
                /* 关闭窗口时的清理不应抛 */
            }
        }
    }

    /// <summary>
    /// 自动应答规则: 收到满足条件的帧时自动回一条 —— 用于**模拟对端**,
    /// 在没有真实设备时把链路跑通 (例如回 "OK" 给 "AT")。
    /// </summary>
    public sealed class AutoReplyRuleViewModel : ViewModelBase
    {
        private readonly Action<AutoReplyRuleViewModel>? _remove;

        public AutoReplyRuleViewModel(Action<AutoReplyRuleViewModel>? remove = null)
        {
            _remove = remove;
            RemoveCommand = ReactiveCommand.Create(() => _remove?.Invoke(this));
        }

        public ReactiveCommand<Unit, Unit> RemoveCommand { get; }

        private bool _enabled = true;
        public bool Enabled
        {
            get => _enabled;
            set => this.RaiseAndSetIfChanged(ref _enabled, value);
        }

        private string _matchType = "包含";
        public string MatchType
        {
            get => _matchType;
            set => this.RaiseAndSetIfChanged(ref _matchType, value);
        }

        private string _matchValue = "";
        public string MatchValue
        {
            get => _matchValue;
            set => this.RaiseAndSetIfChanged(ref _matchValue, value);
        }

        private string _replyText = "OK";
        public string ReplyText
        {
            get => _replyText;
            set => this.RaiseAndSetIfChanged(ref _replyText, value);
        }

        private string _replyEncoding = "UTF-8";
        public string ReplyEncoding
        {
            get => _replyEncoding;
            set => this.RaiseAndSetIfChanged(ref _replyEncoding, value);
        }

        public bool Matches(string text)
        {
            var mode = ProtocolUiOptions.MatchTypeToMode(MatchType);
            try
            {
                return mode switch
                {
                    "regex" => Regex.IsMatch(text, MatchValue),
                    "equals" => text == MatchValue,
                    "startsWith" => text.StartsWith(MatchValue, StringComparison.Ordinal),
                    "endsWith" => text.EndsWith(MatchValue, StringComparison.Ordinal),
                    _ => text.Contains(MatchValue, StringComparison.Ordinal),
                };
            }
            catch
            {
                // 正则写错不应让接收循环崩
                return false;
            }
        }
    }
}
