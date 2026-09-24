using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using IOStudio.Services;
using IOStudio.Services.Motion;
using IOStudio.ViewModels;
using IOStudio.Views;
using Microsoft.Extensions.DependencyInjection;

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
