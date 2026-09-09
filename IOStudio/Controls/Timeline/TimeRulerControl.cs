using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using IOStudio.Models.Motion;
using ReactiveUI;

namespace IOStudio.Controls.Timeline
{
    /// <summary>
    /// 时间标尺控件 — 使用 Avalonia 原生 DrawingContext 绘制
    /// 功能：自适应时间刻度、缩放(滚轮)、平移(中键拖拽)、播放头拖拽、点击跳转
    /// </summary>
    public partial class TimeRulerControl : Control
    {
        // ═══════ 依赖属性 ═══════

        public static readonly StyledProperty<double> DurationMsProperty =
            AvaloniaProperty.Register<TimeRulerControl, double>(nameof(DurationMs), 30000);

        public static readonly StyledProperty<double> CurrentTimeMsProperty =
            AvaloniaProperty.Register<TimeRulerControl, double>(nameof(CurrentTimeMs), 0);

        public static readonly StyledProperty<double> PixelsPerMsProperty =
            AvaloniaProperty.Register<TimeRulerControl, double>(nameof(PixelsPerMs), 0.01);

        public static readonly StyledProperty<double> ScrollOffsetXProperty =
            AvaloniaProperty.Register<TimeRulerControl, double>(nameof(ScrollOffsetX), 0);

        public static readonly StyledProperty<double> VideoDurationMsProperty =
            AvaloniaProperty.Register<TimeRulerControl, double>(nameof(VideoDurationMs), 0);

        public static readonly StyledProperty<List<TimelineEvent>?> EventsProperty =
            AvaloniaProperty.Register<TimeRulerControl, List<TimelineEvent>?>(nameof(Events));

        public static readonly StyledProperty<bool> IsDarkThemeProperty = AvaloniaProperty.Register<
            TimeRulerControl,
            bool
        >(nameof(IsDarkTheme), false);

        public static readonly StyledProperty<double> WorkAreaInMsProperty =
            AvaloniaProperty.Register<TimeRulerControl, double>(nameof(WorkAreaInMs), -1);

        public static readonly StyledProperty<double> WorkAreaOutMsProperty =
            AvaloniaProperty.Register<TimeRulerControl, double>(nameof(WorkAreaOutMs), -1);

        /// <summary>总时长 (毫秒)</summary>
        public double DurationMs
        {
            get => GetValue(DurationMsProperty);
            set => SetValue(DurationMsProperty, value);
        }

        /// <summary>当前播放时间 (毫秒)</summary>
        public double CurrentTimeMs
        {
            get => GetValue(CurrentTimeMsProperty);
            set => SetValue(CurrentTimeMsProperty, value);
        }

        /// <summary>缩放级别: 每毫秒对应的像素数</summary>
        public double PixelsPerMs
        {
            get => GetValue(PixelsPerMsProperty);
            set => SetValue(PixelsPerMsProperty, value);
        }

        /// <summary>水平滚动偏移 (像素)</summary>
        public double ScrollOffsetX
        {
            get => GetValue(ScrollOffsetXProperty);
            set => SetValue(ScrollOffsetXProperty, value);
        }

        /// <summary>参考视频时长 (毫秒), >0 时在标尺上显示参考标记</summary>
        public double VideoDurationMs
        {
            get => GetValue(VideoDurationMsProperty);
            set => SetValue(VideoDurationMsProperty, value);
        }

        /// <summary>独立事件列表, 在标尺上显示事件标记</summary>
        public List<TimelineEvent>? Events
        {
            get => GetValue(EventsProperty);
            set => SetValue(EventsProperty, value);
        }

        /// <summary>是否为深色主题</summary>
        public bool IsDarkTheme
        {
            get => GetValue(IsDarkThemeProperty);
            set => SetValue(IsDarkThemeProperty, value);
        }

        /// <summary>工作区域入点 (毫秒), -1 表示未设置</summary>
        public double WorkAreaInMs
        {
            get => GetValue(WorkAreaInMsProperty);
            set => SetValue(WorkAreaInMsProperty, value);
        }

        /// <summary>工作区域出点 (毫秒), -1 表示未设置</summary>
        public double WorkAreaOutMs
        {
            get => GetValue(WorkAreaOutMsProperty);
            set => SetValue(WorkAreaOutMsProperty, value);
        }

