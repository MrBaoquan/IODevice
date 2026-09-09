using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using IOStudio.Models.Motion;

namespace IOStudio.Controls.Timeline
{
    /// <summary>
    /// TimeRulerControl 渲染部分 (partial) — Render 与全部绘制方法。
    /// 与主文件共享依赖属性/字段; 拆分目的是减小单文件规模, 职责清晰。
    /// </summary>
    public partial class TimeRulerControl
    {
        // ═══════ 渲染 ═══════

        public override void Render(DrawingContext context)
        {
            var bounds = Bounds;
            double w = bounds.Width;
            double h = bounds.Height;

            // 背景
            context.DrawRectangle(BgBrush, null, new Rect(0, 0, w, h));

            double ppm = PixelsPerMs;
            if (ppm <= 0)
                return;

            // ── 分带布局: ticks / markers / events ──
            double ticksBottom = Math.Min(TicksBandH, h);
            double markersTop = ticksBottom;
            double markersBottom = Math.Min(markersTop + MarkersBandH, h);
            double eventsTop = markersBottom;

            // 分带之间的分隔线
            var bandSepPen = BottomPen;
            if (ticksBottom < h)
                context.DrawLine(
                    bandSepPen,
                    new Point(0, ticksBottom - 0.5),
                    new Point(w, ticksBottom - 0.5)
                );
            if (markersBottom < h && markersBottom > ticksBottom + 0.5)
                context.DrawLine(
                    bandSepPen,
                    new Point(0, markersBottom - 0.5),
                    new Point(w, markersBottom - 0.5)
                );
            // 底部分隔线
            context.DrawLine(bandSepPen, new Point(0, h - 0.5), new Point(w, h - 0.5));

            // 计算自适应刻度间距
            var (majorIntervalMs, minorIntervalMs) = CalculateTickInterval(ppm);

            // 可见范围限制在工程时长内。视口可以继续平移，但不会再绘制或命中工程外的时间。
            var (startMs, endMs) = GetVisibleTimeRange(ScrollOffsetX, w, ppm, DurationMs);

            // 刻度绘制限定在顶部刻度带内 (高度 = ticksBottom)
            DrawTicks(
                context,
                startMs,
                endMs,
                majorIntervalMs,
                minorIntervalMs,
                ticksBottom,
                ppm,
                DurationMs
            );

            // 视频/工作区参考线跨全高, 但标签放在刻度带内
            DrawVideoReference(context, h, ppm, ticksBottom);

            // 标记独立轨
            DrawTimelineMarkers(context, ppm, markersTop, markersBottom, h);

            // 事件独立轨 (含多层避让)
            DrawEventMarkers(context, ppm, eventsTop, h);

            // 工作区域高亮跨全高
            DrawWorkArea(context, w, h, ppm, ticksBottom);

            // 播放头跨全高
            DrawPlayhead(context, h, ppm);
        }

