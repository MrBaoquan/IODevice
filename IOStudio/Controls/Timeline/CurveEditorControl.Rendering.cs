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
        /// <summary>判断轨道是否应该被隐藏 (禁用/静音/非Solo/ShowInCurve=false)</summary>
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

            foreach (var clip in track.Clips)
            {
                if (clip.Keyframes.Count < 2)
                    continue;

                bool isBool = string.Equals(
                    track.ValueType,
                    "bool",
                    StringComparison.OrdinalIgnoreCase
                );

                var geometry = new StreamGeometry();
                using (var gc = geometry.Open())
                {
                    bool started = false;
                    // 渲染范围应覆盖最后一个关键帧 (拖拽超出 clip.EndMs 时仍能绘制曲线)
                    double clipDuration = clip.EndMs - clip.StartMs;
                    if (clip.Keyframes.Count > 0)
                    {
                        double maxKfTime = clip.Keyframes[clip.Keyframes.Count - 1].TimeMs;
                        if (maxKfTime > clipDuration)
                            clipDuration = maxKfTime;
                    }
                    int sampleCount = Math.Min(
                        600,
                        Math.Max(60, (int)(clipDuration * PixelsPerMs / 2))
                    );
                    double step = clipDuration / sampleCount;

                    for (int i = 0; i <= sampleCount; i++)
                    {
                        double localMs = i * step;
                        float value;

                        if (isBool)
                        {
                            // Bool: step 插值
                            var lastKf = clip.Keyframes.LastOrDefault(kf => kf.TimeMs <= localMs);
                            value = lastKf != null ? (lastKf.Value >= 0.5f ? 1f : 0f) : 0f;
                        }
                        else
                        {
                            value = Services.Motion.InterpolationEngine.Evaluate(
                                clip.Keyframes,
                                localMs
                            );
                        }

                        double x = (clip.StartMs + localMs) * PixelsPerMs - ScrollOffsetX;
                        double y = ValueToY(value, h);

                        if (x < -20)
                            continue;
                        if (x > w + 20)
                            break;

                        if (!started)
                        {
                            gc.BeginFigure(new Point(x, y), false);
                            started = true;
                        }
                        else
                            gc.LineTo(new Point(x, y));
                    }
                    if (started)
                        gc.EndFigure(false);
                }
                context.DrawGeometry(null, curvePen, geometry);
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

            for (int ci = 0; ci < track.Clips.Count; ci++)
            {
                var clip = track.Clips[ci];
                for (int ki = 0; ki < clip.Keyframes.Count; ki++)
                {
                    var kf = clip.Keyframes[ki];
                    double absTimeMs = clip.StartMs + kf.TimeMs;
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