        // ═══════ 视觉常量 (主题感知) ═══════

        private static readonly IBrush PlayheadBrush = new SolidColorBrush(Color.Parse("#ef4444"));
        private static readonly IBrush VideoRefBrush = new SolidColorBrush(Color.Parse("#8b5cf6"));
        private static readonly IPen VideoRefPen = new Pen(VideoRefBrush, 1.5, DashStyle.Dash);
        private static readonly IBrush EventMarkerBrush = new SolidColorBrush(
            Color.Parse("#f59e0b")
        );

        /// <summary>时间轴标记列表, 在标尺上显示书签标记</summary>
        public List<TimelineMarker>? Markers
        {
            get => _markers;
            set
            {
                _markers = value;
                InvalidateVisual();
            }
        }
        private List<TimelineMarker>? _markers;

        /// <summary>标记被左键单击选中时触发</summary>
        public event Action<TimelineMarker>? MarkerSelected;

        /// <summary>请求删除标记</summary>
        public event Action<TimelineMarker>? MarkerDeleteRequested;

        /// <summary>标记被拖拽到新位置, 参数: (TimelineMarker, newTimeMs)</summary>
        public event Action<TimelineMarker, double>? MarkerMoved;

        // 标记交互状态
        private TimelineMarker? _selectedMarker;
        private bool _isDraggingMarker;
        private TimelineMarker? _draggingMarker;

        // 主题动态画笔
        private IBrush BgBrush =>
            IsDarkTheme
                ? new SolidColorBrush(Color.Parse("#1f2937"))
                : new SolidColorBrush(Color.Parse("#f3f4f6"));
        private IPen MajorPen =>
            new Pen(
                IsDarkTheme
                    ? new SolidColorBrush(Color.Parse("#9ca3af"))
                    : new SolidColorBrush(Color.Parse("#6b7280")),
                1
            );
        private IPen MinorPen =>
            new Pen(
                IsDarkTheme
                    ? new SolidColorBrush(Color.Parse("#4b5563"))
                    : new SolidColorBrush(Color.Parse("#d1d5db")),
                0.5
            );
        private IBrush LabelBrush =>
            IsDarkTheme
                ? new SolidColorBrush(Color.Parse("#e5e7eb"))
                : new SolidColorBrush(Color.Parse("#374151"));
        private IPen PlayheadLinePen => new Pen(PlayheadBrush, 2);
        private IPen BottomPen =>
            new Pen(
                IsDarkTheme
                    ? new SolidColorBrush(Color.Parse("#374151"))
                    : new SolidColorBrush(Color.Parse("#e5e7eb")),
                1
            );

        private readonly Typeface _typeface = new Typeface("Inter, Segoe UI, sans-serif");

        // ═══════ 交互状态 ═══════
        private bool _isDraggingPlayhead;
        private bool _isPanning;
        private Point _panStart;
        private double _panStartOffset;

        /// <summary>播放头被用户拖拽到新位置时触发</summary>
        public event Action<double>? PlayheadSeek;

        /// <summary>缩放级别被滚轮改变时触发</summary>
        public event Action<double>? ZoomChanged;

        /// <summary>滚动偏移改变时触发</summary>
        public event Action<double>? ScrollChanged;

        /// <summary>事件标记被左键单击选中时触发, 参数: TimelineEvent</summary>
        public event Action<TimelineEvent>? EventSelected;

        /// <summary>请求删除事件, 参数: TimelineEvent</summary>
        public event Action<TimelineEvent>? EventDeleteRequested;

        /// <summary>事件被拖拽到新位置, 参数: (TimelineEvent, newTimeMs)</summary>
        public event Action<TimelineEvent, double>? EventMoved;

        // ═══════ 事件交互状态 ═══════
        private TimelineEvent? _selectedEvent;
        private bool _isDraggingEvent;
        private TimelineEvent? _draggingEvent;

        // ═══════ 工作区条带拖拽 (靠齐视频剪辑软件 In/Out) ═══════
        private bool _isDraggingWorkAreaIn;
        private bool _isDraggingWorkAreaOut;
        private bool _isDraggingWorkAreaMove;
        private double _dragWorkAreaStartIn;
        private double _dragWorkAreaStartOut;
        private Point _dragWorkAreaStartPos;
        private const double WorkAreaEdgeHitPx = 8;

