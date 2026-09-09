using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;

namespace IOStudio.Controls.Timeline
{
    /// <summary>
    /// 轨道片段控件 — 使用 Avalonia DrawingContext 绘制 Clip 矩形和关键帧曲线
    /// 每个 TrackClipControl 对应一条轨道的时间轴区域
    /// 支持: 关键帧选择、拖拽、双击添加、右键删除
    /// </summary>
    public partial class TrackClipControl : Control
    {
        // ═══════ 跨轨道共享拖拽预览 ═══════
        // 动作实例跨多轨道: 拖动某轨的 clip 时, 其余绑定同一实例的轨道也要同步显示预览框。
        // 用一个静态广播 (InstanceId + 预览时间), 所有 TrackClipControl 渲染时检查自己的 clip 是否属于该实例。
        private static string? s_sharedDragPreviewInstanceId;
        private static double s_sharedDragPreviewStartMs;
        private static double s_sharedDragPreviewDurationMs;
        private static bool s_sharedDragPreviewActive;

        /// <summary>共享拖拽预览变化时触发 (供窗口/其他控件重绘)。</summary>
        public static event Action? SharedDragPreviewChanged;

        /// <summary>当前是否有跨轨共享拖拽预览。</summary>
        public static bool HasSharedDragPreview => s_sharedDragPreviewActive;

        /// <summary>设置共享拖拽预览 (由拖拽发起方调用, 广播到所有轨道)。</summary>
        public static void PublishSharedDragPreview(
            string instanceId,
            double startMs,
            double durationMs
        )
        {
            s_sharedDragPreviewInstanceId = instanceId;
            s_sharedDragPreviewStartMs = startMs;
            s_sharedDragPreviewDurationMs = durationMs;
            s_sharedDragPreviewActive = true;
            SharedDragPreviewChanged?.Invoke();
        }

        /// <summary>清除共享拖拽预览 (拖拽结束时调用)。</summary>
        public static void ClearSharedDragPreview()
        {
            s_sharedDragPreviewActive = false;
            s_sharedDragPreviewInstanceId = null;
            SharedDragPreviewChanged?.Invoke();
        }

        /// <summary>当前控件是否包含共享预览所指的动作实例 clip。</summary>
        private bool ContainsSharedDragPreviewInstance()
        {
            if (!s_sharedDragPreviewActive || string.IsNullOrEmpty(s_sharedDragPreviewInstanceId))
                return false;
            if (Clips == null)
                return false;
            foreach (var clip in Clips)
                if (
                    clip.ActionInstanceId != null
                    && string.Equals(
                        clip.ActionInstanceId,
                        s_sharedDragPreviewInstanceId,
                        StringComparison.Ordinal
                    )
                )
                    return true;
            return false;
        }

        /// <summary>读取共享预览参数 (供渲染)。</summary>
        private static (
            string InstanceId,
            double StartMs,
            double DurationMs
        )? GetSharedDragPreview()
        {
            if (!s_sharedDragPreviewActive || s_sharedDragPreviewInstanceId == null)
                return null;
            return (
                s_sharedDragPreviewInstanceId,
                s_sharedDragPreviewStartMs,
                s_sharedDragPreviewDurationMs
            );
        }

        // ═══════ 依赖属性 ═══════

        public static readonly StyledProperty<double> DurationMsProperty =
            AvaloniaProperty.Register<TrackClipControl, double>(nameof(DurationMs), 30000);

        public static readonly StyledProperty<double> PixelsPerMsProperty =
            AvaloniaProperty.Register<TrackClipControl, double>(nameof(PixelsPerMs), 0.01);

        public static readonly StyledProperty<double> ScrollOffsetXProperty =
            AvaloniaProperty.Register<TrackClipControl, double>(nameof(ScrollOffsetX), 0);

        public static readonly StyledProperty<double> CurrentTimeMsProperty =
            AvaloniaProperty.Register<TrackClipControl, double>(nameof(CurrentTimeMs), 0);

        public static readonly StyledProperty<List<MotionClip>?> ClipsProperty =
            AvaloniaProperty.Register<TrackClipControl, List<MotionClip>?>(nameof(Clips));

