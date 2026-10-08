using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
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
    /// 一个独立的协议会话 (一路连接) —— 通用调试台的基本单位。
    ///
    /// <para>对标主流同类工具 (NetAssist / SSCOM / Hercules) 的核心工作面:</para>
    /// <list type="bullet">
    /// <item>多客户端服务端的**对端列表** + 定向/广播发送;</item>
    /// <item>**快捷指令列表** (多条预设, 点即发), 每条可**独立定时循环**;</item>
    /// <item>编码 UTF-8/GBK/ASCII/HEX + **发送新行** (\r\n / \n / 无);</item>
    /// <item>自动应答 (模拟对端) / 循环发送 / 日志过滤与导出 / 收发统计;</item>
    /// <item>配置可持久化 (见 <see cref="ProtocolWorkspace"/>)。</item>
    /// </list>
    ///
    /// <para>线程模型: 收帧与调度在线程池上跑, 每条日志与统计都 Post 回 UI 线程再改集合 ——
    /// 后台线程直接改 ObservableCollection 会崩。</para>
    /// </summary>
    public sealed class ProtocolSessionViewModel : ViewModelBase, IDisposable
    {
        /// <summary>日志上限: 调试台会长时间开着, 不设上限必然吃光内存。</summary>
        public const int MaxLogEntries = 5000;

        /// <summary>自动应答限速: 防止两端互相应答形成无限回声。</summary>
        private const int MaxAutoRepliesPerSecond = 20;

        /// <summary>调度 tick: 定时发送与对端刷新的粒度。</summary>
        private const int TickMs = 100;

        public string Id { get; }

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
            AddQuickCommandCommand = ReactiveCommand.Create(() =>
                QuickCommands.Add(new QuickCommandViewModel(this, r => QuickCommands.Remove(r))));
            ClearLogCommand = ReactiveCommand.Create(ClearLog);
            ResetStatsCommand = ReactiveCommand.Create(ResetStats);
            SendBreakCommand = ReactiveCommand.Create(
                () => _endpoint?.SendBreak(),
                this.WhenAnyValue(x => x.IsRunning)
            );
            BrowseFileCommand = ReactiveCommand.CreateFromTask(BrowseFileAsync);
            SendFileCommand = ReactiveCommand.CreateFromTask(
                SendFileAsync,
                this.WhenAnyValue(x => x.IsRunning)
            );
        }

        // ───────────── 标识与配置 ─────────────

        private string _label = "";
        public string Label
        {
            get => _label;
            set => this.RaiseAndSetIfChanged(ref _label, value);
        }

        /// <summary>虚拟会话 (如"场景运行"): 没有真实端点, 只承载日志。</summary>
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

        /// <summary>多客户端服务端才会出现对端列表 (TCP / WebSocket 都是多客户端)。</summary>
        public bool ShowPeers => Transport.Transport is "tcp-server" or "websocket-server";

        private void RaiseFieldVisibility()
        {
            this.RaisePropertyChanged(nameof(ShowHost));
            this.RaisePropertyChanged(nameof(ShowPort));
            this.RaisePropertyChanged(nameof(ShowSerial));
            this.RaisePropertyChanged(nameof(ShowDelimiter));
            this.RaisePropertyChanged(nameof(ShowPrefixBytes));
            this.RaisePropertyChanged(nameof(ShowFixedSize));
            this.RaisePropertyChanged(nameof(ShowPeers));
            this.RaisePropertyChanged(nameof(ShowSerialControls));
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

        /// <summary>统计清零 (主流工具都有; 观察某一段流量时必用)。</summary>
        public void ResetStats()
        {
            _rxCount = _txCount = _rxBytes = _txBytes = 0;
            RaiseStats();
        }

        // ───────────── 对端 (多客户端服务端) ─────────────

        public ObservableCollection<string> Peers { get; } = new();

        private string? _selectedPeer;
        public string? SelectedPeer
        {
            get => _selectedPeer;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedPeer, value);
                if (_endpoint is not null)
                    _endpoint.SelectedPeer = value;
            }
        }

        public string PeerSummary =>
            Peers.Count == 0 ? "无对端" : $"对端 {Peers.Count}";

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

        /// <summary>暂停显示: 仍在收/仍在记, 只是不往界面刷 (高速流下保住可读性)。</summary>
        private bool _pauseDisplay;
        public bool PauseDisplay
        {
            get => _pauseDisplay;
            set => this.RaiseAndSetIfChanged(ref _pauseDisplay, value);
        }

        private bool _showTimestamp = true;
        public bool ShowTimestamp
        {
            get => _showTimestamp;
            set => this.RaiseAndSetIfChanged(ref _showTimestamp, value);
        }

        private bool _autoScroll = true;
        public bool AutoScroll
        {
            get => _autoScroll;
            set => this.RaiseAndSetIfChanged(ref _autoScroll, value);
        }

        /// <summary>日志内容显示方式 (主流调试台的"HEX 显示"开关)。</summary>
        public IReadOnlyList<string> ContentModes { get; } =
            new[] { "文本+HEX", "仅HEX", "HEX转储", "仅文本" };

        private string _contentModeLabel = "文本+HEX";
        public string ContentModeLabel
        {
            get => _contentModeLabel;
            set
            {
                this.RaiseAndSetIfChanged(ref _contentModeLabel, value);
                // 已经显示的行要一起切换, 否则"切了没反应"很反直觉
                var mode = CurrentContentMode();
                foreach (var e in _all)
                    e.DisplayMode = mode;
            }
        }

        private ProtocolContentMode CurrentContentMode() =>
            _contentModeLabel switch
            {
                "仅HEX" => ProtocolContentMode.HexOnly,
                "HEX转储" => ProtocolContentMode.Dump,
                "仅文本" => ProtocolContentMode.Text,
                _ => ProtocolContentMode.TextAndHex,
            };

        private bool MatchesFilter(ProtocolLogEntry e) =>
            string.IsNullOrWhiteSpace(_filterText)
            || (e.PlainText?.Contains(_filterText, StringComparison.OrdinalIgnoreCase) ?? false)
            || (e.HexText?.Contains(_filterText, StringComparison.OrdinalIgnoreCase) ?? false)
            || (e.Direction?.Contains(_filterText, StringComparison.OrdinalIgnoreCase) ?? false)
            || (e.Channel?.Contains(_filterText, StringComparison.OrdinalIgnoreCase) ?? false);

        // ── 时序: Δt(与上一次同方向帧的间隔) 与 RTT(上次发送→本次收到) ──

        private long _lastRxTicks;
        private long _lastTxTicks;
        private long _awaitingReplyAt;

        private void StampTiming(ProtocolLogEntry entry, long now)
        {
            if (!entry.IsReceived && !entry.IsSent)
                return;

            if (entry.IsReceived)
            {
                if (_lastRxTicks != 0)
                    entry.DeltaMs = now - _lastRxTicks;
                if (_awaitingReplyAt != 0)
                {
                    entry.RttMs = now - _awaitingReplyAt;
                    _awaitingReplyAt = 0;
                }
                _lastRxTicks = now;
            }
            else
            {
                if (_lastTxTicks != 0)
                    entry.DeltaMs = now - _lastTxTicks;
                _lastTxTicks = now;
                _awaitingReplyAt = now;
            }
        }

        private void ResetTiming()
        {
            _lastRxTicks = _lastTxTicks = _awaitingReplyAt = 0;
        }

        private void AddEntry(ProtocolLogEntry entry)
        {
            entry.DisplayMode = CurrentContentMode();
            StampTiming(entry, Environment.TickCount64);

            _all.Add(entry);
            if (_all.Count > MaxLogEntries)
                _all.RemoveRange(0, _all.Count - MaxLogEntries);

            if (PauseDisplay || !MatchesFilter(entry))
                return;

            Messages.Add(entry);
            if (Messages.Count > MaxLogEntries)
                Messages.RemoveAt(0);
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
            sb.AppendLine("# 时间\t方向\t对端\t字节\tΔt(ms)\tRTT(ms)\t内容\tHEX");
            foreach (var e in Messages)
                sb.AppendLine(
                    $"{e.Time}\t{e.Direction}\t{e.Channel}\t{e.Size}\t{e.DeltaText}\t{e.RttText}\t{e.PlainText}\t{e.HexText}"
                );
            return sb.ToString();
        }

        /// <summary>导出 CSV (Excel 可直接打开; 用引号转义避免内容里的逗号/引号破坏列)。</summary>
        public string ExportCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("time,direction,peer,bytes,delta_ms,rtt_ms,text,hex");
            foreach (var e in Messages)
            {
                sb.AppendLine(
                    string.Join(
                        ",",
                        Csv(e.Time), Csv(e.Direction), Csv(e.Channel), Csv(e.Size),
                        Csv(e.DeltaText), Csv(e.RttText), Csv(e.PlainText), Csv(e.HexText)
                    )
                );
            }
            return sb.ToString();
        }

        private static string Csv(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

        /// <summary>导出 JSON (便于脚本/AI 直接消费)。</summary>
        public string ExportJson()
        {
            var rows = Messages.Select(e => new
            {
                time = e.Time,
                direction = e.Direction,
                peer = e.Channel,
                bytes = int.TryParse(e.Size, out var n) ? n : 0,
                delta_ms = e.DeltaMs,
                rtt_ms = e.RttMs,
                text = e.PlainText,
                hex = e.HexText,
            });
            return System.Text.Json.JsonSerializer.Serialize(
                rows,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                }
            );
        }

        // ───────────── 发送 ─────────────

        private string _sendText = "{\"Cmd\":\"Ping\"}";
        public string SendText
        {
            get => _sendText;
            set
            {
                this.RaiseAndSetIfChanged(ref _sendText, value);
                this.RaisePropertyChanged(nameof(ChecksumPreview));
            }
        }

        private string _sendEncoding = "UTF-8";
        public string SendEncoding
        {
            get => _sendEncoding;
            set
            {
                this.RaiseAndSetIfChanged(ref _sendEncoding, value);
                this.RaisePropertyChanged(nameof(ChecksumPreview));
            }
        }

        private string _sendNewLine = "无";
        public string SendNewLine
        {
            get => _sendNewLine;
            set => this.RaiseAndSetIfChanged(ref _sendNewLine, value);
        }

        /// <summary>循环发送 (对发送框里的内容)。</summary>
        private bool _loopSend;
        public bool LoopSend
        {
            get => _loopSend;
            set => this.RaiseAndSetIfChanged(ref _loopSend, value);
        }

        private int _loopIntervalMs = 1000;
        public int LoopIntervalMs
        {
            get => _loopIntervalMs;
            set => this.RaiseAndSetIfChanged(ref _loopIntervalMs, value);
        }

        // ───────────── 校验和 (工业协议刚需) ─────────────

        public IReadOnlyList<string> ChecksumModes => Checksums.Names;

        private string _checksumMode = "无";
        public string ChecksumMode
        {
            get => _checksumMode;
            set
            {
                this.RaiseAndSetIfChanged(ref _checksumMode, value);
                this.RaisePropertyChanged(nameof(ChecksumPreview));
            }
        }

        private bool _appendChecksum;
        public bool AppendChecksum
        {
            get => _appendChecksum;
            set => this.RaiseAndSetIfChanged(ref _appendChecksum, value);
        }

        /// <summary>实时显示当前发送内容算出的校验值 (填对之前先看一眼)。</summary>
        public string ChecksumPreview
        {
            get
            {
                if (_checksumMode == "无")
                    return "";
                try
                {
                    var payload = ProtocolEncoding.Encode(SendText, SendEncoding);
                    var cs = Checksums.Compute(_checksumMode, payload);
                    return cs.Length == 0 ? "" : Convert.ToHexString(cs);
                }
                catch
                {
                    return "(内容无法编码)";
                }
            }
        }

        // ───────────── 发送文件 ─────────────

        private string _filePath = "";
        public string FilePath
        {
            get => _filePath;
            set => this.RaiseAndSetIfChanged(ref _filePath, value);
        }

        private int _fileChunkSize;
        /// <summary>分块大小 (字节); 0 = 整份发送。大文件/慢设备按块发更贴近真实协议。</summary>
        public int FileChunkSize
        {
            get => _fileChunkSize;
            set => this.RaiseAndSetIfChanged(ref _fileChunkSize, value);
        }

        private int _fileChunkIntervalMs = 50;
        public int FileChunkIntervalMs
        {
            get => _fileChunkIntervalMs;
            set => this.RaiseAndSetIfChanged(ref _fileChunkIntervalMs, value);
        }

        /// <summary>由窗口注入的文件选择器 (VM 不依赖 UI, 便于测试)。</summary>
        public Func<Task<string?>>? FilePicker { get; set; }

        private async Task BrowseFileAsync()
        {
            if (FilePicker is null)
                return;
            try
            {
                var picked = await FilePicker().ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(picked))
                    FilePath = picked;
            }
            catch (Exception ex)
            {
                AddEntry(ProtocolLogEntry.Info("选择文件失败: " + ex.Message));
            }
        }

        /// <summary>发送文件内容 (原始字节, 不走编码/新行/校验设置)。</summary>
        private async Task SendFileAsync()
        {
            var ep = _endpoint;
            if (ep is null)
                return;

            if (string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath))
            {
                AddEntry(ProtocolLogEntry.Info("文件不存在: " + FilePath));
                return;
            }

            try
            {
                AddEntry(ProtocolLogEntry.Info("开始发送文件: " + Path.GetFileName(FilePath)));

                // 发送循环**不占 UI 线程**: 大文件/多分块时若把续体挂在 dispatcher 上,
                // 界面会卡, 且 headless 下线程池 Post 回来的续体不保证被执行 (会假死)。
                // 这里全部 ConfigureAwait(false), 只有"更新界面"才 Post 回 UI 线程。
                var bytes = await File.ReadAllBytesAsync(FilePath).ConfigureAwait(false);
                var chunk = FileChunkSize > 0 ? FileChunkSize : bytes.Length;
                var ct = _cts?.Token ?? CancellationToken.None;
                var sent = 0;

                for (int off = 0; off < bytes.Length; off += chunk)
                {
                    var len = Math.Min(chunk, bytes.Length - off);
                    var part = new byte[len];
                    Buffer.BlockCopy(bytes, off, part, 0, len);

                    await ep.SendAsync(part, ct).ConfigureAwait(false);

                    Dispatcher.UIThread.Post(() =>
                    {
                        _txCount++;
                        _txBytes += len;
                        RaiseStats();
                        AddEntry(ProtocolLogEntry.Frame("发", "文件", part, null));
                    });

                    sent += len;

                    if (off + len < bytes.Length && FileChunkIntervalMs > 0)
                        await Task.Delay(FileChunkIntervalMs, ct).ConfigureAwait(false);
                }

                var total = sent;
                var per = chunk;
                Dispatcher.UIThread.Post(() =>
                    AddEntry(ProtocolLogEntry.Info($"文件已发送: {total} 字节 (每包 {per} 字节)"))
                );
            }
            catch (OperationCanceledException)
            {
                /* 停止中 */
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                Dispatcher.UIThread.Post(() =>
                    AddEntry(ProtocolLogEntry.Info("发送文件失败: " + msg))
                );
            }
        }

        // ───────────── 串口控制线 ─────────────

        public bool ShowSerialControls => Transport.Transport == "serial";

        public bool? Dtr
        {
            get => _endpoint?.Dtr;
            set
            {
                if (_endpoint is not null)
                    _endpoint.Dtr = value;
                this.RaisePropertyChanged();
            }
        }

        public bool? Rts
        {
            get => _endpoint?.Rts;
            set
            {
                if (_endpoint is not null)
                    _endpoint.Rts = value;
                this.RaisePropertyChanged();
            }
        }

        // ───────────── 快捷指令 ─────────────

        public ObservableCollection<QuickCommandViewModel> QuickCommands { get; } = new();

        private bool _quickCommandsExpanded;

        /// <summary>「快捷指令」区段是否展开 —— 辅助区段默认收起, 把纵向空间让给日志。</summary>
        public bool QuickCommandsExpanded
        {
            get => _quickCommandsExpanded;
            set => this.RaiseAndSetIfChanged(ref _quickCommandsExpanded, value);
        }

        public ReactiveCommand<Unit, Unit> AddQuickCommandCommand { get; }

        // ───────────── 自动应答 ─────────────

        public ObservableCollection<AutoReplyRuleViewModel> Rules { get; } = new();

        private bool _autoReplyExpanded;

        /// <summary>「自动应答」区段是否展开 (默认收起, 同上)。</summary>
        public bool AutoReplyExpanded
        {
            get => _autoReplyExpanded;
            set => this.RaiseAndSetIfChanged(ref _autoReplyExpanded, value);
        }

        public ReactiveCommand<Unit, Unit> AddRuleCommand { get; }

        // ───────────── 命令 ─────────────

        public ReactiveCommand<Unit, Unit> StartCommand { get; }
        public ReactiveCommand<Unit, Unit> StopCommand { get; }
        public ReactiveCommand<Unit, Unit> SendCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearLogCommand { get; }
        public ReactiveCommand<Unit, Unit> ResetStatsCommand { get; }
        public ReactiveCommand<Unit, Unit> SendBreakCommand { get; }
        public ReactiveCommand<Unit, Unit> BrowseFileCommand { get; }
        public ReactiveCommand<Unit, Unit> SendFileCommand { get; }

        private IProtocolEndpoint? _endpoint;
        private CancellationTokenSource? _cts;
        private Task? _receiveLoop;
        private Task? _schedulerLoop;

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
                _endpoint.SelectedPeer = SelectedPeer;
                await _endpoint.OpenAsync(Host, Port, _cts.Token).ConfigureAwait(true);

                IsRunning = true;
                ResetTiming();
                var actual = Transport.Transport == "serial"
                    ? SerialDevice
                    : _endpoint.Port.ToString(CultureInfo.InvariantCulture);
                StatusText = $"已启动 · {actual}";
                AddEntry(ProtocolLogEntry.Info($"已启动 {Transport.Label} @ {actual}"));

                var ct = _cts.Token;
                _receiveLoop = ReceiveLoopAsync(_endpoint, ct);
                _schedulerLoop = SchedulerLoopAsync(ct);
            }
            catch (Exception ex)
            {
                StatusText = "启动失败: " + ex.Message;
                AddEntry(ProtocolLogEntry.Info("启动失败: " + ex.Message));
                await StopAsync().ConfigureAwait(true);
            }
        }

        /// <summary>
        /// 停止: 先取消, **等两个循环真正退出**, 再释放端点与 CTS。
        ///
        /// <para>不能"取消完立刻 Dispose": 在途的 Task.Delay(ct)/Socket.ReceiveAsync(ct)
        /// 会撞上已释放的 CTS/端点, 冒出 ObjectDisposedException 这类无意义报错,
        /// 且紧接着重启会与旧循环抢同一个端点字段。</para>
        /// </summary>
        private async Task StopAsync()
        {
            var cts = _cts;
            var ep = _endpoint;
            _cts = null;
            _endpoint = null;

            try
            {
                cts?.Cancel();
            }
            catch
            {
                /* ignore */
            }

            foreach (var loop in new[] { _receiveLoop, _schedulerLoop })
            {
                if (loop is null)
                    continue;
                try
                {
                    await loop.ConfigureAwait(true);
                }
                catch
                {
                    /* 循环内的异常已在循环里处理 */
                }
            }
            _receiveLoop = null;
            _schedulerLoop = null;

            try
            {
                ep?.Dispose();
            }
            catch
            {
                /* ignore */
            }
            try
            {
                cts?.Dispose();
            }
            catch
            {
                /* ignore */
            }

            IsRunning = false;
            ResetTiming();
            StatusText = "已停止";
            Peers.Clear();
            RaisePeerSummary();
            AddEntry(ProtocolLogEntry.Info("已停止"));
        }

        private void RaisePeerSummary() => this.RaisePropertyChanged(nameof(PeerSummary));

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
                catch (ObjectDisposedException)
                {
                    break; // 正常停止
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
                var peer = ep.LastReceivedPeer ?? "";
                Dispatcher.UIThread.Post(() =>
                {
                    _rxCount++;
                    _rxBytes += frame.Length;
                    RaiseStats();
                    AddEntry(ProtocolLogEntry.Frame("收", peer, frame, null));
                });

                await TryAutoReplyAsync(frame, ct).ConfigureAwait(false);
            }
        }

        /// <summary>一个 tick 循环同时管: 发送框循环 / 各快捷指令循环 / 对端列表刷新。</summary>
        private async Task SchedulerLoopAsync(CancellationToken ct)
        {
            var nextSend = Environment.TickCount64 + Math.Max(50, LoopIntervalMs);
            var nextTick = Environment.TickCount64 + 500;
            var cmdNext = new Dictionary<QuickCommandViewModel, long>();

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TickMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var now = Environment.TickCount64;

                if (LoopSend && now >= nextSend)
                {
                    nextSend = now + Math.Max(50, LoopIntervalMs);
                    await SendPayloadAsync(SendText, SendEncoding, SendNewLine, "定时", ct).ConfigureAwait(false);
                }

                foreach (var cmd in QuickCommands.ToList())
                {
                    if (!cmd.PeriodicEnabled)
                    {
                        cmdNext.Remove(cmd);
                        continue;
                    }
                    if (!cmdNext.TryGetValue(cmd, out var due))
                    {
                        due = now + Math.Max(50, cmd.IntervalMs);
                        cmdNext[cmd] = due;
                        continue;
                    }
                    if (now < due)
                        continue;
                    cmdNext[cmd] = now + Math.Max(50, cmd.IntervalMs);
                    await SendPayloadAsync(cmd.Text, cmd.Encoding, cmd.NewLine, "定时", ct).ConfigureAwait(false);
                }

                if (now >= nextTick)
                {
                    nextTick = now + 500;
                    RefreshPeers();
                }
            }
        }

        private void RefreshPeers()
        {
            var ep = _endpoint;
            if (ep is null)
                return;

            IReadOnlyList<string> current;
            try
            {
                current = ep.Peers;
            }
            catch
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (current.Count == Peers.Count && current.All(Peers.Contains))
                    return;

                Peers.Clear();
                foreach (var p in current)
                    Peers.Add(p);
                if (SelectedPeer is not null && !Peers.Contains(SelectedPeer))
                    SelectedPeer = null; // 目标已断开 → 回到广播
                RaisePeerSummary();
            });
        }

        private async Task SendAsync()
        {
            await SendPayloadAsync(SendText, SendEncoding, SendNewLine, "", CancellationToken.None)
                .ConfigureAwait(true);
        }

        /// <summary>统一的发送入口 (手动/循环/快捷指令共用, 保证编码与换行行为一致)。</summary>
        internal async Task SendPayloadAsync(
            string text,
            string encoding,
            string newLine,
            string channel,
            CancellationToken ct
        )
        {
            var ep = _endpoint;
            if (ep is null)
                return;

            try
            {
                var bytes = ProtocolEncoding.Encode(text + ProtocolUiOptions.NewLineToText(newLine), encoding);

                // 追加校验和: 工业协议常见做法 (先算净值再校验)
                if (AppendChecksum && ChecksumMode != "无")
                {
                    var cs = Checksums.Compute(ChecksumMode, bytes);
                    if (cs.Length > 0)
                        bytes = bytes.Concat(cs).ToArray();
                }

                await ep.SendAsync(bytes, ct).ConfigureAwait(true);
                _txCount++;
                _txBytes += bytes.Length;
                RaiseStats();
                AddEntry(ProtocolLogEntry.Frame("发", channel, bytes, null));
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

            foreach (var rule in Rules.ToList())
            {
                if (!rule.Enabled)
                    continue;
                if (!rule.Matches(frame))
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
                    var bytes = ProtocolEncoding.Encode(
                        rule.ReplyText + ProtocolUiOptions.NewLineToText(rule.ReplyNewLine),
                        rule.ReplyEncoding
                    );
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

        // ───────────── 持久化 ─────────────

        public ProtocolSessionState Snapshot() => new()
        {
            Label = Label,
            Transport = Transport.Transport,
            IsListener = Transport.IsListener,
            Host = Host,
            Port = Port,
            SerialDevice = SerialDevice,
            Baud = Baud,
            FrameMode = FrameMode.Mode,
            Delimiter = Delimiter,
            PrefixBytes = PrefixBytes,
            FixedSize = FixedSize,
            SendText = SendText,
            SendEncoding = SendEncoding,
            SendNewLine = SendNewLine,
            LoopSend = LoopSend,
            LoopIntervalMs = LoopIntervalMs,
            ShowTimestamp = ShowTimestamp,
            AutoScroll = AutoScroll,
            Rules = Rules.Select(r => new AutoReplyRuleState
            {
                Enabled = r.Enabled,
                MatchType = r.MatchType,
                MatchValue = r.MatchValue,
                ReplyText = r.ReplyText,
                ReplyEncoding = r.ReplyEncoding,
            }).ToList(),
            QuickCommands = QuickCommands.Select(q => new QuickCommandState
            {
                Text = q.Text,
                Encoding = q.Encoding,
                NewLine = q.NewLine,
                Periodic = q.PeriodicEnabled,
                IntervalMs = q.IntervalMs,
            }).ToList(),
        };

        public void Restore(ProtocolSessionState s)
        {
            var t = ProtocolUiOptions.Transports.FirstOrDefault(x =>
                x.Transport == s.Transport && x.IsListener == s.IsListener);
            if (t is not null)
                Transport = t;

            var f = ProtocolUiOptions.FrameModes.FirstOrDefault(x => x.Mode == s.FrameMode);
            if (f is not null)
                FrameMode = f;

            Label = string.IsNullOrWhiteSpace(s.Label) ? Label : s.Label;
            Host = s.Host;
            Port = s.Port;
            SerialDevice = s.SerialDevice;
            Baud = s.Baud;
            Delimiter = s.Delimiter;
            PrefixBytes = s.PrefixBytes;
            FixedSize = s.FixedSize;
            SendText = s.SendText;
            SendEncoding = s.SendEncoding;
            SendNewLine = s.SendNewLine;
            LoopSend = s.LoopSend;
            LoopIntervalMs = s.LoopIntervalMs;
            ShowTimestamp = s.ShowTimestamp;
            AutoScroll = s.AutoScroll;

            Rules.Clear();
            foreach (var r in s.Rules)
            {
                Rules.Add(new AutoReplyRuleViewModel(x => Rules.Remove(x))
                {
                    Enabled = r.Enabled,
                    MatchType = r.MatchType,
                    MatchValue = r.MatchValue,
                    ReplyText = r.ReplyText,
                    ReplyEncoding = r.ReplyEncoding,
                });
            }

            QuickCommands.Clear();
            foreach (var q in s.QuickCommands)
            {
                QuickCommands.Add(new QuickCommandViewModel(this, x => QuickCommands.Remove(x))
                {
                    Text = q.Text,
                    Encoding = q.Encoding,
                    NewLine = q.NewLine,
                    PeriodicEnabled = q.Periodic,
                    IntervalMs = q.IntervalMs,
                });
            }
        }

        public void Dispose()
        {
            // 只取消 + 释放端点, **不** Dispose CTS:
            // 循环里在途的 Task.Delay(..., ct) 若撞上已释放的 CTS 会抛 ObjectDisposedException,
            // 冒出无意义的报错。CTS 不含定时器, 留给 GC 即可 (Endpoint 一释放, 在途读即失败, 循环随即退出)。
            try
            {
                _cts?.Cancel();
            }
            catch
            {
                /* ignore */
            }
            _cts = null;

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
        }
    }

    /// <summary>
    /// 快捷指令: 一条预设报文, 点即发, 可独立定时循环。
    ///
    /// <para>主流调试台 (SSCOM / NetAssist) 的标志性功能 —— 现场往往要反复发同一组指令,
    /// 每条都手打一遍既不专业也容易打错。</para>
    /// </summary>
    public sealed class QuickCommandViewModel : ViewModelBase
    {
        private readonly ProtocolSessionViewModel _session;

        public QuickCommandViewModel(ProtocolSessionViewModel session, Action<QuickCommandViewModel>? remove = null)
        {
            _session = session;
            SendCommand = ReactiveCommand.CreateFromTask(() =>
                _session.SendPayloadAsync(Text, Encoding, NewLine, "快捷", CancellationToken.None)
            );
            RemoveCommand = ReactiveCommand.Create(() => remove?.Invoke(this));
        }

        public ReactiveCommand<Unit, Unit> SendCommand { get; }
        public ReactiveCommand<Unit, Unit> RemoveCommand { get; }

        private string _text = "";
        public string Text
        {
            get => _text;
            set => this.RaiseAndSetIfChanged(ref _text, value);
        }

        private string _encoding = "UTF-8";
        public string Encoding
        {
            get => _encoding;
            set => this.RaiseAndSetIfChanged(ref _encoding, value);
        }

        private string _newLine = "无";
        public string NewLine
        {
            get => _newLine;
            set => this.RaiseAndSetIfChanged(ref _newLine, value);
        }

        private bool _periodicEnabled;
        public bool PeriodicEnabled
        {
            get => _periodicEnabled;
            set => this.RaiseAndSetIfChanged(ref _periodicEnabled, value);
        }

        private int _intervalMs = 1000;
        public int IntervalMs
        {
            get => _intervalMs;
            set => this.RaiseAndSetIfChanged(ref _intervalMs, value);
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

        private string _replyNewLine = "无";
        public string ReplyNewLine
        {
            get => _replyNewLine;
            set => this.RaiseAndSetIfChanged(ref _replyNewLine, value);
        }

        /// <summary>
        /// 匹配一帧。**文本与十六进制都能匹配** —— 二进制协议 (无 ASCII 文本)
        /// 用文本匹配是匹配不上的, 主流工具都允许按字节匹配。
        /// </summary>
        public bool Matches(byte[] frame)
        {
            var mode = ProtocolUiOptions.MatchTypeToMode(MatchType);
            var text = ProtocolEncoding.DecodeForDisplay(frame);
            var hex = Convert.ToHexString(frame);
            var needle = MatchValue ?? "";

            // HEX 形式 (形如 "AA BB" / "AABB") 优先按字节比
            var needleHex = needle.Replace(" ", "").Replace("-", "");
            bool looksHex = needleHex.Length >= 2
                && needleHex.Length % 2 == 0
                && needleHex.All(Uri.IsHexDigit);

            try
            {
                if (looksHex)
                {
                    var hit = hex.Contains(needleHex, StringComparison.OrdinalIgnoreCase);
                    return mode == "equals" ? hex.Equals(needleHex, StringComparison.OrdinalIgnoreCase) : hit;
                }

                return mode switch
                {
                    "regex" => Regex.IsMatch(text, needle),
                    "equals" => text == needle,
                    "startsWith" => text.StartsWith(needle, StringComparison.Ordinal),
                    "endsWith" => text.EndsWith(needle, StringComparison.Ordinal),
                    _ => text.Contains(needle, StringComparison.Ordinal),
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
