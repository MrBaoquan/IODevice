using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Threading;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using ReactiveUI;

namespace IOStudio.ViewModels.Timeline
{
    /// <summary>
    /// 时间轴编辑器主 ViewModel
    /// 管理轨道列表、播放控制、文件操作
    /// 实现 ITimelineContext 提供跨模块共享状态
    /// </summary>
    public partial class TimelineEditorViewModel : ViewModelBase, ITimelineContext
    {
        private readonly IPlaybackEngine _engine;
        private readonly IUndoRedoService _undoRedo;
        private readonly Services.IDialogService? _dialogService;

        /// <summary>Undo/Redo 服务 (ITimelineContext 接口)</summary>
        public IUndoRedoService UndoRedo => _undoRedo;

        /// <summary>时间轴标记管理服务</summary>
        private readonly MarkerService _markerService;
        public MarkerService MarkerService => _markerService;

        /// <summary>关键帧属性面板 ViewModel</summary>
        public KeyframePropertyViewModel KeyframePropertyVm { get; } =
            new KeyframePropertyViewModel();

        private IDisposable? _tickSubscription;
        private IDisposable? _autoSaveSubscription;
        private const int AutoSaveIntervalSeconds = 60;

        /// <summary>MotionFile 对话框过滤器。</summary>
        private static readonly System.Collections.Generic.List<Services.DialogFileFilter> MotionFileFilters =
            new()
            {
                new Services.DialogFileFilter { Name = "动作文件", Extensions = new[] { "motion" } },
                new Services.DialogFileFilter { Name = "所有文件", Extensions = new[] { "*" } },
            };

        /// <summary>
        /// 默认构造函数 — 通过 ServiceLocator 获取服务 (兼容 View code-behind 直接实例化)。
        /// </summary>
        public TimelineEditorViewModel()
            : this(
                Services.ServiceLocator.TryResolve<Services.IDialogService>(),
                Services.ServiceLocator.TryResolve<IUndoRedoService>(),
                Services.ServiceLocator.TryResolve<IPlaybackEngine>()
            ) { }

        /// <summary>
        /// DI 构造函数 — 所有依赖通过参数注入 (可测试)。
        /// </summary>
        public TimelineEditorViewModel(
            Services.IDialogService? dialogService,
            IUndoRedoService? undoRedoService = null,
            IPlaybackEngine? playbackEngine = null
        )
        {
            _dialogService = dialogService;
            _undoRedo = undoRedoService ?? new UndoRedoService();
            _engine = playbackEngine ?? new MotionPlaybackEngine();

            // 统一引擎通知 (替代 5 个独立事件订阅)
            _engine.Notified += OnEngineNotified;

            // 监听设备分发值变化 (Phase 3.1.5: 播放时 DO 值同步)
            _engine.Dispatcher.ValueDispatched += OnValueDispatched;

            // 命令初始化
            NewProjectCommand = ReactiveCommand.Create(NewProject);
            OpenFileCommand = ReactiveCommand.Create(OpenFile);
            SaveFileCommand = ReactiveCommand.Create(SaveFile);
            SaveAsCommand = ReactiveCommand.Create(SaveAs);

            PlayCommand = ReactiveCommand.Create(Play);
            PauseCommand = ReactiveCommand.Create(Pause);
            PlayPauseCommand = ReactiveCommand.Create(() =>
            {
                if (IsPlaying)
                    Pause();
                else
                    Play();
            });
            StopCommand = ReactiveCommand.Create(Stop);
            ToggleLoopCommand = ReactiveCommand.Create(ToggleLoop);
            DeleteKeyframeCommand = ReactiveCommand.Create(DeleteSelectedKeyframe);
            AddTrackCommand = ReactiveCommand.Create(() => { }); // View 层通过事件处理
            RemoveTrackCommand = ReactiveCommand.Create<TrackViewModel>(RemoveTrack);
            UndoCommand = ReactiveCommand.Create(Undo);
            RedoCommand = ReactiveCommand.Create(Redo);
            ToggleThemeCommand = ReactiveCommand.Create(ToggleTheme);

            _undoRedo.StateChanged += () =>
            {
                this.RaisePropertyChanged(nameof(CanUndo));
                this.RaisePropertyChanged(nameof(CanRedo));
                this.RaisePropertyChanged(nameof(UndoDescription));
                this.RaisePropertyChanged(nameof(RedoDescription));
            };

            // 初始化标记服务
            _markerService = new MarkerService(this);

            // Tracks 集合变更时失效通道索引 + 更新状态栏轨道计数
            _tracks.CollectionChanged += (_, _) =>
            {
                InvalidateTrackChannelIndex();
                this.RaisePropertyChanged(nameof(TrackCountLabel));
            };

            // 自动初始化一个默认项目, 确保 Timeline 始终不为 null
            NewProject();
        }

        // ---- 属性 ----

