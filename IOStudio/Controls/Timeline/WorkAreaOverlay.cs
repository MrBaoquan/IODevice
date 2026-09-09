using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace IOStudio.Controls.Timeline
{
    /// <summary>
    /// 工作区 (In/Out) 叠加层 — 在轨道主体区叠加显示入点/出点竖线、
    /// 半透明高亮区域与时长徽章, 让 In→Out 范围与持续时长在整个时间轴高度上直观可见。
    /// </summary>
    public class WorkAreaOverlay : Control
    {
        public static readonly StyledProperty<double> WorkAreaInMsProperty =
            AvaloniaProperty.Register<WorkAreaOverlay, double>(nameof(WorkAreaInMs), -1);

        public static readonly StyledProperty<double> WorkAreaOutMsProperty =
            AvaloniaProperty.Register<WorkAreaOverlay, double>(nameof(WorkAreaOutMs), -1);

        public static readonly StyledProperty<double> PixelsPerMsProperty =
            AvaloniaProperty.Register<WorkAreaOverlay, double>(nameof(PixelsPerMs), 0.1);

        public static readonly StyledProperty<double> ScrollOffsetXProperty =
            AvaloniaProperty.Register<WorkAreaOverlay, double>(nameof(ScrollOffsetX), 0);

        public double WorkAreaInMs
        {
            get => GetValue(WorkAreaInMsProperty);
            set => SetValue(WorkAreaInMsProperty, value);
        }

        public double WorkAreaOutMs
        {
            get => GetValue(WorkAreaOutMsProperty);
            set => SetValue(WorkAreaOutMsProperty, value);
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

        private static readonly IBrush AreaBrush = new SolidColorBrush(
            Color.FromArgb(28, 59, 130, 246)
        );
        private static readonly IBrush BandBrush = new SolidColorBrush(
            Color.FromArgb(60, 96, 165, 250)
        );
        private static readonly IPen InPen = new Pen(
            new SolidColorBrush(Color.FromArgb(220, 34, 197, 94)),
            1.4
        );
        private static readonly IPen OutPen = new Pen(
            new SolidColorBrush(Color.FromArgb(220, 239, 68, 68)),
            1.4
        );

        private static readonly Typeface BadgeTypeface = new("Inter, Microsoft YaHei UI");

        static WorkAreaOverlay()
        {
            AffectsRender<WorkAreaOverlay>(
                WorkAreaInMsProperty,
                WorkAreaOutMsProperty,
                PixelsPerMsProperty,
                ScrollOffsetXProperty
            );
        }

        public override void Render(DrawingContext context)
        {
            double ppm = PixelsPerMs;
            if (ppm <= 0)
                return;

            double inMs = WorkAreaInMs;
            double outMs = WorkAreaOutMs;
            if (inMs < 0 && outMs < 0)
                return;

            double w = Bounds.Width;
            double h = Bounds.Height;

            double xIn = inMs * ppm - ScrollOffsetX;
            double xOut = outMs * ppm - ScrollOffsetX;

            // In/Out 均有效 → 高亮区域 + 时长徽章
            if (inMs >= 0 && outMs > inMs)
            {
                double xStart = Math.Max(0, xIn);
                double xEnd = Math.Min(w, xOut);
                if (xEnd > xStart)
                {
                    context.DrawRectangle(AreaBrush, null, new Rect(xStart, 0, xEnd - xStart, h));

                    // 顶部细条带 (与标尺呼应)
                    double bandH = Math.Min(16, h);
                    context.DrawRectangle(
                        BandBrush,
                        null,
                        new Rect(xStart, 0, xEnd - xStart, bandH)
                    );

                    // 时长徽章 (区域中点上方)
                    double durMs = outMs - inMs;
                    string durText = FormatDuration(durMs);
                    var durFmt = new FormattedText(
                        durText,
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        BadgeTypeface,
                        11,
                        new SolidColorBrush(Color.FromArgb(255, 29, 78, 216))
                    );
                    double bw = durFmt.Width + 10;
                    double bh = durFmt.Height + 4;
                    double bx = xStart + (xEnd - xStart) / 2 - bw / 2;
                    double by = 4;
                    context.DrawRectangle(
                        new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
                        new Pen(new SolidColorBrush(Color.FromArgb(160, 96, 165, 250)), 1),
                        new Rect(bx, by, bw, bh)
                    );
                    context.DrawText(durFmt, new Point(bx + 5, by + 2));
                }
            }

            // In 竖线
            if (inMs >= 0 && xIn >= -2 && xIn <= w + 2)
                context.DrawLine(InPen, new Point(xIn, 0), new Point(xIn, h));

            // Out 竖线
            if (outMs >= 0 && xOut >= -2 && xOut <= w + 2)
                context.DrawLine(OutPen, new Point(xOut, 0), new Point(xOut, h));
        }

        private static string FormatDuration(double ms)
        {
            if (ms < 0)
                return "0";
            var ts = TimeSpan.FromMilliseconds(ms);
            if (ms < 1000)
                return $"{ms:0}ms";
            if (ms < 60000)
            {
                double sec = ms / 1000.0;
                return sec == Math.Floor(sec) ? $"{sec:0}s" : $"{sec:0.#}s";
            }
            if (ms < 3600000)
                return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
            return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }
    }
}