        public static readonly StyledProperty<string> TrackColorProperty =
            AvaloniaProperty.Register<TrackClipControl, string>(nameof(TrackColor), "#4FC3F7");

        public static readonly StyledProperty<bool> IsMutedProperty = AvaloniaProperty.Register<
            TrackClipControl,
            bool
        >(nameof(IsMuted), false);

        public static readonly StyledProperty<bool> IsTrackLockedProperty = AvaloniaProperty.Register<
            TrackClipControl,
            bool
        >(nameof(IsTrackLocked), false);

        public static readonly StyledProperty<bool> IsTrackSelectedProperty =
            AvaloniaProperty.Register<TrackClipControl, bool>(nameof(IsTrackSelected), false);

        public static readonly StyledProperty<string?> SelectedActionInstanceIdProperty =
            AvaloniaProperty.Register<TrackClipControl, string?>(nameof(SelectedActionInstanceId));

        public static readonly StyledProperty<string> ValueTypeProperty = AvaloniaProperty.Register<
            TrackClipControl,
            string
        >(nameof(ValueType), "float");

        public double DurationMs
        {
            get => GetValue(DurationMsProperty);
            set => SetValue(DurationMsProperty, value);
        }

        public double PixelsPerMs
        {
            get => GetValue(PixelsPerMsProperty);
            set => SetValue(PixelsPerMsProperty, value);
        }

        public double ScrollOffsetX
        {
            get => GetValue(ScrollOffsetXProperty);
            set => SetValue(ScrollOffsetXProperty, value);
        }

        public double CurrentTimeMs
        {
            get => GetValue(CurrentTimeMsProperty);
            set => SetValue(CurrentTimeMsProperty, value);
        }

        public List<MotionClip>? Clips
        {
            get => GetValue(ClipsProperty);
            set => SetValue(ClipsProperty, value);
        }

        public string TrackColor
        {
            get => GetValue(TrackColorProperty);
            set => SetValue(TrackColorProperty, value);
        }

        public bool IsMuted
        {
            get => GetValue(IsMutedProperty);
            set => SetValue(IsMutedProperty, value);
        }

        public bool IsTrackLocked
        {
            get => GetValue(IsTrackLockedProperty);
            set => SetValue(IsTrackLockedProperty, value);
        }

        public bool IsTrackSelected
        {
            get => GetValue(IsTrackSelectedProperty);
            set => SetValue(IsTrackSelectedProperty, value);
        }

        public string? SelectedActionInstanceId
        {
            get => GetValue(SelectedActionInstanceIdProperty);
            set => SetValue(SelectedActionInstanceIdProperty, value);
        }

        /// <summary>轨道值类型 ("float" 或 "bool")</summary>
        public string ValueType
        {
            get => GetValue(ValueTypeProperty);
            set => SetValue(ValueTypeProperty, value);
        }

        public static readonly StyledProperty<TimelineViewMode> ViewModeProperty =
            AvaloniaProperty.Register<TrackClipControl, TimelineViewMode>(
                nameof(ViewMode),
                TimelineViewMode.Dopesheet
            );

        /// <summary>用于同步自绘轨道表面的主题状态。</summary>
        public static readonly StyledProperty<bool> IsDarkThemeProperty =
            AvaloniaProperty.Register<TrackClipControl, bool>(nameof(IsDarkTheme), false);

        public TimelineViewMode ViewMode
        {
            get => GetValue(ViewModeProperty);
            set => SetValue(ViewModeProperty, value);
        }

        public bool IsDarkTheme
        {
            get => GetValue(IsDarkThemeProperty);
            set => SetValue(IsDarkThemeProperty, value);
        }

        // ═══════ 事件 ═══════

        /// <summary>双击轨道空白区域, 参数: 时间(ms)</summary>
        public event Action<double>? AddKeyframeRequested;

        /// <summary>选中关键帧, 参数: (clipIndex, keyframeIndex)</summary>
        public event Action<int, int>? KeyframeSelected;

        /// <summary>Shift+Click 追加选中, 参数: (clipIndex, keyframeIndex)</summary>
        public event Action<int, int>? KeyframeAddToSelection;

