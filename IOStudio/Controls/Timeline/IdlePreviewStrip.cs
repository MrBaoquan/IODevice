using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;

namespace IOStudio.Controls.Timeline
{
    /// <summary>
    /// Idle 单周期迷你曲线预览 — 在轨道属性对话框中实时渲染 idle 循环曲线。
    /// 绑定 IdleKeyframes (IdleKeyframeItem: TimeMs/Value/InterpolationIndex),
    /// 网格 + 曲线 + 首尾闭合警示标记。
    /// </summary>
    public class IdlePreviewStrip : Control
    {
        /// <summary>待机关键帧 (IdleKeyframeItem 行, 时间相对周期起点)。</summary>
        public static readonly StyledProperty<IEnumerable<object>?> IdleKeyframesProperty =
            AvaloniaProperty.Register<IdlePreviewStrip, IEnumerable<object>?>(
                nameof(IdleKeyframes)
            );

        /// <summary>周期时长 (毫秒)。</summary>
        public static readonly StyledProperty<double> PeriodMsProperty = AvaloniaProperty.Register<
            IdlePreviewStrip,
            double
        >(nameof(PeriodMs), 1000);

        /// <summary>值类型 ("float"/"bool")。</summary>
        public static readonly StyledProperty<string> ValueTypeProperty = AvaloniaProperty.Register<
            IdlePreviewStrip,
            string
        >(nameof(ValueType), "float");

        public IEnumerable<object>? IdleKeyframes
        {
            get => GetValue(IdleKeyframesProperty);
            set => SetValue(IdleKeyframesProperty, value);
        }

        public double PeriodMs
        {
            get => GetValue(PeriodMsProperty);
            set => SetValue(PeriodMsProperty, value);
        }

        public string ValueType
        {
            get => GetValue(ValueTypeProperty);
            set => SetValue(ValueTypeProperty, value);
        }

        static IdlePreviewStrip()
        {
            AffectsRender<IdlePreviewStrip>(
                IdleKeyframesProperty,
                PeriodMsProperty,
                ValueTypeProperty
            );
        }

        private static readonly IBrush GridBrush = new SolidColorBrush(
            Color.FromArgb(24, 148, 163, 184)
        );
        private static readonly IBrush LineBrush = new SolidColorBrush(
            Color.FromArgb(220, 120, 200, 255)
        );
        private static readonly IBrush DotBrush = new SolidColorBrush(Color.Parse("#facc15"));
        private static readonly IBrush DashBrush = new SolidColorBrush(
            Color.FromArgb(70, 148, 163, 184)
        );

        public override void Render(DrawingContext context)
        {
            double w = Bounds.Width;
            double h = Bounds.Height;
            if (w < 8 || h < 8)
                return;

            // 背景网格
            var gridPen = new Pen(GridBrush, 1);
            for (int i = 1; i < 5; i++)
            {
                double y = h * i / 5.0;
                context.DrawLine(gridPen, new Point(0, y), new Point(w, y));
            }

            var kfs = IdleKeyframes?.ToList() ?? new List<object>();
            bool isBool = string.Equals(ValueType, "bool", StringComparison.OrdinalIgnoreCase);
            double period = PeriodMs > 1.0 ? PeriodMs : 1.0;

            // 无关键帧: 画中性水平线占位
            if (kfs.Count == 0)
            {
                double yMid = h / 2.0;
                context.DrawLine(
                    new Pen(DashBrush, 1, new DashStyle(new double[] { 4, 3 }, 0)),
                    new Point(0, yMid),
                    new Point(w, yMid)
                );
                return;
            }

            // 转换为插值可用的 MotionKeyframe 列表 (相位/值)
            var model = kfs.Select(k =>
                {
                    double t = GetPropDouble(k, "TimeMs") % period;
                    if (t < 0)
                        t += period;
                    float v = Math.Clamp((float)GetPropDouble(k, "Value"), 0f, 1f);
                    int interpIdx = Math.Clamp(GetPropInt(k, "InterpolationIndex"), 0, 3);
                    string interp = AddTrackDialogViewModelInterpolations[interpIdx];
                    return new MotionKeyframe
                    {
                        TimeMs = t,
                        Value = v,
                        Interpolation = interp
                    };
                })
                .OrderBy(k => k.TimeMs)
                .ToList();

            // 采样点 (约 48 段)
            int n = 48;
            var pts = new List<Point>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                double t = period * i / n;
                float raw = InterpolationEngine.Evaluate(model, t);
                float v = isBool ? (raw >= 0.5f ? 1f : 0f) : raw;
                pts.Add(new Point(w * i / n, h - v * h));
            }

            // 曲线
            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(pts[0], false);
                foreach (var p in pts)
                    gc.LineTo(p);
                gc.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(LineBrush, 1.5), geo);

            // 闭合警示 + 端点标记 (首点/末点)
            if (model.Count >= 2)
            {
                float first = model[0].Value;
                float last = model[^1].Value;
                bool warn = isBool
                    ? Math.Abs(first - last) > 0.01f
                    : Math.Abs(first - last) > 0.05f;
                if (warn)
                {
                    var warnPen = new Pen(new SolidColorBrush(Color.Parse("#f59e0b")), 1.5);
                    context.DrawLine(warnPen, new Point(0, 3), new Point(0, 12));
                    context.DrawLine(warnPen, new Point(w, 3), new Point(w, 12));
                }
            }
            context.DrawEllipse(
                null,
                new Pen(Brushes.White, 1.2),
                new Rect(pts[0].X - 2.5, pts[0].Y - 2.5, 5, 5)
            );
            context.DrawEllipse(DotBrush, null, new Rect(pts[^1].X - 2.5, pts[^1].Y - 2.5, 5, 5));
        }

        private static readonly string[] AddTrackDialogViewModelInterpolations =
        {
            "bezier",
            "linear",
            "step",
            "ease_in_out"
        };

        private static double GetPropDouble(object o, string name)
        {
            var p = o.GetType().GetProperty(name);
            return p?.GetValue(o) is double d ? d : 0;
        }

        private static int GetPropInt(object o, string name)
        {
            var p = o.GetType().GetProperty(name);
            return p?.GetValue(o) is int i ? i : 0;
        }
    }
}
