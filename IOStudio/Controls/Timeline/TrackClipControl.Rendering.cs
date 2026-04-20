using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;

namespace IOStudio.Controls.Timeline
{
    public partial class TrackClipControl
    {
        // ═══════ 渲染 ═══════

        public override void Render(DrawingContext context)
        {
            var bounds = Bounds;
            double w = bounds.Width;
            double h = bounds.Height;
            double ppm = PixelsPerMs;

            context.DrawRectangle(EmptyBrush, null, new Rect(0, 0, w, h));
            context.DrawLine(GridPen, new Point(0, h - 0.5), new Point(w, h - 0.5));

            if (IsTrackSelected)
                context.DrawRectangle(null, SelectedBorderPen, new Rect(1, 1, w - 2, h - 2));

            if (ppm <= 0)
                return;

            var clips = Clips;
            var nearestKf = FindNearestKeyframeToPlayhead();

            if (clips != null)
            {
                if (ViewMode == TimelineViewMode.Dopesheet)
                    RenderDopesheetClips(context, clips, w, h, ppm, nearestKf);
                else
                {
                    for (int i = 0; i < clips.Count; i++)
                        DrawClip(context, clips[i], i, w, h, ppm, nearestKf);
                }
            }

            // 播放头
            double phX = CurrentTimeMs * ppm - ScrollOffsetX;
            if (phX >= 0 && phX <= w)
                context.DrawLine(PlayheadPen, new Point(phX, 0), new Point(phX, h));

            // 吸附参考线
            if (_isDraggingKf && _snapTargetTimeMs.HasValue)
            {
                double sx = _snapTargetTimeMs.Value * ppm - ScrollOffsetX;
                if (sx >= 0 && sx <= w)
                {
                    context.DrawLine(SnapGuidePen, new Point(sx, 0), new Point(sx, h));
                    var ft = new FormattedText(
                        TimeSpan.FromMilliseconds(_snapTargetTimeMs.Value).ToString(@"mm\:ss\.f"),
                        System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Consolas"),
                        9,
                        Brushes.White
                    );
                    double lx = sx - ft.Width / 2;
                    context.DrawRectangle(
                        SnapLabelBg,
                        null,
                        new Rect(lx - 2, 1, ft.Width + 4, ft.Height + 2),
                        2,
                        2
                    );
                    context.DrawText(ft, new Point(lx, 2));
                }
            }

            // 框选矩形
            if (_isBoxSelecting)
            {
                double bx = Math.Min(_boxSelectStart.X, _boxSelectCurrent.X);
                double by = Math.Min(_boxSelectStart.Y, _boxSelectCurrent.Y);
                double bw = Math.Abs(_boxSelectCurrent.X - _boxSelectStart.X);
                double bh = Math.Abs(_boxSelectCurrent.Y - _boxSelectStart.Y);
                if (bw > 2 || bh > 2)
                {
                    var boxFill = new SolidColorBrush(Color.Parse("#203b82f6"));
                    var boxPen = new Pen(
                        new SolidColorBrush(Color.Parse("#3b82f6")),
                        1,
                        DashStyle.Dash
                    );
                    context.DrawRectangle(boxFill, boxPen, new Rect(bx, by, bw, bh));
                }
            }

            // 静音覆盖
            if (IsMuted)
                context.DrawRectangle(MutedOverlayBrush, null, new Rect(0, 0, w, h));
        }

        private void DrawClip(
            DrawingContext context,
            MotionClip clip,
            int clipIdx,
            double viewW,
            double viewH,
            double ppm,
            (int clipIdx, int kfIdx) nearestKf
        )
        {
            double x1 = clip.StartMs * ppm - ScrollOffsetX;
            double x2 = clip.EndMs * ppm - ScrollOffsetX;
            if (x2 < 0 || x1 > viewW)
                return;

            double left = Math.Max(0, x1);
            double right = Math.Min(viewW, x2);
            double clipW = right - left;

            Color.TryParse(TrackColor, out var trackColor);
            if (trackColor == default)
                trackColor = Color.Parse("#4FC3F7");

            var clipFill = new SolidColorBrush(
                Color.FromArgb(30, trackColor.R, trackColor.G, trackColor.B)
            );
            var clipBorder = new SolidColorBrush(
                Color.FromArgb(80, trackColor.R, trackColor.G, trackColor.B)
            );
            var clipPen = new Pen(clipBorder, 1);
            context.DrawRectangle(clipFill, clipPen, new Rect(left, 2, clipW, viewH - 4), 3, 3);

            if (clip.Keyframes.Count >= 2)
                DrawKeyframeCurve(context, clip, viewW, viewH, ppm, trackColor);

            DrawKeyframeMarkers(context, clip, clipIdx, viewH, ppm, viewW, trackColor, nearestKf);
        }