        /// <summary>Ctrl+Click 切换选中, 参数: (clipIndex, keyframeIndex)</summary>
        public event Action<int, int>? KeyframeToggleSelection;

        /// <summary>关键帧被拖拽修改, 参数: (clipIndex, keyframeIndex, newTimeMs, newValue)</summary>
        public event Action<int, int, double, float>? KeyframeMoved;

        /// <summary>请求删除关键帧, 参数: (clipIndex, keyframeIndex)</summary>
        public event Action<int, int>? DeleteKeyframeRequested;

        /// <summary>请求设置关键帧插值类型, 参数: (clipIndex, keyframeIndex, interpolation)</summary>
        public event Action<int, int, string>? SetInterpolationRequested;

        /// <summary>请求复制选中关键帧 (单选或多选)</summary>
        public event Action? CopyKeyframesRequested;

        /// <summary>请求粘贴关键帧到当前播放头位置</summary>
        public event Action? PasteKeyframesRequested;

        /// <summary>请求在指定时间位置添加关键帧, 参数: timeMs (由右键菜单计算)</summary>
        public event Action<double>? AddKeyframeAtTimeRequested;

        /// <summary>请求在指定时间位置添加事件, 参数: timeMs</summary>
        public event Action<double>? AddEventAtTimeRequested;

        /// <summary>请求对选中关键帧应用曲线预设, 参数: (clipIndex, keyframeIndex, presetName)</summary>
        public event Action<int, int, string>? ApplyPresetRequested;

        /// <summary>请求批量设置多选关键帧的插值类型, 参数: interpolation</summary>
        public event Action<string>? BatchSetInterpolationRequested;

        /// <summary>请求批量应用多选关键帧的曲线预设, 参数: presetName</summary>
        public event Action<string>? BatchApplyPresetRequested;

        /// <summary>请求将动作实例复制到播放头, 参数: actionInstanceId</summary>
        public event Action<string>? DuplicateActionInstanceRequested;

        /// <summary>请求将动作实例整体移动到播放头, 参数: actionInstanceId</summary>
        public event Action<string>? MoveActionInstanceRequested;

        /// <summary>请求将动作实例另存为预设, 参数: actionInstanceId</summary>
        public event Action<string>? SaveActionInstanceAsPresetRequested;

        /// <summary>请求删除动作实例及全部关联通道, 参数: actionInstanceId</summary>
        public event Action<string>? DeleteActionInstanceRequested;

        /// <summary>将动作实例作为跨轨整体选中。</summary>
        public event Action<string>? ActionInstanceSelected;

        /// <summary>将动作实例整体移动到指定时间。</summary>
        public event Action<string, double>? MoveActionInstanceToTimeRequested;

        /// <summary>调整动作实例边界（拖左边界=改 in 点, 拖右边界=改时长）。参数: (instanceId, deltaStartMs, deltaEndMs)</summary>
        public event Action<string, double, double>? ActionInstanceResizeRequested;

        /// <summary>请求缩放时间轴 (滚轮), 参数: (newPixelsPerMs, newScrollOffsetX)。保持鼠标下时间不变。</summary>
        public event Action<double, double>? ZoomRequested;

        /// <summary>请求平移时间轴 (中键拖拽), 参数: newScrollOffsetX。</summary>
        public event Action<double>? PanRequested;

        /// <summary>将预设按默认参数放置到指定时间。</summary>
        public event Action<string, double, string?>? PresetDroppedAtTimeRequested;

        public static readonly StyledProperty<string?> TrackIdProperty = AvaloniaProperty.Register<
            TrackClipControl,
            string?
        >(nameof(TrackId));

        public string? TrackId
        {
            get => GetValue(TrackIdProperty);
            set => SetValue(TrackIdProperty, value);
        }

        /// <summary>Shift+滚轮调整轨道高度, 参数: delta (正=增高, 负=降低)</summary>
        public event Action<double>? TrackHeightChangeRequested;

        /// <summary>剪贴板是否有关键帧 (用于控制粘贴菜单项的启用状态)</summary>
        public bool HasClipboardKeyframes { get; set; }

        // ═══════ 视觉常量 ═══════

