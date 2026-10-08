using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using IOStudio.Services;
using IOStudio.Services.Motion;
using IOStudio.ViewModels;
using IOStudio.Views;
using Microsoft.Extensions.DependencyInjection;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using System;
using System.Linq;

namespace IOStudio
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // ── 日志与全局异常捕获最先就位: 后续任何一步失败都能留下证据 ──
            AppLog.Initialize();
            InstallGlobalExceptionHandlers();
            AppLog.Info("App", $"启动 pid={Environment.ProcessId} version={GetType().Assembly.GetName().Version} os={Environment.OSVersion}");
            AppLog.Info("App", $"args={string.Join(' ', Environment.GetCommandLineArgs().Skip(1))}");

            // ── 注册 DI 服务 ──
            var services = new ServiceCollection();
            ConfigureServices(services);
            var provider = services.BuildServiceProvider();
            ServiceLocator.Configure(provider);

            // 归一化 → 工程量的设备量程来源：设备自己在 IODevice.xml + config.ini 里声明，
            // IOStudio 不硬编码任何设备语义。读不到就直通（不影响既有动作文件）。
            Services.Motion.NativeMotionPlaybackEngine.DeviceRangeProvider =
                Services.Motion.DeviceRangeCatalog.Resolve;
            Services.Motion.NativeMotionPlaybackEngine.DeviceRangePrimer =
                Services.Motion.DeviceRangeCatalog.Prime;

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Exit += (_, _) =>
                {
                    AppLog.Info("App", "退出");
                    AppLog.Shutdown();
                };

                // ── CLI 支持：让「加载 .motion → 播放 → 观察 Simulator」可被脚本驱动 ──
                var cli = Cli.CliOptions.Parse(desktop.Args);
                if (cli.ShowHelp)
                {
                    Cli.CliRunner.WriteStatus(cli, "help");
                    Cli.CliRunner.RequestShutdown(desktop, 0);
                    return;
                }
                if (cli.DumpTracks)
                {
                    Cli.CliRunner.WriteStatus(cli, "dumped");
                    Cli.CliRunner.RequestShutdown(desktop, 0);
                    return;
                }
                // 由演出规格生成 .motion：纯构建, 不需要窗口
                if (!string.IsNullOrWhiteSpace(cli.MotionBuildSpec))
                {
                    Cli.CliRunner.RunBuild(cli, desktop);
                    return;
                }
                // 协议测试：纯网络收发, 不需要窗口, 也不依赖 --motion
                if (!string.IsNullOrWhiteSpace(cli.ProtocolTestPath))
                {
                    Cli.CliRunner.WriteStatus(cli, "protocol-test");
                    Cli.CliRunner.RunProtocolTest(cli, desktop);
                    return;
                }

                var mainViewModel = new MainWindowViewModel();
                var mainWindow = new MainWindow { DataContext = mainViewModel, };
                desktop.MainWindow = mainWindow;

                if (cli.MotionPath is not null || cli.ExitAfterMs is not null)
                {
                    Cli.CliRunner.ApplyWindowMode(mainWindow, cli);
                    Cli.CliRunner.Run(desktop, mainViewModel, cli);
                }
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// 安装全局异常捕获 —— 此前 UI 层任何异常都无处可查 (无处理器, 直接静默或弹崩溃框)。
        /// 全部落盘; UI 线程异常额外提示用户并保留会话, 便于先存盘再排查。
        /// </summary>
        private static void InstallGlobalExceptionHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                AppLog.Error(
                    "App",
                    $"AppDomain 未处理异常 (IsTerminating={e.IsTerminating})",
                    e.ExceptionObject as Exception
                );
            };

            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                AppLog.Error("App", "未观察的任务异常", e.Exception);
                e.SetObserved();
            };

            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                AppLog.Error("App", "UI 线程未处理异常", e.Exception);
                try
                {
                    // 保留会话: 让用户有机会先保存工程, 再据日志排查
                    e.Handled = true;
                    _ = MessageBoxManager
                        .GetMessageBoxStandard(
                            "出现未处理异常",
                            $"操作已中断, 详情已写入日志:\n{AppLog.CurrentFile}\n\n{AppLog.Describe(e.Exception)}",
                            ButtonEnum.Ok
                        )
                        .ShowAsync();
                }
                catch
                {
                    /* 提示失败不影响日志已落盘 */
                }
            };
        }

        /// <summary>注册所有服务到 DI 容器。</summary>
        private static void ConfigureServices(IServiceCollection services)
        {
            // ── Motion 服务 (Singleton — 全局唯一实例) ──
            services.AddSingleton<IKeyboardShortcutService, KeyboardShortcutService>();
            services.AddSingleton<IDeviceSchemaService, Services.Motion.DeviceSchemaService>();
            services.AddSingleton<ICurvePresetService, CurvePresetServiceInstance>();
            services.AddSingleton<IInterpolationEngine, InterpolationEngineInstance>();
            services.AddSingleton<IMotionFileReader, MotionFileReaderInstance>();
            services.AddSingleton<IMotionExportService, MotionExportServiceInstance>();

            // ── UI 层服务 ──
            services.AddSingleton<IDialogService, AvaloniaDialogService>();

            // ── 工具窗口管理器 (单例窗口 + 聚焦, 驱动"窗口"菜单复选态) ──
            services.AddSingleton<IWindowManager, AvaloniaWindowManager>();

            // ── HiMind 软件分发更新服务 (Singleton) ──
            services.AddSingleton<ISoftwareUpdateService, SoftwareUpdateService>();

            // ── 每时间轴实例的服务 (Transient) ──
            services.AddTransient<IUndoRedoService, UndoRedoService>();
            services.AddTransient<IPlaybackEngine, NativeMotionPlaybackEngine>();
            services.AddTransient<IVideoPlayerService, LibVlcVideoPlayerService>();

            // ── ViewModel (Transient — 每个窗口一个实例) ──
            services.AddTransient<ViewModels.Timeline.TimelineEditorViewModel>();
        }
    }
}
