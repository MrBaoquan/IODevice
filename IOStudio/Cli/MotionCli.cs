using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using IOStudio.Services;
using IOStudio.Services.Motion;
using IOStudio.Services.ProtocolTest;
using IOStudio.ViewModels;
using IOStudio.ViewModels.Timeline;

namespace IOStudio.Cli
{
    /// <summary>
    /// IOStudio 命令行选项。
    ///
    /// 目的：让「打开动作编排 → 播放 .motion → 观察 Simulator 反馈」这条链路可以被脚本驱动，
    /// 而不是每次都要人工点界面。IOStudio 是 Avalonia 桌面程序，本身没有 CLI，这里补上最小可用的一套。
    ///
    /// 用法:
    ///   IOStudio.exe --motion &lt;file.motion&gt; [--play] [--loop] [--exit-after &lt;ms&gt;] [--headless]
    ///   IOStudio.exe &lt;file.motion&gt; --play --exit-after 30000
    ///   IOStudio.exe --dump-tracks &lt;file.motion&gt;
    ///
    /// 为方便自动化断言，会输出一份 JSON 状态文件（默认与 .motion 同目录的
    /// `&lt;name&gt;.cli-status.json`，可用 --status-file 指定），内容含解析出的轨道映射与运行阶段。
    /// </summary>
    public sealed class CliOptions
    {
        public string? MotionPath { get; private set; }
        public bool Play { get; private set; }
        public bool Loop { get; private set; }
        /// <summary>是否把预览值实时输出到物理设备（对应编辑器里的「设备输出」开关）。
        /// 默认关闭，所以脚本化播放必须显式打开，否则动作只在编辑器里预览、不下发到设备。</summary>
        public bool LiveOutput { get; private set; }
        public int? ExitAfterMs { get; private set; }
        public bool Headless { get; private set; }
        public bool ShowHelp { get; private set; }
        /// <summary>只打印版本号然后退出。</summary>
        public bool ShowVersion { get; private set; }
        /// <summary>
        /// 显式关闭启动时的自动检查更新。
        ///
        /// <para>带任何 CLI 动词时本来就会自动跳过 (见 <see cref="IsNonInteractive"/>)：
        /// 「发现新版本」是个模态框，会把自动化调用**挂死**在等点击上。这个开关是给
        /// "只想安静起个 GUI" 的场景用的。</para>
        /// </summary>
        public bool NoUpdateCheck { get; private set; }
        public bool DumpTracks { get; private set; }
        /// <summary>仅做编排预检并输出问题清单, 然后退出。发现 Error 级问题时退出码为 2 (自动化门禁)。</summary>
        public bool Validate { get; private set; }
        public string? StatusFile { get; private set; }
        /// <summary>把预检结果写成人类可读的 Markdown 报告 (可选)。</summary>
        public string? ValidateReport { get; private set; }

        /// <summary>从演出规格 (show spec) 生成 .motion。</summary>
        public string? MotionBuildSpec { get; private set; }

        /// <summary>--motion-build 的输出路径 (缺省 = spec 同名的 .motion)。</summary>
        public string? OutFile { get; private set; }

        /// <summary>是否请求了任何 CLI 行为（否则按原来的纯 GUI 方式启动）。</summary>
        public bool HasWork =>
            MotionPath is not null
            || DumpTracks
            || Validate
            || ShowHelp
            || ShowVersion
            || !string.IsNullOrWhiteSpace(MotionBuildSpec)
            || !string.IsNullOrWhiteSpace(ProtocolTestPath);

        /// <summary>
        /// 该调用是否不该出现任何交互式提示（模态框）。
        ///
        /// <para>任何 CLI 动词都算：脚本/AI 驱动的调用必须能无人值守跑完，
        /// 而「发现新版本」弹的是模态框，会一直等人点确定。</para>
        /// </summary>
        public bool IsNonInteractive => HasWork || NoUpdateCheck;