        private static readonly IBrush EmptyLightBrush = new SolidColorBrush(Color.Parse("#fafafa"));
        private static readonly IBrush EmptyDarkBrush = new SolidColorBrush(Color.Parse("#171a1e"));
        private static readonly IPen GridLightPen = new Pen(
            new SolidColorBrush(Color.Parse("#f3f4f6")),
            0.5
        );
        private static readonly IPen GridDarkPen = new Pen(
            new SolidColorBrush(Color.Parse("#252a30")),
            0.5
        );
        private static readonly IBrush PlayheadBrush = new SolidColorBrush(Color.Parse("#ef4444"));
        private static readonly IPen PlayheadPen = new Pen(PlayheadBrush, 1.5);
        private static readonly IBrush MutedOverlayBrush = new SolidColorBrush(
            Color.Parse("#80ffffff")
        );
        private static readonly IBrush SelectedBorderBrush = new SolidColorBrush(
            Color.Parse("#3b82f6")
        );
        private static readonly IPen SelectedBorderPen = new Pen(SelectedBorderBrush, 2);
        private static readonly IBrush SelectedKfBrush = new SolidColorBrush(
            Color.Parse("#facc15")
        );
        private static readonly IPen SelectedKfPen = new Pen(Brushes.Black, 2);
        private static readonly IBrush MultiSelectedKfBrush = new SolidColorBrush(
            Color.Parse("#60a5fa")
        );
        private static readonly IPen MultiSelectedKfPen = new Pen(Brushes.White, 2);

        // Dedicated action lane for discoverable movie-level selection.
        internal const double ActionBandHeight = 14;

        // 播放头附近最近关键帧高亮 (绿色光晕)
        private static readonly IBrush PlayheadNearKfBrush = new SolidColorBrush(
            Color.Parse("#34d399")
        );
        private static readonly IPen PlayheadNearKfPen = new Pen(
            new SolidColorBrush(Color.Parse("#10b981")),
            2.5
        );
        private static readonly IBrush PlayheadNearKfGlow = new SolidColorBrush(
            Color.FromArgb(60, 52, 211, 153)
        );

        private const double KeyframeDiamondSize = 6;
        private const double KeyframeHitRadius = 8;

        // ═══════ 吸附参考线视觉 ═══════

        private static readonly IPen SnapGuidePen = new Pen(
            new SolidColorBrush(Color.Parse("#22d3ee")),
            1,
            new DashStyle(new double[] { 4, 3 }, 0)
        );
        private static readonly IBrush SnapLabelBg = new SolidColorBrush(
            Color.FromArgb(200, 34, 211, 238)
        );

        /// <summary>拖拽期间的吸附目标时间 (ms), null 表示未吸附</summary>
        private double? _snapTargetTimeMs;

        /// <summary>外部传入的所有吸附点 (毫秒)</summary>
        private List<double>? _snapPoints;

        /// <summary>吸附阈值 (像素距离)</summary>
        private const double SnapVisualThresholdPx = 12;

        /// <summary>设置当前可用的吸附点列表 (由 View 层从 ViewModel 获取后传入)</summary>
        public void SetSnapPoints(List<double>? points) => _snapPoints = points;

        /// <summary>直接操控开始时获取最新吸附点，避免标记或事件变化后使用旧缓存。</summary>
        public Func<List<double>?>? SnapPointsProvider { get; set; }

        // ═══════ 拖拽状态 ═══════

        private int _selectedClipIdx = -1;
        private int _selectedKfIdx = -1;
        private bool _isDraggingKf;
        private Point _dragStartPos;
        private double _dragStartTimeMs;
        private float _dragStartValue;
        private MotionKeyframe? _draggedKfRef; // 拖拽期间追踪关键帧引用 (排序后索引可能变化)

        private bool _isPreparingActionDrag;
        private bool _isDraggingAction;
        private string? _dragActionInstanceId;
        private double _dragActionStartMs;
        private double _dragActionDurationMs;
        private double _dragActionPreviewStartMs;

        // ═══════ 中键拖拽平移 ═══════
        private bool _isPanning;
        private Point _panStartPos;
        private double _panStartScrollOffset;

        private bool _isPresetDragOver;
        private double _presetDropPreviewTimeMs;
        private double _presetDropPreviewDurationMs;

