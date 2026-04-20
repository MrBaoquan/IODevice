using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using IOStudio.Services;
using IOStudio.Services.Motion;

namespace IOStudio.Controls.Timeline
{
    /// <summary>
    /// 视频文件列表项
    /// </summary>
    public class VideoFileItem
    {
        public string FilePath { get; set; } = "";
        public string FileName => Path.GetFileName(FilePath);

        public VideoFileItem(string filePath)
        {
            FilePath = filePath;
        }

        public override string ToString() => FileName;
    }

    /// <summary>
    /// 视频预览控件 — 通过 IVideoPlayerService 实现视频渲染与时间轴同步。
    /// 不再直接依赖 LibVLC, 所有播放操作委托给注入的服务。
    /// </summary>
    public partial class VideoPreviewControl : UserControl
    {
        /// <summary>视频文件加载完成, 参数: filePath</summary>
        public event System.Action<string>? VideoLoaded;

        /// <summary>视频时长检测完成, 参数: durationMs</summary>
        public event System.Action<double>? VideoDurationDetected;

        /// <summary>请求全屏/退出全屏, 参数: isEnterFullscreen</summary>
        public event System.Action<bool>? FullscreenRequested;

        /// <summary>当前视频文件路径</summary>
        public string? VideoFilePath { get; private set; }

        /// <summary>是否处于全屏模式</summary>
        public bool IsFullscreen { get; private set; }

        /// <summary>视频播放器服务 (通过 DI 获取)。</summary>
        private IVideoPlayerService? _player;

        private bool _isInitializing;
        private bool _needsResync;
        private double _pendingSyncTimeMs;

        /// <summary>已加载的视频文件路径列表</summary>
        private readonly ObservableCollection<VideoFileItem> _videoFiles = new();

        public VideoPreviewControl()
        {
            InitializeComponent();
            VideoListBox.ItemsSource = _videoFiles;
        }

        /// <summary>延迟获取播放器服务实例。</summary>
        private IVideoPlayerService? EnsurePlayer()
        {
            if (_player is not null)
                return _player;

            _player = ServiceLocator.TryResolve<IVideoPlayerService>();
            if (_player is null)
                return null;

            // 订阅服务事件 → 驱动 UI
            _player.DurationDetected += dur =>
                Dispatcher.UIThread.Post(() => VideoDurationDetected?.Invoke(dur));
            _player.VideoLoaded += path =>
                Dispatcher.UIThread.Post(() => VideoLoaded?.Invoke(path));
            _player.MediaPlaying += OnServicePlaying;
            _player.MediaPaused += OnServicePaused;
            _player.MediaStopped += OnServiceStopped;

            return _player;
        }

        // ═══════ 加载视频 ═══════