        /// <summary>本进程解析出的 CLI 选项（供 ViewModel 判断是否该弹交互式提示）。</summary>
        internal static CliOptions Current { get; set; } = new();

        /// <summary>协议测试场景 (JSON)。设置后仅执行协议测试并退出 (0 通过 / 2 失败)。</summary>
        public string? ProtocolTestPath { get; private set; }

        /// <summary>协议测试报告输出路径 (JSON)。</summary>
        public string? ReportFile { get; private set; }

        public const string Usage =
            "IOStudio CLI\n" +
            "  --motion <path>        打开动作编排编辑器并加载 .motion 文件\n" +
            "  --play                 加载后自动播放\n" +
            "  --live-output          播放时把值实时下发到设备（编辑器「设备输出」开关；默认关闭）\n" +
            "  --loop                 开启循环播放\n" +
            "  --exit-after <ms>      运行指定毫秒后自动退出（脚本化必用）\n" +
            "  --headless             窗口最小化且不占任务栏（便于自动化时不抢焦点）\n" +
            "  --dump-tracks          仅解析并输出轨道映射，然后退出（不打开窗口）\n" +
            "  --validate             仅做编排预检并输出问题清单，然后退出；有 Error 时退出码 2\n" +
            "  --version              打印版本号后退出\n" +
            "  --no-update-check      不检查更新（带任何命令时本就自动跳过，模态框会挂死自动化）\n" +
            "  --protocol-test <path> 执行协议测试场景 (JSON)，退出码 0 通过 / 2 失败\n" +
            "  --report <path>        协议测试报告输出 (JSON)\n" +
            "  --status-file <path>   指定 JSON 状态文件路径\n" +
            "  --validate-report <path>  预检时额外写一份人类可读的 Markdown 报告\n" +
            "  --motion-build <spec.json>  由演出规格生成 .motion (官方程序化生成入口)\n" +
            "  --out <path>            --motion-build 的输出路径 (缺省 = spec 同名 .motion)\n" +
            "  --help                 显示本帮助\n" +
            "  （也支持直接给一个 .motion 路径作为位置参数）\n";

