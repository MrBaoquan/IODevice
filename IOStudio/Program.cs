using Avalonia;
using ReactiveUI.Avalonia;
using System;
using System.IO;
using System.Reflection;
using IOStudio.Cli;

namespace IOStudio
{
    internal class Program
    {
        /// <summary>版本文本（与标题栏同源，避免两处各报一个版本）。</summary>
        public static string VersionText => "IOStudio " + ResolveVersion();

        private static string ResolveVersion()
        {
            try
            {
                return HiMind.Distribution.DistributionOptions.GetCurrentVersion();
            }
            catch
            {
                // 取不到分发配置时退回程序集版本，不要因为一个版本号让命令失败。
                var asm = Assembly.GetEntryAssembly() ?? typeof(Program).Assembly;
                return asm.GetName().Version?.ToString() ?? "unknown";
            }
        }

        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            var cli = CliOptions.Parse(args);
            CliOptions.Current = cli;

            // 命令行场景下先把 stdout/stderr 接上 —— WinExe 默认没有控制台，
            // 不接的话所有输出都会进黑洞（脚本与 AI 拿不到任何结果）。
            // 接不上时也不中断：结果仍在 --status-file 的 JSON 与退出码里。
            if (cli.HasWork)
                ConsoleBridge.Ensure();

            // 用户设置是进程级状态，任何代码路径都可能要用它。
            // 此前只有 MainWindowViewModel 会加载，于是**命令行路径一直用的是默认值** ——
            // 最明显的后果是「设备表预设」（DeviceTableFile）被忽略，预检拿缺省表去判
            // "设备是否声明"，把预设表里明明声明过的设备报成未声明。
            if (cli.HasWork)
                Models.IOStudioSettings.LoadOrCreate();

            // ── 纯命令行动词：不启 Avalonia、不建设备，亚秒返回 ──
            if (CliRunner.TryRunWithoutUi(cli) is int code)
            {
                Environment.Exit(code);
                return;
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder
                .Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .UseReactiveUI();
    }
}