        /// <summary>加载视频按钮点击</summary>
        public async void OnLoadVideo(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null)
                return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "加载参考视频",
                    AllowMultiple = true,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("视频文件")
                        {
                            Patterns = new[] { "*.mp4", "*.avi", "*.mkv", "*.mov", "*.wmv" }
                        },
                        new FilePickerFileType("所有文件") { Patterns = new[] { "*.*" } },
                    }
                }
            );

            if (files.Count > 0)
            {
                foreach (var file in files)
                {
                    var path = file.TryGetLocalPath();
                    if (!string.IsNullOrEmpty(path))
                    {
                        var existing = _videoFiles.FirstOrDefault(v => v.FilePath == path);
                        if (existing is null)
                            _videoFiles.Add(new VideoFileItem(path));
                    }
                }

                var firstPath = files[0].TryGetLocalPath();
                if (!string.IsNullOrEmpty(firstPath))
                    LoadVideoFile(firstPath);
            }
        }

        /// <summary>视频列表双击 — 切换预览到选中的视频</summary>
        private void OnVideoListDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (VideoListBox.SelectedItem is VideoFileItem item && File.Exists(item.FilePath))
            {
                LoadVideoFile(item.FilePath);
            }
        }

        /// <summary>
        /// 加载视频文件并初始化播放器 (不自动播放, 仅显示当前时间帧)。
        /// 通过 IVideoPlayerService 委托所有 LibVLC 操作。
        /// </summary>
        public void LoadVideoFile(string filePath)
        {
            var player = EnsurePlayer();
            if (player is null)
            {
                System.Diagnostics.Debug.WriteLine(
                    "[VideoPreview] IVideoPlayerService not available"
                );
                return;
            }

            VideoFilePath = filePath;
            var fileName = Path.GetFileName(filePath);
            VideoFileLabel.Text = fileName;
            EmptyPlaceholder.IsVisible = false;
            VideoDisplay.IsVisible = true;
            VideoStatusText.Text = $"正在加载: {fileName}";

            // 添加到视频列表 (去重)
            var existing = _videoFiles.FirstOrDefault(v => v.FilePath == filePath);
            if (existing is null)
            {
                var item = new VideoFileItem(filePath);
                _videoFiles.Add(item);
                VideoListBox.SelectedItem = item;
            }
            else
            {
                VideoListBox.SelectedItem = existing;
            }

            // 保持遮罩可见
            VideoOverlay.IsVisible = true;
            _isInitializing = true;

            // 静音预设
            player.SetMute(true);

            // 延迟到布局完成后再绑定 HWND + 加载, 否则 NativeControlHost 的子窗口尚未创建,
            // 导致 Hwnd==IntPtr.Zero, LibVLC 会弹出独立窗口
            Dispatcher.UIThread.Post(
                () =>
                {
                    var p = EnsurePlayer();
                    if (p is null)
                        return;

                    if (VideoViewHost.Hwnd != IntPtr.Zero)
                        p.SetHwnd(VideoViewHost.Hwnd);

                    _ = p.LoadAsync(filePath!);
                },
                DispatcherPriority.Loaded
            );
        }

        // ═══════ 时间同步 ═══════

        /// <summary>外部设置当前时间轴时间, 用于加载视频后 seek 到正确位置</summary>
        public void SetCurrentTimeMs(double timeMs)
        {
            _pendingSyncTimeMs = timeMs;
        }

        /// <summary>
        /// 同步时间 — 由外部时间轴驱动, seek 视频到指定位置。
        /// 仅在视频未播放时 seek (避免与自身播放冲突)。
        /// </summary>
        public void SyncTime(double timeMs)
        {
            _pendingSyncTimeMs = timeMs;

            if (_player is null || !_player.IsLoaded || _player.IsPlaying)
                return;

            if (_needsResync)
                return;

            _player.SyncTime(timeMs);
        }

        /// <summary>
        /// 精确帧预览 — 拖拽播放头时调用, 使用逐帧解码获取精确画面。
        /// </summary>
        public void SeekAccurate(double timeMs)
        {
            _pendingSyncTimeMs = timeMs;

            if (_player is null || !_player.IsLoaded || _player.IsPlaying)
                return;

            VideoOverlay.IsVisible = false;
            _player.SeekAccurate(timeMs);
        }

        // ═══════ 播放控制 ═══════

        /// <summary>开始播放视频 (与时间轴同步)</summary>
        public void Play()
        {
            if (_player is null || !_player.IsLoaded)
                return;

            if (VideoViewHost.Hwnd != IntPtr.Zero)
                _player.SetHwnd(VideoViewHost.Hwnd);

            VideoOverlay.IsVisible = false;
            _player.Play();
        }

        /// <summary>暂停视频</summary>
        public void Pause()
        {
            if (_player is not null && _player.IsPlaying)
            {
                _player.Pause();
                VideoOverlay.IsVisible = true;
            }
        }

        /// <summary>停止视频</summary>
        public void Stop()
        {
            _player?.Stop();
            VideoOverlay.IsVisible = true;
        }

        // ═══════ 视频缩放/全屏 ═══════

        private double _videoZoom = 1.0;
        private bool _isFitMode = true; // 默认为适配模式

        private void OnVideoZoomIn(object? sender, RoutedEventArgs e)
        {
            _isFitMode = false;
            _videoZoom = Math.Min(3.0, _videoZoom * 1.25);
            ApplyVideoZoom();
        }

        private void OnVideoZoomOut(object? sender, RoutedEventArgs e)
        {
            _isFitMode = false;
            _videoZoom = Math.Max(0.25, _videoZoom / 1.25);
            ApplyVideoZoom();
        }

        private void OnVideoZoomFit(object? sender, RoutedEventArgs e)
        {
            _isFitMode = true;
            _videoZoom = 1.0;
            ApplyVideoZoom();
        }

        /// <summary>鼠标滚轮缩放视频预览画面</summary>
        private void OnVideoWheelZoom(object? sender, PointerWheelEventArgs e)
        {
            if (e.Delta.Y == 0)
                return;

            _isFitMode = false;
            double factor = e.Delta.Y > 0 ? 1.15 : (1.0 / 1.15);
            _videoZoom = Math.Clamp(_videoZoom * factor, 0.25, 3.0);
            ApplyVideoZoom();
            e.Handled = true;
        }

        private void ApplyVideoZoom()
        {
            if (_isFitMode)
            {
                VideoZoomText.Text = "适配";
                _player?.SetScale(0); // LibVLC 0=auto-fit
            }
            else
            {
                VideoZoomText.Text = $"{(int)(_videoZoom * 100)}%";
                _player?.SetScale((float)_videoZoom);
            }
        }

        private void OnVideoFullscreen(object? sender, RoutedEventArgs e)
        {
            IsFullscreen = !IsFullscreen;
            BtnFullscreen.Content = IsFullscreen ? "⬜" : "⛶";
            ToolTip.SetTip(BtnFullscreen, IsFullscreen ? "退出全屏" : "全屏预览");
            SetFullscreenMode(IsFullscreen);
            FullscreenRequested?.Invoke(IsFullscreen);
        }

        /// <summary>从外部退出全屏模式 (ESC 键)</summary>
        public void ExitFullscreen()
        {
            IsFullscreen = false;
            BtnFullscreen.Content = "⛶";
            ToolTip.SetTip(BtnFullscreen, "全屏预览");
            SetFullscreenMode(false);
        }

        /// <summary>
        /// 设置全屏模式 — 隐藏/显示控件内部的标题栏、视频列表和底部工具栏,
        /// 使视频画面占满整个控件区域。
        /// </summary>
        public void SetFullscreenMode(bool fullscreen)
        {
            // 标题栏 (DockPanel.Dock="Top")
            var titleBar = this.FindControl<Border>("TitleBar");
            if (titleBar != null)
                titleBar.IsVisible = !fullscreen;

            // 左侧视频列表
            var videoListPanel = this.FindControl<Border>("VideoListPanel");
            if (videoListPanel != null)
                videoListPanel.IsVisible = !fullscreen;

            // 列表与预览之间的分隔条
            var listSplitter = this.FindControl<GridSplitter>("VideoListSplitter");
            if (listSplitter != null)
                listSplitter.IsVisible = !fullscreen;

            // 底部视频工具栏
            var videoToolbar = this.FindControl<Border>("VideoToolbar");
            if (videoToolbar != null)
                videoToolbar.IsVisible = !fullscreen;

            // 视频状态遮罩
            if (VideoOverlay != null)
                VideoOverlay.IsVisible = !fullscreen;

            // 全屏时自动适配 (scale=0), 退出时恢复缩放
            if (fullscreen)
            {
                _isFitMode = true;
                _videoZoom = 1.0;
                _player?.SetScale(0); // auto-fit — 100% 撞满全屏窗口
            }
            else
            {
                ApplyVideoZoom();
            }

            // 收缩/恢复 Grid 列定义: 列0 (视频列表) + 列1 (分隔条)
            var contentGrid = videoListPanel?.Parent as Grid;
            if (contentGrid != null && contentGrid.ColumnDefinitions.Count >= 3)
            {
                if (fullscreen)
                {
                    _savedListColWidth = contentGrid.ColumnDefinitions[0].Width;
                    _savedSplitterColWidth = contentGrid.ColumnDefinitions[1].Width;
                    contentGrid.ColumnDefinitions[0].Width = new GridLength(0);
                    contentGrid.ColumnDefinitions[0].MinWidth = 0;
                    contentGrid.ColumnDefinitions[0].MaxWidth = 0;
                    contentGrid.ColumnDefinitions[1].Width = new GridLength(0);
                }
                else
                {
                    contentGrid.ColumnDefinitions[0].Width = _savedListColWidth;
                    contentGrid.ColumnDefinitions[0].MinWidth = 80;
                    contentGrid.ColumnDefinitions[0].MaxWidth = 240;
                    contentGrid.ColumnDefinitions[1].Width = _savedSplitterColWidth;
                }
            }
        }

        private GridLength _savedListColWidth;
        private GridLength _savedSplitterColWidth;

        // ═══════ IVideoPlayerService 事件回调 ═══════

        private void OnServicePlaying()
        {
            if (_isInitializing)
            {
                // 初始化期间: 立即暂停以停在首帧
                _player?.Pause();
                return;
            }
            VideoOverlay.IsVisible = false;
        }

        private void OnServicePaused()
        {
            if (_isInitializing)
            {
                _isInitializing = false;
                _player?.SyncTime(0);
                _player?.SetMute(false);
                VideoOverlay.IsVisible = true;
                VideoStatusText.Text = $"已加载: {Path.GetFileName(VideoFilePath)}";
                return;
            }
            VideoOverlay.IsVisible = true;
            VideoStatusText.Text = "已暂停";
        }

        private void OnServiceStopped()
        {
            VideoOverlay.IsVisible = true;
            VideoStatusText.Text = "已停止";
        }

        // ═══════ 资源释放 ═══════

        /// <summary>释放视频播放器资源</summary>
        public void DisposeVlc()
        {
            if (_player is not null)
            {
                _player.MediaPlaying -= OnServicePlaying;
                _player.MediaPaused -= OnServicePaused;
                _player.MediaStopped -= OnServiceStopped;
                _player.Dispose();
                _player = null;
            }
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            base.OnUnloaded(e);
            DisposeVlc();
        }
    }
}