        private MotionTimeline? _timeline;
        public MotionTimeline? Timeline
        {
            get => _timeline;
            set => this.RaiseAndSetIfChanged(ref _timeline, value);
        }

        private ObservableCollection<TrackViewModel> _tracks = new();
        public ObservableCollection<TrackViewModel> Tracks
        {
            get => _tracks;
            set => this.RaiseAndSetIfChanged(ref _tracks, value);
        }

        private double _currentTimeMs;
        public double CurrentTimeMs
        {
            get => _currentTimeMs;
            set
            {
                var bounded = ClampTime(value);
                this.RaiseAndSetIfChanged(ref _currentTimeMs, bounded);
            }
        }

        /// <summary>将时间限制在当前项目时间轴范围内。</summary>
        public double ClampTime(double timeMs)
        {
            if (double.IsNaN(timeMs) || double.IsInfinity(timeMs))
                return 0;
            var max =
                DurationMs > 0
                    ? DurationMs
                    : (Timeline?.DurationMs > 0 ? Timeline.DurationMs : double.MaxValue);
            return Math.Clamp(timeMs, 0, max);
        }

        private double _durationMs;
        public double DurationMs
        {
            get => _durationMs;
            set
            {
                var bounded =
                    double.IsNaN(value) || double.IsInfinity(value) ? 0 : Math.Max(0, value);
                this.RaiseAndSetIfChanged(ref _durationMs, bounded);
                if (bounded > 0 && CurrentTimeMs > bounded)
                    CurrentTimeMs = bounded;
            }
        }

        /// <summary>用户手动设置的总时长 (ms), 0 表示自动计算</summary>
        private double _manualDurationMs;
        public double ManualDurationMs
        {
            get => _manualDurationMs;
            set => this.RaiseAndSetIfChanged(ref _manualDurationMs, value);
        }

        /// <summary>
        /// 用户手动设置总时长 (毫秒). 设为 0 恢复自动计算. (支持 Undo/Redo)
        /// </summary>
        public void SetManualDuration(double durationMs)
        {
            double oldManual = ManualDurationMs;
            double oldDuration = DurationMs;

            void ApplyDuration(double manual)
            {
                ManualDurationMs = manual;
                if (manual > 0)
                {
                    DurationMs = manual;
                    if (Timeline is not null)
                        Timeline.DurationMs = manual;
                    this.RaisePropertyChanged(nameof(DurationDisplay));
                }
                else
                {
                    _durationMs = 0;
                    RecalculateDuration();
                }
            }

            _undoRedo.Execute(
                new LambdaCommand(
                    "设置时长",
                    () => ApplyDuration(durationMs),
                    () => ApplyDuration(oldManual)
                )
            );
            MarkDirty();
        }

        private PlaybackState _playbackState = PlaybackState.Idle;
        public PlaybackState PlaybackState
        {
            get => _playbackState;
            set => this.RaiseAndSetIfChanged(ref _playbackState, value);
        }

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            set => this.RaiseAndSetIfChanged(ref _isPlaying, value);
        }

        private bool _isLoop;
        public bool IsLoop
        {
            get => _isLoop;
            set
            {
                this.RaiseAndSetIfChanged(ref _isLoop, value);
                _engine.Loop = value;
            }
        }

        private bool _isLiveOutputEnabled;

        /// <summary>是否将预览值实时输出到物理设备 (设备输出开关)</summary>
        public bool IsLiveOutputEnabled
        {
            get => _isLiveOutputEnabled;
            set
            {
                this.RaiseAndSetIfChanged(ref _isLiveOutputEnabled, value);
                _engine.LiveOutputToDevice = value;

                // 启用时立即输出当前时刻的轨道值到设备
                if (value)
                    _engine.Seek(CurrentTimeMs);
            }
        }

        private double _playbackSpeed = 1.0;
        public double PlaybackSpeed
        {
            get => _playbackSpeed;
            set
            {
                this.RaiseAndSetIfChanged(ref _playbackSpeed, value);
                _engine.Speed = value;
            }
        }

        // ---- 缩放/滚动 ----

        private double _pixelsPerMs = 0.01;

        /// <summary>每毫秒对应像素数 (缩放级别)</summary>
        public double PixelsPerMs
        {
            get => _pixelsPerMs;
            set => this.RaiseAndSetIfChanged(ref _pixelsPerMs, value);
        }

        private double _scrollOffsetX;

        /// <summary>水平滚动偏移 (像素)</summary>
        public double ScrollOffsetX
        {
            get => _scrollOffsetX;
            set => this.RaiseAndSetIfChanged(ref _scrollOffsetX, value);
        }

        private TrackViewModel? _selectedTrack;

