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
    public class TimeRulerControl : Control
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

        // 右键菜单 (确保同时只有一个打开)
        private ContextMenu? _activeContextMenu;

        /// <summary>请求在指定时间位置添加事件 (右键菜单触发)</summary>
        public event Action<double>? AddEventRequested;

        /// <summary>请求在指定时间位置添加标记 (右键菜单触发)</summary>
        public event Action<double>? AddMarkerRequested;

        public TimeRulerControl()
        {
            ClipToBounds = true;
            Height = 32;
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
                IsDarkThemeProperty
            );
        }

        // ═══════ 渲染 ═══════

        public override void Render(DrawingContext context)
        {
            var bounds = Bounds;
            double w = bounds.Width;
            double h = bounds.Height;

            // 背景
            context.DrawRectangle(BgBrush, null, new Rect(0, 0, w, h));

            // 底部分隔线
            context.DrawLine(BottomPen, new Point(0, h - 0.5), new Point(w, h - 0.5));

            double ppm = PixelsPerMs;
            if (ppm <= 0)
                return;

            // 计算自适应刻度间距
            var (majorIntervalMs, minorIntervalMs) = CalculateTickInterval(ppm);

            // 可见范围 (毫秒)
            double startMs = ScrollOffsetX / ppm;
            double endMs = (ScrollOffsetX + w) / ppm;

            // 绘制刻度
            DrawTicks(context, startMs, endMs, majorIntervalMs, minorIntervalMs, h, ppm);

            // 绘制视频时长参考线
            DrawVideoReference(context, h, ppm);

            // 绘制事件标记
            DrawEventMarkers(context, h, ppm);

            // 绘制时间轴标记 (书签/旗帜)
            DrawTimelineMarkers(context, h, ppm);

            // 绘制工作区域标记
            DrawWorkArea(context, w, h, ppm);

            // 绘制播放头
            DrawPlayhead(context, h, ppm);
        }

        private void DrawTicks(
            DrawingContext context,
            double startMs,
            double endMs,
            double majorMs,
            double minorMs,
            double height,
            double ppm
        )
        {
            var majorPen = MajorPen;
            var minorPen = MinorPen;
            var labelBrush = LabelBrush;

            // Minor ticks
            double firstMinor = Math.Floor(startMs / minorMs) * minorMs;
            for (double ms = firstMinor; ms <= endMs; ms += minorMs)
            {
                if (ms < 0)
                    continue;
                double x = ms * ppm - ScrollOffsetX;
                context.DrawLine(minorPen, new Point(x, height - 6), new Point(x, height - 1));
            }

            // Major ticks + labels
            double firstMajor = Math.Floor(startMs / majorMs) * majorMs;
            for (double ms = firstMajor; ms <= endMs; ms += majorMs)
            {
                if (ms < 0)
                    continue;
                double x = ms * ppm - ScrollOffsetX;

                // Tick line
                context.DrawLine(majorPen, new Point(x, height - 14), new Point(x, height - 1));

                // Label
                string label = FormatTime(ms);
                var formattedText = new FormattedText(
                    label,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    _typeface,
                    10,
                    labelBrush
                );

                context.DrawText(formattedText, new Point(x + 3, 2));
            }
        }

        private void DrawPlayhead(DrawingContext context, double height, double ppm)
        {
            double x = CurrentTimeMs * ppm - ScrollOffsetX;
            if (x < -10 || x > Bounds.Width + 10)
                return;

            // 播放头线条
            context.DrawLine(PlayheadLinePen, new Point(x, 0), new Point(x, height));

            // 播放头三角形
            var triangle = new StreamGeometry();
            using (var gc = triangle.Open())
            {
                gc.BeginFigure(new Point(x - 5, 0), true);
                gc.LineTo(new Point(x + 5, 0));
                gc.LineTo(new Point(x, 8));
                gc.EndFigure(true);
            }
            context.DrawGeometry(PlayheadBrush, null, triangle);
        }

        /// <summary>
        /// 绘制工作区域标记 (半透明蓝色高亮范围 + I/O 标签)
        /// </summary>
        private void DrawWorkArea(DrawingContext context, double width, double height, double ppm)
        {
            double inMs = WorkAreaInMs;
            double outMs = WorkAreaOutMs;
            if (inMs < 0 && outMs < 0)
                return;

            // 入点标记
            if (inMs >= 0)
            {
                double xIn = inMs * ppm - ScrollOffsetX;
                if (xIn >= -20 && xIn <= width + 20)
                {
                    var inPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 34, 197, 94)), 1.5);
                    context.DrawLine(inPen, new Point(xIn, 0), new Point(xIn, height));

                    var inLabel = new FormattedText(
                        "I",
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        _typeface,
                        9,
                        new SolidColorBrush(Color.FromArgb(255, 34, 197, 94))
                    );
                    context.DrawText(inLabel, new Point(xIn + 2, height - 12));
                }
            }

            // 出点标记
            if (outMs >= 0)
            {
                double xOut = outMs * ppm - ScrollOffsetX;
                if (xOut >= -20 && xOut <= width + 20)
                {
                    var outPen = new Pen(
                        new SolidColorBrush(Color.FromArgb(200, 239, 68, 68)),
                        1.5
                    );
                    context.DrawLine(outPen, new Point(xOut, 0), new Point(xOut, height));

                    var outLabel = new FormattedText(
                        "O",
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        _typeface,
                        9,
                        new SolidColorBrush(Color.FromArgb(255, 239, 68, 68))
                    );
                    context.DrawText(outLabel, new Point(xOut - 10, height - 12));
                }
            }

            // 区域高亮 (入点和出点都有效时)
            if (inMs >= 0 && outMs > inMs)
            {
                double xStart = Math.Max(0, inMs * ppm - ScrollOffsetX);
                double xEnd = Math.Min(width, outMs * ppm - ScrollOffsetX);
                if (xEnd > xStart)
                {
                    var areaBrush = new SolidColorBrush(Color.FromArgb(20, 59, 130, 246));
                    var areaRect = new Rect(xStart, 0, xEnd - xStart, height);
                    context.DrawRectangle(areaBrush, null, areaRect);
                }
            }
        }

        /// <summary>
        /// 绘制视频时长参考标记 (紫色虚线 + 🎬 标签)
        /// </summary>
        private void DrawVideoReference(DrawingContext context, double height, double ppm)
        {
            double videoMs = VideoDurationMs;
            if (videoMs <= 0)
                return;

            double x = videoMs * ppm - ScrollOffsetX;
            if (x < -20 || x > Bounds.Width + 20)
                return;

            // 紫色虚线
            context.DrawLine(VideoRefPen, new Point(x, 0), new Point(x, height));

            // 标签
            var label = new FormattedText(
                "🎬",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                _typeface,
                9,
                VideoRefBrush
            );
            context.DrawText(label, new Point(x + 2, 1));
        }

        /// <summary>
        /// 绘制独立事件标记 (旗帜样式, 选中时高亮, 自动避让重叠)
        /// </summary>
        private void DrawEventMarkers(DrawingContext context, double height, double ppm)
        {
            var events = Events;
            if (events == null || events.Count == 0)
                return;

            var selectedHighlightBrush = new SolidColorBrush(Color.Parse("#ef4444"));
            var selectedBorderPen = new Pen(Brushes.White, 1.5);
            var normalBorderPen = new Pen(new SolidColorBrush(Color.Parse("#d97706")), 0.8);
            var stalkPen = new Pen(new SolidColorBrush(Color.Parse("#a0f59e0b")), 1);
            var stalkPenSelected = new Pen(new SolidColorBrush(Color.Parse("#a0ef4444")), 1);
            var bgBrush = new SolidColorBrush(Color.Parse("#E01e1e1e"));

            // ── 计算每个标记的 X 位置并排序 ──
            var markers = new List<(TimelineEvent evt, double x)>();
            foreach (var evt in events)
            {
                double x = evt.TimeMs * ppm - ScrollOffsetX;
                if (x < -30 || x > Bounds.Width + 30)
                    continue;
                markers.Add((evt, x));
            }
            markers.Sort((a, b) => a.x.CompareTo(b.x));

            // ── 避让: 计算每个标签的 Y 层级 (密集时交错) ──
            const double flagWidth = 60; // 标签最小间距 (px)
            const double flagH = 14; // 旗帜高度
            const double stalkTop = 4; // 杆顶部距控件顶
            const double maxLevels = 3;
            double[] levelEndX = new double[(int)maxLevels]; // 每层已占用的右边界
            for (int i = 0; i < levelEndX.Length; i++)
                levelEndX[i] = double.MinValue;

            foreach (var (evt, x) in markers)
            {
                bool isSelected = (evt == _selectedEvent);

                // 确定层级 (找第一个不冲突的层)
                int level = 0;
                for (int lv = 0; lv < levelEndX.Length; lv++)
                {
                    if (x >= levelEndX[lv])
                    {
                        level = lv;
                        break;
                    }
                    if (lv == levelEndX.Length - 1)
                        level = lv; // 全满则用最后一层
                }

                double flagY = stalkTop + level * (flagH + 2);

                // ── 竖直杆 (从旗帜底部延伸到标尺底部) ──
                context.DrawLine(
                    isSelected ? stalkPenSelected : stalkPen,
                    new Point(x, flagY + flagH),
                    new Point(x, height)
                );

                // ── 小圆点标记精确时间位置 ──
                var dotBrush = isSelected ? selectedHighlightBrush : EventMarkerBrush;
                context.DrawEllipse(dotBrush, null, new Point(x, height - 2), 2.5, 2.5);

                // ── 旗帜标签 ──
                string label = string.IsNullOrEmpty(evt.EventName) ? "event" : evt.EventName;
                // 缩放较小时截断名称
                if (label.Length > 8 && ppm < 0.08)
                    label = label.Substring(0, 6) + "…";

                IBrush labelBrush = isSelected
                    ? Brushes.White
                    : new SolidColorBrush(Color.Parse("#fbbf24"));
                var nameText = new FormattedText(
                    label,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    _typeface,
                    9.5,
                    labelBrush
                );

                double textW = Math.Min(nameText.Width, 80);
                double boxW = textW + 10;
                double boxX = x + 1;
                double boxY = flagY;

                // 绘制旗帜背景 (圆角矩形)
                var flagBrush = isSelected
                    ? new SolidColorBrush(Color.Parse("#D0ef4444"))
                    : new SolidColorBrush(Color.Parse("#D0b45309"));
                var flagRect = new Rect(boxX, boxY, boxW, flagH);
                context.DrawRectangle(
                    flagBrush,
                    isSelected ? selectedBorderPen : normalBorderPen,
                    flagRect,
                    3,
                    3
                );

                // 绘制文字
                var textOrigin = new Point(boxX + 5, boxY + (flagH - nameText.Height) / 2);
                using (context.PushClip(flagRect))
                {
                    context.DrawText(nameText, textOrigin);
                }

                // 更新层级占用
                levelEndX[level] = boxX + boxW + 4;
            }
        }

        /// <summary>
        /// 绘制时间轴标记 (彩色旗帜 🏁, 可自定义颜色)
        /// </summary>
        private void DrawTimelineMarkers(DrawingContext context, double height, double ppm)
        {
            var markers = Markers;
            if (markers == null || markers.Count == 0)
                return;

            var selectedBorderPen = new Pen(Brushes.White, 1.5);

            foreach (var marker in markers)
            {
                double x = marker.TimeMs * ppm - ScrollOffsetX;
                if (x < -10 || x > Bounds.Width + 10)
                    continue;

                bool isSelected = (marker == _selectedMarker);
                var markerColor = Color.Parse(marker.Color ?? "#f59e0b");
                var markerBrush = new SolidColorBrush(markerColor);

                // 小旗帜形状 (在刻度顶部)
                var flag = new StreamGeometry();
                using (var gc = flag.Open())
                {
                    gc.BeginFigure(new Point(x, 0), true);
                    gc.LineTo(new Point(x + 10, 0));
                    gc.LineTo(new Point(x + 10, 8));
                    gc.LineTo(new Point(x + 3, 5));
                    gc.LineTo(new Point(x, 8));
                    gc.EndFigure(true);
                }

                if (isSelected)
                {
                    context.DrawGeometry(markerBrush, selectedBorderPen, flag);
                }
                else
                {
                    context.DrawGeometry(markerBrush, null, flag);
                }

                // 垂直虚线
                var dashPen = new Pen(markerBrush, 0.8, DashStyle.Dash);
                context.DrawLine(dashPen, new Point(x, 8), new Point(x, height));

                // 标记名称标签
                if (!string.IsNullOrEmpty(marker.Name))
                {
                    var labelBrush = isSelected
                        ? markerBrush
                        : new SolidColorBrush(
                            Color.FromArgb(180, markerColor.R, markerColor.G, markerColor.B)
                        );
                    var nameText = new FormattedText(
                        "🏷" + marker.Name,
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        _typeface,
                        8,
                        labelBrush
                    );
                    context.DrawText(nameText, new Point(x + 11, 0));
                }
            }
        }

        // ═══════ 自适应刻度计算 ═══════

        /// <summary>
        /// 根据缩放级别计算合适的主/次刻度间距 (毫秒)
        /// 目标: 主刻度之间 80~180 像素, 覆盖从极限放大 (10ms) 到极限缩小 (600s)
        /// </summary>
        private static (double major, double minor) CalculateTickInterval(double ppm)
        {
            // 完整候选间距: 10ms ~ 600s, 覆盖所有缩放级别
            // 每个 (majorMs, minorCount) 对应主刻度间距和次刻度细分数
            (double ms, int minorDiv)[] candidates =
            {
                (10, 2), // 10ms  → minor 5ms
                (20, 4), // 20ms  → minor 5ms
                (50, 5), // 50ms  → minor 10ms
                (100, 5), // 100ms → minor 20ms
                (200, 4), // 200ms → minor 50ms
                (500, 5), // 500ms → minor 100ms
                (1000, 5), // 1s    → minor 200ms
                (2000, 4), // 2s    → minor 500ms
                (5000, 5), // 5s    → minor 1s
                (10000, 5), // 10s   → minor 2s
                (30000, 6), // 30s   → minor 5s
                (60000, 6), // 60s   → minor 10s
                (120000, 6), // 2min  → minor 20s
                (300000, 5), // 5min  → minor 60s
                (600000, 6), // 10min → minor 100s
            };

            const double minPx = 80;
            const double maxPx = 180;

            // 选择使主刻度像素间距落在 [minPx, maxPx] 的最小候选
            foreach (var (ms, divs) in candidates)
            {
                double pixels = ms * ppm;
                if (pixels >= minPx && pixels <= maxPx)
                    return (ms, ms / divs);
                if (pixels > maxPx)
                    return (ms, ms / divs);
            }

            // 所有候选都太窄 → 极限缩小, 使用最大候选
            var last = candidates[^1];
            return (last.ms, last.ms / last.minorDiv);
        }

        // ═══════ 时间格式化 ═══════

        private static string FormatTime(double ms)
        {
            if (ms < 0)
                return "0";
            var ts = TimeSpan.FromMilliseconds(ms);

            if (ms < 1000)
                return $"{ms:0}ms";
            if (ms < 60000)
            {
                // 小于 60s: 显示秒, 有小数则保留
                double sec = ms / 1000.0;
                return sec == Math.Floor(sec) ? $"{sec:0}s" : $"{sec:0.#}s";
            }
            if (ms < 3600000)
            {
                // 小于 1h: 显示 m:ss
                return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
            }
            // 大于 1h: 显示 h:mm:ss
            return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        // ═══════ 交互: 鼠标 ═══════

        /// <summary>
        /// 命中测试: 查找鼠标位置最近的事件标记 (旗帜+竖杆区域)
        /// 仅在标尺上半区域 (旗帜渲染区) 检测, 下半区域保留给播放头拖拽
        /// </summary>
        private TimelineEvent? HitTestEvent(Point pos)
        {
            var events = Events;
            if (events == null || events.Count == 0)
                return null;

            double ppm = PixelsPerMs;
            if (ppm <= 0)
                return null;

            double h = Bounds.Height;
            const double hitRadiusX = 10; // 水平命中范围 (收窄避免误操作)

            // 事件旗帜渲染在标尺上半部分, 下半部分为时间刻度 → 播放头优先区
            double eventZoneMaxY = h * 0.6;
            if (pos.Y > eventZoneMaxY)
                return null;

            foreach (var evt in events)
            {
                double x = evt.TimeMs * ppm - ScrollOffsetX;
                double dx = Math.Abs(pos.X - x);
                if (dx <= hitRadiusX)
                    return evt;
            }
            return null;
        }

        /// <summary>
        /// 命中测试: 查找鼠标位置最近的时间轴标记 (在命中半径内)
        /// </summary>
        private TimelineMarker? HitTestMarker(Point pos)
        {
            var markers = Markers;
            if (markers == null || markers.Count == 0)
                return null;

            double ppm = PixelsPerMs;
            if (ppm <= 0)
                return null;

            const double hitRadius = 10;

            foreach (var marker in markers)
            {
                double x = marker.TimeMs * ppm - ScrollOffsetX;
                // 标记旗帜中心约在 (x+5, 4)
                double dy = pos.Y - 4;
                double dx = pos.X - (x + 5);
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist <= hitRadius)
                    return marker;
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
                // 优先检测标记命中
                var hitMarker = HitTestMarker(pos);
                if (hitMarker != null)
                {
                    _selectedMarker = hitMarker;
                    _isDraggingMarker = true;
                    _draggingMarker = hitMarker;
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
                    _isDraggingEvent = true;
                    _draggingEvent = hitEvent;
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

            if (_isDraggingMarker && _draggingMarker != null)
            {
                // 拖拽标记到新时间位置
                double ppm = PixelsPerMs;
                if (ppm > 0)
                {
                    double newMs = Math.Max(0, (ScrollOffsetX + pos.X) / ppm);
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
                    double newMs = Math.Max(0, (ScrollOffsetX + pos.X) / ppm);
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
                double newOffset = Math.Max(0, _panStartOffset + dx);
                ScrollOffsetX = newOffset;
                ScrollChanged?.Invoke(newOffset);
                InvalidateVisual();
                e.Handled = true;
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
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
            ScrollOffsetX = Math.Max(0, timeAtMouse * newPpm - mouseX);

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

            double ms = Math.Max(0, (ScrollOffsetX + x) / ppm);
            // 不直接设置 CurrentTimeMs, 让 ViewModel 应用吸附后通过绑定回传,
            // 避免标尺与轨道区之间播放头线断裂
            PlayheadSeek?.Invoke(ms);
            InvalidateVisual();
        }

        /// <summary>
        /// 将毫秒时间转换为控件内 X 坐标
        /// </summary>
        public double TimeToX(double ms) => ms * PixelsPerMs - ScrollOffsetX;

        /// <summary>
        /// 将控件内 X 坐标转换为毫秒时间
        /// </summary>
        public double XToTime(double x) => (ScrollOffsetX + x) / PixelsPerMs;

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
            double clickTimeMs = Math.Max(0, (ScrollOffsetX + pos.X) / ppm);
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