        private void DrawTicks(
            DrawingContext context,
            double startMs,
            double endMs,
            double majorMs,
            double minorMs,
            double ticksBandH,
            double ppm,
            double durationMs
        )
        {
            var majorPen = MajorPen;
            var minorPen = MinorPen;
            var labelBrush = LabelBrush;

            // Minor ticks (短, 顶部刻度带底部 6px)
            double firstMinor = Math.Floor(startMs / minorMs) * minorMs;
            for (double ms = firstMinor; ms <= endMs; ms += minorMs)
            {
                if (ms < 0)
                    continue;
                double x = ms * ppm - ScrollOffsetX;
                context.DrawLine(
                    minorPen,
                    new Point(x, ticksBandH - 6),
                    new Point(x, ticksBandH - 1)
                );
            }

            // Major ticks + labels
            double firstMajor = Math.Floor(startMs / majorMs) * majorMs;
            for (double ms = firstMajor; ms <= endMs; ms += majorMs)
            {
                if (ms < 0)
                    continue;
                double x = ms * ppm - ScrollOffsetX;

                // Tick line (较长, 在刻度带底部)
                context.DrawLine(
                    majorPen,
                    new Point(x, ticksBandH - 12),
                    new Point(x, ticksBandH - 1)
                );

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

            // 工程时长不是整刻度时，补一个明确的终点刻度，帮助用户判断可编辑范围。
            double duration = Math.Max(0, durationMs);
            if (duration > 0 && duration >= startMs - 0.001 && duration <= endMs + 0.001)
            {
                bool alreadyDrawn =
                    Math.Abs(duration / majorMs - Math.Round(duration / majorMs)) < 0.000001;
                if (!alreadyDrawn)
                {
                    double x = duration * ppm - ScrollOffsetX;
                    context.DrawLine(
                        majorPen,
                        new Point(x, ticksBandH - 12),
                        new Point(x, ticksBandH - 1)
                    );
                    var formattedText = new FormattedText(
                        FormatTime(duration),
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        _typeface,
                        10,
                        labelBrush
                    );
                    context.DrawText(formattedText, new Point(x + 3, 2));
                }
            }
        }

        private void DrawPlayhead(DrawingContext context, double height, double ppm)
        {
            double x = ClampToDuration(CurrentTimeMs, DurationMs) * ppm - ScrollOffsetX;
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
        private void DrawWorkArea(
            DrawingContext context,
            double width,
            double height,
            double ppm,
            double ticksBandH
        )
        {
            double inMs = WorkAreaInMs < 0 ? -1 : ClampToDuration(WorkAreaInMs, DurationMs);
            double outMs = WorkAreaOutMs < 0 ? -1 : ClampToDuration(WorkAreaOutMs, DurationMs);
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
                    context.DrawText(inLabel, new Point(xIn + 2, ticksBandH - 12));
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
                    context.DrawText(outLabel, new Point(xOut - 10, ticksBandH - 12));
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

                    // 刻度带内的醒目条带 (可拖拽视觉, 靠齐剪辑软件 In/Out)
                    double bandH = Math.Min(ticksBandH, height);
                    if (bandH > 0)
                    {
                        var bandBrush = new SolidColorBrush(Color.FromArgb(120, 59, 130, 246));
                        context.DrawRectangle(
                            bandBrush,
                            null,
                            new Rect(xStart, 0, xEnd - xStart, bandH)
                        );
                        var bandBorderPen = new Pen(
                            new SolidColorBrush(Color.FromArgb(200, 96, 165, 250)),
                            1
                        );
                        context.DrawLine(bandBorderPen, new Point(xStart, 0), new Point(xEnd, 0));
                        context.DrawLine(
                            bandBorderPen,
                            new Point(xStart, bandH - 0.5),
                            new Point(xEnd, bandH - 0.5)
                        );

                        // 区域时长徽章: 直观显示 In→Out 持续时长 (刻度带中央)
                        double durMs = outMs - inMs;
                        string durText = FormatTime(durMs);
                        var durFmt = new FormattedText(
                            durText,
                            CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight,
                            _typeface,
                            10,
                            new SolidColorBrush(Color.FromArgb(255, 29, 78, 216))
                        );
                        double bandMidX = xStart + (xEnd - xStart) / 2 - durFmt.Width / 2;
                        double bandMidY = (bandH - durFmt.Height) / 2;
                        // 半透明底 (增强可读性)
                        var durBg = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255));
                        context.DrawRectangle(
                            durBg,
                            null,
                            new Rect(
                                bandMidX - 4,
                                bandMidY - 1,
                                durFmt.Width + 8,
                                durFmt.Height + 2
                            )
                        );
                        context.DrawText(durFmt, new Point(bandMidX, bandMidY));
                    }
                }
            }

