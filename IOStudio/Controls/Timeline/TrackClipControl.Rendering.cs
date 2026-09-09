using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;

namespace IOStudio.Controls.Timeline
{
    public partial class TrackClipControl
    {
        // ═══════ 渲染 ═══════

        public override void Render(DrawingContext context)
        {
            RenderCore(context);
        }

        private void RenderCore(DrawingContext context)
        {
            var bounds = Bounds;
            double w = bounds.Width;
            double h = bounds.Height;
            double ppm = PixelsPerMs;

            context.DrawRectangle(
                IsDarkTheme ? EmptyDarkBrush : EmptyLightBrush,
                null,
                new Rect(0, 0, w, h)
            );
            context.DrawLine(
                IsDarkTheme ? GridDarkPen : GridLightPen,
                new Point(0, h - 0.5),
                new Point(w, h - 0.5)
            );

            // An action instance is the primary selection across every bound track.
            // Suppress the single-track outline so the two selection models do not compete.
            if (IsTrackSelected && string.IsNullOrWhiteSpace(SelectedActionInstanceId))
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

            DrawDirectManipulationPreview(context, w, h, ppm);

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

        private void DrawDirectManipulationPreview(
            DrawingContext context,
            double width,
            double height,
            double ppm
        )
        {
            // 跨轨道共享拖拽预览: 本轨包含共享实例但非发起轨道 → 画共享预览框 (发起轨道走 _isDraggingAction)
            if (
                !_isDraggingAction
                && !_isPresetDragOver
                && !(_isPreparingActionResize || _isResizingAction)
                && ContainsSharedDragPreviewInstance()
            )
            {
                var shared = GetSharedDragPreview();
                if (shared.HasValue)
                {
                    double sharedX = shared.Value.StartMs * ppm - ScrollOffsetX;
                    double pw = Math.Max(8, shared.Value.DurationMs * ppm);
                    var sharedAccent = Color.Parse("#0d9488");
                    var sharedPen = new Pen(new SolidColorBrush(sharedAccent), 1.5, DashStyle.Dash);
                    var sharedFill = new SolidColorBrush(
                        Color.FromArgb(26, sharedAccent.R, sharedAccent.G, sharedAccent.B)
                    );
                    context.DrawRectangle(
                        sharedFill,
                        sharedPen,
                        new Rect(
                            sharedX,
                            2,
                            Math.Min(pw, Math.Max(0, width - sharedX)),
                            Math.Max(0, height - 4)
                        ),
                        2,
                        2
                    );
                }
                return;
            }

            if (
                !_isDraggingAction
                && !_isPresetDragOver
                && !(_isPreparingActionResize || _isResizingAction)
            )
                return;

            // 边界拉伸预览: 虚线框 + 起止/时长标签, 覆盖 Dopesheet 与曲线两种视图。
            // 冲突时红框提示; 无缝衔接时显示边界值跳变预警。
            if (_isPreparingActionResize || _isResizingAction)
            {
                double newStart = _resizePreviewClipStartMs;
                double newEnd = _resizePreviewClipEndMs;
                double x0 = newStart * ppm - ScrollOffsetX;
                double x1 = newEnd * ppm - ScrollOffsetX;
                bool conflict = IsRangeConflicting(newStart, newEnd, _dragResizeInstanceId);
                var warn = GetBoundaryValueWarning(
                    newStart,
                    newEnd,
                    _resizeClipIdx,
                    _dragResizeInstanceId
                );

                var resizeAccent = Color.Parse(conflict ? "#dc2626" : "#0d9488");
                var resizePen = new Pen(new SolidColorBrush(resizeAccent), 1.5, DashStyle.Dash);
                context.DrawRectangle(
                    null,
                    resizePen,
                    new Rect(x0, 1, Math.Max(1, x1 - x0), Math.Max(0, height - 2)),
                    3,
                    3
                );
                string resizeLabelText =
                    $"{newStart / 1000.0:F2}s → {newEnd / 1000.0:F2}s  ({(newEnd - newStart) / 1000.0:F2}s)";
                if (conflict)
                    resizeLabelText += " · 冲突";
                else if (!string.IsNullOrEmpty(warn))
                    resizeLabelText += $" · {warn}";
                var resizeLabel = new FormattedText(
                    resizeLabelText,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Consolas", FontStyle.Normal, FontWeight.SemiBold),
                    10,
                    Brushes.White
                );
                double resizeLabelX = Math.Max(2, Math.Min(width - resizeLabel.Width - 8, x0));
                context.DrawRectangle(
                    new SolidColorBrush(resizeAccent),
                    null,
                    new Rect(
                        resizeLabelX,
                        Math.Max(2, height - resizeLabel.Height - 6),
                        resizeLabel.Width + 8,
                        resizeLabel.Height + 4
                    ),
                    2,
                    2
                );
                context.DrawText(
                    resizeLabel,
                    new Point(resizeLabelX + 4, Math.Max(4, height - resizeLabel.Height - 4))
                );
                return;
            }

            double startMs = _isDraggingAction
                ? _dragActionPreviewStartMs
                : _presetDropPreviewTimeMs;
            double durationMs = _isDraggingAction
                ? _dragActionDurationMs
                : _presetDropPreviewDurationMs;
            double x = startMs * ppm - ScrollOffsetX;
            double previewWidth = Math.Max(8, durationMs * ppm);
            bool moveConflict =
                _isDraggingAction
                && IsRangeConflicting(startMs, startMs + durationMs, _dragActionInstanceId);
            var accent = Color.Parse(
                moveConflict
                    ? "#dc2626"
                    : _isDraggingAction
                        ? "#0f766e"
                        : "#2563eb"
            );
            var fill = new SolidColorBrush(Color.FromArgb(38, accent.R, accent.G, accent.B));
            var pen = new Pen(new SolidColorBrush(accent), 1.5, DashStyle.Dash);
            context.DrawRectangle(
                fill,
                pen,
                new Rect(
                    x,
                    2,
                    Math.Min(previewWidth, Math.Max(0, width - x)),
                    Math.Max(0, height - 4)
                ),
                2,
                2
            );

            string moveLabelText = TimeSpan.FromMilliseconds(startMs).ToString(@"mm\:ss\.fff");
            if (moveConflict)
                moveLabelText += " · 冲突";
            var label = new FormattedText(
                moveLabelText,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Consolas", FontStyle.Normal, FontWeight.SemiBold),
                10,
                Brushes.White
            );
            double labelX = Math.Max(2, Math.Min(width - label.Width - 8, x));
            context.DrawRectangle(
                new SolidColorBrush(accent),
                null,
                new Rect(
                    labelX,
                    Math.Max(2, height - label.Height - 6),
                    label.Width + 8,
                    label.Height + 4
                ),
                2,
                2
            );
            context.DrawText(label, new Point(labelX + 4, Math.Max(4, height - label.Height - 4)));
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
            // 拉伸预览: 用预览边界 + 按比例缩放的关键帧构造临时绘制对象, 拖拽过程实时反馈, 松手前不修改真实数据。
            MotionClip drawClip = clip;
            double edgeX1 = clip.StartMs * ppm - ScrollOffsetX;
            double edgeX2 = clip.EndMs * ppm - ScrollOffsetX;
            if (
                (_isPreparingActionResize || _isResizingAction)
                && clip.ActionInstanceId != null
                && clip.ActionInstanceId == _dragResizeInstanceId
            )
            {
                double oldDur = Math.Max(1, clip.EndMs - clip.StartMs);
                double newStart = _resizePreviewClipStartMs;
                double newEnd = _resizePreviewClipEndMs;
                double newDur = Math.Max(1, newEnd - newStart);
                double scale = newDur / oldDur;
                drawClip = new MotionClip
                {
                    StartMs = newStart,
                    EndMs = newEnd,
                    Keyframes = clip.Keyframes
                        .Select(
                            k =>
                                new MotionKeyframe
                                {
                                    TimeMs = k.TimeMs * scale,
                                    Value = k.Value,
                                    Interpolation = k.Interpolation,
                                    TangentIn = k.TangentIn,
                                    TangentOut = k.TangentOut,
                                    Cp1x = k.Cp1x,
                                    Cp2x = k.Cp2x,
                                    Event = k.Event,
                                }
                        )
                        .ToList(),
                };
                edgeX1 = newStart * ppm - ScrollOffsetX;
                edgeX2 = newEnd * ppm - ScrollOffsetX;
            }

            double x1 = drawClip.StartMs * ppm - ScrollOffsetX;
            double x2 = drawClip.EndMs * ppm - ScrollOffsetX;
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

            DrawActionIdentity(context, drawClip, left, right, viewH);

            // 单关键帧: 画满 clip 的常值水平线, 避免数值线断裂
            if (drawClip.Keyframes.Count == 1)
            {
                double y = ValueToY(drawClip.Keyframes[0].Value, viewH);
                var kfPen = new Pen(
                    new SolidColorBrush(
                        Color.FromArgb(200, trackColor.R, trackColor.G, trackColor.B)
                    ),
                    1.5
                );
                context.DrawLine(kfPen, new Point(left, y), new Point(right, y));
            }
            else if (drawClip.Keyframes.Count >= 2)
                DrawKeyframeCurve(context, drawClip, viewW, viewH, ppm, trackColor);

            DrawKeyframeMarkers(
                context,
                drawClip,
                clipIdx,
                viewH,
                ppm,
                viewW,
                trackColor,
                nearestKf
            );

            // 动作实例 clip: 左右边缘绘制可拖拽手柄 (边界剪裁)
            if (clip.ActionInstanceId is { Length: > 0 })
                DrawActionClipEdges(context, edgeX1, edgeX2, viewH);
        }

        /// <summary>动作实例 clip 左右边缘手柄, 提示可拖拽调整边界。</summary>
        private void DrawActionClipEdges(DrawingContext context, double x1, double x2, double viewH)
        {
            const double w = 4;
            var handleFill = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255));
            var handlePen = new Pen(new SolidColorBrush(Color.FromArgb(230, 180, 180, 180)), 1);
            double maxX = Bounds.Width;
            if (x1 > -w && x1 < maxX)
                context.DrawRectangle(handleFill, handlePen, new Rect(x1 - w / 2, 2, w, viewH - 4));
            if (x2 > -w && x2 < maxX)
                context.DrawRectangle(handleFill, handlePen, new Rect(x2 - w / 2, 2, w, viewH - 4));
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
                DrawActionIdentity(context, clip, left, right, h);

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

