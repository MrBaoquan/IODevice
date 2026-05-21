using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using IOStudio.Controls.Timeline;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using IOStudio.ViewModels.Timeline;

namespace IOStudio.Views.Timeline
{
    public partial class TimelineEditorWindow : Window
    {
        private TimelineEditorViewModel? ViewModel => DataContext as TimelineEditorViewModel;

        // 右键菜单 (确保同时只有一个打开)
        private ContextMenu? _activeContextMenu;

        public TimelineEditorWindow()
        {
            InitializeComponent();
            SetupRulerEvents();
            SetupPropertyPanelEvents();
            SetupVideoPreviewEvents();
            SetupScrollSync();
        }

        public TimelineEditorWindow(TimelineEditorViewModel viewModel)
            : this()
        {
            DataContext = viewModel;
        }

        // ═══════ 初始化与绑定 ═══════

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            SetupViewModelInteractions();
        }

        private void SetupViewModelInteractions()
        {
            if (ViewModel == null)
                return;

            ViewModel.PropertyChanged += OnViewModelPropertyChanged;

            // 播放时自动滚动: ViewModel 触发 → 同步 Ruler + TrackClipControls
            ViewModel.AutoScrollTriggered += newOffset =>
            {
                var ruler = this.FindControl<TimeRulerControl>("TimeRuler");
                if (ruler != null && ViewModel != null)
                {
                    ruler.ScrollOffsetX = newOffset;
                    ruler.PixelsPerMs = ViewModel.PixelsPerMs;
                    ruler.InvalidateVisual();
                }
                // 同步所有 TrackClipControl 的 ScrollOffsetX
                SyncScrollToClips();
            };

            // 标记变更时自动同步到标尺
            ViewModel.MarkerService.MarkersChanged += () =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => SyncMarkersToRuler());
            };

            // 事件集合变更时自动同步到标尺 (文件加载 / 添加 / 删除)
            ViewModel.Events.CollectionChanged += (_, _) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => SyncEventsToRuler());
            };

            // 初始同步标记和事件
            SyncMarkersToRuler();
            SyncEventsToRuler();
            // 初始采样一次所有轨道实时值 (确保文件加载后轨道头显示 t=0 的值)
            RefreshAllTrackLiveValues();
            // 轨道集合变化时也重新采样 (增删轨道)
            ViewModel.Tracks.CollectionChanged += (_, _) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => RefreshAllTrackLiveValues());
            };
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (
                args.PropertyName == nameof(TimelineEditorViewModel.OpenFileRequested)
                && ViewModel?.OpenFileRequested == true
            )
            {
                OnOpenFile();
            }
            else if (
                args.PropertyName == nameof(TimelineEditorViewModel.SaveAsRequested)
                && ViewModel?.SaveAsRequested == true
            )
            {
                OnSaveAs();
            }
            else if (args.PropertyName == nameof(TimelineEditorViewModel.CurrentTimeMs))
            {
                // 同步视频预览时间
                var videoPreview = this.FindControl<VideoPreviewControl>("VideoPreview");
                if (videoPreview != null && ViewModel != null)
                {
                    videoPreview.SyncTime(ViewModel.CurrentTimeMs);
                }
                // 检查器始终跟随播放头刷新
                RefreshPropertyPanel();
                // 轨道头实时值始终跟随播放头刷新 (未播放时由采样驱动,播放时由引擎推送覆盖)
                if (ViewModel != null && !ViewModel.IsPlaying)
                    RefreshAllTrackLiveValues();
            }
            else if (args.PropertyName == nameof(TimelineEditorViewModel.SelectedTrack))
            {
                // 选中轨道时立即刷新检查器 (不等待播放头移动)
                RefreshPropertyPanel();
            }
            else if (args.PropertyName == nameof(TimelineEditorViewModel.IsPlaying))
            {
                // 同步视频播放状态
                var videoPreview = this.FindControl<VideoPreviewControl>("VideoPreview");
                if (videoPreview != null && ViewModel != null)
                {
                    if (ViewModel.IsPlaying)
                        videoPreview.Play();
                    else
                        videoPreview.Pause();
                }
            }
            else if (args.PropertyName == nameof(TimelineEditorViewModel.PlaybackState))
            {
                // 时间轴停止时, 同步停止视频
                var videoPreview = this.FindControl<VideoPreviewControl>("VideoPreview");
                if (videoPreview != null && ViewModel?.PlaybackState == PlaybackState.Idle)
                {
                    videoPreview.Stop();
                }
            }
        }

        /// <summary>
        /// 连接 TimeRulerControl 事件到 ViewModel
        /// </summary>
        private void SetupRulerEvents()
        {
            var ruler = this.FindControl<TimeRulerControl>("TimeRuler");
            if (ruler != null)
            {
                ruler.PlayheadSeek += ms =>
                {
                    ViewModel?.SeekTo(ms);
                    // 精确帧预览: 拖拽播放头时使用逐帧 seek 显示精确画面
                    var videoPreview = this.FindControl<VideoPreviewControl>("VideoPreview");
                    if (videoPreview != null && ViewModel != null)
                        videoPreview.SeekAccurate(ViewModel.CurrentTimeMs);
                };
                ruler.ZoomChanged += ppm =>
                {
                    if (ViewModel != null)
                        ViewModel.PixelsPerMs = ppm;
                };
                ruler.ScrollChanged += offset =>
                {
                    if (ViewModel != null)
                        ViewModel.ScrollOffsetX = offset;
                };

                // 事件选中
                ruler.EventSelected += evt =>
                {
                    if (ViewModel != null)
                    {
                        ViewModel.SelectedEvent = evt;
                        // 在属性面板显示事件信息
                        ViewModel.KeyframePropertyVm.ShowEvent(evt);
                    }
                };

                // 事件删除
                ruler.EventDeleteRequested += evt =>
                {
                    if (ViewModel != null)
                    {
                        ViewModel.RemoveEvent(evt);
                        SyncEventsToRuler();
                        ViewModel.KeyframePropertyVm.Clear();
                    }
                };

                // 事件拖拽移动
                ruler.EventMoved += (evt, newTimeMs) =>
                {
                    if (ViewModel != null)
                    {
                        ViewModel.UpdateEvent(evt, timeMs: newTimeMs);
                        SyncEventsToRuler();
                        // 更新属性面板中的时间
                        ViewModel.KeyframePropertyVm.ShowEvent(evt);
                    }
                };

                // 标记选中
                ruler.MarkerSelected += marker =>
                {
                    if (ViewModel != null)
                    {
                        ViewModel.MarkerService.SelectedMarker = marker;
                    }
                };

                // 标记删除
                ruler.MarkerDeleteRequested += marker =>
                {
                    if (ViewModel != null)
                    {
                        ViewModel.MarkerService.RemoveMarker(marker);
                        SyncMarkersToRuler();
                    }
                };

                // 标记拖拽移动
                ruler.MarkerMoved += (marker, newTimeMs) =>
                {
                    if (ViewModel != null)
                    {
                        ViewModel.MarkerService.UpdateMarker(marker, timeMs: newTimeMs);
                        SyncMarkersToRuler();
                    }
                };

                // 右键空白区域 → 添加事件
                ruler.AddEventRequested += timeMs =>
                {
                    if (ViewModel != null)
                    {
                        ViewModel.AddEvent(timeMs, "NewEvent");
                        SyncEventsToRuler();
                    }
                };

                // 右键空白区域 → 添加标记
                ruler.AddMarkerRequested += timeMs =>
                {
                    if (ViewModel != null)
                    {
                        ViewModel.MarkerService.AddMarker(timeMs, "标记");
                        SyncMarkersToRuler();
                    }
                };
            }
        }

        /// <summary>
        /// 同步事件列表到时间标尺控件
        /// </summary>
        private void SyncEventsToRuler()
        {
            var ruler = this.FindControl<TimeRulerControl>("TimeRuler");
            if (ruler != null && ViewModel?.Timeline != null)
            {
                ruler.Events = ViewModel.Timeline.Events;
                ruler.InvalidateVisual();
            }
        }

        /// <summary>
        /// 同步标记列表到时间标尺控件
        /// </summary>
        private void SyncMarkersToRuler()
        {
            var ruler = this.FindControl<TimeRulerControl>("TimeRuler");
            if (ruler != null && ViewModel != null)
            {
                ruler.Markers = ViewModel.MarkerService.GetMarkers().ToList();
                ruler.InvalidateVisual();
            }
        }

        /// <summary>
        /// 连接 KeyframePropertyPanel 事件
        /// </summary>
        private void SetupPropertyPanelEvents()
        {
            var panel = this.FindControl<KeyframePropertyPanel>("KfPropertyPanel");
            if (panel == null)
                return;

            // 将 ViewModel 的 KeyframePropertyVm 设为面板 DataContext
            if (ViewModel != null)
            {
                panel.DataContext = ViewModel.KeyframePropertyVm;
                WirePropertyVmEvents(ViewModel.KeyframePropertyVm);
            }

            // DataContext 变更时重新连接
            this.GetObservable(DataContextProperty)
                .Subscribe(_ =>
                {
                    if (ViewModel != null)
                    {
                        panel.DataContext = ViewModel.KeyframePropertyVm;
                        WirePropertyVmEvents(ViewModel.KeyframePropertyVm);
                    }
                });
        }

        /// <summary>
        /// 连接 KeyframePropertyViewModel 事件到 Window 操作。
        /// </summary>
        private void WirePropertyVmEvents(KeyframePropertyViewModel kfVm)
        {
            kfVm.KeyframePropertyEdited += (
                time,
                value,
                interp,
                tangentIn,
                tangentOut,
                cp1x,
                cp2x
            ) =>
            {
                ViewModel?.UpdateKeyframeProperties(
                    time,
                    value,
                    interp,
                    tangentIn,
                    tangentOut,
                    cp1x,
                    cp2x
                );
                SyncCurveEditorData();
                RefreshAllTrackControls();
                RefreshPropertyPanel();
            };
            kfVm.DeleteEventRequested += evt =>
            {
                if (ViewModel != null)
                {
                    ViewModel.RemoveEvent(evt);
                    SyncEventsToRuler();
                    ViewModel.KeyframePropertyVm.Clear();
                }
            };
            kfVm.EventPropertyEdited += (eventName, eventData) =>
            {
                ViewModel?.UpdateKeyframeEvent(eventName, eventData);
            };
            kfVm.StandaloneEventEdited += (evt, timeMs, eventName, eventData) =>
            {
                if (ViewModel != null)
                {
                    bool ok = ViewModel.UpdateEvent(evt, timeMs, eventName, eventData);
                    if (ok)
                        SyncEventsToRuler();
                }
            };
            kfVm.ValidateEventName = (name, exclude) =>
            {
                return ViewModel?.IsEventNameUnique(name, exclude) ?? true;
            };
            kfVm.BatchInterpolationChanged += interp =>
            {
                if (ViewModel != null)
                {
                    ViewModel.SetSelectedKeyframesInterpolation(interp);
                    RefreshAllTrackControls();
                }
            };
            // ◆ 切换关键帧: 在播放头位置添加/删除关键帧
            kfVm.ToggleKeyframeRequested += timeMs =>
            {
                if (ViewModel?.SelectedTrack is null)
                    return;
                ViewModel.AddKeyframeAtTime(ViewModel.SelectedTrack, timeMs);
                SyncCurveEditorData();
                RefreshAllTrackControls();
                RefreshPropertyPanel();
            };
            // AutoKey: 创建关键帧并赋予用户指定的值
            kfVm.AutoKeyCreateRequested += (timeMs, desiredValue) =>
            {
                if (ViewModel?.SelectedTrack is null)
                    return;
                ViewModel.AddKeyframeAtTime(ViewModel.SelectedTrack, timeMs);
                // 赋予用户拖拽/输入的值
                if (ViewModel.SelectedKeyframe != null)
                {
                    var kf = ViewModel.SelectedKeyframe;
                    ViewModel.UpdateKeyframeProperties(
                        kf.TimeMs,
                        desiredValue,
                        kf.Interpolation,
                        kf.TangentIn,
                        kf.TangentOut,
                        kf.Cp1x,
                        kf.Cp2x
                    );
                }
                SyncCurveEditorData();
                RefreshAllTrackControls();
                RefreshPropertyPanel();
            };
            // ◀ 跳转到前一个关键帧
            kfVm.NavigatePrevKeyframeRequested += () =>
            {
                NavigateToPrevKeyframe();
                RefreshPropertyPanel();
            };
            // ▶ 跳转到后一个关键帧
            kfVm.NavigateNextKeyframeRequested += () =>
            {
                NavigateToNextKeyframe();
                RefreshPropertyPanel();
            };
            // 播放头处事件编辑
            kfVm.PlayheadEventEdited += (evt, name, data, dataType) =>
            {
                if (ViewModel != null)
                {
                    bool ok = ViewModel.UpdateEvent(evt, evt.TimeMs, name, data, dataType);
                    if (ok)
                        SyncEventsToRuler();
                }
            };
        }

        /// <summary>
        /// 连接视频预览控件事件
        /// </summary>
        private void SetupVideoPreviewEvents()
        {
            var videoPreview = this.FindControl<VideoPreviewControl>("VideoPreview");
            if (videoPreview != null)
            {
                videoPreview.VideoLoaded += path =>
                {
                    if (ViewModel != null)
                    {
                        ViewModel.VideoFilePath = path;
                        // 加载视频后将播放头移动到起始位置, 视频 seek 到首帧
                        ViewModel.CurrentTimeMs = 0;
                        videoPreview.SetCurrentTimeMs(0);

                        // 自动提取音频波形
                        ExtractWaveformAsync(path);
                    }
                };

                videoPreview.VideoDurationDetected += durationMs =>
                {
                    if (ViewModel != null)
                    {
                        ViewModel.VideoDurationMs = durationMs;
                    }
                };

                videoPreview.FullscreenRequested += isFullscreen =>
                {
                    ToggleVideoFullscreen(isFullscreen);
                };
            }
        }

        // ═══════ 音频波形提取 ═══════

        private AudioWaveformExtractor? _waveformExtractor;
        private System.Threading.CancellationTokenSource? _waveformCts;

        /// <summary>
        /// 异步提取音频波形 — 加载视频后自动调用。
        /// 支持渐进式波形显示 (边提取边渲染) + 可选节拍检测。
        /// </summary>
        private async void ExtractWaveformAsync(string filePath)
        {
            if (ViewModel is null)
                return;

            // 取消之前的提取任务
            _waveformCts?.Cancel();
            _waveformCts = new System.Threading.CancellationTokenSource();
            var ct = _waveformCts.Token;

            _waveformExtractor ??= new AudioWaveformExtractor();

            ViewModel.IsExtractingWaveform = true;
            ViewModel.WaveformExtractionProgress = 0;
            ViewModel.WaveformData = null;
            ViewModel.BeatMarkers = null;

            var waveformControl = this.FindControl<WaveformControl>("WaveformTrack");

            // 监听进度
            void OnProgress(double progress)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (ViewModel != null)
                        ViewModel.WaveformExtractionProgress = progress;
                });
            }

            // 渐进式波形更新 — 在提取过程中实时显示部分波形
            void OnProgressiveUpdate(WaveformData snapshot)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (ViewModel is null || ct.IsCancellationRequested)
                        return;
                    ViewModel.WaveformData = snapshot;
                    waveformControl?.SetWaveformData(snapshot);
                });
            }

            _waveformExtractor.ProgressChanged += OnProgress;
            _waveformExtractor.ProgressiveWaveformUpdate += OnProgressiveUpdate;

            try
            {
                var waveform = await _waveformExtractor.ExtractAsync(filePath, 4000, ct);

                if (ct.IsCancellationRequested)
                    return;

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (ViewModel is null)
                        return;

                    ViewModel.WaveformData = waveform;
                    ViewModel.IsExtractingWaveform = false;

                    // 同步最终波形数据到控件
                    if (waveformControl != null)
                    {
                        waveformControl.SetWaveformData(waveform);
                    }

                    // 节拍检测 — 仅在用户启用时执行
                    if (waveform is not null && ViewModel.IsBeatDetectionEnabled)
                    {
                        var beats = BeatDetector.DetectBeats(waveform);
                        ViewModel.BeatMarkers = beats;
                        waveformControl?.SetBeatMarkers(beats);
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // 取消: 忽略
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[TimelineEditorWindow] Waveform extraction failed: {ex.Message}"
                );
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (ViewModel != null)
                        ViewModel.IsExtractingWaveform = false;
                });
            }
            finally
            {
                // 清理事件订阅, 防止内存泄漏
                _waveformExtractor.ProgressChanged -= OnProgress;
                _waveformExtractor.ProgressiveWaveformUpdate -= OnProgressiveUpdate;
            }
        }

        // ═══════ 视频全屏切换 (真全屏: 无标题栏 + 撑满屏幕) ═══════

        private bool _isVideoFullscreen;
        private GridLength _savedUpperRowHeight;
        private GridLength _savedLowerRowHeight;
        private GridLength _savedPropertyColWidth;
        private WindowState _savedWindowState;
        private SystemDecorations _savedSystemDecorations;

        /// <summary>
        /// 切换视频全屏模式 — 真全屏 (无标题栏, 窗口撑满屏幕)
        /// 隐藏属性面板、时间轴区域, 并将窗口设为 FullScreen + 无装饰
        /// </summary>
        private void ToggleVideoFullscreen(bool enterFullscreen)
        {
            _isVideoFullscreen = enterFullscreen;

            var mainGrid = this.FindControl<Grid>("MainContentGrid");
            var upperGrid = this.FindControl<Grid>("UpperGrid");
            var splitter = this.FindControl<GridSplitter>("MainHorizontalSplitter");
            var lowerPanel = this.FindControl<DockPanel>("LowerPanel");
            var kfPanel = this.FindControl<Control>("KfPropertyPanel");
            var vertSplitter = this.FindControl<GridSplitter>("UpperVerticalSplitter");
            var menu = this.FindControl<Menu>("MainMenu");

            if (mainGrid == null || upperGrid == null)
                return;

            if (enterFullscreen)
            {
                // 保存当前布局和窗口状态
                _savedUpperRowHeight = mainGrid.RowDefinitions[0].Height;
                _savedLowerRowHeight = mainGrid.RowDefinitions[2].Height;
                _savedPropertyColWidth = upperGrid.ColumnDefinitions[2].Width;
                _savedWindowState = this.WindowState;
                _savedSystemDecorations = this.SystemDecorations;

                // 隐藏菜单栏
                if (menu != null)
                    menu.IsVisible = false;

                // 隐藏下半部和分隔条
                if (splitter != null)
                    splitter.IsVisible = false;
                if (lowerPanel != null)
                    lowerPanel.IsVisible = false;
                mainGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
                mainGrid.RowDefinitions[0].MinHeight = 0;
                mainGrid.RowDefinitions[2].Height = new GridLength(0);
                mainGrid.RowDefinitions[2].MinHeight = 0;

                // 隐藏右侧属性面板
                if (kfPanel != null)
                    kfPanel.IsVisible = false;
                if (vertSplitter != null)
                    vertSplitter.IsVisible = false;
                upperGrid.ColumnDefinitions[2].Width = new GridLength(0);
                upperGrid.ColumnDefinitions[2].MinWidth = 0;

                // 真全屏: 去掉标题栏, 窗口撑满屏幕
                this.SystemDecorations = SystemDecorations.None;
                this.WindowState = WindowState.FullScreen;
            }
            else
            {
                // 恢复窗口状态
                this.WindowState = _savedWindowState;
                this.SystemDecorations = _savedSystemDecorations;

                // 恢复菜单栏
                if (menu != null)
                    menu.IsVisible = true;

                // 恢复下半部和分隔条
                if (splitter != null)
                    splitter.IsVisible = true;
                if (lowerPanel != null)
                    lowerPanel.IsVisible = true;
                mainGrid.RowDefinitions[0].Height = _savedUpperRowHeight;
                mainGrid.RowDefinitions[0].MinHeight = 120;
                mainGrid.RowDefinitions[2].Height = _savedLowerRowHeight;
                mainGrid.RowDefinitions[2].MinHeight = 180;

                // 恢复右侧属性面板
                if (kfPanel != null)
                    kfPanel.IsVisible = true;
                if (vertSplitter != null)
                    vertSplitter.IsVisible = true;
                upperGrid.ColumnDefinitions[2].Width = _savedPropertyColWidth;
                upperGrid.ColumnDefinitions[2].MinWidth = 220;
            }
        }

        /// <summary>
        /// 同步左侧轨道头和右侧轨道片段的垂直滚动
        /// </summary>
        private void SetupScrollSync()
        {
            var clipScroller = this.FindControl<ScrollViewer>("TrackClipScroller");
            var headerScroller = this.FindControl<ScrollViewer>("TrackHeaderScroller");
            if (clipScroller != null && headerScroller != null)
            {
                clipScroller.ScrollChanged += (s, e) =>
                {
                    headerScroller.Offset = new Vector(0, clipScroller.Offset.Y);
                };
            }
        }

        // ═══════ 文件对话框 ═══════

        public async void OnOpenFile()
        {
            var files = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "打开动作文件",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("动作文件") { Patterns = new[] { "*.motion" } },
                        new FilePickerFileType("所有文件") { Patterns = new[] { "*.*" } },
                    }
                }
            );

            if (files.Count > 0)
            {
                var path = files[0].TryGetLocalPath();
                if (!string.IsNullOrEmpty(path))
                    ViewModel?.LoadFromFile(path);
            }
        }

        public async void OnSaveAs()
        {
            var file = await StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "保存动作文件",
                    DefaultExtension = ".motion",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("动作文件") { Patterns = new[] { "*.motion" } },
                    },
                    SuggestedFileName = ViewModel?.Timeline?.Name ?? "untitled"
                }
            );

            if (file != null)
            {
                var path = file.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path))
                    ViewModel?.SaveToFile(path);
            }
        }

        /// <summary>导出对话框</summary>
        private async void OnExportDialog(object? sender, RoutedEventArgs e)
        {
            if (ViewModel?.Timeline == null)
                return;

            var dialog = new ExportDialog(ViewModel.Timeline);
            var result = await dialog.ShowDialog<string?>(this);

            if (!string.IsNullOrEmpty(result))
            {
                // 导出成功, 可选: 打开文件夹
                System.Diagnostics.Debug.WriteLine($"[Export] Success: {result}");
            }
        }

        // ═══════ 键盘快捷键 ═══════

        private readonly IKeyboardShortcutService _shortcutService = new KeyboardShortcutService();

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (ViewModel is null)
                return;

            // 当焦点在 TextBox 内时, 不拦截键盘事件 (允许正常文本输入)
            if (
                TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement()
                is Avalonia.Controls.TextBox
            )
                return;

            var action = _shortcutService.Resolve(
                e.Key,
                e.KeyModifiers,
                isVideoFullscreen: _isVideoFullscreen,
                hasWorkArea: ViewModel.HasWorkArea
            );

            if (action is ShortcutAction.None)
                return;

            // View 层需要直接处理的动作 (对话框/控件导航/全屏)
            switch (action)
            {
                case ShortcutAction.ExitVideoFullscreen:
                    this.FindControl<VideoPreviewControl>("VideoPreview")?.ExitFullscreen();
                    ToggleVideoFullscreen(false);
                    e.Handled = true;
                    return;
                case ShortcutAction.SaveAs:
                    OnSaveAs();
                    e.Handled = true;
                    return;
                case ShortcutAction.OpenFile:
                    OnOpenFile();
                    e.Handled = true;
                    return;
                case ShortcutAction.AddKeyframeAtPlayhead:
                    OnAddKeyframeAtPlayhead(null, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case ShortcutAction.AddEventAtPlayhead:
                    OnAddEventAtPlayhead(null, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case ShortcutAction.ZoomToFit:
                    OnZoomToFit(null, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case ShortcutAction.PreviousKeyframe:
                    NavigateToPrevKeyframe();
                    e.Handled = true;
                    return;
                case ShortcutAction.NextKeyframe:
                    NavigateToNextKeyframe();
                    e.Handled = true;
                    return;
            }

            // UX-D1: F2 重命名 — 根据当前选中项路由 (轨道→属性对话框; 事件/标记→属性面板聚焦名称输入)
            if (action == ShortcutAction.RenameSelected)
            {
                _ = HandleRenameSelectedAsync();
                e.Handled = true;
                return;
            }

            // ViewModel 可处理的动作 — 委托执行并根据结果同步 UI
            var result = ViewModel.ExecuteShortcut(action);

            switch (result)
            {
                case ExecuteShortcutResult.Handled:
                    e.Handled = true;
                    break;
                case ExecuteShortcutResult.HandledNeedsRefresh:
                    RefreshAllTrackControls();
                    RefreshPropertyPanel();
                    e.Handled = true;
                    break;
                case ExecuteShortcutResult.HandledNeedsMultiSelectRefresh:
                    SyncMultiSelectionToControls();
                    RefreshAllTrackControls();
                    RefreshPropertyPanel();
                    e.Handled = true;
                    break;
                case ExecuteShortcutResult.HandledNeedsSyncClipboard:
                    SyncClipboardState();
                    e.Handled = true;
                    break;
                case ExecuteShortcutResult.HandledNeedsSyncZoom:
                    SyncZoomToRuler();
                    e.Handled = true;
                    break;
                case ExecuteShortcutResult.HandledNeedsSyncMarkers:
                    SyncMarkersToRuler();
                    e.Handled = true;
                    break;
                case ExecuteShortcutResult.HandledNeedsSyncWorkArea:
                    SyncWorkAreaToRuler();
                    e.Handled = true;
                    break;
                case ExecuteShortcutResult.HandledNeedsSyncEvents:
                    SyncEventsToRuler();
                    RefreshPropertyPanel();
                    e.Handled = true;
                    break;
            }
        }

        protected override async void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);

            if (ViewModel != null && ViewModel.IsDirty)
            {
                e.Cancel = true;

                var msgBox = new Window
                {
                    Title = "未保存的更改",
                    Width = 400,
                    Height = 160,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false
                };

                var discardBtn = new Button { Content = "不保存退出", Margin = new Thickness(4) };
                var cancelBtn = new Button { Content = "取消", Margin = new Thickness(4) };
                var saveBtn = new Button { Content = "保存并退出", Margin = new Thickness(4) };

                int choice = 0; // 0=cancel, 1=discard, 2=save
                discardBtn.Click += (_, _) =>
                {
                    choice = 1;
                    msgBox.Close();
                };
                cancelBtn.Click += (_, _) =>
                {
                    choice = 0;
                    msgBox.Close();
                };
                saveBtn.Click += (_, _) =>
                {
                    choice = 2;
                    msgBox.Close();
                };

                var panel = new StackPanel
                {
                    Margin = new Thickness(16),
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = "当前文件有未保存的更改，是否保存？", FontSize = 14 },
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Spacing = 8,
                            Children = { discardBtn, cancelBtn, saveBtn }
                        }
                    }
                };
                msgBox.Content = panel;
                await msgBox.ShowDialog(this);

                if (choice == 2)
                {
                    // 保存并退出
                    ViewModel.SaveFileCommand.Execute().Subscribe();
                }

                if (choice == 1 || choice == 2)
                {
                    // 清除 dirty 标志后关闭
                    ViewModel.IsDirty = false;
                    Close();
                }
                // choice == 0: 取消, 不关闭
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (ViewModel != null)
                ViewModel.PropertyChanged -= OnViewModelPropertyChanged;

            // 清理波形提取器
            _waveformCts?.Cancel();
            _waveformCts?.Dispose();
            _waveformExtractor?.Dispose();

            ViewModel?.Dispose();
            base.OnClosed(e);
        }
    }
}
