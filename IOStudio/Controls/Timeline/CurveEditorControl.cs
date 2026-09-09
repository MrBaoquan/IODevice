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
    /// 曲线编辑器控件 — Phase 4
    /// 用于在曲线视图模式下编辑多轨道的贝塞尔曲线
    /// 功能: 曲线渲染、关键帧选择/拖拽、贝塞尔切线手柄、值域网格、缩放/平移
    /// </summary>
    public partial class CurveEditorControl : Control
    {
        // ═══════ 依赖属性 ═══════

        public static readonly StyledProperty<double> DurationMsProperty =
            AvaloniaProperty.Register<CurveEditorControl, double>(nameof(DurationMs), 30000);

        public static readonly StyledProperty<double> PixelsPerMsProperty =
            AvaloniaProperty.Register<CurveEditorControl, double>(nameof(PixelsPerMs), 0.1);

        public static readonly StyledProperty<double> ScrollOffsetXProperty =
            AvaloniaProperty.Register<CurveEditorControl, double>(nameof(ScrollOffsetX), 0);

        public static readonly StyledProperty<double> CurrentTimeMsProperty =
            AvaloniaProperty.Register<CurveEditorControl, double>(nameof(CurrentTimeMs), 0);

        public static readonly StyledProperty<List<CurveTrackData>?> CurveTracksProperty =
            AvaloniaProperty.Register<CurveEditorControl, List<CurveTrackData>?>(
                nameof(CurveTracks)
            );

        public static readonly StyledProperty<double> ValueZoomProperty = AvaloniaProperty.Register<
            CurveEditorControl,
            double
        >(nameof(ValueZoom), 1.0);

        public static readonly StyledProperty<double> ValueOffsetProperty =
            AvaloniaProperty.Register<CurveEditorControl, double>(nameof(ValueOffset), 0.0);

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
        public List<CurveTrackData>? CurveTracks
        {
            get => GetValue(CurveTracksProperty);
            set => SetValue(CurveTracksProperty, value);
        }
        public double ValueZoom
        {
            get => GetValue(ValueZoomProperty);
            set => SetValue(ValueZoomProperty, value);
        }
        public double ValueOffset
        {
            get => GetValue(ValueOffsetProperty);
            set => SetValue(ValueOffsetProperty, value);
        }

        private int _focusedTrackIndex = -1;

        /// <summary>
        /// 焦点轨道索引 (-1=无焦点, 显示所有可显示轨道)。方案 A 焦点模式: 有选中轨道时曲线视图默认只显示焦点轨, 多轨用 Solo。
        /// </summary>
        public int FocusedTrackIndex
        {
            get => _focusedTrackIndex;
            set
            {
                if (_focusedTrackIndex != value)
                {
                    _focusedTrackIndex = value;
                    _focusedTrackIndexes.Clear();
                    if (value >= 0)
                        _focusedTrackIndexes.Add(value);
                    InvalidateVisual();
                }
            }
        }

        private readonly HashSet<int> _focusedTrackIndexes = new();

        /// <summary>多选焦点轨道索引集合 (空 = 无焦点, 显示所有)。用于轨道多选后曲线同时显示多条。</summary>
        public IReadOnlyCollection<int> FocusedTrackIndexes => _focusedTrackIndexes;

        /// <summary>
        /// 是否在动画曲线视图叠加 idle 装饰 (幽灵曲线/周期边界/淡化带)。
        /// 独立待机编辑器接管后默认 false (动画视图仅显示动画曲线, 减负)。
        /// 可在曲线视图工具栏切换, 用于"可控查看"待机形态。
        /// </summary>
        public static readonly StyledProperty<bool> ShowIdleOverlaysInCurveProperty =
            AvaloniaProperty.Register<CurveEditorControl, bool>(
                nameof(ShowIdleOverlaysInCurve),
                false
            );

        public bool ShowIdleOverlaysInCurve
        {
            get => GetValue(ShowIdleOverlaysInCurveProperty);
            set => SetValue(ShowIdleOverlaysInCurveProperty, value);
        }

        /// <summary>设置焦点轨道集合 (轨道多选后调用)。</summary>
        public void SetFocusedTrackIndexes(IEnumerable<int> indexes)
        {
            _focusedTrackIndexes.Clear();
            if (indexes != null)
                foreach (var i in indexes)
                    if (i >= 0)
                        _focusedTrackIndexes.Add(i);
            _focusedTrackIndex =
                _focusedTrackIndexes.Count == 1 ? _focusedTrackIndexes.First() : -1;
            InvalidateVisual();
        }

        // ═══════ 事件 ═══════

        /// <summary>关键帧选中, 参数: (trackIndex, clipIndex, kfIndex)</summary>
        public event Action<int, int, int>? KeyframeSelected;

        /// <summary>关键帧移动完成, 参数: (trackIndex, clipIndex, kfIndex, newTimeMs, newValue)</summary>
        public event Action<int, int, int, double, float>? KeyframeMoved;

        /// <summary>贝塞尔切线修改, 参数: (trackIndex, clipIndex, kfIndex, tangentIn, tangentOut, cp1x, cp2x)</summary>
        public event Action<int, int, int, float, float, float?, float?>? TangentChanged;

        /// <summary>双击空白区域请求添加关键帧, 参数: (trackIndex, timeMs, value)</summary>
        public event Action<int, double, float>? AddKeyframeRequested;

        /// <summary>插值类型切换, 参数: (trackIndex, clipIndex, kfIndex, interpolation)</summary>
        public event Action<int, int, int, string>? InterpolationChanged;

        /// <summary>预设应用请求, 参数: (trackIndex, clipIndex, kfIndex, presetName)</summary>
        public event Action<int, int, int, string>? PresetApplyRequested;

        /// <summary>批量删除选中关键帧请求, 参数: List of (trackIndex, clipIndex, kfIndex)</summary>
        public event Action<
            List<(int TrackIdx, int ClipIdx, int KfIdx)>
        >? DeleteMultiSelectedRequested;

        /// <summary>Shift+滚轮调整轨道高度请求, 参数: delta pixels</summary>
        public event Action<double>? TrackHeightChangeRequested;

        /// <summary>请求缩放时间轴 (Ctrl+滚轮), 参数: (newPixelsPerMs, newScrollOffsetX)。保持鼠标下时间不变。</summary>
        public event Action<double, double>? ZoomRequested;

        /// <summary>请求平移时间轴 (中键拖拽), 参数: newScrollOffsetX。</summary>
        public event Action<double>? PanRequested;

        /// <summary>批量移动选中关键帧完成, 参数: List of (trackIdx, clipIdx, kfIdx, newAbsTimeMs, newValue)</summary>
        public event Action<
            List<(int TrackIdx, int ClipIdx, int KfIdx, double AbsTimeMs, float Value)>
        >? MultiKeyframeMoved;

        /// <summary>
        /// 曲线视图多选集合变化时触发, 参数: 当前多选列表 (trackIdx, clipIdx, kfIdx)。
        /// 用于把曲线多选同步到全局 VM 多选集 (Delete/批量插值统一)。
        /// </summary>
        public event Action<IReadOnlyList<(int Ti, int Ci, int Ki)>>? MultiSelectionChanged;

        /// <summary>批量设置插值请求 (来自多选右键菜单), 参数: interpolation 名称。</summary>
        public event Action<string>? BatchInterpolationRequested;

        /// <summary>批量应用曲线预设请求 (来自多选右键菜单), 参数: presetName。</summary>
        public event Action<string>? BatchPresetApplyRequested;

        /// <summary>
        /// 关键帧编辑提交 (拖拽释放时), 供撤销: 参数为 (kf引用, beforeTimeMs, beforeValue, afterTimeMs, afterValue)。
        /// 时间均为相对 clip 起点。仅在实际发生移动时触发。
        /// </summary>
        public event Action<
            List<(MotionKeyframe Kf, double T0, float V0, double T1, float V1)>
        >? KeyframeEditCommitted;

        /// <summary>空窗双击请求添加 Overlay 覆盖关键帧, 参数: (trackIndex, 绝对timeMs, value)。</summary>
        public event Action<int, double, float>? OverrideKeyframeAddRequested;

        /// <summary>空窗双击请求添加 idle 待机循环关键帧 (轨道已启用 idle 时), 参数: (trackIndex, 相位ms, value)。</summary>
        public event Action<int, double, float>? IdleKeyframeAddRequested;

        /// <summary>Overlay 覆盖关键帧移动完成, 参数: (trackIndex, 绝对timeMs, value)。</summary>
        public event Action<int, double, float>? OverrideKeyframeMoved;

        /// <summary>
        /// Overlay 覆盖关键帧编辑提交 (拖拽释放时), 供撤销:
        /// 参数 (trackIndex, kf引用, beforeTimeMs, beforeValue, afterTimeMs, afterValue)。
        /// </summary>
        public event Action<
            int,
            MotionKeyframe,
            double,
            float,
            double,
            float
        >? OverrideKeyframeEditCommitted;

        /// <summary>Overlay 覆盖关键帧删除请求, 参数: (trackIndex, 绝对timeMs, value)。</summary>
        public event Action<int, double, float>? OverrideKeyframeDeleteRequested;

        /// <summary>
        /// idle 幽灵曲线关键帧编辑提交 (拖拽释放时), 供撤销:
        /// 参数 (trackIndex, kf引用, beforePhaseMs, beforeValue, afterPhaseMs, afterValue)。
        /// </summary>
        public event Action<
            int,
            MotionKeyframe,
            double,
            float,
            double,
            float
        >? IdleKeyframeEditCommitted;

        /// <summary>idle 幽灵曲线关键帧删除请求, 参数: (trackIndex, phaseMs, value)。</summary>
        public event Action<int, double, float>? IdleKeyframeDeleteRequested;

        /// <summary>
        /// 贝塞尔切线编辑提交 (拖拽释放时), 供撤销: 参数为 (ti, ci, ki, before四值, after四值)。
        /// </summary>
        public event Action<(
            int Ti,
            int Ci,
            int Ki,
            float Tin0,
            float Tout0,
            float Cp10,
            float Cp20,
            float Tin1,
            float Tout1,
            float Cp11,
            float Cp21
        )>? TangentEditCommitted;

        // ═══════ 视觉常量 ═══════

        private static readonly IBrush BgBrush = new SolidColorBrush(Color.Parse("#1e1e2e"));
        private static readonly IBrush GridMajorBrush = new SolidColorBrush(
            Color.FromArgb(40, 255, 255, 255)
        );
        private static readonly IBrush GridMinorBrush = new SolidColorBrush(
            Color.FromArgb(15, 255, 255, 255)
        );
        private static readonly IPen GridMajorPen = new Pen(GridMajorBrush, 0.5);
        private static readonly IPen GridMinorPen = new Pen(GridMinorBrush, 0.5);
        private static readonly IBrush PlayheadBrush = new SolidColorBrush(Color.Parse("#ef4444"));
        private static readonly IPen PlayheadPen = new Pen(PlayheadBrush, 1.5);
        private static readonly IBrush LabelBrush = new SolidColorBrush(
            Color.FromArgb(120, 255, 255, 255)
        );
        private static readonly IBrush SelectedKfBrush = new SolidColorBrush(
            Color.Parse("#facc15")
        );
        private static readonly IPen SelectedKfPen = new Pen(Brushes.Black, 2);
        private static readonly IBrush HandleBrush = new SolidColorBrush(Color.Parse("#a78bfa"));
        private static readonly IPen HandleLinePen = new Pen(
            new SolidColorBrush(Color.FromArgb(120, 167, 139, 250)),
            1
        );

        // 吸附参考线
        private static readonly IPen SnapGuidePen = new Pen(
            new SolidColorBrush(Color.Parse("#22d3ee")),
            1,
            new DashStyle(new double[] { 4, 3 }, 0)
        );

        private static readonly Typeface LabelTypeface = new Typeface("Consolas");

        private const double KfDiamondSize = 6;
        private const double KfHitRadius = 9;
        private const double HandleRadius = 4;
        private const double HandleHitRadius = 7;
        private const double TangentHandleLength = 40; // 切线手柄显示长度 (pixels)
        private const double DragThreshold = 4.0; // 拖拽启动阈值 (pixels), 防止点击时微小抖动修改值
        private const double GridPaddingTop = 20;
        private const double GridPaddingBottom = 20;

        // ═══════ 交互状态 ═══════

        private int _selectedTrackIdx = -1;
        private int _selectedClipIdx = -1;
        private int _selectedKfIdx = -1;

        /// <summary>选中的 Overlay 覆盖关键帧索引 (空窗自由数值点, -1=未选)。</summary>
        private int _selectedOverrideKfIdx = -1;

        /// <summary>选中的 idle 幽灵曲线关键帧索引 (循环相位, -1=未选)。</summary>
        private int _selectedIdleKfIdx = -1;

        /// <summary>从轨道面板同步的选中轨道 (不会被空白点击清除)</summary>
        private int _trackPanelSelectedIdx = -1;
        private DragMode _dragMode = DragMode.None;
        private Point _dragStart;
        private double _dragStartTimeMs;
        private float _dragStartValue;
        private float _dragStartTangent;
        private float _dragStartCpx; // 切线手柄水平距离系数拖拽起始值
        private long _lastDragEndTick; // 上次拖拽结束的时间戳 (防止误触双击添加)
        private bool _hasDragStarted; // 拖拽是否超过阈值 (区分点击与拖拽)
        private bool _isPanning;
        private Point _panStart;
        private double _panStartOffsetX;
        private double _panStartValueOffset;

        private enum DragMode
        {
            None,
            Keyframe,
            MultiKeyframe,
            OverrideKeyframe, // 空窗 Overlay 覆盖关键帧拖拽
            IdleKeyframe, // idle 幽灵曲线关键帧拖拽 (循环相位)
            TangentIn,
            TangentOut,
            Pan,
            BoxSelect
        }

        // 框选状态
        private Point _boxSelectStart;
        private Rect _boxSelectRect;
        private readonly HashSet<(int trackIdx, int clipIdx, int kfIdx)> _multiSelectedKfs = new();

        // 多选拖拽起始快照
        private List<(
            int ti,
            int ci,
            MotionKeyframe kf,
            double timeMs,
            float value
        )>? _multiDragSnapshot;

        // 右键菜单 (确保同时只有一个打开)
        private Avalonia.Controls.ContextMenu? _activeContextMenu;

        // 吸附状态
        private List<double>? _snapPoints;
        private double? _snapTargetTimeMs;
        private bool _isSnapEnabled = true;
        private const double SnapVisualThresholdPx = 12;

        /// <summary>设置吸附点列表和启用状态</summary>
        public void SetSnapInfo(bool enabled, List<double>? points)
        {
            _isSnapEnabled = enabled;
            _snapPoints = points;
        }

        public CurveEditorControl()
        {
            ClipToBounds = true;
            Focusable = true;
        }

        static CurveEditorControl()
        {
            AffectsRender<CurveEditorControl>(
                DurationMsProperty,
                PixelsPerMsProperty,
                ScrollOffsetXProperty,
                CurrentTimeMsProperty,
                CurveTracksProperty,
                ValueZoomProperty,
                ValueOffsetProperty,
                ShowIdleOverlaysInCurveProperty
            );
        }

        // ═══════ 渲染 ═══════
    }

    // ═══════ 数据传输对象 ═══════

    /// <summary>
    /// 曲线编辑器使用的轨道数据 (从 TrackViewModel 转换)
    /// </summary>
    public class CurveTrackData
    {
        public string Label { get; set; } = "";
        public string Color { get; set; } = "#4FC3F7";
        public string ValueType { get; set; } = "float";
        public bool IsMuted { get; set; }
        public bool IsSolo { get; set; }
        public bool IsEnabled { get; set; } = true;
        public bool IsLocked { get; set; }

        /// <summary>中性/空闲值 (null=按类型默认)。用于曲线无覆盖区渲染与求值。</summary>
        public float? NeutralValue { get; set; }

        /// <summary>Idle 循环 (gap 填充) — 无 clip 覆盖时的待机循环动作。null=无 idle。</summary>
        public IdleLoop? IdleLoop { get; set; }

        /// <summary>Overlay 覆盖关键帧 (空窗自由数值点, 绝对时间)。优先级高于 idle。</summary>
        public List<MotionKeyframe>? OverrideKeyframes { get; set; }

        /// <summary>UX-B1: 是否在曲线编辑器中显示 (焦点模式)</summary>
        public bool ShowInCurve { get; set; } = true;
        public List<MotionClip> Clips { get; set; } = new();
    }
}