        /// <summary>当前选中的轨道</summary>
        public TrackViewModel? SelectedTrack
        {
            get => _selectedTrack;
            set
            {
                var changed = !ReferenceEquals(_selectedTrack, value);
                this.RaiseAndSetIfChanged(ref _selectedTrack, value);
                if (changed && _presetPanelVm != null)
                    _presetPanelVm.SetTargetTrack(value?.Id, value?.Label, value?.IsLocked == true);
                // UX-B2: 仅选中 / 仅当前分组模式需随选择变化刷新显示列表
                if (
                    changed
                    && _displayFilter != null
                    && (
                        _displayFilter.Mode == DisplayFilterMode.SelectedTrackOnly
                        || _displayFilter.Mode == DisplayFilterMode.ActiveGroupOnly
                    )
                )
                {
                    RefreshDisplayList();
                }
            }
        }

        /// <summary>验证轨道是否允许内容编辑，并向状态栏解释被拒绝的原因。</summary>
        internal bool TryBeginTrackEdit(TrackViewModel? track, string operation)
        {
            if (track == null)
                return false;
            if (!track.IsLocked)
                return true;

            PushStatus($"轨道“{track.Label}”已锁定，无法{operation}");
            return false;
        }

        /// <summary>批量编辑必须保证所有涉及轨道均未锁定，避免产生部分提交。</summary>
        internal bool TryBeginTrackEdit(IEnumerable<TrackViewModel> tracks, string operation)
        {
            var locked = tracks.Distinct().FirstOrDefault(track => track.IsLocked);
            if (locked == null)
                return true;

            PushStatus($"轨道“{locked.Label}”已锁定，无法{operation}");
            return false;
        }

        // ---- 轨道多选状态 (Ctrl+点击轨道头切换) ----

        private readonly ObservableCollection<TrackViewModel> _multiSelectedTracks = new();

        /// <summary>多选轨道集合 (Ctrl+点击轨道头切换)。</summary>
        public ObservableCollection<TrackViewModel> MultiSelectedTracks => _multiSelectedTracks;

        /// <summary>切换轨道多选状态 (Ctrl 点击轨道头)。首次进入多选时保留当前激活轨道。</summary>
        public void ToggleTrackMultiSelect(TrackViewModel trackVm)
        {
            if (trackVm == null)
                return;
            // 从单选过渡到多选: 首次 Ctrl 点击时, 把当前已经激活的轨道一并纳入多选, 避免它被取消选择
            if (
                _multiSelectedTracks.Count == 0
                && SelectedTrack != null
                && !ReferenceEquals(SelectedTrack, trackVm)
            )
                _multiSelectedTracks.Add(SelectedTrack);

            if (_multiSelectedTracks.Contains(trackVm))
                _multiSelectedTracks.Remove(trackVm);
            else
                _multiSelectedTracks.Add(trackVm);

            if (_multiSelectedTracks.Count > 0)
                SelectedTrack = _multiSelectedTracks[^1];
        }

        /// <summary>清空多选 (单选时调用)。</summary>
        public void ClearMultiSelection()
        {
            _multiSelectedTracks.Clear();
        }

        // ---- 关键帧选中状态 ----

        private int _selectedClipIndex = -1;
        public int SelectedClipIndex
        {
            get => _selectedClipIndex;
            set => this.RaiseAndSetIfChanged(ref _selectedClipIndex, value);
        }

        private int _selectedKeyframeIndex = -1;
        public int SelectedKeyframeIndex
        {
            get => _selectedKeyframeIndex;
            set => this.RaiseAndSetIfChanged(ref _selectedKeyframeIndex, value);
        }

        private string? _selectedActionInstanceId;

        /// <summary>当前作为整体选中的影片动作实例。</summary>
        public string? SelectedActionInstanceId
        {
            get => _selectedActionInstanceId;
            set => this.RaiseAndSetIfChanged(ref _selectedActionInstanceId, value);
        }

        /// <summary>当前选中的关键帧 ViewModel (用于属性面板绑定)</summary>
        private KeyframeViewModel? _selectedKeyframe;
        public KeyframeViewModel? SelectedKeyframe
        {
            get => _selectedKeyframe;
            set => this.RaiseAndSetIfChanged(ref _selectedKeyframe, value);
        }

        // ---- 多关键帧选择 ----

        /// <summary>多选的关键帧列表: (TrackViewModel, clipIndex, keyframeIndex)</summary>
        private List<(TrackViewModel Track, int ClipIdx, int KfIdx)> _selectedKeyframes = new();

        /// <summary>多选的关键帧列表 (只读)</summary>
        public IReadOnlyList<(TrackViewModel Track, int ClipIdx, int KfIdx)> SelectedKeyframes =>
            _selectedKeyframes;

        /// <summary>是否处于多选模式</summary>
        public bool IsMultiSelectMode => _selectedKeyframes.Count > 1;

        /// <summary>多选关键帧数量</summary>
        public int SelectedKeyframeCount => _selectedKeyframes.Count;

