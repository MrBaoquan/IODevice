using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using IOStudio.Services.ProtocolTest;
using ReactiveUI;

namespace IOStudio.ViewModels
{
    /// <summary>
    /// 协议测试台窗口 ViewModel —— **通用协议调试台**。
    ///
    /// <list type="bullet">
    /// <item>**多会话并行**: 每路 TCP/UDP/WebSocket/串口 独立配置、独立收发、独立统计;</item>
    /// <item>**自由收发**: 文本/HEX 随时发, 不必先写场景;</item>
    /// <item>**自动应答**: 规则化回包, 无真实设备时也能把链路跑通 (模拟对端);</item>
    /// <item>**定时发送**: 周期性发同一条, 用于心跳/压测;</item>
    /// <item>**场景**: 与 CLI <c>--protocol-test</c> 共用同一套执行器, 结论一致。</item>
    /// </list>
    /// </summary>
    public sealed class ProtocolTestViewModel : ViewModelBase, IProtocolTestObserver, IDisposable
    {
        public ObservableCollection<ProtocolSessionViewModel> Sessions { get; } = new();

        public IReadOnlyList<ProtocolTransportOption> Transports => ProtocolUiOptions.Transports;

        public ObservableCollection<string> Scenarios { get; } = new();

        public ProtocolTestViewModel()
        {
            LoadScenarios();

            NewSessionCommand = ReactiveCommand.CreateFromTask(NewSessionAsync);
            RemoveSessionCommand = ReactiveCommand.Create(
                RemoveSelectedSession,
                this.WhenAnyValue(x => x.SelectedSession, (ProtocolSessionViewModel? s) => s is not null)
            );
            ExportCommand = ReactiveCommand.Create(
                ExportLogs,
                this.WhenAnyValue(x => x.SelectedSession, (ProtocolSessionViewModel? s) => s is not null)
            );
            RunScenarioCommand = ReactiveCommand.CreateFromTask(
                RunScenarioAsync,
                this.WhenAnyValue(x => x.SelectedScenario, (string? s) => !string.IsNullOrWhiteSpace(s))
            );
            SaveWorkspaceCommand = ReactiveCommand.Create(() =>
            {
                SaveWorkspace();
                StatusText = "配置已保存";
            });

            // 恢复上次的工作区; 没有配置就开一路 UDP 监听 (开箱即用)
            RestoreWorkspace();
            if (Sessions.Count == 0)
                CreateSession(Transports[0]);
            SelectedSession = Sessions.FirstOrDefault();
        }

        private void RestoreWorkspace()
        {
            var ws = ProtocolWorkspace.Load();
            foreach (var st in ws.Sessions)
            {
                var t = Transports.FirstOrDefault(x =>
                    x.Transport == st.Transport && x.IsListener == st.IsListener) ?? Transports[0];
                _sessionCounter++;
                var s = new ProtocolSessionViewModel($"s{_sessionCounter}", t);
                s.Restore(st);
                s.FilePicker = _filePicker;
                Sessions.Add(s);
            }
        }

        /// <summary>把当前会话配置落盘 (不含日志/统计)。</summary>
        public void SaveWorkspace()
        {
            var ws = new ProtocolWorkspace();
            foreach (var s in Sessions.Where(x => !x.IsVirtual))
                ws.Sessions.Add(s.Snapshot());
            ws.Save();
        }

        private ProtocolSessionViewModel? _selectedSession;
        public ProtocolSessionViewModel? SelectedSession
        {
            get => _selectedSession;
            set
            {
                if (ReferenceEquals(_selectedSession, value))
                    return;

                if (_selectedSession is not null)
                    _selectedSession.IsActive = false;

                this.RaiseAndSetIfChanged(ref _selectedSession, value);
                this.RaisePropertyChanged(nameof(HasSelection));

                if (value is not null)
                {
                    value.IsActive = true;
                    value.MarkSeen(); // 切过去就把标签角标清掉
                }
            }
        }

