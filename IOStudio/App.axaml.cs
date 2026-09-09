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

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow { DataContext = new MainWindowViewModel(), };
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
