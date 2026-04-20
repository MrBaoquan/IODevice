using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using IOStudio.Services.Motion;

namespace IOStudio.Controls.Timeline
{
    /// <summary>
    /// 音频波形控件 — 使用 Avalonia DrawingContext 绘制波形可视化。
    /// 支持: 缩放、滚动同步、节拍标记、深色/浅色主题。
    /// 显示在时间标尺下方作为参考轨。
    /// </summary>
    public class WaveformControl : Control
    {
        // ═══════ 依赖属性 ═══════

        public static readonly StyledProperty<double> DurationMsProperty =
            AvaloniaProperty.Register<WaveformControl, double>(nameof(DurationMs), 30000);

        public static readonly StyledProperty<double> PixelsPerMsProperty =
            AvaloniaProperty.Register<WaveformControl, double>(nameof(PixelsPerMs), 0.01);

        public static readonly StyledProperty<double> ScrollOffsetXProperty =
            AvaloniaProperty.Register<WaveformControl, double>(nameof(ScrollOffsetX), 0);

        public static readonly StyledProperty<double> CurrentTimeMsProperty =
            AvaloniaProperty.Register<WaveformControl, double>(nameof(CurrentTimeMs), 0);

        public static readonly StyledProperty<bool> IsDarkThemeProperty = AvaloniaProperty.Register<
            WaveformControl,
            bool
        >(nameof(IsDarkTheme), true);

        /// <summary>总时长 (毫秒)。</summary>
        public double DurationMs
        {
            get => GetValue(DurationMsProperty);
            set => SetValue(DurationMsProperty, value);
        }

        /// <summary>缩放级别: 每毫秒对应的像素数。</summary>
        public double PixelsPerMs
        {
            get => GetValue(PixelsPerMsProperty);
            set => SetValue(PixelsPerMsProperty, value);
        }

        /// <summary>水平滚动偏移 (像素)。</summary>
        public double ScrollOffsetX
        {
            get => GetValue(ScrollOffsetXProperty);
            set => SetValue(ScrollOffsetXProperty, value);
        }

        /// <summary>当前播放时间 (毫秒)。</summary>
        public double CurrentTimeMs
        {
            get => GetValue(CurrentTimeMsProperty);
            set => SetValue(CurrentTimeMsProperty, value);
        }

        /// <summary>是否为深色主题。</summary>
        public bool IsDarkTheme
        {
            get => GetValue(IsDarkThemeProperty);
            set => SetValue(IsDarkThemeProperty, value);
        }

        // ═══════ 数据 ═══════

        private WaveformData? _waveformData;
        private List<double>? _beatMarkers; // 节拍标记时间 (毫秒)

        /// <summary>设置波形数据并刷新。</summary>
        public void SetWaveformData(WaveformData? data)
        {
            _waveformData = data;
            InvalidateVisual();
        }

        /// <summary>设置节拍标记列表 (毫秒)。</summary>
        public void SetBeatMarkers(List<double>? beats)
        {
            _beatMarkers = beats;
            InvalidateVisual();
        }

        /// <summary>当前波形数据。</summary>
        public WaveformData? WaveformData => _waveformData;

        // ═══════ 视觉常量 ═══════

        // 波形填充色 (RMS 区域)
        private static readonly IBrush WaveformFillBrush = new SolidColorBrush(
            Color.FromArgb(80, 59, 130, 246)
        );

        // 波形描边色 (峰值轮廓)
        private static readonly IPen WaveformPeakPen = new Pen(
            new SolidColorBrush(Color.Parse("#3b82f6")),
            0.8
        );

        // 播放头
        private static readonly IPen PlayheadPen = new Pen(
            new SolidColorBrush(Color.Parse("#ef4444")),
            1.5
        );

        // 节拍标记
        private static readonly IPen BeatPen = new Pen(
            new SolidColorBrush(Color.FromArgb(180, 251, 191, 36)),
            1
        );

        // 标签字体
        private readonly Typeface _typeface = new Typeface("Inter, Segoe UI, sans-serif");