        public bool HasSelection => SelectedSession is not null;

        private Func<Task<string?>>? _filePicker;

        /// <summary>由窗口注入的文件选择器 (下发到各会话; VM 不依赖 UI 才能脱离桌面测试)。</summary>
        public Func<Task<string?>>? FilePicker
        {
            get => _filePicker;
            set
            {
                _filePicker = value;
                foreach (var s in Sessions)
                    s.FilePicker = value;
            }
        }

        private Func<string, Task>? _clipboardWriter;

        /// <summary>由窗口注入的剪贴板写入器 (同样下发到各会话)。</summary>
        public Func<string, Task>? ClipboardWriter
        {
            get => _clipboardWriter;
            set
            {
                _clipboardWriter = value;
                foreach (var s in Sessions)
                    s.ClipboardWriter = value;
            }
        }

        private string? _selectedScenario;
        public string? SelectedScenario
        {
            get => _selectedScenario;
            set => this.RaiseAndSetIfChanged(ref _selectedScenario, value);
        }

        private string _statusText = "就绪";
        public string StatusText
        {
            get => _statusText;
            private set => this.RaiseAndSetIfChanged(ref _statusText, value);
        }

        public ReactiveCommand<Unit, Unit> NewSessionCommand { get; }
        public ReactiveCommand<Unit, Unit> RemoveSessionCommand { get; }
        public ReactiveCommand<Unit, Unit> ExportCommand { get; }
        public ReactiveCommand<Unit, Unit> RunScenarioCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveWorkspaceCommand { get; }

        private int _sessionCounter;

        private ProtocolSessionViewModel CreateSession(ProtocolTransportOption transport)
        {
            _sessionCounter++;
            var session = new ProtocolSessionViewModel($"s{_sessionCounter}", transport)
            {
                Label = $"{transport.Label} #{_sessionCounter}",
                FilePicker = _filePicker,
                ClipboardWriter = _clipboardWriter,
            };
            session.Port = SuggestPort(transport);
            Sessions.Add(session);
            return session;
        }

        /// <summary>给个不太可能撞车的默认端口, 用户按需改。</summary>
        private int SuggestPort(ProtocolTransportOption transport)
        {
            if (transport.Transport == "serial")
                return 0;
            // 监听类给固定常用口, 连接类给同一个口 (现场多半是连到本机测试)
            return 9000 + Sessions.Count;
        }

        private async Task NewSessionAsync()
        {
            // 传输类型跟随当前会话 —— 新开一路"与当前同类型"是最常见用法
            // (同时开多个 TCP 服务端 / 多路 UDP); 无选中时退回默认 (UDP 监听)。
            // 这样"新建会话"不再需要一个专门的下拉框, 与连接带的"传输"合并为一个。
            var transport = SelectedSession?.Transport ?? Transports[0];
            var session = CreateSession(transport);
            SelectedSession = session;
            // 直接启动: 调试台的高频用法就是"开一路马上开始看"
            session.StartCommand.Execute().Subscribe();
            SaveWorkspace();
            await Task.CompletedTask;
        }

        private void RemoveSelectedSession()
        {
            var s = SelectedSession;
            if (s is null)
                return;
            try
            {
                s.Dispose();
            }
            catch
            {
                /* ignore */
            }
            Sessions.Remove(s);
            SelectedSession = Sessions.FirstOrDefault();
            SaveWorkspace();
        }

        /// <summary>导出格式 (按选中会话导出; 主流工具都是"另存为")。</summary>
        public IReadOnlyList<string> ExportFormats { get; } = new[] { "文本", "CSV", "JSON" };

        private string _exportFormat = "文本";
        public string ExportFormat
        {
            get => _exportFormat;
            set => this.RaiseAndSetIfChanged(ref _exportFormat, value);
        }

