using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using HiMind.Distribution;
using IOStudio.Services;
using IOStudio.ViewModels;

namespace IOStudio.Views
{
    /// <summary>
    /// 「正在下载更新」窗口 —— 把 150MB 级下载的进度、速度、剩余时间与取消变成可见可操作的。
    ///
    /// <para>非模态: 下载期间不冻结主界面。窗口自己驱动下载, 完成后由调用方决定启动安装与退出。</para>
    /// </summary>
    public partial class UpdateDownloadWindow : Window
    {
        private readonly CancellationTokenSource _cts = new();
        private bool _finished;

        public UpdateDownloadWindow()
        {
            InitializeComponent();
        }

        public UpdateDownloadViewModel ViewModel => (UpdateDownloadViewModel)DataContext!;

        /// <summary>下载成功且已启动安装器 (调用方据此退出应用)。</summary>
        public bool Installed { get; private set; }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        /// <summary>
        /// 执行下载, 完成后按需启动安装器。
        /// </summary>
        /// <returns>是否已启动安装 (true = 调用方应尽快退出应用)。</returns>
        public async Task<bool> RunAsync(
            ISoftwareUpdateService updateService,
            SoftwareUpdateManifest manifest
        )
        {
            DataContext ??= new UpdateDownloadViewModel();
            ViewModel.SetManifest(
                manifest.Version,
                updateService.CurrentVersion,
                manifest.ReleaseName,
                manifest.Size
            );

            // 取消按钮 → 取消下载令牌
            ViewModel.CancelCommand.Subscribe(_ => _cts.Cancel());

            // 进度由下载线程回调 —— 必须切回 UI 线程再改绑定属性
            var progress = new Progress<DownloadProgress>(p =>
                Dispatcher.UIThread.Post(() => ViewModel.ReportProgress(p.BytesReceived, p.TotalBytes))
            );

            try
            {
                var downloaded = await updateService.DownloadAsync(manifest, progress, _cts.Token);

                ViewModel.MarkInstalling();
                if (updateService.LaunchInstaller(downloaded))
                {
                    Installed = true;
                    _finished = true;
                    return true;
                }

                // 包下好了但装不了 —— 与"下载失败"是两回事, 文案必须分开
                FinishWithError("更新包已下载，但更新组件不可用。请重新安装当前版本后再试。");
                return false;
            }
            catch (OperationCanceledException)
            {
                FinishWithError(null, canceled: true);
                return false;
            }
            catch (Exception ex)
            {
                // 明确的下载失败 —— 不能再报成"检查更新失败" (那会把人引向网络排查)
                FinishWithError($"下载失败：{ex.Message}");
                return false;
            }
        }

        private void FinishWithError(string? message, bool canceled = false)
        {
            _finished = true;
            if (canceled)
                ViewModel.MarkCanceled();
            else
                ViewModel.MarkFailed(message ?? "下载失败");

            if (this.FindControl<Button>("CancelButton") is { } cancel)
                cancel.IsVisible = false;
            if (this.FindControl<Button>("CloseButton") is { } close)
                close.IsVisible = true;
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e)
        {
            ViewModel.RequestCancel();
            _cts.Cancel();
        }

        private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            // 下载还没结束就关窗口 = 取消, 否则后台任务会一直跑到失败
            if (!_finished)
                _cts.Cancel();
            base.OnClosing(e);
        }
    }
}
