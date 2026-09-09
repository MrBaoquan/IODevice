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
    // Rendering methods extracted from CurveEditorControl
    public partial class CurveEditorControl
    {
        /// <summary>判断轨道是否应该被隐藏 (禁用/静音/非Solo/ShowInCurve=false/非焦点轨)</summary>
        private bool IsTrackHidden(int trackIdx)
        {
            var tracks = CurveTracks;
            if (tracks == null || trackIdx < 0 || trackIdx >= tracks.Count)
                return true;
            var t = tracks[trackIdx];
            if (!t.IsEnabled)
                return true;
            if (t.IsMuted)
                return true;
            // UX-B1: 用户显式在轨道头隐藏了曲线显示
            if (!t.ShowInCurve)
                return true;
            // Solo 逻辑: 如果任何轨道开启了 Solo, 则只显示 Solo 轨道
            bool anySolo = false;
            for (int i = 0; i < tracks.Count; i++)
            {
                if (tracks[i].IsEnabled && tracks[i].IsSolo)
                {
                    anySolo = true;
                    break;
                }
            }
            if (anySolo && !t.IsSolo)
                return true;
            // 方案A 焦点模式: 有选中轨道(焦点, 支持多选)时只显示焦点轨, 多轨查看用 Solo
            if (
                !anySolo && FocusedTrackIndexes.Count > 0 && !FocusedTrackIndexes.Contains(trackIdx)
            )
                return true;
            return false;
        }

        public override void Render(DrawingContext context)
        {
            var bounds = Bounds;
            double w = bounds.Width;
            double h = bounds.Height;

            // 背景
            context.DrawRectangle(BgBrush, null, new Rect(0, 0, w, h));

            if (PixelsPerMs <= 0)
                return;

            // 网格
            DrawValueGrid(context, w, h);
            DrawTimeGrid(context, w, h);

            // 曲线
            var tracks = CurveTracks;
            if (tracks != null)
            {
                for (int ti = 0; ti < tracks.Count; ti++)
                {
                    if (IsTrackHidden(ti))
                        continue;
                    DrawTrackCurve(context, tracks[ti], ti, w, h);
                }
            }

            // idle 周期边界虚线 + 首尾闭合警示 (在曲线之上、关键帧之下)。
            // 注: 独立待机编辑器接管 idle 编辑后, 动画曲线视图不再叠加 idle 装饰 (减负)。
            if (tracks != null && ShowIdleOverlaysInCurve)
            {
                for (int ti = 0; ti < tracks.Count; ti++)
                {
                    if (IsTrackHidden(ti))
                        continue;
                    DrawIdleCycleBoundaries(context, tracks[ti], ti, w, h);
                    DrawIdleBlendZones(context, tracks[ti], ti, w, h);
                }
            }

            // 关键帧标记 + 切线手柄 (选中的轨道在最上层)
            if (tracks != null)
            {
                for (int ti = 0; ti < tracks.Count; ti++)
                {
                    if (IsTrackHidden(ti))
                        continue;
                    DrawTrackKeyframes(context, tracks[ti], ti, w, h);
                }
            }

            // 框选矩形
            if (_dragMode == DragMode.BoxSelect)
            {
                var boxBrush = new SolidColorBrush(Color.FromArgb(30, 59, 130, 246));
                var boxPen = new Pen(
                    new SolidColorBrush(Color.Parse("#3b82f6")),
                    1,
                    DashStyle.Dash
                );
                context.DrawRectangle(boxBrush, boxPen, _boxSelectRect);
            }

            // 吸附参考线
            if (_dragMode == DragMode.Keyframe && _snapTargetTimeMs.HasValue)
            {
                double snapX = _snapTargetTimeMs.Value * PixelsPerMs - ScrollOffsetX;
                if (snapX >= 0 && snapX <= w)
                {
                    context.DrawLine(SnapGuidePen, new Point(snapX, 0), new Point(snapX, h));
                }
            }

            // 播放头
            double playheadX = CurrentTimeMs * PixelsPerMs - ScrollOffsetX;
            if (playheadX >= 0 && playheadX <= w)
            {
                context.DrawLine(PlayheadPen, new Point(playheadX, 0), new Point(playheadX, h));
            }
        }

        // ─── 值域网格 ───

        private void DrawValueGrid(DrawingContext context, double w, double h)
        {
            // 绘制 0.0, 0.25, 0.5, 0.75, 1.0 的水平线和标签
            double[] majorValues = { 0.0, 0.25, 0.5, 0.75, 1.0 };

            foreach (var val in majorValues)
            {
                double y = ValueToY((float)val, h);
                if (y < 0 || y > h)
                    continue;

                bool isMajor = val == 0.0 || val == 0.5 || val == 1.0;
                context.DrawLine(
                    isMajor ? GridMajorPen : GridMinorPen,
                    new Point(0, y),
                    new Point(w, y)
                );

                // 值标签
                var text = new FormattedText(
                    val.ToString("F2"),
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    LabelTypeface,
                    9,
                    LabelBrush
                );
                context.DrawText(text, new Point(4, y - text.Height - 1));
            }
        }

        // ─── 时间网格 ───

        private void DrawTimeGrid(DrawingContext context, double w, double h)
        {
            double ppm = PixelsPerMs;
            // 自适应时间间隔: 确保网格线间距 >= 40px
            double pixelsPerSec = ppm * 1000;
            double interval; // ms
            if (pixelsPerSec > 200)
                interval = 500;
            else if (pixelsPerSec > 100)
                interval = 1000;
            else if (pixelsPerSec > 40)
                interval = 2000;
            else if (pixelsPerSec > 20)
                interval = 5000;
            else if (pixelsPerSec > 8)
                interval = 10000;
            else if (pixelsPerSec > 3)
                interval = 30000;
            else if (pixelsPerSec > 1.5)
                interval = 60000;
            else if (pixelsPerSec > 0.5)
                interval = 120000;
            else
                interval = 300000;

            double startMs = ScrollOffsetX / ppm;
            double endMs = (ScrollOffsetX + w) / ppm;
            double t = Math.Floor(startMs / interval) * interval;

            while (t <= endMs)
            {
                double x = t * ppm - ScrollOffsetX;
                if (x >= 0 && x <= w)
                {
                    context.DrawLine(GridMinorPen, new Point(x, 0), new Point(x, h));
                }
                t += interval;
            }
        }

        // ─── 轨道曲线绘制 ───

        private void DrawTrackCurve(
            DrawingContext context,
            CurveTrackData track,
            int trackIdx,
            double w,
            double h
        )
        {
            var trackColor = Color.TryParse(track.Color, out var c) ? c : Color.Parse("#4FC3F7");
            bool isSelected = (trackIdx == _selectedTrackIdx);
            byte alpha = isSelected ? (byte)230 : (byte)140;
            double thickness = isSelected ? 2.0 : 1.2;

            var curvePen = new Pen(
                new SolidColorBrush(
                    Color.FromArgb(alpha, trackColor.R, trackColor.G, trackColor.B)
                ),
                thickness
            );

            // 分段解析绘制 (主流做法): clip 内按关键帧区间自适应细分 + gap 区中性水平线, 复杂度 O(关键帧)。
            var clips = track.Clips;
            if (clips == null || PixelsPerMs <= 0)
                return;

            double viewStartMs = ScrollOffsetX / Math.Max(0.001, PixelsPerMs);
            double viewEndMs = (ScrollOffsetX + w) / Math.Max(0.001, PixelsPerMs);
            if (viewEndMs <= viewStartMs)
                return;

            bool isBool = string.Equals(
                track.ValueType,
                "bool",
                StringComparison.OrdinalIgnoreCase
            );
            float neutral = Services.Motion.TrackValueEvaluator.ResolveNeutral(
                track.ValueType,
                track.NeutralValue
            );
            // 动画视图默认不显示 idle 幽灵曲线/填充 (独立待机编辑器接管), 除非显式开启 ShowIdleOverlaysInCurve
            var idle = ShowIdleOverlaysInCurve ? track.IdleLoop : null;
            bool hasIdle =
                idle != null && idle.Enabled && idle.Keyframes != null && idle.Keyframes.Count > 0;

            var viewClips = clips
                .Where(c => c.EndMs > viewStartMs && c.StartMs < viewEndMs)
                .OrderBy(c => c.StartMs)
                .ToList();

            // 整轨无覆盖: 一条贯穿视口的线 (overlay → 自由曲线; 有 idle → 幽灵循环曲线; 无 → 中性水平线)
            if (viewClips.Count == 0)
            {
                double ax = viewStartMs * PixelsPerMs - ScrollOffsetX;
                double bx = viewEndMs * PixelsPerMs - ScrollOffsetX;
                var overrideKfs = track.OverrideKeyframes;
                bool hasOverlay = overrideKfs != null && overrideKfs.Count > 0;

                // 统一求值: overlay > idle > neutral (与运行时一致)
                float EvalAt(double t) =>
                    Services.Motion.TrackValueEvaluator.Evaluate(
                        clips,
                        track.ValueType,
                        track.NeutralValue,
                        idle,
                        overrideKfs,
                        t
                    );

                // 幽灵画笔 (仅 idle 时用虚线半透明, 与 clip 实线区分)
                var ghostPen2 = new Pen(
                    new SolidColorBrush(
                        Color.FromArgb(90, trackColor.R, trackColor.G, trackColor.B)
                    ),
                    1.0,
                    DashStyle.Dash
                );
                var effPen = hasOverlay || !hasIdle ? curvePen : ghostPen2;
                if (hasOverlay || hasIdle)
                {
                    var gg = new StreamGeometry();
                    using (var gc2 = gg.Open())
                    {
                        int n = Math.Max(
                            2,
                            Math.Min(48, (int)((viewEndMs - viewStartMs) * PixelsPerMs / 8.0))
                        );
                        bool started = false;
                        for (int i = 0; i <= n; i++)
                        {
                            double t = viewStartMs + (viewEndMs - viewStartMs) * i / n;
                            float v = EvalAt(t);
                            if (isBool)
                                v = v >= 0.5f ? 1f : 0f;
                            double x = t * PixelsPerMs - ScrollOffsetX;
                            // 离屏样本: 结束当前图元, 避免直线连远点产生毛刺。
                            if (x < -20 || x > w + 20)
                            {
                                if (started)
                                {
                                    gc2.EndFigure(false);
                                    started = false;
                                }
                                continue;
                            }
                            double y = ValueToY(v, h);
                            if (!started)
                            {
                                gc2.BeginFigure(new Point(x, y), false);
                                started = true;
                            }
                            else
                                gc2.LineTo(new Point(x, y));
                        }
                        if (started)
                            gc2.EndFigure(false);
                    }
                    context.DrawGeometry(null, effPen, gg);
                }
                else
                {
                    double y0 = ValueToY(neutral, h);
                    context.DrawLine(
                        curvePen,
                        new Point(Math.Max(-20, ax), y0),
                        new Point(Math.Min(w + 20, bx), y0)
                    );
                }
                return;
            }

            var geometry = new StreamGeometry();
            var idleGeometry = new StreamGeometry();
            using (var gc = geometry.Open())
            using (var igc = idleGeometry.Open())
            {
                bool started = false;
                bool idleStarted = false;
                void Ensure(double ms, float v)
                {
                    double x = ms * PixelsPerMs - ScrollOffsetX;
                    // 离屏样本: 结束当前图元, 避免把远处两点用直线相连产生锯齿毛刺。
                    if (x < -20 || x > w + 20)
                    {
                        if (started)
                        {
                            gc.EndFigure(false);
                            started = false;
                        }
                        return;
                    }
                    double y = ValueToY(v, h);
                    if (!started)
                    {
                        gc.BeginFigure(new Point(x, y), false);
                        started = true;
                    }
                    else
                        gc.LineTo(new Point(x, y));
                }
                // 区间细分: 每约 6px 一个样本 (最多 24 点), 曲率平滑且开销小
                void Span(double fromMs, double toMs, Func<double, float> valueAt)
                {
                    if (toMs <= fromMs)
                    {
                        Ensure(fromMs, valueAt(fromMs));
                        return;
                    }
                    int n = Math.Max(2, Math.Min(24, (int)((toMs - fromMs) * PixelsPerMs / 6.0)));
                    for (int i = 0; i <= n; i++)
                    {
                        double t = fromMs + (toMs - fromMs) * i / n;
                        Ensure(t, valueAt(t));
                    }
                }
                // idle 幽灵段 (虚线, 单开图元)
                void EnsureIdle(double ms, float v)
                {
                    double x = ms * PixelsPerMs - ScrollOffsetX;
                    // 离屏样本: 结束当前图元, 避免虚线跨屏连接产生毛刺。
                    if (x < -20 || x > w + 20)
                    {
                        if (idleStarted)
                        {
                            igc.EndFigure(false);
                            idleStarted = false;
                        }
                        return;
                    }
                    double y = ValueToY(v, h);
                    if (!idleStarted)
                    {
                        igc.BeginFigure(new Point(x, y), false);
                        idleStarted = true;
                    }
                    else
                        igc.LineTo(new Point(x, y));
                }
                void SpanIdle(double fromMs, double toMs, Func<double, float> valueAt)
                {
                    if (toMs <= fromMs)
                    {
                        EnsureIdle(fromMs, valueAt(fromMs));
                        return;
                    }
                    int n = Math.Max(2, Math.Min(24, (int)((toMs - fromMs) * PixelsPerMs / 8.0)));
                    for (int i = 0; i <= n; i++)
                    {
                        double t = fromMs + (toMs - fromMs) * i / n;
                        EnsureIdle(t, valueAt(t));
                    }
                }

                double cursor = viewStartMs;
                // Overlay 覆盖关键帧 (空窗自由数值点, 绝对时间), 渲染优先于 idle 幽灵
                var overrideKfs = track.OverrideKeyframes;
                bool hasOverlay = overrideKfs != null && overrideKfs.Count > 0;
                // 统一空窗求值: overlay > idle > neutral (与运行时一致)
                float GapValueAt(double t) =>
                    Services.Motion.TrackValueEvaluator.Evaluate(
                        clips,
                        track.ValueType,
                        track.NeutralValue,
                        idle,
                        overrideKfs,
                        t
                    );

                foreach (var clip in viewClips)
                {
                    // gap: overlay → 实线自由曲线; 有 idle → 幽灵循环曲线; 无 → 中性水平线
                    if (clip.StartMs > cursor)
                    {
                        if (hasOverlay || hasIdle)
                        {
                            Func<double, float> sampler = hasOverlay
                                ? (t => isBool ? (GapValueAt(t) >= 0.5f ? 1f : 0f) : GapValueAt(t))
                                : (
                                    t =>
                                    {
                                        float v = Services.Motion.TrackValueEvaluator.Evaluate(
                                            clips,
                                            track.ValueType,
                                            track.NeutralValue,
                                            idle,
                                            t
                                        );
                                        return isBool ? (v >= 0.5f ? 1f : 0f) : v;
                                    }
                                );
                            // overlay 实线 / idle 幽灵曲线
                            if (hasOverlay)
                                Span(cursor, clip.StartMs, sampler);
                            else
                                SpanIdle(cursor, clip.StartMs, sampler);
                        }
                        else
                            Span(cursor, clip.StartMs, _ => neutral);
                    }
                    double clipStart = Math.Max(cursor, clip.StartMs);
                    cursor = Math.Max(cursor, clip.StartMs);
                    double clipEnd = Math.Max(clip.EndMs, clip.StartMs);
                    // 关键帧绝对时间基准必须是 clip 真实起点, 不能用裁剪后的 clipStart:
                    // clipStart 会被夹到视口左缘/前一 clip 末尾, 用它做基准会导致关键帧被错误地
                    // "重新锚定到视口左缘" — 拖动/缩放改变 viewStartMs 时轨迹形状随之漂移。
                    double clipTrueStart = clip.StartMs;
                    var kfs = clip.Keyframes;

                    if (kfs.Count == 0)
                    {
                        Span(clipStart, Math.Max(clipStart, clipEnd), _ => neutral);
                    }
                    else
                    {
                        double c0 = clipStart; // 视口内实际可见起点 (仅用于裁剪绘制范围)
                        double cEnd = clipEnd; // 视口内实际可见终点
                        // 首帧前 = 首值 (保持): 关键帧绝对时间一律以 clipTrueStart 为基准
                        double t0 = clipTrueStart + kfs[0].TimeMs;
                        if (t0 > c0)
                            Span(c0, Math.Min(t0, cEnd), _ => kfs[0].Value);
                        // 相邻关键帧间: 自适应细分 (float 插值 / bool 阶梯)
                        for (int i = 0; i < kfs.Count - 1; i++)
                        {
                            double ts = clipTrueStart + kfs[i].TimeMs;
                            double te = clipTrueStart + kfs[i + 1].TimeMs;
                            if (te <= ts)
                                continue;
                            double ss = Math.Max(ts, c0);
                            double ee = Math.Min(te, cEnd);
                            if (ee <= ss)
                                continue;
                            if (isBool)
                            {
                                float hv = kfs[i].Value >= 0.5f ? 1f : 0f;
                                Span(ss, ee, _ => hv);
                            }
                            else
                            {
                                Span(
                                    ss,
                                    ee,
                                    t =>
                                        Services.Motion.InterpolationEngine.Evaluate(
                                            kfs,
                                            t - clipTrueStart
                                        )
                                );
                            }
                        }
                        // 末帧后 = 末值 (保持到 clip 结束)
                        double tEnd = clipTrueStart + kfs[^1].TimeMs;
                        if (cEnd > tEnd)
                            Span(Math.Max(tEnd, c0), cEnd, _ => kfs[^1].Value);
                    }
                    cursor = Math.Max(cursor, clipEnd);
                }
                if (cursor < viewEndMs)
                {
                    if (hasOverlay || hasIdle)
                    {
                        Func<double, float> sampler = hasOverlay
                            ? (t => isBool ? (GapValueAt(t) >= 0.5f ? 1f : 0f) : GapValueAt(t))
                            : (
                                t =>
                                {
                                    float v = Services.Motion.TrackValueEvaluator.Evaluate(
                                        clips,
                                        track.ValueType,
                                        track.NeutralValue,
                                        idle,
                                        t
                                    );
                                    return isBool ? (v >= 0.5f ? 1f : 0f) : v;
                                }
                            );
                        if (hasOverlay)
                            Span(cursor, viewEndMs, sampler);
                        else
                            SpanIdle(cursor, viewEndMs, sampler);
                    }
                    else
                        Span(cursor, viewEndMs, _ => neutral);
                }
                if (started)
                    gc.EndFigure(false);
                if (idleStarted)
                    igc.EndFigure(false);
            }
            context.DrawGeometry(null, curvePen, geometry);
            if (hasIdle)
            {
                // idle 幽灵曲线: 虚线半透明 (区别于 clip 实线)
                var ghostPen = new Pen(
                    new SolidColorBrush(
                        Color.FromArgb(90, trackColor.R, trackColor.G, trackColor.B)
                    ),
                    1.0,
                    DashStyle.Dash
                );
                context.DrawGeometry(null, ghostPen, idleGeometry);
            }
        }

        // ─── idle 周期边界 + 首尾闭合警示 ───

        /// <summary>
        /// 在 idle 空窗区绘制周期边界竖虚线 (k*period), 并在首尾值不闭合时用黄色警示。
        /// 帮助用户直观确认"循环周期边界在哪""无缝性是否成立"。
        /// </summary>
        private void DrawIdleCycleBoundaries(
            DrawingContext context,
            CurveTrackData track,
            int trackIdx,
            double w,
            double h
        )
        {
            var idle = track.IdleLoop;
            if (
                idle == null || !idle.Enabled || idle.Keyframes == null || idle.Keyframes.Count == 0
            )
                return;
            double period = idle.PeriodMs > 1.0 ? idle.PeriodMs : 1.0;
            double ppm = PixelsPerMs > 0.001 ? PixelsPerMs : 0.001;
            double viewStart = ScrollOffsetX / ppm;
            double viewEnd = (ScrollOffsetX + w) / ppm;

            // 首尾闭合判定: 首关键帧值 vs 末关键帧值 差异 > 阈值 → 周期边界处跳变警示
            bool closureWarning = false;
            float warnThreshold = 0.05f;
            if (idle.Keyframes.Count >= 2)
            {
                float first = idle.Keyframes[0].Value;
                float last = idle.Keyframes[^1].Value;
                // bool 轨按 0/1 判定
                if (string.Equals(track.ValueType, "bool", StringComparison.OrdinalIgnoreCase))
                    closureWarning = Math.Abs(first - last) > 0.01f;
                else
                    closureWarning = Math.Abs(first - last) > warnThreshold;
            }

            var boundaryPen = new Pen(
                new SolidColorBrush(Color.FromArgb(50, 148, 163, 184)),
                1,
                DashStyle.Dash
            );
            var warnPen = new Pen(new SolidColorBrush(Color.Parse("#f59e0b")), 1.5, DashStyle.Dash);

            // 视口内所有周期边界 k*period
            long kMin = (long)Math.Floor(viewStart / period) - 1;
            long kMax = (long)Math.Ceiling(viewEnd / period) + 1;
            for (long k = kMin; k <= kMax; k++)
            {
                double t = k * period;
                double x = t * PixelsPerMs - ScrollOffsetX;
                if (x < -20 || x > w + 20)
                    continue;
                // 只在"无 clip 覆盖的空窗区"绘制 (周期边界是 idle 语义, 不应压在 clip 实线上)
                bool inAnyClip = false;
                foreach (var clip in track.Clips)
                {
                    if (t >= clip.StartMs && t <= clip.EndMs)
                    {
                        inAnyClip = true;
                        break;
                    }
                }
                if (inAnyClip)
                    continue;

                context.DrawLine(
                    closureWarning ? warnPen : boundaryPen,
                    new Point(x, 0),
                    new Point(x, h)
                );

                // 首尾不闭合时: 边界处画小警示标记 (双竖线提示"此处有跳变")
                if (closureWarning)
                {
                    context.DrawLine(
                        warnPen,
                        new Point(x - 3, GridPaddingTop),
                        new Point(x - 3, GridPaddingTop + 14)
                    );
                    context.DrawLine(
                        warnPen,
                        new Point(x + 3, GridPaddingTop),
                        new Point(x + 3, GridPaddingTop + 14)
                    );
                }
            }
        }

        // ─── idle ↔ clip 交叉淡化过渡带可视化 ───

        /// <summary>
        /// 在 idle.blend_ms > 0 时, 于每个 clip 边界绘制半透明淡化带:
        /// [startMs - blend, startMs] = idle→clip 淡入, [endMs, endMs + blend] = clip→idle 淡出。
        /// 帮助用户直观看到"过渡发生在哪里、多长"。
        /// </summary>
        private void DrawIdleBlendZones(
            DrawingContext context,
            CurveTrackData track,
            int trackIdx,
            double w,
            double h
        )
        {
            var idle = track.IdleLoop;
            if (idle == null || !idle.Enabled || idle.BlendMs <= 0 || idle.Keyframes == null)
                return;
            double blend = idle.BlendMs;
            double ppm = PixelsPerMs > 0.001 ? PixelsPerMs : 0.001;
            double viewStart = ScrollOffsetX / ppm;
            double viewEnd = (ScrollOffsetX + w) / ppm;

            var fadeBrush = new SolidColorBrush(Color.FromArgb(36, 148, 163, 184));
            var fadePen = new Pen(
                new SolidColorBrush(Color.FromArgb(80, 148, 163, 184)),
                1,
                DashStyle.Dash
            );

            foreach (var clip in track.Clips)
            {
                // 淡入带 [startMs - blend, startMs]
                double inL = clip.StartMs - blend;
                double inR = clip.StartMs;
                DrawBand(inL, inR, inR - inL, fadeBrush, fadePen, w);
                // 淡出带 [endMs, endMs + blend]
                double outL = clip.EndMs;
                double outR = clip.EndMs + blend;
                DrawBand(outL, outR, outR - outL, fadeBrush, fadePen, w);
            }

            void DrawBand(
                double fromMs,
                double toMs,
                double bandMs,
                IBrush brush,
                Pen pen,
                double cw
            )
            {
                if (bandMs <= 0 || toMs <= viewStart || fromMs >= viewEnd)
                    return;
                double x1 = Math.Max(fromMs, viewStart) * PixelsPerMs - ScrollOffsetX;
                double x2 = Math.Min(toMs, viewEnd) * PixelsPerMs - ScrollOffsetX;
                if (x2 <= x1)
                    return;
                context.DrawRectangle(
                    brush,
                    pen,
                    new Rect(x1, GridPaddingTop, x2 - x1, h - GridPaddingTop - GridPaddingBottom)
                );
            }
        }

        // ─── 关键帧标记 + 切线手柄 ───

        private void DrawTrackKeyframes(
            DrawingContext context,
            CurveTrackData track,
            int trackIdx,
            double w,
            double h
        )
        {
            var trackColor = Color.TryParse(track.Color, out var c) ? c : Color.Parse("#4FC3F7");
            var markerBrush = new SolidColorBrush(trackColor);
            var markerPen = new Pen(Brushes.White, 1.2);

            // idle 仅作为"查看参考"幽灵曲线保留, 不在动画曲线视图绘制/编辑其关键帧点 (见 DrawTrackCurve)。
            // 待机关键帧编辑已收敛到独立 IdleEditorWindow。

            // Overlay 覆盖关键帧 (空窗自由数值点): 圆形标记, 与 clip 菱形区分
            var overrideKfs = track.OverrideKeyframes;
            if (overrideKfs != null)
            {
                for (int oi = 0; oi < overrideKfs.Count; oi++)
                {
                    var okf = overrideKfs[oi];
                    double x = okf.TimeMs * PixelsPerMs - ScrollOffsetX;
                    if (x < -20 || x > w + 20)
                        continue;
                    bool isBool = string.Equals(
                        track.ValueType,
                        "bool",
                        StringComparison.OrdinalIgnoreCase
                    );
                    float ov = isBool ? (okf.Value >= 0.5f ? 1f : 0f) : okf.Value;
                    double y = ValueToY(ov, h);
                    double r = KfDiamondSize * 0.9;

                    bool isSelected = trackIdx == _selectedTrackIdx && oi == _selectedOverrideKfIdx;
                    var fill = isSelected
                        ? SelectedKfBrush
                        : new SolidColorBrush(
                            Color.FromArgb(220, trackColor.R, trackColor.G, trackColor.B)
                        );
                    context.DrawEllipse(fill, markerPen, new Rect(x - r, y - r, r * 2, r * 2));
                }
            }

            // 先计算每个 clip 的"实际绘制起点" (与 DrawTrackCurve 的 cursor 裁剪完全一致):
            // 重叠/被更早 clip 覆盖的区域, 曲线不会绘制 → 该区域内的关键帧也不应显示,
            // 否则会出现"孤立关键帧" (有菱形标记但无曲线穿过, 且拖动/缩放时随视口变化)。
            double effPpm = PixelsPerMs > 0.001 ? PixelsPerMs : 0.001;
            double effViewStartMs = ScrollOffsetX / effPpm;
            double effViewEndMs = (ScrollOffsetX + w) / effPpm;
            var orderedClips = track.Clips
                .Select((cp, idx) => (cp, idx))
                .Where(x => x.cp.EndMs > effViewStartMs && x.cp.StartMs < effViewEndMs)
                .OrderBy(x => x.cp.StartMs)
                .ToList();
            var effStartByClip = new Dictionary<int, double>();
            double effCursor = effViewStartMs;
            foreach (var (cp, idx) in orderedClips)
            {
                double cs = Math.Max(effCursor, cp.StartMs);
                effStartByClip[idx] = cs;
                effCursor = Math.Max(effCursor, cp.EndMs);
            }

            for (int ci = 0; ci < track.Clips.Count; ci++)
            {
                var clip = track.Clips[ci];
                double clipDrawnStart = effStartByClip.TryGetValue(ci, out var es)
                    ? es
                    : clip.StartMs;
                for (int ki = 0; ki < clip.Keyframes.Count; ki++)
                {
                    var kf = clip.Keyframes[ki];
                    double absTimeMs = clip.StartMs + kf.TimeMs;
                    // 关键帧落在"曲线未绘制的覆盖/裁剪区" → 跳过, 避免孤立标记
                    if (absTimeMs < clipDrawnStart - 0.5)
                        continue;
                    if (absTimeMs > clip.EndMs + 0.5)
                        continue;
                    double x = absTimeMs * PixelsPerMs - ScrollOffsetX;
                    if (x < -20 || x > w + 20)
                        continue;

                    bool isBool = string.Equals(
                        track.ValueType,
                        "bool",
                        StringComparison.OrdinalIgnoreCase
                    );
                    float drawValue = isBool ? (kf.Value >= 0.5f ? 1f : 0f) : kf.Value;
                    double y = ValueToY(drawValue, h);
                    double s = KfDiamondSize;

                    bool isSelected = (
                        trackIdx == _selectedTrackIdx
                        && ci == _selectedClipIdx
                        && ki == _selectedKfIdx
                    );
                    bool isMultiSelected = _multiSelectedKfs.Contains((trackIdx, ci, ki));

                    // 贝塞尔切线手柄 (仅选中的 bezier 关键帧显示)
                    if (
                        (isSelected || isMultiSelected)
                        && string.Equals(
                            kf.Interpolation,
                            "bezier",
                            StringComparison.OrdinalIgnoreCase
                        )
                        && !isBool
                    )
                    {
                        DrawTangentHandles(context, x, y, kf, h);
                    }

                    // 菱形关键帧
                    var diamond = new StreamGeometry();
                    using (var gc = diamond.Open())
                    {
                        gc.BeginFigure(new Point(x, y - s), true);
                        gc.LineTo(new Point(x + s, y));
                        gc.LineTo(new Point(x, y + s));
                        gc.LineTo(new Point(x - s, y));
                        gc.EndFigure(true);
                    }

                    if (isMultiSelected)
                        context.DrawGeometry(
                            new SolidColorBrush(Color.Parse("#60a5fa")),
                            new Pen(Brushes.White, 2),
                            diamond
                        );
                    else if (isSelected)
                        context.DrawGeometry(SelectedKfBrush, SelectedKfPen, diamond);
                    else
                        context.DrawGeometry(markerBrush, markerPen, diamond);
                }
            }
        }

        // ─── 贝塞尔切线手柄 ───

        private void DrawTangentHandles(
            DrawingContext context,
            double kfX,
            double kfY,
            MotionKeyframe kf,
            double viewH
        )
        {
            // TangentIn: 左侧手柄 (支持水平距离系数)
            double inX = kfX - (kf.Cp1x ?? 1f) * TangentHandleLength;
            double inY = kfY + (kf.TangentIn ?? 0f) * TangentHandleLength;

            // TangentOut: 右侧手柄 (支持水平距离系数)
            double outX = kfX + (kf.Cp2x ?? 1f) * TangentHandleLength;
            double outY = kfY - (kf.TangentOut ?? 0f) * TangentHandleLength;

            // 连接线
            context.DrawLine(HandleLinePen, new Point(inX, inY), new Point(kfX, kfY));
            context.DrawLine(HandleLinePen, new Point(kfX, kfY), new Point(outX, outY));

            // 手柄圆点
            context.DrawEllipse(HandleBrush, null, new Point(inX, inY), HandleRadius, HandleRadius);
            context.DrawEllipse(
                HandleBrush,
                null,
                new Point(outX, outY),
                HandleRadius,
                HandleRadius
            );
        }

        // ═══════ 坐标转换 ═══════

        private double ValueToY(float value, double viewH)
        {
            double usableH = viewH - GridPaddingTop - GridPaddingBottom;
            if (usableH <= 0)
                return viewH / 2;
            double normalized = (value - (float)ValueOffset) * ValueZoom;
            return viewH - GridPaddingBottom - (normalized * usableH);
        }

        private float YToValue(double y, double viewH)
        {
            double usableH = viewH - GridPaddingTop - GridPaddingBottom;
            if (usableH <= 0)
                return 0.5f;
            double normalized = (viewH - GridPaddingBottom - y) / usableH;
            float value = (float)(normalized / ValueZoom + ValueOffset);
            return Math.Clamp(value, 0f, 1f);
        }

        private double TimeMsToX(double timeMs) => timeMs * PixelsPerMs - ScrollOffsetX;

        private double XToTimeMs(double x) => (ScrollOffsetX + x) / PixelsPerMs;
    }
}