        /// <summary>
        /// 导出日志。落到 &lt;程序目录&gt;/Logs/ProtocolTests/ —— 不弹对话框,
        /// 便于脚本/AI 也能拿到路径。
        /// </summary>
        private void ExportLogs()
        {
            try
            {
                var session = SelectedSession;
                if (session is null)
                    return;

                var (ext, body) = ExportFormat switch
                {
                    "CSV" => ("csv", session.ExportCsv()),
                    "JSON" => ("json", session.ExportJson()),
                    _ => ("txt", session.ExportText()),
                };

                var dir = Path.Combine(AppContext.BaseDirectory, "Logs", "ProtocolTests");
                Directory.CreateDirectory(dir);
                var safe = string.Concat(session.Label.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
                var path = Path.Combine(
                    dir,
                    $"{safe}-{DateTime.Now:yyyyMMdd-HHmmss}.{ext}"
                );

                File.WriteAllText(path, body, new System.Text.UTF8Encoding(false));
                StatusText = "已导出: " + path;
            }
            catch (Exception ex)
            {
                StatusText = "导出失败: " + ex.Message;
            }
        }

        private void LoadScenarios()
        {
            Scenarios.Clear();
            try
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "Config", "ProtocolTests");
                if (Directory.Exists(dir))
                {
                    foreach (var f in Directory.GetFiles(dir, "*.json").OrderBy(f => f))
                        Scenarios.Add(Path.GetFileName(f));
                }
            }
            catch
            {
                /* 场景目录不可读不影响手动调试 */
            }
            SelectedScenario = Scenarios.FirstOrDefault();
        }

        // ── 场景运行: 日志落到一个"虚拟会话"里, 与手动会话并列可见 ──

        private ProtocolSessionViewModel? _scenarioSession;

        private ProtocolSessionViewModel ScenarioSession()
        {
            if (_scenarioSession is null)
            {
                _scenarioSession = new ProtocolSessionViewModel("scenario", Transports[0])
                {
                    Label = "场景运行",
                    IsVirtual = true,
                };
                Sessions.Add(_scenarioSession);
            }
            return _scenarioSession;
        }

        private async Task RunScenarioAsync()
        {
            if (string.IsNullOrWhiteSpace(SelectedScenario))
                return;

            var path = Path.Combine(AppContext.BaseDirectory, "Config", "ProtocolTests", SelectedScenario!);
            if (!File.Exists(path))
            {
                StatusText = "场景不存在: " + path;
                return;
            }

            var log = ScenarioSession();
            SelectedSession = log;
            StatusText = $"运行场景: {SelectedScenario}";
            log.AppendEntry(ProtocolLogEntry.Info($"=== 运行场景 {SelectedScenario} ==="));

            try
            {
                var report = await ProtocolTestRunner
                    .RunFileAsync(path, this, CancellationToken.None)
                    .ConfigureAwait(true);
                StatusText = report.Ok
                    ? $"场景通过 · {report.Summary.Passed} 步"
                    : $"场景失败 · {report.Summary.Failed} 步未通过";
            }
            catch (Exception ex)
            {
                StatusText = "场景执行失败: " + ex.Message;
                log.AppendEntry(ProtocolLogEntry.Info("场景执行失败: " + ex.Message));
            }
        }

        // ── IProtocolTestObserver ──

        void IProtocolTestObserver.OnStepResult(ProtocolTestStepResult result) =>
            ScenarioSession().AppendEntry(ProtocolLogEntry.Step(result));

        void IProtocolTestObserver.OnSent(string via, byte[] data) =>
            ScenarioSession().AppendEntry(ProtocolLogEntry.Frame("发", via, data, null));

        void IProtocolTestObserver.OnReceived(string via, byte[] data, bool matched) =>
            ScenarioSession().AppendEntry(ProtocolLogEntry.Frame("收", via, data, matched));

        public void Dispose()
        {
            SaveWorkspace();
            foreach (var s in Sessions)
            {
                try
                {
                    s.Dispose();
                }
                catch
                {
                    /* ignore */
                }
            }
            Sessions.Clear();
        }
    }
}