        public static CliOptions Parse(string[]? args)
        {
            var o = new CliOptions();
            if (args is null) return o;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--play": o.Play = true; break;
                    case "--live-output": o.LiveOutput = true; break;
                    case "--loop": o.Loop = true; break;
                    case "--headless": o.Headless = true; break;
                    case "--version": o.ShowVersion = true; break;
                    case "--no-update-check": o.NoUpdateCheck = true; break;
                    case "--dump-tracks": o.DumpTracks = true; break;
                    case "--validate": o.Validate = true; break;
                    case "--help":
                    case "-h":
                    case "/?": o.ShowHelp = true; break;
                    case "--motion":
                        if (i + 1 < args.Length) { o.MotionPath = args[++i]; }
                        break;
                    case "--exit-after":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
                        {
                            o.ExitAfterMs = Math.Max(0, ms);
                            i++;
                        }
                        break;
                    case "--status-file":
                        if (i + 1 < args.Length) { o.StatusFile = args[++i]; }
                        break;
                    case "--validate-report":
                        if (i + 1 < args.Length) { o.ValidateReport = args[++i]; }
                        break;
                    case "--motion-build":
                        if (i + 1 < args.Length) { o.MotionBuildSpec = args[++i]; }
                        break;
                    case "--out":
                        if (i + 1 < args.Length) { o.OutFile = args[++i]; }
                        break;
                    case "--protocol-test":
                        if (i + 1 < args.Length) { o.ProtocolTestPath = args[++i]; }
                        break;
                    case "--report":
                        if (i + 1 < args.Length) { o.ReportFile = args[++i]; }
                        break;
                    default:
                        // 位置参数：把 .motion 路径当 --motion 用
                        if (!a.StartsWith("-", StringComparison.Ordinal) &&
                            a.EndsWith(".motion", StringComparison.OrdinalIgnoreCase) &&
                            o.MotionPath is null)
                        {
                            o.MotionPath = a;
                        }
                        break;
                }
            }
            return o;
        }

        /// <summary>状态文件路径：显式指定优先，否则放在 .motion 同目录。</summary>
        public string? ResolveStatusFile()
        {
            if (!string.IsNullOrWhiteSpace(StatusFile)) return StatusFile;
            if (string.IsNullOrWhiteSpace(MotionPath)) return null;
            try
            {
                var full = Path.GetFullPath(MotionPath);
                return Path.Combine(Path.GetDirectoryName(full) ?? ".", Path.GetFileNameWithoutExtension(full) + ".cli-status.json");
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>CLI 执行器：驱动窗口 → 加载 → 播放 → （可选）退出，并写状态文件。</summary>
    public static class CliRunner
    {
        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        /// <summary>解析 .motion 的轨道映射（不依赖 Motion 模型，直接读 JSON，避免与编辑器模型耦合）。</summary>
        public static List<Dictionary<string, object?>> ReadTrackMap(string path)
        {
            var list = new List<Dictionary<string, object?>>();
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
                if (doc.RootElement.TryGetProperty("tracks", out var tracks) && tracks.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in tracks.EnumerateArray())
                    {
                        list.Add(new Dictionary<string, object?>
                        {
                            ["label"] = t.TryGetProperty("label", out var l) ? l.GetString() : null,
                            ["device"] = t.TryGetProperty("device", out var d) ? d.GetString() : null,
                            ["outputType"] = t.TryGetProperty("output_type", out var ot) ? ot.GetString() : null,
                            ["oaxisChannel"] = t.TryGetProperty("oaxis_channel", out var ch) ? ch.GetString() : null,
                            ["enabled"] = t.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.True,
                            // 工程量程：显式覆盖值。null 表示未声明 → 走设备声明量程或直通。
                            // 暴露出来是为了让"值域"可被脚本与编辑器观察，而不是隐含在代码里。
                            ["engineeringRange"] = t.TryGetProperty("engineering_range", out var er) && er.ValueKind == JsonValueKind.Number
                                ? er.GetDouble()
                                : (double?)null,
                            ["neutralValue"] = t.TryGetProperty("neutral_value", out var nv) && nv.ValueKind == JsonValueKind.Number
                                ? nv.GetDouble()
                                : (double?)null,
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                list.Add(new Dictionary<string, object?> { ["error"] = ex.Message });
            }
            return list;
        }

        /// <summary>写状态文件（失败不抛，CLI 不应因为写文件而崩）。</summary>
        /// <summary>最近一次写入状态文件的 phase (用于避免退出定时器覆盖错误状态)。</summary>
        private static string? _lastPhase;

        /// <summary>最近一次写入状态文件的时间。</summary>
        public static void WriteStatus(
            CliOptions options,
            string phase,
            TimelineEditorViewModel? vm = null,
            IReadOnlyDictionary<string, object?>? validation = null
        )
        {
            _lastPhase = phase;
            string? target = options.ResolveStatusFile();
            if (string.IsNullOrWhiteSpace(target)) return;
            try
            {
                var payload = new Dictionary<string, object?>
                {
                    ["phase"] = phase,
                    ["motion"] = options.MotionPath,
                    ["motionExists"] = options.MotionPath is not null && File.Exists(options.MotionPath),
                    ["play"] = options.Play,
                    ["liveOutput"] = options.LiveOutput,
                    ["loop"] = options.Loop,
                    ["exitAfterMs"] = options.ExitAfterMs,
                    ["pid"] = Environment.ProcessId,
                    ["utc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                };
                if (options.MotionPath is not null && File.Exists(options.MotionPath))
                {
                    payload["tracks"] = ReadTrackMap(options.MotionPath);
                }
                if (vm is not null)
                {
                    try
                    {
                        payload["isPlaying"] = vm.IsPlaying;
                        payload["currentTimeMs"] = vm.CurrentTimeMs;
                        // 引擎内部错误 (如 native 派发失败) —— 不暴露就会变成静默失效
                        payload["lastEngineError"] = vm.LastError;
                        payload["tickError"] = vm.TickError;
                        payload["tickCount"] = vm.TickCount;
                        payload["tickMaxEngineMs"] = vm.TickMaxEngineMs;
                        payload["tickMaxMediaMs"] = vm.TickMaxMediaMs;
                        payload["tickStartCount"] = vm.TickStartCount;
                        payload["tickStopCount"] = vm.TickStopCount;
                        // 未送达设备的通道 (native 静默丢弃的输出) —— 不暴露就只剩"动作在走、设备不动"
                        payload["unresolvedChannels"] = vm.UnresolvedChannels;
                        // 与"未送达"互补: 这些是发出去了但被速率上限削慢的通道。
                        // 放进来是为了让 --validate 之外的实际播放也能被脚本取证。
                        payload["rateLimitedChannels"] = vm.RateLimitedChannels;
                        // 后台设备轮询耗时 (定位"定时调度被饿死")
                        payload["pollCount"] = Services.Motion.PollDiagnostics.UpdateCount;
                        payload["pollMaxMs"] = Services.Motion.PollDiagnostics.MaxUpdateMs;
                        payload["pollTotalMs"] = Services.Motion.PollDiagnostics.TotalUpdateMs;
                        // 进程调度压力 (区分"线程耗尽"与"CPU 忙")
                        payload["sampleCount"] = Services.Motion.PollDiagnostics.SampleCount;
                        payload["maxThreads"] = Services.Motion.PollDiagnostics.MaxThreadCount;
                        payload["maxPendingWork"] = Services.Motion.PollDiagnostics.MaxPendingWork;
                        payload["cpuDeltaMs"] = Services.Motion.PollDiagnostics.CpuDeltaMs;
                    }
                    catch { /* 编辑器状态不是关键路径，取不到就跳过 */ }
                }
                if (validation is not null)
                {
                    payload["validation"] = validation;
                }
                File.WriteAllText(target, JsonSerializer.Serialize(payload, WriteOptions), Encoding.UTF8);
            }
            catch { /* ignore */ }
        }

        // ---- 无需 UI 的动词 (在 Avalonia 启动前执行, 亚秒返回) ----

        /// <summary>
        /// 处理不需要窗口的动词。返回 null 表示该调用需要完整应用（播放 / 协议测试）。
        ///
        /// <para><b>为什么必须前置</b>: 这些动词原本排在 <c>new MainWindowViewModel()</c>
        /// （设备与插件初始化）之后，实测 <c>--validate</c> 要 4.9s；而同样是纯命令行的
        /// <c>--dump-tracks</c> 因为提前返回只要 0.7s —— 差的正是这段初始化。
        /// 把命令当命令用（尤其给脚本与 AI）就不能让人等十几秒。</para>
        ///
        /// <para>这些动词只依赖文件解析与静态查表（<c>MotionDeviceCatalog</c> /
        /// <c>DeviceRangeCatalog</c>），不建设备、不开窗口，因此可以安全地跑在 Avalonia 之前。</para>
        /// </summary>
        public static int? TryRunWithoutUi(CliOptions options)
        {
            if (options.ShowHelp)
            {
                SafePrint(CliOptions.Usage);
                WriteStatus(options, "help");
                return 0;
            }

            if (options.ShowVersion)
            {
                SafePrint(Program.VersionText);
                return 0;
            }

            if (options.DumpTracks)
            {
                WriteStatus(options, "dumped");
                string? status = options.ResolveStatusFile();
                SafePrint(
                    status is not null && File.Exists(status)
                        ? $"status written: {status}"
                        : "status not written"
                );
                return 0;
            }

            if (options.Validate)
                return RunValidate(options);

            if (!string.IsNullOrWhiteSpace(options.MotionBuildSpec))
                return RunBuild(options);

            return null;
        }

        /// <summary>仅预检：退出码 2 = 存在 Error 级问题（自动化门禁）。</summary>
        private static int RunValidate(CliOptions options)
        {
            var report = ValidateMotion(options.MotionPath);
            WriteStatus(options, "validated", validation: report);

            if (!string.IsNullOrWhiteSpace(options.ValidateReport))
            {
                try
                {
                    File.WriteAllText(
                        options.ValidateReport!,
                        BuildValidationReportMarkdown(options.MotionPath, report),
                        Encoding.UTF8
                    );
                    SafePrint($"validation report written: {options.ValidateReport}");
                }
                catch (Exception ex)
                {
                    SafePrint("validation report failed: " + ex.Message);
                }
            }

            bool hasErrors =
                report is not null
                && report.TryGetValue("hasErrors", out var he)
                && he is bool b
                && b;

            SafePrint(
                hasErrors ? "validation: ERRORS found (exit 2)" : "validation: ok (exit 0)"
            );
            return hasErrors ? 2 : 0;
        }

        /// <summary>
        /// 在 UI 线程就绪后执行 CLI 动作。必须在 OnFrameworkInitializationCompleted 里调用。
        ///
        /// <para>只处理**需要窗口**的动词（播放 / 协议测试）。纯文件动词
        /// （--help/--version/--dump-tracks/--validate/--motion-build）已在
        /// <see cref="TryRunWithoutUi"/> 里提前处理，不会走到这里。</para>
        /// </summary>
        public static void Run(IClassicDesktopStyleApplicationLifetime desktop, MainWindowViewModel mainVm, CliOptions options)
        {
            // ── 等窗口与 UI 线程起来后再操作：直接构造窗口会被 Avalonia 生命周期拒绝 ──
            Dispatcher.UIThread.Post(() =>
            {
                TimelineEditorViewModel? editor = null;
                try
                {
                    editor = mainVm.OpenTimelineEditorForCli(options.MotionPath);

                    if (editor is not null)
                    {
                        // 关键：编辑器默认「设备输出」是关闭的，动作只做预览、不下发设备。
                        // 脚本化播放必须先打开它，否则 Simulator 收到的 DO 永远是 0。
                        // 用 session 级设置: 不覆盖用户保存的偏好。
                        if (options.LiveOutput) { editor.SetLiveOutputForSession(true); }

                        if (options.Play)
                        {
                            if (options.Loop) { TryExecute(editor.ToggleLoopCommand); }
                            TryExecute(editor.PlayCommand);
                        }
                    }
                    WriteStatus(options, options.Play ? "playing" : "loaded", editor);
                }
                catch (Exception ex)
                {
                    AppLog.Error("Cli", "CLI 运行失败", ex);
                    WriteStatus(options, "error:" + ex.Message, editor);
                }

                // ── 自动退出：脚本化调试必须能自己结束 ──
                if (options.ExitAfterMs is int ms)
                {
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
                    timer.Tick += (_, _) =>
                    {
                        timer.Stop();
                        // 不回退已写入的错误状态: 此前会把 "error:…" 覆盖成 "exited",
                        // 异常信息被静默丢弃 (排查时只剩一个"正常退出"的假象)。
                        if (_lastPhase is { } p && p.StartsWith("error", StringComparison.OrdinalIgnoreCase))
                        {
                            AppLog.Warn("Cli", $"退出定时器跳过覆盖 (保留 phase={p})");
                            desktop.Shutdown(1);
                            return;
                        }
                        WriteStatus(options, "exited", editor);
                        desktop.Shutdown(0);
                    };
                    timer.Start();
                }
            }, DispatcherPriority.Background);
        }

        private static readonly JsonSerializerOptions SpecReadOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>
        /// 执行协议测试场景 (JSON), 不开窗口。
        ///
        /// <para>退出码: 0 全通过 / 2 有失败 —— 与 --validate 一致, 便于脚本与 CI 门禁。
        /// 场景只做网络收发, 不依赖任何设备/窗口, 因此可脱离 --motion 单独运行。</para>
        /// </summary>
        public static void RunProtocolTest(
            CliOptions options,
            IClassicDesktopStyleApplicationLifetime desktop
        )
        {
            string scenarioPath = options.ProtocolTestPath!;
            ProtocolTestReport? report = null;
            string? error = null;
            try
            {
                // 场景内部一律 ConfigureAwait(false), 不会回到 UI 线程, 可安全同步等待
                report = ProtocolTestRunner
                    .RunFileAsync(scenarioPath, observer: null, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            if (report is not null && !string.IsNullOrWhiteSpace(options.ReportFile))
            {
                try
                {
                    // 不带 BOM: JSON 报告要能被标准解析器 (Python json / jq) 直接读,
                    // Encoding.UTF8 会写 BOM, 会让 json.loads 报 "Unexpected UTF-8 BOM"。
                    File.WriteAllText(
                        options.ReportFile!,
                        report.ToJson(),
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                    );
                    SafePrint($"protocol-test report written: {options.ReportFile}");
                }
                catch (Exception ex)
                {
                    SafePrint("protocol-test report failed: " + ex.Message);
                }
            }

            if (report is null)
            {
                SafePrint("protocol-test: 场景执行失败: " + (error ?? "unknown"));
                WriteStatus(options, "protocol-test-failed");
                RequestShutdown(desktop, 2);
                return;
            }

            foreach (var s in report.Steps)
            {
                SafePrint(
                    $"  [{(s.Ok ? "PASS" : "FAIL")}] #{s.Index} {s.Op}"
                        + (string.IsNullOrEmpty(s.Via) ? "" : $" via={s.Via}")
                        + (s.Ok ? $" ({s.ElapsedMs}ms)" : $" — {s.Reason}")
                );
            }
            SafePrint(
                $"protocol-test: {report.Summary.Passed} passed, {report.Summary.Failed} failed"
                    + (report.Ok ? " -> ok (exit 0)" : " -> failed (exit 2)")
            );
            WriteStatus(options, report.Ok ? "protocol-test-ok" : "protocol-test-failed");
            RequestShutdown(desktop, report.Ok ? 0 : 2);
        }

        /// <summary>由演出规格 (show spec) 生成 .motion, 不开窗口。</summary>
        /// <summary>
        /// 由演出规格生成 .motion（纯构建，不开窗口）。返回退出码：0 成功 / 2 失败。
        /// </summary>
        private static int RunBuild(CliOptions options)
        {
            string specPath = options.MotionBuildSpec!;
            if (!File.Exists(specPath))
            {
                SafePrint($"motion-build: spec not found: {specPath}");
                WriteStatus(options, "build-failed");
                return 2;
            }

            ShowSpec? spec;
            try
            {
                spec = JsonSerializer.Deserialize<ShowSpec>(
                    File.ReadAllText(specPath, Encoding.UTF8),
                    SpecReadOptions
                );
            }
            catch (Exception ex)
            {
                SafePrint("motion-build: 解析 spec 失败: " + ex.Message);
                WriteStatus(options, "build-failed");
                return 2;
            }

            var result = MotionShowBuilder.Build(spec!);
            if (result.HasErrors || result.Timeline is null)
            {
                SafePrint("motion-build: 规格有错误, 未生成:");
                foreach (var issue in result.Issues)
                    SafePrint("  " + issue);
                WriteStatus(options, "build-failed", validation: BuildIssuesPayload(result.Issues));
                return 2;
            }

            string outPath =
                !string.IsNullOrWhiteSpace(options.OutFile)
                    ? options.OutFile!
                    : Path.ChangeExtension(specPath, ".motion");

            if (!MotionFileReader.Write(result.Timeline, outPath))
            {
                SafePrint("motion-build: 写出失败: " + outPath);
                WriteStatus(options, "build-failed");
                return 2;
            }

            var t = result.Timeline;
            int clipCount = t.Tracks.Sum(tr => tr.Clips.Count);
            SafePrint($"motion-build: ok -> {outPath}");
            SafePrint(
                $"  tracks={t.Tracks.Count} clips={clipCount} instances={t.ActionInstances.Count} "
                    + $"presets={t.EmbeddedPresets.Count} events={t.Events.Count} markers={(t.Markers?.Count ?? 0)}"
            );
            WriteStatus(options, "built");
            return 0;
        }

        private static IReadOnlyDictionary<string, object?> BuildIssuesPayload(
            IReadOnlyList<ShowBuildIssue> issues
        ) =>
            new Dictionary<string, object?>
            {
                ["hasErrors"] = issues.Any(i => i.Severity == MotionIssueSeverity.Error),
                ["errorCount"] = issues.Count(i => i.Severity == MotionIssueSeverity.Error),
                ["warningCount"] = issues.Count(i => i.Severity == MotionIssueSeverity.Warning),
                ["infoCount"] = issues.Count(i => i.Severity == MotionIssueSeverity.Info),
                ["issues"] = issues
                    .Select(i => new Dictionary<string, object?>
                    {
                        ["severity"] = i.Severity.ToString(),
                        ["code"] = i.Code,
                        ["message"] = i.Message,
                    })
                    .ToList(),
            };

        /// <summary>
        /// 把预检结果渲染成人类可读的 Markdown 报告 (便于贴到评审/缺陷单)。
        /// 纯函数 (无 IO), 因此可单测。
        /// </summary>
        public static string BuildValidationReportMarkdown(
            string? motionPath,
            IReadOnlyDictionary<string, object?> report
        )
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 动作编排预检报告");
            sb.AppendLine();
            sb.AppendLine($"- 文件: `{motionPath ?? "(未指定)"}`");
            sb.AppendLine($"- 时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            bool hasErrors =
                report.TryGetValue("hasErrors", out var he) && he is bool b && b;
            sb.AppendLine(
                $"- 结论: {(hasErrors ? "**存在 Error**" : "通过")} "
                    + $"(Error {AsInt(report, "errorCount")} / Warning {AsInt(report, "warningCount")} / Info {AsInt(report, "infoCount")})"
            );
            sb.AppendLine();

            var issues =
                report.TryGetValue("issues", out var iv)
                && iv is IEnumerable<Dictionary<string, object?>> list
                    ? list.ToList()
                    : new List<Dictionary<string, object?>>();

            if (issues.Count == 0)
            {
                sb.AppendLine("无问题。");
                return sb.ToString();
            }

            foreach (string sev in new[] { "Error", "Warning", "Info" })
            {
                var group = issues
                    .Where(i =>
                        string.Equals(
                            i.GetValueOrDefault("severity")?.ToString(),
                            sev,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .ToList();
                if (group.Count == 0)
                    continue;

                sb.AppendLine($"## {sev} ({group.Count})");
                sb.AppendLine();
                sb.AppendLine("| 代码 | 说明 | 轨道 | 时间(ms) |");
                sb.AppendLine("| --- | --- | --- | --- |");
                foreach (var issue in group)
                {
                    string code = issue.GetValueOrDefault("code")?.ToString() ?? "";
                    string msg = (issue.GetValueOrDefault("message")?.ToString() ?? "")
                        .Replace("|", "\\|")
                        .Replace("\r", " ")
                        .Replace("\n", " ");
                    string track = issue.GetValueOrDefault("track")?.ToString() ?? "";
                    string time = issue.GetValueOrDefault("timeMs")?.ToString() ?? "";
                    sb.AppendLine($"| `{code}` | {msg} | {track} | {time} |");
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static int AsInt(IReadOnlyDictionary<string, object?> dict, string key) =>
            dict.TryGetValue(key, out var v)
            && v is not null
            && int.TryParse(v.ToString(), out var n)
                ? n
                : 0;

        /// <summary>
        /// 编排预检 — 把"会静默失效"的编排问题变成明确清单, 供脚本化门禁使用。
        ///
        /// 注: 此处<b>不</b>注入设备存在性判定 —— 预检可能早于设备加载, 会误报
        /// "设备未加载"; 该检查留给已知设备状态的编辑器侧。
        /// 量程判定走纯缓存查表, 未预热时返回 null (跳过), 不会误报。
        /// </summary>
        private static Dictionary<string, object?>? ValidateMotion(string? path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return new Dictionary<string, object?>
                    {
                        ["error"] = "未指定 .motion 文件",
                        ["hasErrors"] = true,
                    };
                }

                var timeline = IOStudio.Services.Motion.MotionFileReader.Read(path);
                if (timeline is null)
                {
                    return new Dictionary<string, object?>
                    {
                        ["error"] = "无法解析 .motion",
                        ["hasErrors"] = true,
                    };
                }

                var issues = IOStudio.Services.Motion.MotionTimelineValidator.Validate(
                    timeline,
                    new IOStudio.Services.Motion.MotionValidationOptions
                    {
                        // 设备名是否被逻辑映射表认领 —— 未被认领的轨道播放时会被静默丢弃,
                        // 这是交付门禁必须拦下的一类问题。
                        IsDeviceDeclared = IOStudio.Services.Motion.MotionDeviceCatalog.IsDeclared,
                        // 动作是否在该设备上声明 —— 同一类静默丢失, 且 native 侧连"未解析"计数都没有。
                        IsOActionDeclared = IOStudio.Services.Motion.MotionDeviceCatalog
                            .IsOActionDeclared,
                        DeviceRange = IOStudio.Services.Motion.DeviceRangeCatalog.Resolve,
                    }
                );

                return new Dictionary<string, object?>
                {
                    ["hasErrors"] = IOStudio.Services.Motion.MotionTimelineValidator.HasErrors(issues),
                    ["errorCount"] = issues.Count(i =>
                        i.Severity == IOStudio.Services.Motion.MotionIssueSeverity.Error
                    ),
                    ["warningCount"] = issues.Count(i =>
                        i.Severity == IOStudio.Services.Motion.MotionIssueSeverity.Warning
                    ),
                    ["infoCount"] = issues.Count(i =>
                        i.Severity == IOStudio.Services.Motion.MotionIssueSeverity.Info
                    ),
                    ["issues"] = issues
                        .Select(i => new Dictionary<string, object?>
                        {
                            ["severity"] = i.Severity.ToString(),
                            ["code"] = i.Code,
                            ["message"] = i.Message,
                            ["track"] = i.TrackLabel,
                            ["timeMs"] = i.TimeMs,
                        })
                        .ToList(),
                };
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object?> { ["error"] = ex.Message, ["hasErrors"] = true };
            }
        }

        private static void TryExecute(System.Windows.Input.ICommand? command)
        {
            try
            {
                if (command?.CanExecute(null) == true) command.Execute(null);
            }
            catch { /* ignore */ }
        }

        private static void SafePrint(string message)
        {
            try { Console.WriteLine(message); } catch { /* WinExe 无控制台 */ }
        }

        /// <summary>
        /// 请求退出：必须 Post 到 UI 线程之后再执行。
        /// 在 OnFrameworkInitializationCompleted 里直接 desktop.Shutdown() 会抛出 CLR 异常、
        /// 退出码变成 0xE0434352，脚本无法判断成功。
        /// </summary>
        public static void RequestShutdown(IClassicDesktopStyleApplicationLifetime desktop, int exitCode = 0)
        {
            Dispatcher.UIThread.Post(() => desktop.Shutdown(exitCode), DispatcherPriority.Background);
        }

        /// <summary>按 CLI 选项调整主窗口呈现（headless 时最小化、不占任务栏）。</summary>
        public static void ApplyWindowMode(Window window, CliOptions options)
        {
            if (!options.Headless) return;
            try
            {
                window.ShowInTaskbar = false;
                window.WindowState = WindowState.Minimized;
            }
            catch { /* ignore */ }
        }
    }
}