        /// <summary>工作区 In/Out 被拖动改变时触发, 参数: (inMs, outMs)。实时触发。</summary>
        public event Action<double, double>? WorkAreaChanged;

        /// <summary>工作区条带命中区域。</summary>
        private enum RulerWorkAreaZone
        {
            None,
            In,
            Out,
            Move,
        }

        /// <summary>命中工作区条带 (仅刻度带内, 边界±8px 或条带内部)。</summary>
        private RulerWorkAreaZone HitTestWorkArea(Point pos)
        {
            if (WorkAreaInMs < 0 || WorkAreaOutMs < 0 || pos.Y > TicksBandH)
                return RulerWorkAreaZone.None;
            double ppm = Math.Max(0.001, PixelsPerMs);
            double xIn = WorkAreaInMs * ppm - ScrollOffsetX;
            double xOut = WorkAreaOutMs * ppm - ScrollOffsetX;
            if (Math.Abs(pos.X - xIn) <= WorkAreaEdgeHitPx)
                return RulerWorkAreaZone.In;
            if (Math.Abs(pos.X - xOut) <= WorkAreaEdgeHitPx)
                return RulerWorkAreaZone.Out;
            double x0 = Math.Min(xIn, xOut);
            double x1 = Math.Max(xIn, xOut);
            if (pos.X >= x0 && pos.X <= x1)
                return RulerWorkAreaZone.Move;
            return RulerWorkAreaZone.None;
        }

        // ═══════ 分带布局常量 (UX-B3: 独立事件/标记轨, 彻底消除与刻度的叠放) ═══════
        /// <summary>刻度+时间标签区高度</summary>
        public const double TicksBandH = 20;

        /// <summary>标记(Marker)独立轨高度</summary>
        public const double MarkersBandH = 22;

        /// <summary>事件(Event)独立轨高度 (单层, 多层将累加)</summary>
        public const double EventsBandH = 26;

        /// <summary>总默认高度: 20 + 22 + 26 = 68</summary>
        public const double DefaultHeight = TicksBandH + MarkersBandH + EventsBandH;

        // 渲染时缓存的绘制矩形, 用于精确命中测试 (修复: 点击标签文字无法选中)
        private readonly List<(TimelineEvent evt, Rect flagRect, double stemX)> _drawnEventHits =
            new();
        private readonly List<(
            TimelineMarker marker,
            Rect labelRect,
            double stemX
        )> _drawnMarkerHits = new();

        // 右键菜单 (确保同时只有一个打开)
        private ContextMenu? _activeContextMenu;

        /// <summary>请求在指定时间位置添加事件 (右键菜单触发)</summary>
        public event Action<double>? AddEventRequested;

        /// <summary>请求在指定时间位置添加标记 (右键菜单触发)</summary>
        public event Action<double>? AddMarkerRequested;

        public TimeRulerControl()
        {
            ClipToBounds = true;
            Height = DefaultHeight;
        }

        static TimeRulerControl()
        {
            AffectsRender<TimeRulerControl>(
                DurationMsProperty,
                CurrentTimeMsProperty,
                PixelsPerMsProperty,
                ScrollOffsetXProperty,
                VideoDurationMsProperty,
                EventsProperty,
                IsDarkThemeProperty,
                WorkAreaInMsProperty,
                WorkAreaOutMsProperty
            );
        }

        // ═══════ 交互: 鼠标 ═══════

        /// <summary>
        /// 命中测试: 使用绘制阶段缓存的精确矩形 (旗帜标签) + 时间点竖杆邻域 (6px)
        /// 修复: 先前 ±10px 水平圆形命中范围导致的"标签文字不可点击"问题
        /// </summary>
        private TimelineEvent? HitTestEvent(Point pos)
        {
            // 优先匹配旗帜矩形 (从后向前匹配 → 顶层优先)
            for (int i = _drawnEventHits.Count - 1; i >= 0; i--)
            {
                var (evt, rect, _) = _drawnEventHits[i];
                if (rect.Contains(pos))
                    return evt;
            }
            // 其次匹配竖杆 (事件带垂直范围内 ±6px)
            double bandTop = TicksBandH + MarkersBandH;
            if (pos.Y >= bandTop && pos.Y <= Bounds.Height)
            {
                foreach (var (evt, _, stemX) in _drawnEventHits)
                {
                    if (Math.Abs(pos.X - stemX) <= 6)
                        return evt;
                }
            }
            return null;
        }