        // ---- 关键帧剪贴板 ----

        /// <summary>剪贴板中的关键帧快照 (相对时间偏移, 值, 插值, tangentIn, tangentOut, 源轨道名称)</summary>
        private List<(
            double RelTimeMs,
            float Value,
            string Interpolation,
            float? TangentIn,
            float? TangentOut,
            string TrackName
        )> _clipboardKeyframes = new();

        /// <summary>剪贴板中是否有数据</summary>
        public bool HasClipboardKeyframes => _clipboardKeyframes.Count > 0;

        // ---- Dirty 状态 ----

        private bool _isDirty;

        /// <summary>文件是否有未保存的修改</summary>
        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _isDirty, value) != value)
                    UpdateWindowTitle();
            }
        }

        private string _currentFilePath = "";
        public string CurrentFilePath
        {
            get => _currentFilePath;
            set => this.RaiseAndSetIfChanged(ref _currentFilePath, value);
        }

        private string _windowTitle = "动作编排 — 未命名";
        public string WindowTitle
        {
            get => _windowTitle;
            set => this.RaiseAndSetIfChanged(ref _windowTitle, value);
        }

        // ---- View 层交互标志 ----

        private bool _openFileRequested;

        /// <summary>通知 View 层打开文件对话框</summary>
        public bool OpenFileRequested
        {
            get => _openFileRequested;
            set => this.RaiseAndSetIfChanged(ref _openFileRequested, value);
        }

        private bool _saveAsRequested;

        /// <summary>通知 View 层打开另存为对话框</summary>
        public bool SaveAsRequested
        {
            get => _saveAsRequested;
            set => this.RaiseAndSetIfChanged(ref _saveAsRequested, value);
        }

        // ---- 视图模式 ----

        private TimelineViewMode _viewMode = TimelineViewMode.Dopesheet;

        /// <summary>时间轴视图模式 (关键帧/曲线)</summary>
        public TimelineViewMode ViewMode
        {
            get => _viewMode;
            set
            {
                this.RaiseAndSetIfChanged(ref _viewMode, value);
                this.RaisePropertyChanged(nameof(IsDopesheetMode));
                this.RaisePropertyChanged(nameof(IsCurvesMode));
                this.RaisePropertyChanged(nameof(TrackHeight));
                RefreshAllTrackCompactDisplay();
            }
        }

        /// <summary>是否为关键帧视图 (Dopesheet)</summary>
        public bool IsDopesheetMode => ViewMode == TimelineViewMode.Dopesheet;

        /// <summary>是否为曲线视图 (Curves)</summary>
        public bool IsCurvesMode => ViewMode == TimelineViewMode.Curves;

        /// <summary>
        /// 曲线视图是否叠加显示待机循环 (幽灵) 曲线。
        /// 独立待机编辑器接管后默认 false (减负); 用户在曲线视图工具栏可随时切换"可控查看"。
        /// </summary>
        private bool _showIdleOverlaysInCurve;
        public bool ShowIdleOverlaysInCurve
        {
            get => _showIdleOverlaysInCurve;
            set => this.RaiseAndSetIfChanged(ref _showIdleOverlaysInCurve, value);
        }

        /// <summary>轨道高度级别: 0=紧凑(28/60), 1=标准(36/80), 2=展开(52/120)</summary>
        private int _trackHeightLevel = 1;
        public int TrackHeightLevel
        {
            get => _trackHeightLevel;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _trackHeightLevel, value) != value)
                    return;
                this.RaisePropertyChanged(nameof(TrackHeight));
                this.RaisePropertyChanged(nameof(TrackHeightLabel));
                this.RaisePropertyChanged(nameof(IsCompactTrackMode));
                RefreshAllTrackCompactDisplay();
            }
        }

        /// <summary>轨道高度 (根据视图模式和高度级别调整)</summary>
        public double TrackHeight
        {
            get
            {
                if (ViewMode == TimelineViewMode.Dopesheet)
                    return _trackHeightLevel switch
                    {
                        0 => 36,
                        2 => 52,
                        _ => 36
                    };
                else
                    return _trackHeightLevel switch
                    {
                        0 => 60,
                        2 => 120,
                        _ => 80
                    };
            }
        }

        /// <summary>获取指定轨道的实际高度 (优先使用独立高度, 否则使用全局)</summary>
        public double GetTrackHeight(TrackViewModel track)
        {
            return track.HasIndividualHeight ? track.IndividualTrackHeight : TrackHeight;
        }

        /// <summary>设置指定轨道的独立高度</summary>
        public void SetTrackIndividualHeight(TrackViewModel track, double height)
        {
            track.SetIndividualHeight(height);
            track.UpdateCompactDisplay(TrackHeight);
            this.RaisePropertyChanged(nameof(TrackHeight)); // 通知 UI 刷新
        }

        /// <summary>重置指定轨道为全局高度</summary>
        public void ResetTrackHeight(TrackViewModel track)
        {
            track.ResetToGlobalHeight();
            track.UpdateCompactDisplay(TrackHeight);
            this.RaisePropertyChanged(nameof(TrackHeight));
        }

        /// <summary>紧凑模式 — 隐藏副标题行和缩小徽章</summary>
        public bool IsCompactTrackMode => _trackHeightLevel == 0;

        /// <summary>当前轨道高度级别标签</summary>
        public string TrackHeightLabel =>
            _trackHeightLevel switch
            {
                0 => "紧凑",
                2 => "展开",
                _ => "标准"
            };

        /// <summary>循环切换轨道高度级别</summary>
        public void CycleTrackHeight()
        {
            TrackHeightLevel = (_trackHeightLevel + 1) % 3;
        }

        /// <summary>重置所有轨道为全局默认高度</summary>
        public void ResetAllTrackHeights()
        {
            foreach (var track in Tracks)
                track.ResetToGlobalHeight();
            RefreshAllTrackCompactDisplay();
            this.RaisePropertyChanged(nameof(TrackHeight));
        }

        /// <summary>刷新所有轨道的紧凑显示状态</summary>
        private void RefreshAllTrackCompactDisplay()
        {
            double gh = TrackHeight;
            foreach (var track in Tracks)
                track.UpdateCompactDisplay(gh);
        }

        // ---- 工作区域 (In/Out 标记) ----

        private double? _workAreaInMs;

        /// <summary>工作区域入点 (毫秒), null 表示未设置</summary>
        public double? WorkAreaInMs
        {
            get => _workAreaInMs;
            set
            {
                this.RaiseAndSetIfChanged(ref _workAreaInMs, value);
                this.RaisePropertyChanged(nameof(HasWorkArea));
                this.RaisePropertyChanged(nameof(WorkAreaDisplay));
            }
        }

        private double? _workAreaOutMs;

        /// <summary>工作区域出点 (毫秒), null 表示未设置</summary>
        public double? WorkAreaOutMs
        {
            get => _workAreaOutMs;
            set
            {
                this.RaiseAndSetIfChanged(ref _workAreaOutMs, value);
                this.RaisePropertyChanged(nameof(HasWorkArea));
                this.RaisePropertyChanged(nameof(WorkAreaDisplay));
            }
        }

        /// <summary>是否设置了有效工作区域</summary>
        public bool HasWorkArea =>
            _workAreaInMs.HasValue
            && _workAreaOutMs.HasValue
            && _workAreaOutMs.Value > _workAreaInMs.Value;

        /// <summary>工作区域显示文本</summary>
        public string WorkAreaDisplay
        {
            get
            {
                if (!HasWorkArea)
                    return "未设置";
                return $"[{_workAreaInMs!.Value:F0} ~ {_workAreaOutMs!.Value:F0}] ms";
            }
        }

        /// <summary>设置工作区域入点为当前播放头位置</summary>
        public void SetWorkAreaIn() => SetWorkAreaInAt(CurrentTimeMs);

        /// <summary>设置工作区域出点为当前播放头位置</summary>
        public void SetWorkAreaOut() => SetWorkAreaOutAt(CurrentTimeMs);

        /// <summary>在指定时间设置入点 (支持拖动工作区条带边界到任意时间)。</summary>
        public void SetWorkAreaInAt(double ms)
        {
            WorkAreaInMs = Math.Max(0, ms);
            if (_workAreaOutMs.HasValue && _workAreaOutMs.Value <= WorkAreaInMs)
                WorkAreaOutMs = null;
        }

        /// <summary>在指定时间设置出点 (支持拖动工作区条带边界到任意时间)。</summary>
        public void SetWorkAreaOutAt(double ms)
        {
            WorkAreaOutMs = Math.Max(0, ms);
            if (_workAreaInMs.HasValue && _workAreaInMs.Value >= WorkAreaOutMs)
                WorkAreaInMs = null;
        }

        /// <summary>整体移动工作区 (保持时长), deltaMs 为正向右, 自动夹在时间轴范围内。</summary>
        public void MoveWorkArea(double deltaMs)
        {
            if (!HasWorkArea || Math.Abs(deltaMs) < 0.1)
                return;
            double len = _workAreaOutMs!.Value - _workAreaInMs!.Value;
            double inMs = _workAreaInMs.Value + deltaMs;
            double outMs = inMs + len;
            double max = DurationMs > 0 ? DurationMs : 60000;
            if (inMs < 0)
            {
                inMs = 0;
                outMs = len;
            }
            if (outMs > max)
            {
                outMs = max;
                inMs = Math.Max(0, max - len);
            }
            WorkAreaInMs = inMs;
            WorkAreaOutMs = outMs;
        }

        /// <summary>清除工作区域标记</summary>
        public void ClearWorkArea()
        {
            WorkAreaInMs = null;
            WorkAreaOutMs = null;
        }

        // ---- 视频 ----

        private string _videoFilePath = "";

        /// <summary>参考视频文件路径</summary>
        public string VideoFilePath
        {
            get => _videoFilePath;
            set => this.RaiseAndSetIfChanged(ref _videoFilePath, value);
        }

        private double _videoDurationMs;

        /// <summary>参考视频时长 (毫秒), 用于在时间标尺上显示参考线</summary>
        public double VideoDurationMs
        {
            get => _videoDurationMs;
            set => this.RaiseAndSetIfChanged(ref _videoDurationMs, value);
        }

        // ---- 波形 ----

        private WaveformData? _waveformData;

        /// <summary>音频波形数据 (从视频提取)。</summary>
        public WaveformData? WaveformData
        {
            get => _waveformData;
            set => this.RaiseAndSetIfChanged(ref _waveformData, value);
        }

        private bool _isWaveformVisible = true;

        /// <summary>是否显示波形轨道。</summary>
        public bool IsWaveformVisible
        {
            get => _isWaveformVisible;
            set => this.RaiseAndSetIfChanged(ref _isWaveformVisible, value);
        }

        private bool _isExtractingWaveform;

        /// <summary>是否正在提取波形数据。</summary>
        public bool IsExtractingWaveform
        {
            get => _isExtractingWaveform;
            set => this.RaiseAndSetIfChanged(ref _isExtractingWaveform, value);
        }

        private double _waveformExtractionProgress;

        /// <summary>波形提取进度 (0.0 ~ 1.0)。</summary>
        public double WaveformExtractionProgress
        {
            get => _waveformExtractionProgress;
            set => this.RaiseAndSetIfChanged(ref _waveformExtractionProgress, value);
        }

        private List<double>? _beatMarkers;

        /// <summary>节拍标记时间列表 (毫秒)。</summary>
        public List<double>? BeatMarkers
        {
            get => _beatMarkers;
            set => this.RaiseAndSetIfChanged(ref _beatMarkers, value);
        }

        private bool _isBeatDetectionEnabled;

        /// <summary>是否启用节拍检测 (默认关闭以提升加载速度)。</summary>
        public bool IsBeatDetectionEnabled
        {
            get => _isBeatDetectionEnabled;
            set => this.RaiseAndSetIfChanged(ref _isBeatDetectionEnabled, value);
        }

        /// <summary>格式化的当前时间显示</summary>
        public string CurrentTimeDisplay =>
            TimeSpan.FromMilliseconds(CurrentTimeMs).ToString(@"mm\:ss\.fff");

        /// <summary>格式化的总时长显示</summary>
        public string DurationDisplay =>
            TimeSpan.FromMilliseconds(DurationMs).ToString(@"mm\:ss\.fff");

        // ---- 命令 ----

        public ReactiveCommand<Unit, Unit> NewProjectCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenFileCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveFileCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveAsCommand { get; }
        public ReactiveCommand<Unit, Unit> PlayCommand { get; }
        public ReactiveCommand<Unit, Unit> PauseCommand { get; }
        public ReactiveCommand<Unit, Unit> PlayPauseCommand { get; }
        public ReactiveCommand<Unit, Unit> StopCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleLoopCommand { get; }
        public ReactiveCommand<Unit, Unit> DeleteKeyframeCommand { get; }
        public ReactiveCommand<Unit, Unit> AddTrackCommand { get; }
        public ReactiveCommand<TrackViewModel, Unit> RemoveTrackCommand { get; }
        public ReactiveCommand<Unit, Unit> UndoCommand { get; }
        public ReactiveCommand<Unit, Unit> RedoCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleThemeCommand { get; }

        /// <summary>
        /// 执行键盘快捷键动作 — 由 View 层 OnKeyDown 调用
        /// 纯 ViewModel 逻辑在此处理, 需要 View 交互的返回 false
        /// </summary>
        /// <returns>true 已处理, false 需要 View 层补充处理 (如刷新控件/对话框)</returns>
        public ExecuteShortcutResult ExecuteShortcut(Models.Motion.ShortcutAction action)
        {
            switch (action)
            {
                case Models.Motion.ShortcutAction.Undo:
                    if (CanUndo)
                    {
                        Undo();
                        return ExecuteShortcutResult.HandledNeedsRefresh;
                    }
                    return ExecuteShortcutResult.Handled;

                case Models.Motion.ShortcutAction.Redo:
                    if (CanRedo)
                    {
                        Redo();
                        return ExecuteShortcutResult.HandledNeedsRefresh;
                    }
                    return ExecuteShortcutResult.Handled;

                case Models.Motion.ShortcutAction.Save:
                    SaveFile();
                    return ExecuteShortcutResult.Handled;

                case Models.Motion.ShortcutAction.NewProject:
                    NewProject();
                    return ExecuteShortcutResult.Handled;

                case Models.Motion.ShortcutAction.Copy:
                    CopySelectedKeyframes();
                    return ExecuteShortcutResult.HandledNeedsSyncClipboard;

                case Models.Motion.ShortcutAction.Paste:
                    if (HasClipboardKeyframes)
                    {
                        PasteKeyframes();
                        return ExecuteShortcutResult.HandledNeedsRefresh;
                    }
                    return ExecuteShortcutResult.Handled;

                case Models.Motion.ShortcutAction.Delete:
                    // 优先删除选中的事件 (事件选中后再按 Delete 应删除事件, 不是关键帧)
                    if (SelectedEvent is not null)
                    {
                        RemoveEvent(SelectedEvent);
                        return ExecuteShortcutResult.HandledNeedsSyncEvents;
                    }
                    if (IsMultiSelectMode)
                    {
                        DeleteSelectedKeyframes();
                        return ExecuteShortcutResult.HandledNeedsMultiSelectRefresh;
                    }
                    if (SelectedKeyframe is not null)
                    {
                        DeleteSelectedKeyframe();
                        return ExecuteShortcutResult.HandledNeedsRefresh;
                    }
                    return ExecuteShortcutResult.Handled;

                case Models.Motion.ShortcutAction.PlayPauseToggle:
                    if (IsPlaying)
                        Pause();
                    else
                        Play();
                    return ExecuteShortcutResult.Handled;

                case Models.Motion.ShortcutAction.SeekToStart:
                    SeekTo(0);
                    return ExecuteShortcutResult.Handled;

                case Models.Motion.ShortcutAction.SeekToEnd:
                    SeekTo(DurationMs);
                    return ExecuteShortcutResult.Handled;

                case Models.Motion.ShortcutAction.ZoomIn:
                    PixelsPerMs = Math.Min(1.0, PixelsPerMs * 1.25);
                    return ExecuteShortcutResult.HandledNeedsSyncZoom;

                case Models.Motion.ShortcutAction.ZoomOut:
                    PixelsPerMs = Math.Max(0.001, PixelsPerMs / 1.25);
                    return ExecuteShortcutResult.HandledNeedsSyncZoom;

                case Models.Motion.ShortcutAction.CycleTrackHeight:
                    CycleTrackHeight();
                    return ExecuteShortcutResult.HandledNeedsRefresh;

                case Models.Motion.ShortcutAction.PreviousMarker:
                    MarkerService.NavigatePrevious();
                    return ExecuteShortcutResult.Handled;

                case Models.Motion.ShortcutAction.NextMarker:
                    MarkerService.NavigateNext();
                    return ExecuteShortcutResult.Handled;

                case Models.Motion.ShortcutAction.AddMarkerAtPlayhead:
                    MarkerService.AddMarker(CurrentTimeMs);
                    return ExecuteShortcutResult.HandledNeedsSyncMarkers;

                case Models.Motion.ShortcutAction.SetWorkAreaIn:
                    SetWorkAreaIn();
                    return ExecuteShortcutResult.HandledNeedsSyncWorkArea;

                case Models.Motion.ShortcutAction.SetWorkAreaOut:
                    SetWorkAreaOut();
                    return ExecuteShortcutResult.HandledNeedsSyncWorkArea;

                case Models.Motion.ShortcutAction.ClearWorkArea:
                    if (HasWorkArea)
                    {
                        ClearWorkArea();
                        return ExecuteShortcutResult.HandledNeedsSyncWorkArea;
                    }
                    return ExecuteShortcutResult.NotHandled;

                // 以下动作需要 View 层参与 (对话框/控件交互)
                case Models.Motion.ShortcutAction.SaveAs:
                case Models.Motion.ShortcutAction.OpenFile:
                case Models.Motion.ShortcutAction.AddKeyframeAtPlayhead:
                case Models.Motion.ShortcutAction.AddEventAtPlayhead:
                case Models.Motion.ShortcutAction.ZoomToFit:
                case Models.Motion.ShortcutAction.PreviousKeyframe:
                case Models.Motion.ShortcutAction.NextKeyframe:
                case Models.Motion.ShortcutAction.ExitVideoFullscreen:
                    return ExecuteShortcutResult.DelegateToView;

                default:
                    return ExecuteShortcutResult.NotHandled;
            }
        }

        /// <summary>是否为深色主题</summary>
        private bool _isDarkTheme;
        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set => this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
        }

        /// <summary>是否可以撤销</summary>
        public bool CanUndo => _undoRedo.CanUndo;

        /// <summary>是否可以重做</summary>
        public bool CanRedo => _undoRedo.CanRedo;

        /// <summary>撤销描述 (Tooltip 用)</summary>
        public string? UndoDescription => _undoRedo.UndoDescription;

        /// <summary>重做描述 (Tooltip 用)</summary>
        public string? RedoDescription => _undoRedo.RedoDescription;

        // ---- 状态反馈 (状态栏 / 操作日志) ----

        /// <summary>最近操作日志 (最多保留 6 条, 最新在前)</summary>
        public ObservableCollection<string> RecentOperations { get; } = new();

        private string _statusMessage = "就绪";

        /// <summary>当前状态提示 (状态栏主文本)</summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        /// <summary>
        /// 推送一条操作反馈: 更新状态栏并写入最近操作日志。
        /// 供执行命令、保存、自动保存等用户可见事件调用。
        /// </summary>
        public void PushStatus(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;
            StatusMessage = message;
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            RecentOperations.Insert(0, line);
            while (RecentOperations.Count > 6)
                RecentOperations.RemoveAt(RecentOperations.Count - 1);
        }

        /// <summary>状态栏轨道计数标签</summary>
        public string TrackCountLabel => $"轨道: {Tracks.Count} · 时长: {DurationDisplay}";

        /// <summary>时间轴视口宽度 (由 View 层同步, 用于 ZoomToFit 计算)</summary>
        private double _timelineViewportWidth = 800;
        public double TimelineViewportWidth
        {
            get => _timelineViewportWidth;
            set => this.RaiseAndSetIfChanged(ref _timelineViewportWidth, value);
        }

        /// <summary>是否启用自动吸附</summary>
        private bool _isSnapEnabled = true;
        public bool IsSnapEnabled
        {
            get => _isSnapEnabled;
            set => this.RaiseAndSetIfChanged(ref _isSnapEnabled, value);
        }

        /// <summary>曲线编辑器 ViewModel (Phase 4.2.8)</summary>
        private CurveEditorViewModel? _curveEditorVm;
        public CurveEditorViewModel CurveEditorVm =>
            _curveEditorVm ??= new CurveEditorViewModel(this);

        // see TimelineEditorViewModel.TrackGroups.cs

        /// <summary>吸附阈值 (毫秒) — 在此范围内自动吸附到最近的吸附点</summary>
        public double SnapThresholdMs => 10.0 / Math.Max(0.001, PixelsPerMs); // 约 10 像素的距离

        /// <summary>
        /// 获取所有吸附点 (毫秒): 关键帧位置、视频结尾
        /// </summary>
        public System.Collections.Generic.List<double> GetSnapPoints()
        {
            var points = new System.Collections.Generic.List<double>();
            points.Add(0); // 起点

            if (Timeline != null)
            {
                foreach (var track in Timeline.Tracks)
                {
                    foreach (var clip in track.Clips)
                    {
                        foreach (var kf in clip.Keyframes)
                        {
                            double absTime = clip.StartMs + kf.TimeMs;
                            points.Add(absTime);
                        }
                    }
                }
            }

            // 视频结尾
            if (VideoDurationMs > 0)
                points.Add(VideoDurationMs);

            // 独立事件
            foreach (var evt in Events)
                points.Add(evt.TimeMs);

            return points;
        }

        /// <summary>
        /// 对给定时间应用吸附逻辑，返回吸附后的时间
        /// </summary>
        public double ApplySnap(double timeMs)
        {
            if (!IsSnapEnabled)
                return timeMs;

            double threshold = SnapThresholdMs;
            var points = GetSnapPoints();
            double bestDist = double.MaxValue;
            double bestTime = timeMs;

            foreach (var pt in points)
            {
                double dist = Math.Abs(timeMs - pt);
                if (dist < bestDist && dist <= threshold)
                {
                    bestDist = dist;
                    bestTime = pt;
                }
            }

            return bestTime;
        }

        /// <summary>
        /// 缩放适配当前窗口大小 — 使整个时间轴内容恰好填满可见区域
        /// </summary>
        public void ZoomToFit()
        {
            if (DurationMs <= 0 || TimelineViewportWidth <= 0)
                return;

            // 留 20px 左右余量
            double usableWidth = Math.Max(100, TimelineViewportWidth - 40);
            double newPpm = usableWidth / DurationMs;
            newPpm = Math.Clamp(newPpm, 0.001, 1.0);

            PixelsPerMs = newPpm;
            ScrollOffsetX = 0;
        }

        // see TimelineEditorViewModel.FileOps.cs
        // see TimelineEditorViewModel.Playback.cs
        // see TimelineEditorViewModel.TrackGroups.cs (track CRUD)
        // see TimelineEditorViewModel.Keyframes.cs
        // see TimelineEditorViewModel.Events.cs

        // ---- 清理 ----

        public void Dispose()
        {
            StopAutoSave();
            StopTick();
            _engine.ForceStop();
            _engine.Notified -= OnEngineNotified;
            _engine.Dispatcher.ValueDispatched -= OnValueDispatched;
        }
    }
}
