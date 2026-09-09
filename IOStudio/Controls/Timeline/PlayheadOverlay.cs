using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace IOStudio.Controls.Timeline
{
    /// <summary>
    /// 播放头叠加层 — 绘制一条垂直红色播放头线, 覆盖整个轨道区域。
    /// 即使没有轨道, 播放头线也始终可见, 方便用户在空状态下定位时间。
    /// 已配置工作区 (In/Out) 时, 播放头超出工作区范围会降透明, 提示"循环范围之外"。
    /// </summary>
    public class PlayheadOverlay : Control
    {
        public static readonly StyledProperty<double> CurrentTimeMsProperty =
            AvaloniaProperty.Register<PlayheadOverlay, double>(nameof(CurrentTimeMs), 0);

        public static readonly StyledProperty<double> PixelsPerMsProperty =
            AvaloniaProperty.Register<PlayheadOverlay, double>(nameof(PixelsPerMs), 0.01);

        public static readonly StyledProperty<double> ScrollOffsetXProperty =
            AvaloniaProperty.Register<PlayheadOverlay, double>(nameof(ScrollOffsetX), 0);

        public static readonly StyledProperty<double> WorkAreaInMsProperty =
            AvaloniaProperty.Register<PlayheadOverlay, double>(nameof(WorkAreaInMs), -1);

        public static readonly StyledProperty<double> WorkAreaOutMsProperty =
            AvaloniaProperty.Register<PlayheadOverlay, double>(nameof(WorkAreaOutMs), -1);

        public double CurrentTimeMs
        {
            get => GetValue(CurrentTimeMsProperty);
            set => SetValue(CurrentTimeMsProperty, value);
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

        private static readonly IPen PlayheadPen = new Pen(
            new SolidColorBrush(Color.Parse("#ef4444")),
            1.5
        );

        private static readonly IPen PlayheadOutOfRangePen = new Pen(
            new SolidColorBrush(Color.FromArgb(70, 239, 68, 68)),
            1.5
        );

        static PlayheadOverlay()
        {
            AffectsRender<PlayheadOverlay>(
                CurrentTimeMsProperty,
                PixelsPerMsProperty,
                ScrollOffsetXProperty,
                WorkAreaInMsProperty,
                WorkAreaOutMsProperty
            );
        }

        public override void Render(DrawingContext context)
        {
            double ppm = PixelsPerMs;
            if (ppm <= 0)
                return;

            double x = CurrentTimeMs * ppm - ScrollOffsetX;
            double h = Bounds.Height;

            if (x >= -2 && x <= Bounds.Width + 2 && h > 0)
            {
                bool inWorkArea =
                    WorkAreaInMs >= 0
                    && WorkAreaOutMs > WorkAreaInMs
                    && CurrentTimeMs >= WorkAreaInMs
                    && CurrentTimeMs <= WorkAreaOutMs;
                context.DrawLine(
                    (inWorkArea || WorkAreaInMs < 0) ? PlayheadPen : PlayheadOutOfRangePen,
                    new Point(x, 0),
                    new Point(x, h)
                );
            }
        }
    }
}
