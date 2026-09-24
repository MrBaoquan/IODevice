using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using IOStudio.Services;
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
        public bool DumpTracks { get; private set; }
        /// <summary>由 native 承担派发 (量程换算 + 限速 + 写设备都在 native)。默认 C# 侧派发。</summary>
        public bool NativeDispatch { get; private set; }
        /// <summary>仅做编排预检并输出问题清单, 然后退出。发现 Error 级问题时退出码为 2 (自动化门禁)。</summary>
        public bool Validate { get; private set; }
        public string? StatusFile { get; private set; }

        /// <summary>是否请求了任何 CLI 行为（否则按原来的纯 GUI 方式启动）。</summary>
        public bool HasWork => MotionPath is not null || DumpTracks || Validate || ShowHelp;

        public const string Usage =
            "IOStudio CLI\n" +
            "  --motion <path>        打开动作编排编辑器并加载 .motion 文件\n" +
            "  --play                 加载后自动播放\n" +
            "  --live-output          播放时把值实时下发到设备（编辑器「设备输出」开关；默认关闭）\n" +
            "  --native-dispatch      由 native 承担派发（量程换算/限速/写设备都在 native，与放映端同源）\n" +
            "  --loop                 开启循环播放\n" +
            "  --exit-after <ms>      运行指定毫秒后自动退出（脚本化必用）\n" +
            "  --headless             窗口最小化且不占任务栏（便于自动化时不抢焦点）\n" +
            "  --dump-tracks          仅解析并输出轨道映射，然后退出（不打开窗口）\n" +
            "  --validate             仅做编排预检并输出问题清单，然后退出；有 Error 时退出码 2\n" +
            "  --status-file <path>   指定 JSON 状态文件路径\n" +
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
                    case "--native-dispatch": o.NativeDispatch = true; break;
                    case "--loop": o.Loop = true; break;
                    case "--headless": o.Headless = true; break;
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
        public static void WriteStatus(
            CliOptions options,
            string phase,
            TimelineEditorViewModel? vm = null,
            IReadOnlyDictionary<string, object?>? validation = null
        )
        {
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

        /// <summary>
        /// 在 UI 线程就绪后执行 CLI 动作。必须在 OnFrameworkInitializationCompleted 里调用。
        /// </summary>
        public static void Run(IClassicDesktopStyleApplicationLifetime desktop, MainWindowViewModel mainVm, CliOptions options)
        {
            // ── 仅解析轨道：不开窗口、立即退出（最快的自动化断言路径）──
            if (options.DumpTracks)
            {
                WriteStatus(options, "dumped");
                SafePrint(File.Exists(options.ResolveStatusFile() ?? string.Empty)
                    ? $"status written: {options.ResolveStatusFile()}"
                    : "status not written");
                // 同 validate: 直接 Shutdown 会把退出码变成 CLR 异常码
                RequestShutdown(desktop, 0);
                return;
            }

            // ── 仅预检：不开窗口，按结果决定退出码（自动化门禁）──
            if (options.Validate)
            {
                var report = ValidateMotion(options.MotionPath);
                WriteStatus(options, "validated", validation: report);

                bool hasErrors =
                    report is not null
                    && report.TryGetValue("hasErrors", out var he)
                    && he is bool b
                    && b;
                SafePrint(
                    hasErrors
                        ? "validation: ERRORS found (exit 2)"
                        : "validation: ok (exit 0)"
                );
                // 必须走 RequestShutdown: 在 OnFrameworkInitializationCompleted 里直接
                // desktop.Shutdown() 会抛 CLR 异常, 退出码变成 0xE0434352, 脚本无法判断结果。
                RequestShutdown(desktop, hasErrors ? 2 : 0);
                return;
            }

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
                        if (options.LiveOutput) { editor.IsLiveOutputEnabled = true; }
                        // native 派发: 量程换算/限速/写设备都交给 native (与放映端同源)
                        if (options.NativeDispatch) { editor.IsNativeDispatchEnabled = true; }

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
                    WriteStatus(options, "error:" + ex.Message, editor);
                }

                // ── 自动退出：脚本化调试必须能自己结束 ──
                if (options.ExitAfterMs is int ms)
                {
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
                    timer.Tick += (_, _) =>
                    {
                        timer.Stop();
                        WriteStatus(options, "exited", editor);
                        desktop.Shutdown(0);
                    };
                    timer.Start();
                }
            }, DispatcherPriority.Background);
        }

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