        static WaveformControl()
        {
            AffectsRender<WaveformControl>(
                DurationMsProperty,
                PixelsPerMsProperty,
                ScrollOffsetXProperty,
                CurrentTimeMsProperty,
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
            var bgBrush = IsDarkTheme
                ? new SolidColorBrush(Color.Parse("#0f172a"))
                : new SolidColorBrush(Color.Parse("#f8fafc"));
            context.DrawRectangle(bgBrush, null, new Rect(0, 0, w, h));

            if (PixelsPerMs <= 0 || h <= 0)
                return;

            // 波形
            if (_waveformData is not null && _waveformData.BinCount > 0)
            {
                DrawWaveform(context, w, h);
            }
            else
            {
                DrawEmptyHint(context, w, h);
            }

            // 节拍标记
            if (_beatMarkers is not null && _beatMarkers.Count > 0)
            {
                DrawBeatMarkers(context, w, h);
            }

            // 底部分隔线
            var borderPen = new Pen(
                IsDarkTheme
                    ? new SolidColorBrush(Color.Parse("#1e293b"))
                    : new SolidColorBrush(Color.Parse("#e2e8f0")),
                1
            );
            context.DrawLine(borderPen, new Point(0, h - 0.5), new Point(w, h - 0.5));

            // 播放头
            DrawPlayhead(context, w, h);
        }

        /// <summary>绘制波形 (峰值轮廓 + RMS 填充)。</summary>
        private void DrawWaveform(DrawingContext context, double w, double h)
        {
            var data = _waveformData!;
            double msPerBin = data.MsPerBin;
            double centerY = h / 2.0;
            double halfH = h / 2.0 - 1; // 留 1px 上下边距

            // 可见区域的时间范围
            double startMs = ScrollOffsetX / PixelsPerMs;
            double endMs = startMs + w / PixelsPerMs;

            int startBin = Math.Max(0, (int)(startMs / msPerBin) - 1);
            int endBin = Math.Min(data.BinCount - 1, (int)(endMs / msPerBin) + 1);

            if (startBin >= endBin)
                return;

            // ---- RMS 填充 (使用 StreamGeometry) ----
            var fillGeo = new StreamGeometry();
            using (var ctx = fillGeo.Open())
            {
                // 上半部 (正向)
                double firstX = BinToX(startBin, msPerBin);
                ctx.BeginFigure(new Point(firstX, centerY - data.Rms[startBin] * halfH), true);

                for (int b = startBin + 1; b <= endBin; b++)
                {
                    double x = BinToX(b, msPerBin);
                    double yTop = centerY - data.Rms[b] * halfH;
                    ctx.LineTo(new Point(x, yTop));
                }

                // 下半部 (反向, 镜像)
                for (int b = endBin; b >= startBin; b--)
                {
                    double x = BinToX(b, msPerBin);
                    double yBottom = centerY + data.Rms[b] * halfH;
                    ctx.LineTo(new Point(x, yBottom));
                }

                ctx.EndFigure(true);
            }
            context.DrawGeometry(WaveformFillBrush, null, fillGeo);

            // ---- 峰值轮廓 (上半部 + 下半部) ----
            var peakGeo = new StreamGeometry();
            using (var ctx = peakGeo.Open())
            {
                // 上半部
                double fx = BinToX(startBin, msPerBin);
                ctx.BeginFigure(new Point(fx, centerY - data.Peaks[startBin] * halfH), false);

                for (int b = startBin + 1; b <= endBin; b++)
                {
                    double x = BinToX(b, msPerBin);
                    ctx.LineTo(new Point(x, centerY - data.Peaks[b] * halfH));
                }
                ctx.EndFigure(false);

                // 下半部
                ctx.BeginFigure(new Point(fx, centerY + data.Peaks[startBin] * halfH), false);
                for (int b = startBin + 1; b <= endBin; b++)
                {
                    double x = BinToX(b, msPerBin);
                    ctx.LineTo(new Point(x, centerY + data.Peaks[b] * halfH));
                }
                ctx.EndFigure(false);
            }
            context.DrawGeometry(null, WaveformPeakPen, peakGeo);

            // ---- 中心线 ----
            var centerPen = new Pen(
                IsDarkTheme
                    ? new SolidColorBrush(Color.FromArgb(40, 148, 163, 184))
                    : new SolidColorBrush(Color.FromArgb(40, 100, 116, 139)),
                0.5
            );
            context.DrawLine(centerPen, new Point(0, centerY), new Point(w, centerY));
        }

        /// <summary>将 bin 索引转换为画布 X 坐标。</summary>
        private double BinToX(int bin, double msPerBin)
        {
            double timeMs = bin * msPerBin;
            return timeMs * PixelsPerMs - ScrollOffsetX;
        }

        /// <summary>绘制节拍标记。</summary>
        private void DrawBeatMarkers(DrawingContext context, double w, double h)
        {
            double startMs = ScrollOffsetX / PixelsPerMs;
            double endMs = startMs + w / PixelsPerMs;

            foreach (double beatMs in _beatMarkers!)
            {
                if (beatMs < startMs || beatMs > endMs)
                    continue;

                double x = beatMs * PixelsPerMs - ScrollOffsetX;
                context.DrawLine(BeatPen, new Point(x, 0), new Point(x, h));
            }
        }

        /// <summary>绘制播放头。</summary>
        private void DrawPlayhead(DrawingContext context, double w, double h)
        {
            double x = CurrentTimeMs * PixelsPerMs - ScrollOffsetX;
            if (x >= 0 && x <= w)
            {
                context.DrawLine(PlayheadPen, new Point(x, 0), new Point(x, h));
            }
        }

        /// <summary>无波形数据时显示提示。</summary>
        private void DrawEmptyHint(DrawingContext context, double w, double h)
        {
            var text = new FormattedText(
                "🎵 加载视频后自动提取音频波形",
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                _typeface,
                10,
                IsDarkTheme
                    ? new SolidColorBrush(Color.Parse("#475569"))
                    : new SolidColorBrush(Color.Parse("#94a3b8"))
            );

            double tx = (w - text.Width) / 2;
            double ty = (h - text.Height) / 2;
            if (tx > 0 && ty > 0)
                context.DrawText(text, new Point(tx, ty));
        }
    }
}