        private void DrawKeyframeCurve(
            DrawingContext context,
            MotionClip clip,
            double viewW,
            double viewH,
            double ppm,
            Color trackColor
        )
        {
            var curveColor = new SolidColorBrush(
                Color.FromArgb(200, trackColor.R, trackColor.G, trackColor.B)
            );
            var curvePen = new Pen(curveColor, 1.5);

            if (string.Equals(ValueType, "bool", StringComparison.OrdinalIgnoreCase))
                DrawBoolStepCurve(context, clip, viewW, viewH, ppm, curvePen);
            else
                DrawSmoothCurve(context, clip, viewW, viewH, ppm, curvePen);
        }

        private void DrawBoolStepCurve(
            DrawingContext context,
            MotionClip clip,
            double viewW,
            double viewH,
            double ppm,
            IPen curvePen
        )
        {
            var kfs = clip.Keyframes;
            if (kfs.Count < 2)
                return;

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                bool started = false;
                for (int i = 0; i < kfs.Count; i++)
                {
                    var kf = kfs[i];
                    double x = (clip.StartMs + kf.TimeMs) * ppm - ScrollOffsetX;
                    double y = ValueToY(kf.Value >= 0.5f ? 1f : 0f, viewH);
                    if (x > viewW + 10)
                        break;

                    if (!started)
                    {
                        ctx.BeginFigure(new Point(x, y), false);
                        started = true;
                    }
                    else
                    {
                        double prevY = ValueToY(kfs[i - 1].Value >= 0.5f ? 1f : 0f, viewH);
                        ctx.LineTo(new Point(x, prevY));
                        ctx.LineTo(new Point(x, y));
                    }
                }
                // 延伸到最后
                if (started && kfs.Count > 0)
                {
                    var last = kfs[kfs.Count - 1];
                    double endX = clip.EndMs * ppm - ScrollOffsetX;
                    double lastY = ValueToY(last.Value >= 0.5f ? 1f : 0f, viewH);
                    if (endX > (clip.StartMs + last.TimeMs) * ppm - ScrollOffsetX)
                        ctx.LineTo(new Point(endX, lastY));
                }
                if (started)
                    ctx.EndFigure(false);
            }
            context.DrawGeometry(null, curvePen, geo);
        }

        private void DrawSmoothCurve(
            DrawingContext context,
            MotionClip clip,
            double viewW,
            double viewH,
            double ppm,
            IPen curvePen
        )
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                bool started = false;
                var kfs = clip.Keyframes;
                double dur = clip.EndMs - clip.StartMs;
                int steps = Math.Min(500, Math.Max(50, (int)(dur * ppm / 3.0)));
                double dt = dur / steps;