        /// <summary>
        /// 命中测试: 使用绘制阶段缓存的精确矩形 (标签) + 竖杆邻域 (6px)
        /// </summary>
        private TimelineMarker? HitTestMarker(Point pos)
        {
            for (int i = _drawnMarkerHits.Count - 1; i >= 0; i--)
            {
                var (m, rect, _) = _drawnMarkerHits[i];
                if (rect.Contains(pos))
                    return m;
            }
            // 标记带或贯穿竖杆
            double markersTop = TicksBandH;
            double markersBottom = TicksBandH + MarkersBandH;
            foreach (var (m, _, stemX) in _drawnMarkerHits)
            {
                // 标记带内 ±6px 命中竖杆; 其它区域不拦截以免干扰事件/播放头
                if (pos.Y >= markersTop && pos.Y <= markersBottom && Math.Abs(pos.X - stemX) <= 6)
                    return m;
            }
            return null;
        }

        /// <summary>标记的右键上下文菜单</summary>
        private void ShowMarkerContextMenu(TimelineMarker marker)
        {
            var menu = new ContextMenu();

            var deleteItem = new MenuItem { Header = "删除标记" };
            deleteItem.Click += (_, _) => MarkerDeleteRequested?.Invoke(marker);
            menu.Items.Add(deleteItem);

            _activeContextMenu?.Close();
            _activeContextMenu = menu;
            menu.Open(this);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var props = e.GetCurrentPoint(this).Properties;
            var pos = e.GetPosition(this);

            if (props.IsMiddleButtonPressed)
            {
                // 中键: 开始平移
                _isPanning = true;
                _panStart = pos;
                _panStartOffset = ScrollOffsetX;
                e.Handled = true;
            }
            else if (props.IsLeftButtonPressed)
            {
                // 工作区条带拖拽优先 (仅刻度带内命中, 不干扰标记/事件/播放头)
                var workAreaHit = HitTestWorkArea(pos);
                if (workAreaHit != RulerWorkAreaZone.None)
                {
                    _dragWorkAreaStartIn = WorkAreaInMs;
                    _dragWorkAreaStartOut = WorkAreaOutMs;
                    _dragWorkAreaStartPos = pos;
                    _isDraggingWorkAreaIn = workAreaHit == RulerWorkAreaZone.In;
                    _isDraggingWorkAreaOut = workAreaHit == RulerWorkAreaZone.Out;
                    _isDraggingWorkAreaMove = workAreaHit == RulerWorkAreaZone.Move;
                    e.Handled = true;
                    return;
                }

                // UX-A3: 仅 Alt+左键 才进入拖拽模式; 普通左键仅选中
                bool isAltDrag = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

                // 优先检测标记命中
                var hitMarker = HitTestMarker(pos);
                if (hitMarker != null)
                {
                    _selectedMarker = hitMarker;
                    if (isAltDrag)
                    {
                        _isDraggingMarker = true;
                        _draggingMarker = hitMarker;
                    }
                    MarkerSelected?.Invoke(hitMarker);
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                }

                // 检测事件标记命中
                var hitEvent = HitTestEvent(pos);
                if (hitEvent != null)
                {
                    _selectedEvent = hitEvent;
                    _selectedMarker = null;
                    if (isAltDrag)
                    {
                        _isDraggingEvent = true;
                        _draggingEvent = hitEvent;
                    }
                    EventSelected?.Invoke(hitEvent);
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                }

                // 未命中 → 播放头跳转 + 拖拽
                _selectedEvent = null;
                _selectedMarker = null;
                _isDraggingPlayhead = true;
                SeekToX(pos.X);
                e.Handled = true;
            }
            else if (props.IsRightButtonPressed)
            {
                // 右键: 检测标记或事件命中
                var hitMarker = HitTestMarker(pos);
                if (hitMarker != null)
                {
                    _selectedMarker = hitMarker;
                    MarkerSelected?.Invoke(hitMarker);
                    InvalidateVisual();
                    ShowMarkerContextMenu(hitMarker);
                    e.Handled = true;
                    return;
                }

                var hitEvent = HitTestEvent(pos);
                if (hitEvent != null)
                {
                    _selectedEvent = hitEvent;
                    EventSelected?.Invoke(hitEvent);
                    InvalidateVisual();
                    ShowEventContextMenu(hitEvent);
                    e.Handled = true;
                    return;
                }

                // 空白区域右键菜单: 添加事件 / 添加标记
                ShowRulerAreaContextMenu(pos);
                e.Handled = true;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var pos = e.GetPosition(this);

            if (_isDraggingWorkAreaIn || _isDraggingWorkAreaOut || _isDraggingWorkAreaMove)
            {
                double ppm = Math.Max(0.001, PixelsPerMs);
                double newMs = ClampToDuration((ScrollOffsetX + pos.X) / ppm, DurationMs);
                if (_isDraggingWorkAreaIn)
                {
                    WorkAreaInMs = Math.Min(newMs, WorkAreaOutMs - 1);
                }
                else if (_isDraggingWorkAreaOut)
                {
                    WorkAreaOutMs = Math.Max(newMs, WorkAreaInMs + 1);
                }
                else
                {
                    // 整体移动: 保持时长, 夹在时间轴范围内
                    double deltaMs = (pos.X - _dragWorkAreaStartPos.X) / ppm;
                    double len = _dragWorkAreaStartOut - _dragWorkAreaStartIn;
                    double newIn = Math.Clamp(
                        _dragWorkAreaStartIn + deltaMs,
                        0,
                        Math.Max(0, DurationMs - len)
                    );
                    WorkAreaInMs = newIn;
                    WorkAreaOutMs = newIn + len;
                }
                WorkAreaChanged?.Invoke(WorkAreaInMs, WorkAreaOutMs);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (_isDraggingMarker && _draggingMarker != null)
            {
                // 拖拽标记到新时间位置
                double ppm = PixelsPerMs;
                if (ppm > 0)
                {
                    double newMs = ClampToDuration((ScrollOffsetX + pos.X) / ppm, DurationMs);
                    MarkerMoved?.Invoke(_draggingMarker, newMs);
                    InvalidateVisual();
                }
                e.Handled = true;
            }
            else if (_isDraggingEvent && _draggingEvent != null)
            {
                // 拖拽事件标记到新时间位置
                double ppm = PixelsPerMs;
                if (ppm > 0)
                {
                    double newMs = ClampToDuration((ScrollOffsetX + pos.X) / ppm, DurationMs);
                    EventMoved?.Invoke(_draggingEvent, newMs);
                    InvalidateVisual();
                }
                e.Handled = true;
            }
            else if (_isDraggingPlayhead)
            {
                SeekToX(pos.X);
                e.Handled = true;
            }
            else if (_isPanning)
            {
                double dx = _panStart.X - pos.X;
                double newOffset = ClampScrollOffset(
                    _panStartOffset + dx,
                    Bounds.Width,
                    PixelsPerMs,
                    DurationMs
                );
                ScrollOffsetX = newOffset;
                ScrollChanged?.Invoke(newOffset);
                InvalidateVisual();
                e.Handled = true;
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _isDraggingWorkAreaIn = false;
            _isDraggingWorkAreaOut = false;
            _isDraggingWorkAreaMove = false;
            _isDraggingPlayhead = false;
            _isPanning = false;
            _isDraggingEvent = false;
            _draggingEvent = null;
            _isDraggingMarker = false;
            _draggingMarker = null;
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);

            double factor = e.Delta.Y > 0 ? 1.2 : 1 / 1.2;
            double mouseX = e.GetPosition(this).X;

            // 保持鼠标下的时间位置不变
            double timeAtMouse = (ScrollOffsetX + mouseX) / PixelsPerMs;
            double newPpm = Math.Clamp(PixelsPerMs * factor, 0.001, 1.0);

            PixelsPerMs = newPpm;
            ScrollOffsetX = ClampScrollOffset(
                timeAtMouse * newPpm - mouseX,
                Bounds.Width,
                newPpm,
                DurationMs
            );

            ZoomChanged?.Invoke(newPpm);
            ScrollChanged?.Invoke(ScrollOffsetX);
            InvalidateVisual();
            e.Handled = true;
        }

        // ═══════ 辅助 ═══════

        private void SeekToX(double x)
        {
            double ppm = PixelsPerMs;
            if (ppm <= 0)
                return;

            double ms = ClampToDuration((ScrollOffsetX + x) / ppm, DurationMs);
            // 不直接设置 CurrentTimeMs, 让 ViewModel 应用吸附后通过绑定回传,
            // 避免标尺与轨道区之间播放头线断裂
            PlayheadSeek?.Invoke(ms);
            InvalidateVisual();
        }

        /// <summary>
        /// 将毫秒时间转换为控件内 X 坐标
        /// </summary>
        public double TimeToX(double ms) =>
            ClampToDuration(ms, DurationMs) * PixelsPerMs - ScrollOffsetX;

        /// <summary>
        /// 将控件内 X 坐标转换为毫秒时间
        /// </summary>
        public double XToTime(double x) =>
            ClampToDuration((ScrollOffsetX + x) / PixelsPerMs, DurationMs);

        /// <summary>将时间限制在工程范围内，供交互和纯逻辑测试复用。</summary>
        public static double ClampToDuration(double timeMs, double durationMs)
        {
            var duration = Math.Max(0, durationMs);
            return Math.Clamp(double.IsFinite(timeMs) ? timeMs : 0, 0, duration);
        }

        /// <summary>计算时间尺当前应绘制的工程内时间范围。</summary>
        public static (double startMs, double endMs) GetVisibleTimeRange(
            double scrollOffsetX,
            double viewportWidth,
            double pixelsPerMs,
            double durationMs
        )
        {
            var duration = Math.Max(0, durationMs);
            if (duration <= 0 || !double.IsFinite(pixelsPerMs) || pixelsPerMs <= 0)
                return (0, 0);

            var start = ClampToDuration(scrollOffsetX / pixelsPerMs, duration);
            var end = ClampToDuration(
                (scrollOffsetX + Math.Max(0, viewportWidth)) / pixelsPerMs,
                duration
            );
            return (Math.Min(start, end), Math.Max(start, end));
        }

        /// <summary>限制水平滚动范围，确保视口不会越过工程末尾。</summary>
        public static double ClampScrollOffset(
            double scrollOffsetX,
            double viewportWidth,
            double pixelsPerMs,
            double durationMs
        )
        {
            if (!double.IsFinite(pixelsPerMs) || pixelsPerMs <= 0)
                return 0;
            var maxOffset = Math.Max(
                0,
                Math.Max(0, durationMs) * pixelsPerMs - Math.Max(0, viewportWidth)
            );
            return Math.Clamp(double.IsFinite(scrollOffsetX) ? scrollOffsetX : 0, 0, maxOffset);
        }

        /// <summary>
        /// 外部设置选中的事件 (用于同步 ViewModel 状态)
        /// </summary>
        public void SetSelectedEvent(TimelineEvent? evt)
        {
            _selectedEvent = evt;
            InvalidateVisual();
        }

        // ═══════ 事件右键菜单 ═══════

        private void ShowEventContextMenu(TimelineEvent evt)
        {
            var menu = new ContextMenu();

            var deleteItem = new MenuItem { Header = "删除事件" };
            deleteItem.Click += (_, _) =>
            {
                EventDeleteRequested?.Invoke(evt);
                _selectedEvent = null;
                InvalidateVisual();
            };

            menu.Items.Add(deleteItem);
            _activeContextMenu?.Close();
            _activeContextMenu = menu;
            menu.Open(this);
        }

        /// <summary>
        /// 空白区域右键菜单: 在点击位置添加事件 / 标记
        /// </summary>
        private void ShowRulerAreaContextMenu(Point pos)
        {
            double ppm = PixelsPerMs;
            if (ppm <= 0)
                return;
            double clickTimeMs = ClampToDuration((ScrollOffsetX + pos.X) / ppm, DurationMs);
            string timeLabel = FormatTime(clickTimeMs);

            var menu = new ContextMenu();

            var addEventItem = new MenuItem { Header = $"在此处添加事件 ({timeLabel})" };
            addEventItem.Click += (_, _) => AddEventRequested?.Invoke(clickTimeMs);
            menu.Items.Add(addEventItem);

            var addMarkerItem = new MenuItem { Header = $"在此处添加标记 ({timeLabel})" };
            addMarkerItem.Click += (_, _) => AddMarkerRequested?.Invoke(clickTimeMs);
            menu.Items.Add(addMarkerItem);

            _activeContextMenu?.Close();
            _activeContextMenu = menu;
            menu.Open(this);
        }
    }
}