        private void DrawActionIdentity(
            DrawingContext context,
            MotionClip clip,
            double left,
            double right,
            double height
        )
        {
            if (!clip.IsGeneratedFromAction || right <= left)
                return;

            string identityKey = string.IsNullOrWhiteSpace(clip.SourceDefinitionId)
                ? clip.ActionInstanceId!
                : clip.SourceDefinitionId;
            var color = GetActionIdentityColor(identityKey);
            bool isSelected = string.Equals(
                SelectedActionInstanceId,
                clip.ActionInstanceId,
                StringComparison.Ordinal
            );
            byte chromeAlpha =
                SelectedActionInstanceId is null || isSelected ? (byte)255 : (byte)105;
            var brush = new SolidColorBrush(Color.FromArgb(chromeAlpha, color.R, color.G, color.B));
            double bandHeight = Math.Min(ActionBandHeight, Math.Max(0, height - 4));
            var bandFill = new SolidColorBrush(
                Color.FromArgb(isSelected ? (byte)54 : (byte)24, color.R, color.G, color.B)
            );
            context.DrawRectangle(
                bandFill,
                null,
                new Rect(left, 2, right - left, bandHeight),
                2,
                2
            );
            context.DrawRectangle(
                brush,
                null,
                new Rect(left, 2, right - left, isSelected ? 3 : 2),
                2,
                2
            );
            context.DrawRectangle(
                brush,
                null,
                new Rect(left, 2, isSelected ? 4 : 3, Math.Max(0, height - 4))
            );

            if (isSelected)
            {
                var selectionFill = new SolidColorBrush(
                    Color.FromArgb(24, color.R, color.G, color.B)
                );
                var selectionPen = new Pen(new SolidColorBrush(color), 1.5);
                context.DrawRectangle(
                    selectionFill,
                    selectionPen,
                    new Rect(
                        left + 0.75,
                        2.75,
                        Math.Max(0, right - left - 1.5),
                        Math.Max(0, height - 5.5)
                    ),
                    2,
                    2
                );
            }

            double available = right - left - 12;
            if (available < 34 || height < 26)
                return;

            string label = clip.SourceActionName ?? "动作";
            if (!string.IsNullOrWhiteSpace(clip.SourceRole) && available > 110)
                label += $" · {clip.SourceRole}";

            var typeface = new Typeface("Segoe UI", FontStyle.Normal, FontWeight.SemiBold);
            var textBrush = new SolidColorBrush(
                isSelected ? Color.Parse("#172033") : Color.Parse("#475569")
            );
            var text = CreateFittedText(label, available, typeface, textBrush);
            context.DrawText(text, new Point(left + 8, 3));
        }

        private static FormattedText CreateFittedText(
            string value,
            double maxWidth,
            Typeface typeface,
            IBrush brush
        )
        {
            string candidate = value;
            var formatted = CreateText(candidate, typeface, brush);
            while (formatted.Width > maxWidth && candidate.Length > 2)
            {
                candidate = candidate[..^1];
                formatted = CreateText(candidate + "…", typeface, brush);
            }
            return formatted;
        }

        private static FormattedText CreateText(string value, Typeface typeface, IBrush brush) =>
            new(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 9, brush);

        private static Color GetActionIdentityColor(string id)
        {
            Color[] palette =
            {
                Color.Parse("#0f766e"),
                Color.Parse("#2563eb"),
                Color.Parse("#7c3aed"),
                Color.Parse("#c2410c"),
                Color.Parse("#be123c"),
                Color.Parse("#047857"),
            };
            int hash = 17;
            foreach (char c in id)
                hash = unchecked(hash * 31 + c);
            return palette[(hash & int.MaxValue) % palette.Length];
        }
    }
}