            // 边界把手 (刻度带内, 示意可拖)
            if (inMs >= 0)
            {
                double xIn = inMs * ppm - ScrollOffsetX;
                if (xIn >= -20 && xIn <= width + 20)
                {
                    var inHandle = new SolidColorBrush(Color.FromArgb(230, 34, 197, 94));
                    context.DrawRectangle(
                        inHandle,
                        null,
                        new Rect(xIn - 2, 0, 4, Math.Min(ticksBandH, height))
                    );
                }
            }
            if (outMs >= 0)
            {
                double xOut = outMs * ppm - ScrollOffsetX;
                if (xOut >= -20 && xOut <= width + 20)
                {
                    var outHandle = new SolidColorBrush(Color.FromArgb(230, 239, 68, 68));
                    context.DrawRectangle(
                        outHandle,
                        null,
                        new Rect(xOut - 2, 0, 4, Math.Min(ticksBandH, height))
                    );
                }
            }
        }

        /// <summary>
        /// 绘制视频时长参考标记 (紫色虚线 + V 标签)
        /// </summary>
        private void DrawVideoReference(
            DrawingContext context,
            double height,
            double ppm,
            double ticksBandH
        )
        {
            double videoMs = Math.Min(VideoDurationMs, Math.Max(0, DurationMs));
            if (videoMs <= 0)
                return;

            double x = videoMs * ppm - ScrollOffsetX;
            if (x < -20 || x > Bounds.Width + 20)
                return;

            // 紫色虚线
            context.DrawLine(VideoRefPen, new Point(x, 0), new Point(x, height));

            // 标签 (限定在刻度带内)
            var label = new FormattedText(
                "V",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                _typeface,
                9,
                VideoRefBrush
            );
            context.DrawText(label, new Point(x + 2, 1));
        }

        /// <summary>
        /// 绘制独立事件轨 (eventsBand 内): 小圆点(精确时间) + 旗帜标签(名称), 支持多层避让
        /// 同时缓存每个旗帜的绘制矩形到 _drawnEventHits, 供精确命中测试
        /// </summary>
        private void DrawEventMarkers(
            DrawingContext context,
            double ppm,
            double bandTop,
            double totalH
        )
        {
            _drawnEventHits.Clear();
            var events = Events;
            if (events == null || events.Count == 0)
                return;

            var selectedHighlightBrush = new SolidColorBrush(Color.Parse("#ef4444"));
            var selectedBorderPen = new Pen(Brushes.White, 1.5);
            var normalBorderPen = new Pen(new SolidColorBrush(Color.Parse("#d97706")), 0.8);
            var stalkPen = new Pen(
                new SolidColorBrush(Color.Parse("#70f59e0b")),
                1,
                DashStyle.Dash
            );
            var stalkPenSelected = new Pen(
                new SolidColorBrush(Color.Parse("#a0ef4444")),
                1.2,
                DashStyle.Dash
            );

            // ── 计算每个标记的 X 位置并排序 ──
            var candidates = new List<(TimelineEvent evt, double x)>();
            foreach (var evt in events)
            {
                if (evt.TimeMs < 0 || evt.TimeMs > DurationMs)
                    continue;
                double x = evt.TimeMs * ppm - ScrollOffsetX;
                if (x < -30 || x > Bounds.Width + 30)
                    continue;
                candidates.Add((evt, x));
            }
            candidates.Sort((a, b) => a.x.CompareTo(b.x));

            // ── 避让: 层级排布, 每层高度 flagH + 2, 位于事件带内 ──
            const double flagH = 14;
            const int maxLevels = 3;
            double bandBottom = totalH; // 事件带底部 = 控件底部
            // 至多可容纳几层 (受事件带高度限制)
            int availableLevels = Math.Max(
                1,
                (int)Math.Floor((bandBottom - bandTop) / (flagH + 2))
            );
            int levels = Math.Min(maxLevels, availableLevels);
            double[] levelEndX = new double[levels];
            for (int i = 0; i < levelEndX.Length; i++)
                levelEndX[i] = double.MinValue;

            foreach (var (evt, x) in candidates)
            {
                bool isSelected = (evt == _selectedEvent);

                // 确定层级 (找第一个不冲突的层, 否则用最后一层)
                int level = levels - 1;
                for (int lv = 0; lv < levels; lv++)
                {
                    if (x >= levelEndX[lv])
                    {
                        level = lv;
                        break;
                    }
                }

                // 事件带内的 Y 坐标 (自顶向下堆叠)
                double flagY = bandTop + 2 + level * (flagH + 2);

                // ── 竖直虚线杆 (贯穿整个控件, 提供时间对齐视觉) ──
                context.DrawLine(
                    isSelected ? stalkPenSelected : stalkPen,
                    new Point(x, 0),
                    new Point(x, totalH)
                );

                // ── 小圆点标记精确时间位置 (位于事件带底部) ──
                var dotBrush = isSelected ? selectedHighlightBrush : EventMarkerBrush;
                context.DrawEllipse(dotBrush, null, new Point(x, totalH - 2), 2.5, 2.5);

                // ── 旗帜标签 ──
                string label = string.IsNullOrEmpty(evt.EventName) ? "event" : evt.EventName;
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

                var textOrigin = new Point(boxX + 5, boxY + (flagH - nameText.Height) / 2);
                using (context.PushClip(flagRect))
                {
                    context.DrawText(nameText, textOrigin);
                }

                // 记录精确命中矩形 (覆盖整个旗帜, 修复点击标签文字无响应问题)
                _drawnEventHits.Add((evt, flagRect, x));

                // 更新层级占用
                levelEndX[level] = boxX + boxW + 4;
            }
        }

        /// <summary>
        /// 绘制独立标记轨 (markersBand 内): 圆角矩形标签 + 贯穿实线, 同时缓存绘制矩形供命中测试
        /// </summary>
        private void DrawTimelineMarkers(
            DrawingContext context,
            double ppm,
            double bandTop,
            double bandBottom,
            double totalH
        )
        {
            _drawnMarkerHits.Clear();
            var markers = Markers;
            if (markers == null || markers.Count == 0)
                return;

            var selectedBorderPen = new Pen(Brushes.White, 1.5);

            // 排序 + 水平避让 (单层, 仅当绝对重叠时轻微错位)
            var candidates = new List<(TimelineMarker m, double x)>();
            foreach (var m in markers)
            {
                if (m.TimeMs < 0 || m.TimeMs > DurationMs)
                    continue;
                double x = m.TimeMs * ppm - ScrollOffsetX;
                if (x < -40 || x > Bounds.Width + 40)
                    continue;
                candidates.Add((m, x));
            }
            candidates.Sort((a, b) => a.x.CompareTo(b.x));

            double lastRight = double.MinValue;
            foreach (var (marker, x) in candidates)
            {
                bool isSelected = (marker == _selectedMarker);
                var markerColor = Color.Parse(marker.Color ?? "#10b981");
                var markerBrush = new SolidColorBrush(markerColor);

                string labelText = string.IsNullOrEmpty(marker.Name) ? "◆" : marker.Name;
                var textBrush = new SolidColorBrush(Colors.White);
                var nameText = new FormattedText(
                    labelText,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    _typeface,
                    10,
                    textBrush
                );

                double padH = 6;
                double padV = 2;
                double labelW = Math.Max(18, nameText.Width + padH * 2);
                double labelH = Math.Min(nameText.Height + padV * 2, bandBottom - bandTop - 2);
                double labelX = x - 2;
                // 同时刻避让: 若与上一个完全重叠, 向右微偏
                if (labelX < lastRight)
                    labelX = lastRight + 2;

                double labelY = bandTop + ((bandBottom - bandTop) - labelH) / 2;
                var labelRect = new Rect(labelX, labelY, labelW, labelH);
                var labelGeo = new RectangleGeometry(labelRect, 4, 4);
                context.DrawGeometry(markerBrush, isSelected ? selectedBorderPen : null, labelGeo);

                // 文本
                using (context.PushClip(labelRect))
                {
                    context.DrawText(nameText, new Point(labelX + padH, labelY + padV));
                }

                // 贯穿实线 (从刻度带底延伸到控件底)
                var solidPen = new Pen(
                    new SolidColorBrush(
                        Color.FromArgb(160, markerColor.R, markerColor.G, markerColor.B)
                    ),
                    1
                );
                context.DrawLine(solidPen, new Point(x, bandTop), new Point(x, totalH));

                _drawnMarkerHits.Add((marker, labelRect, x));
                lastRight = labelX + labelW;
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
    }
}