        // ═══════ 多选状态 ═══════

        private readonly HashSet<(int clipIdx, int kfIdx)> _multiSelectedKeyframes = new();
        private bool _isDraggingMultiKf;
        private double _multiDragStartAbsTimeMs;

        /// <summary>多选关键帧拖拽时间偏移, 参数: deltaTimeMs</summary>
        public event Action<double>? MultiKeyframeDragDelta;

        /// <summary>多选关键帧拖拽完成</summary>
        public event Action? MultiKeyframeDragCompleted;

        // ═══════ 框选状态 ═══════

        private bool _isBoxSelecting;
        private Point _boxSelectStart;
        private Point _boxSelectCurrent;

        /// <summary>框选关键帧完成事件, 参数: 被选中的 (clipIdx, kfIdx) 列表</summary>
        public event Action<List<(int clipIdx, int kfIdx)>>? BoxSelectCompleted;

        // 右键菜单 (确保同时只有一个打开)
        private ContextMenu? _activeContextMenu;

        /// <summary>当前选中的关键帧索引 (clipIndex, keyframeIndex). (-1,-1) 表示无选中</summary>
        public (int clipIdx, int kfIdx) SelectedKeyframe => (_selectedClipIdx, _selectedKfIdx);

        public TrackClipControl()
        {
            ClipToBounds = true;
            Focusable = true; // 接收键盘事件
            DragDrop.SetAllowDrop(this, true);
            DragDrop.AddDragOverHandler(this, OnPresetDragOver);
            DragDrop.AddDragLeaveHandler(this, OnPresetDragLeave);
            DragDrop.AddDropHandler(this, OnPresetDrop);

            // 跨轨道共享拖拽预览: 其他轨道也在共享预览变化时重绘
            AttachedToVisualTree += (_, _) =>
            {
                SharedDragPreviewChanged += OnSharedDragPreviewChanged;
                InvalidateVisual();
            };
            DetachedFromVisualTree += (_, _) =>
                SharedDragPreviewChanged -= OnSharedDragPreviewChanged;
        }

        private void OnSharedDragPreviewChanged() => InvalidateVisual();

        static TrackClipControl()
        {
            AffectsRender<TrackClipControl>(
                DurationMsProperty,
                PixelsPerMsProperty,
                ScrollOffsetXProperty,
                CurrentTimeMsProperty,
                ClipsProperty,
                TrackColorProperty,
                IsMutedProperty,
                IsTrackLockedProperty,
                IsTrackSelectedProperty,
                SelectedActionInstanceIdProperty,
                ViewModeProperty,
                ValueTypeProperty,
                IsDarkThemeProperty
            );
        }

        // ═══════ 坐标转换 ═══════

        private double ValueToY(float value, double viewH)
        {
            double usableH = viewH - 8.0;
            if (usableH <= 0)
                return viewH / 2.0;
            return viewH - 4.0 - (double)value * usableH + 2.0;
        }

        private float YToValue(double y, double viewH)
        {
            double usableH = viewH - 8.0;
            if (usableH <= 0)
                return 0.5f;
            float v = (float)((viewH - 4.0 - y + 2.0) / usableH);
            return Math.Clamp(v, 0f, 1f);
        }

        private double TimeMsToX(double timeMs) => timeMs * PixelsPerMs - ScrollOffsetX;

        private double XToTimeMs(double x) => (ScrollOffsetX + x) / PixelsPerMs;

        // ═══════ 查找 / 命中测试 ═══════

        private (int clipIdx, int kfIdx) FindNearestKeyframeToPlayhead()
        {
            var clips = Clips;
            if (clips == null)
                return (-1, -1);

            double now = CurrentTimeMs;
            double bestDist = 50; // 50ms 阈值
            int bestCi = -1,
                bestKi = -1;

            for (int ci = 0; ci < clips.Count; ci++)
            {
                var clip = clips[ci];
                var kfs = clip.Keyframes;
                if (kfs.Count == 0)
                    continue;

                double relTime = now - clip.StartMs;
                // 二分查找最近帧
                int lo = 0,
                    hi = kfs.Count - 1;
                while (lo < hi)
                {
                    int mid = (lo + hi) / 2;
                    if (kfs[mid].TimeMs < relTime)
                        lo = mid + 1;
                    else
                        hi = mid;
                }
                for (int d = -1; d <= 1; d++)
                {
                    int idx = lo + d;
                    if (idx >= 0 && idx < kfs.Count)
                    {
                        double dist = Math.Abs(clip.StartMs + kfs[idx].TimeMs - now);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestCi = ci;
                            bestKi = idx;
                        }
                    }
                }
            }
            return (bestCi, bestKi);
        }