                for (int i = 0; i <= steps; i++)
                {
                    double t = i * dt;
                    float val = InterpolationEngine.Evaluate(kfs, t);
                    double x = (clip.StartMs + t) * ppm - ScrollOffsetX;
                    double y = ValueToY(val, viewH);
                    if (x < -10)
                        continue;
                    if (x > viewW + 10)
                        break;

                    if (!started)
                    {
                        ctx.BeginFigure(new Point(x, y), false);
                        started = true;
                    }
                    else
                        ctx.LineTo(new Point(x, y));
                }
                if (started)
                    ctx.EndFigure(false);
            }
            context.DrawGeometry(null, curvePen, geo);
        }

        private void DrawKeyframeMarkers(
            DrawingContext context,
            MotionClip clip,
            int clipIdx,
            double viewH,
            double ppm,
            double viewW,
            Color trackColor,
            (int clipIdx, int kfIdx) nearestKf
        )
        {
            var kfBrush = new SolidColorBrush(trackColor);
            var kfPen = new Pen(Brushes.White, 1.5);

            for (int i = 0; i < clip.Keyframes.Count; i++)
            {
                var kf = clip.Keyframes[i];
                double x = (clip.StartMs + kf.TimeMs) * ppm - ScrollOffsetX;
                if (x < -10 || x > viewW + 10)
                    continue;

                float drawVal = string.Equals(ValueType, "bool", StringComparison.OrdinalIgnoreCase)
                    ? (kf.Value >= 0.5f ? 1f : 0f)
                    : kf.Value;
                double y = ValueToY(drawVal, viewH);
                double sz = KeyframeDiamondSize;

                bool isSel = clipIdx == _selectedClipIdx && i == _selectedKfIdx;
                bool isMulti = _multiSelectedKeyframes.Contains((clipIdx, i));
                bool isNear = clipIdx == nearestKf.clipIdx && i == nearestKf.kfIdx;

                // 菱形路径
                var diamond = new StreamGeometry();
                using (var dc = diamond.Open())
                {
                    dc.BeginFigure(new Point(x, y - sz), true);
                    dc.LineTo(new Point(x + sz, y));
                    dc.LineTo(new Point(x, y + sz));
                    dc.LineTo(new Point(x - sz, y));
                    dc.EndFigure(true);
                }

                if (isMulti)
                {
                    context.DrawGeometry(MultiSelectedKfBrush, MultiSelectedKfPen, diamond);
                }
                else if (isNear && !isSel)
                {
                    // 播放头附近 — 光晕
                    double glowSz = sz + 4;
                    var glow = new StreamGeometry();
                    using (var gc = glow.Open())
                    {
                        gc.BeginFigure(new Point(x, y - glowSz), true);
                        gc.LineTo(new Point(x + glowSz, y));
                        gc.LineTo(new Point(x, y + glowSz));
                        gc.LineTo(new Point(x - glowSz, y));
                        gc.EndFigure(true);
                    }
                    context.DrawGeometry(PlayheadNearKfGlow, null, glow);
                    context.DrawGeometry(PlayheadNearKfBrush, PlayheadNearKfPen, diamond);
                }
                else if (isSel)
                {
                    context.DrawGeometry(SelectedKfBrush, SelectedKfPen, diamond);
                }
                else
                {
                    context.DrawGeometry(kfBrush, kfPen, diamond);
                }
            }
        }

        private void RenderDopesheetClips(
            DrawingContext context,
            List<MotionClip> clips,
            double w,
            double h,
            double ppm,
            (int clipIdx, int kfIdx) nearestKf
        )
        {
            double centerY = h / 2.0;
            Color.TryParse(TrackColor, out var trackColor);
            if (trackColor == default)
                trackColor = Color.Parse("#4FC3F7");

            // 水平中心线
            var centerPen = new Pen(
                new SolidColorBrush(Color.FromArgb(50, trackColor.R, trackColor.G, trackColor.B)),
                1
            );
            context.DrawLine(centerPen, new Point(0, centerY), new Point(w, centerY));

            for (int ci = 0; ci < clips.Count; ci++)
            {
                var clip = clips[ci];
                double x1 = clip.StartMs * ppm - ScrollOffsetX;
                double x2 = clip.EndMs * ppm - ScrollOffsetX;
                if (x2 < 0 || x1 > w)
                    continue;

                double left = Math.Max(0, x1);
                double right = Math.Min(w, x2);

                // 淡色片段背景
                var clipBg = new SolidColorBrush(
                    Color.FromArgb(12, trackColor.R, trackColor.G, trackColor.B)
                );
                context.DrawRectangle(clipBg, null, new Rect(left, 2, right - left, h - 4), 2, 2);

                // 关键帧之间的连线
                if (clip.Keyframes.Count >= 2)
                {
                    var linePen = new Pen(
                        new SolidColorBrush(
                            Color.FromArgb(70, trackColor.R, trackColor.G, trackColor.B)
                        ),
                        1.5
                    );
                    double lx1 = Math.Max(
                        -10,
                        (clip.StartMs + clip.Keyframes[0].TimeMs) * ppm - ScrollOffsetX
                    );
                    double lx2 = Math.Min(
                        w + 10,
                        (clip.StartMs + clip.Keyframes[clip.Keyframes.Count - 1].TimeMs) * ppm
                            - ScrollOffsetX
                    );
                    context.DrawLine(linePen, new Point(lx1, centerY), new Point(lx2, centerY));
                }

                // 关键帧菱形
                var kfBrush = new SolidColorBrush(trackColor);
                var kfPen = new Pen(Brushes.White, 1.5);
                double dSz = 7;

                for (int ki = 0; ki < clip.Keyframes.Count; ki++)
                {
                    var kf = clip.Keyframes[ki];
                    double kx = (clip.StartMs + kf.TimeMs) * ppm - ScrollOffsetX;
                    if (kx < -10 || kx > w + 10)
                        continue;

                    bool isSel = ci == _selectedClipIdx && ki == _selectedKfIdx;
                    bool isMulti = _multiSelectedKeyframes.Contains((ci, ki));
                    bool isNear = ci == nearestKf.clipIdx && ki == nearestKf.kfIdx;

                    var diamond = new StreamGeometry();
                    using (var dc = diamond.Open())
                    {
                        dc.BeginFigure(new Point(kx, centerY - dSz), true);
                        dc.LineTo(new Point(kx + dSz, centerY));
                        dc.LineTo(new Point(kx, centerY + dSz));
                        dc.LineTo(new Point(kx - dSz, centerY));
                        dc.EndFigure(true);
                    }

                    if (isMulti)
                    {
                        context.DrawGeometry(MultiSelectedKfBrush, MultiSelectedKfPen, diamond);
                    }
                    else if (isNear && !isSel)
                    {
                        double glowSz = dSz + 4;
                        var glow = new StreamGeometry();
                        using (var gc = glow.Open())
                        {
                            gc.BeginFigure(new Point(kx, centerY - glowSz), true);
                            gc.LineTo(new Point(kx + glowSz, centerY));
                            gc.LineTo(new Point(kx, centerY + glowSz));
                            gc.LineTo(new Point(kx - glowSz, centerY));
                            gc.EndFigure(true);
                        }
                        context.DrawGeometry(PlayheadNearKfGlow, null, glow);
                        context.DrawGeometry(PlayheadNearKfBrush, PlayheadNearKfPen, diamond);
                    }
                    else if (isSel)
                    {
                        context.DrawGeometry(SelectedKfBrush, SelectedKfPen, diamond);
                    }
                    else
                    {
                        context.DrawGeometry(kfBrush, kfPen, diamond);
                    }
                }
            }
        }
    }
}