        private (int clipIdx, int kfIdx)? HitTestKeyframe(Point pos)
        {
            var clips = Clips;
            if (clips == null)
                return null;

            double h = Bounds.Height;
            double ppm = PixelsPerMs;
            double bestDist = KeyframeHitRadius;
            (int, int)? result = null;

            for (int ci = 0; ci < clips.Count; ci++)
            {
                var clip = clips[ci];
                for (int ki = 0; ki < clip.Keyframes.Count; ki++)
                {
                    var kf = clip.Keyframes[ki];
                    double kx = (clip.StartMs + kf.TimeMs) * ppm - ScrollOffsetX;
                    float drawVal = string.Equals(
                        ValueType,
                        "bool",
                        StringComparison.OrdinalIgnoreCase
                    )
                        ? (kf.Value >= 0.5f ? 1f : 0f)
                        : kf.Value;
                    double ky =
                        ViewMode == TimelineViewMode.Dopesheet ? h / 2.0 : ValueToY(drawVal, h);
                    double dist = Math.Sqrt(
                        (pos.X - kx) * (pos.X - kx) + (pos.Y - ky) * (pos.Y - ky)
                    );
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        result = (ci, ki);
                    }
                }
            }
            return result;
        }

        private int? HitTestClip(Point pos)
        {
            var clips = Clips;
            if (clips == null || PixelsPerMs <= 0)
                return null;

            double timeMs = XToTimeMs(pos.X);
            for (int i = clips.Count - 1; i >= 0; i--)
            {
                if (timeMs >= clips[i].StartMs && timeMs <= clips[i].EndMs)
                    return i;
            }
            return null;
        }

        private int? HitTestActionBand(Point pos)
        {
            var clips = Clips;
            if (
                clips == null
                || PixelsPerMs <= 0
                || pos.Y < 1
                || pos.Y > Math.Min(ActionBandHeight, Bounds.Height - 1)
            )
                return null;

            double timeMs = XToTimeMs(pos.X);
            for (int i = clips.Count - 1; i >= 0; i--)
            {
                var clip = clips[i];
                if (clip.IsGeneratedFromAction && timeMs >= clip.StartMs && timeMs <= clip.EndMs)
                    return i;
            }
            return null;
        }

        // ═══════ 公共方法 ═══════

        private void SetInterpolation(string interp)
        {
            if (_selectedClipIdx >= 0 && _selectedKfIdx >= 0)
            {
                SetInterpolationRequested?.Invoke(_selectedClipIdx, _selectedKfIdx, interp);
                InvalidateVisual();
            }
        }

        public void SelectKeyframe(int clipIdx, int kfIdx)
        {
            _selectedClipIdx = clipIdx;
            _selectedKfIdx = kfIdx;
            _multiSelectedKeyframes.Clear();
            InvalidateVisual();
        }

        public void ClearSelection()
        {
            _selectedClipIdx = -1;
            _selectedKfIdx = -1;
            _multiSelectedKeyframes.Clear();
            InvalidateVisual();
        }

        public void SetMultiSelectedKeyframes(IEnumerable<(int clipIdx, int kfIdx)> selections)
        {
            _multiSelectedKeyframes.Clear();
            foreach (var sel in selections)
                _multiSelectedKeyframes.Add(sel);
            InvalidateVisual();
        }

        public void AddMultiSelection(int clipIdx, int kfIdx)
        {
            _multiSelectedKeyframes.Add((clipIdx, kfIdx));
            InvalidateVisual();
        }

        public void ClearMultiSelection()
        {
            _multiSelectedKeyframes.Clear();
            InvalidateVisual();
        }
    }
}
