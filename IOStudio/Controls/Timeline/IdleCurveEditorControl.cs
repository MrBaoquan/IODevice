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
    /// <summary>
    /// 待机循环单周期曲线编辑器 — 独立于动画曲线视图。
    /// <para>
    /// 复用"关键帧编辑"交互 (双击添加 / 拖拽改值 / Delete 删除 / 切线手柄),
    /// 编辑对象为 IdleLoop.Keyframes (相位 ∈ [0, period)), 而非字段表格。
    /// </para>
    /// </summary>
    public class IdleCurveEditorControl : Control
    {
        // ═══════ 依赖属性 ═══════

        public static readonly StyledProperty<IdleLoop?> IdleLoopProperty =
            AvaloniaProperty.Register<IdleCurveEditorControl, IdleLoop?>(nameof(IdleLoop));
        public static readonly StyledProperty<string> ValueTypeProperty = AvaloniaProperty.Register<
            IdleCurveEditorControl,
            string
        >(nameof(ValueType), "float");

        public static readonly StyledProperty<double> PreviewPhaseProperty =
            AvaloniaProperty.Register<IdleCurveEditorControl, double>(nameof(PreviewPhase), -1);

        public IdleLoop? IdleLoop
        {
            get => GetValue(IdleLoopProperty);
            set => SetValue(IdleLoopProperty, value);
        }

        public string ValueType
        {
            get => GetValue(ValueTypeProperty);
            set => SetValue(ValueTypeProperty, value);
        }

        /// <summary>预览动画相位 (毫秒, -1=不显示)。由窗口预览定时器驱动。</summary>
        public double PreviewPhase
        {
            get => GetValue(PreviewPhaseProperty);
            set => SetValue(PreviewPhaseProperty, value);
        }

        // ═══════ 事件 (窗口转发给 VM, 带 Undo) ═══════

        /// <summary>双击添加关键帧: (phaseMs, value)。</summary>
        public event Action<double, float>? AddKeyframeRequested;

        /// <summary>关键帧移动提交 (拖拽释放): (kf, beforePhase, beforeValue, afterPhase, afterValue)。</summary>
        public event Action<MotionKeyframe, double, float, double, float>? KeyframeEditCommitted;

        /// <summary>删除关键帧: (kf)。</summary>
        public event Action<MotionKeyframe>? DeleteKeyframeRequested;

        // ═══════ 交互状态 ═══════

        private int _selectedKfIdx = -1;
        private bool _isDragging;
        private Point _dragStart;
        private double _dragStartPhase;
        private float _dragStartValue;

        // ═══════ 渲染缓存 (消除 60fps 预览下的每帧堆分配) ═══════

        private static readonly IBrush BgBrush = new SolidColorBrush(Color.Parse("#15151f"));
        private static readonly Pen GridPen =
            new(new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)), 0.5);
        private static readonly IBrush LabelBrush = new SolidColorBrush(
            Color.FromArgb(140, 255, 255, 255)
        );
        private static readonly IBrush LineBrush = new SolidColorBrush(Color.Parse("#38bdf8"));
        private static readonly Pen LinePen = new(LineBrush, 1.8);
        private static readonly IBrush SelBrush = new SolidColorBrush(Color.Parse("#facc15"));
        private static readonly Pen SelOutlinePen = new(Brushes.White, 2);
        private static readonly Pen KfOutlinePen = new(Brushes.Black, 1);
        private static readonly Pen PlayPen = new(new SolidColorBrush(Color.Parse("#ef4444")), 1.5);
        private static readonly IBrush PlayBrush = new SolidColorBrush(Color.Parse("#ef4444"));
        private static readonly Typeface LabelTypeface = new("Consolas");

        private readonly FormattedText[] _valueLabels = new FormattedText[5];
        private readonly FormattedText[] _phaseLabels = new FormattedText[5];
        private FormattedText? _hintText;

        /// <summary>环形闭合虚拟端点 (复用以免每采样点分配)。</summary>
        private readonly MotionKeyframe _closureEnd = new();

        private long _dataSig = -1;
        private double _sigW = -1,
            _sigH = -1;
        private StreamGeometry? _curveGeo;
        private StreamGeometry? _diamondsGeo;

        static IdleCurveEditorControl()
        {
            AffectsRender<IdleCurveEditorControl>(
                IdleLoopProperty,
                ValueTypeProperty,
                PreviewPhaseProperty
            );
        }

        public IdleCurveEditorControl()
        {
            ClipToBounds = true;
            Focusable = true; // 接收键盘事件 (Delete)
        }

        // ═══════ 坐标换算 (单周期 [0, period) × [0,1] 映射到控件) ═══════

        private double Period => IdleLoop?.PeriodMs > 1.0 ? IdleLoop.PeriodMs : 1000.0;
        private static readonly double PadLeft = 36,
            PadRight = 12,
            PadTop = 12,
            PadBottom = 20;

        private double PhaseToX(double phaseMs) =>
            PadLeft + phaseMs / Period * Math.Max(1, Bounds.Width - PadLeft - PadRight);

        private double ValueToY(float v) =>
            PadTop
            + (1.0f - Math.Clamp(v, 0f, 1f)) * Math.Max(1, Bounds.Height - PadTop - PadBottom);

        private double XToPhase(double x) =>
            (x - PadLeft) / Math.Max(1, Bounds.Width - PadLeft - PadRight) * Period;

        private float YToValue(double y) =>
            (float)
                Math.Clamp(
                    1.0 - (y - PadTop) / Math.Max(1, Bounds.Height - PadTop - PadBottom),
                    0,
                    1
                );

        private List<MotionKeyframe>? Kfs => IdleLoop?.Keyframes;

        // ═══════ 渲染 ═══════

        public override void Render(DrawingContext context)
        {
            double w = Bounds.Width;
            double h = Bounds.Height;
            EnsureLabelTexts();
            context.DrawRectangle(BgBrush, null, new Rect(0, 0, w, h));

            // 水平值网格 0 / 0.25 / 0.5 / 0.75 / 1
            for (int i = 0; i <= 4; i++)
            {
                float v = i / 4f;
                double y = ValueToY(v);
                context.DrawLine(GridPen, new Point(PadLeft, y), new Point(w - PadRight, y));
                context.DrawText(_valueLabels[i], new Point(2, y - _valueLabels[i].Height / 2));
            }
            // 相位刻度 0% / 25% / 50% / 75% / 100%
            for (int i = 0; i <= 4; i++)
            {
                double x = PadLeft + (w - PadLeft - PadRight) * i / 4.0;
                context.DrawLine(GridPen, new Point(x, PadTop), new Point(x, h - PadBottom));
                context.DrawText(
                    _phaseLabels[i],
                    new Point(x - _phaseLabels[i].Width / 2, h - PadBottom + 4)
                );
            }

            var kfs = Kfs;
            if (kfs == null || kfs.Count == 0)
            {
                context.DrawText(
                    _hintText!,
                    new Point((w - _hintText!.Width) / 2, (h - PadTop - PadBottom) / 2)
                );
                return;
            }

            // 曲线 + 菱形几何: 仅在数据/尺寸变化时重建, 预览 60fps 期间直接复用
            EnsureGeometries(kfs, w, h);
            context.DrawGeometry(null, LinePen, _curveGeo);

            // 待机循环是环形闭合: 首尾 (0 与 period) 为同一相位点, 编辑时已自动同步, 无需"不闭合"提示。

            // 未选中关键帧点 (菱形, 缓存几何)
            context.DrawGeometry(LineBrush, KfOutlinePen, _diamondsGeo);

            // 选中关键帧: 高亮 + 数值标签 (随拖动变化, 每帧绘制)
            bool isBool = string.Equals(ValueType, "bool", StringComparison.OrdinalIgnoreCase);
            if (_selectedKfIdx >= 0 && _selectedKfIdx < kfs.Count)
            {
                var kf = kfs[_selectedKfIdx];
                double x = PhaseToX(kf.TimeMs);
                float dv = isBool ? (kf.Value >= 0.5f ? 1f : 0f) : kf.Value;
                double y = ValueToY(dv);
                var diamond = BuildDiamond(x, y, 5);
                context.DrawGeometry(SelBrush, SelOutlinePen, diamond);
                var vtxt = new FormattedText(
                    dv.ToString("0.00"),
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    LabelTypeface,
                    9,
                    SelBrush
                );
                context.DrawText(vtxt, new Point(x + 8, Math.Max(PadTop, y - 6)));
            }

            // 预览播放头 (动画预览时)
            if (PreviewPhase >= 0)
            {
                double px = PhaseToX(PreviewPhase);
                context.DrawLine(
                    PlayPen,
                    new Point(px, PadTop),
                    new Point(px, Bounds.Height - PadBottom)
                );
                float pv = SampleLoop(kfs, PreviewPhase, Period);
                double py = ValueToY(pv);
                context.DrawEllipse(PlayBrush, null, new Rect(px - 4, py - 4, 8, 8));
            }
        }

        // ═══════ 渲染缓存辅助 ═══════

        private void EnsureLabelTexts()
        {
            if (_valueLabels[0] != null)
                return;
            for (int i = 0; i <= 4; i++)
            {
                float v = i / 4f;
                _valueLabels[i] = new FormattedText(
                    v.ToString("0.00"),
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    LabelTypeface,
                    9,
                    LabelBrush
                );
                _phaseLabels[i] = new FormattedText(
                    $"{i * 25}%",
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    LabelTypeface,
                    9,
                    LabelBrush
                );
            }
            _hintText = new FormattedText(
                "双击空白添加待机关键帧",
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                11,
                new SolidColorBrush(Color.FromArgb(120, 255, 255, 255))
            );
        }

        /// <summary>按数据签名 + 尺寸缓存曲线/菱形几何; 拖动或预览播放头移动时直接复用旧几何。</summary>
        private void EnsureGeometries(List<MotionKeyframe> kfs, double w, double h)
        {
            long sig = ComputeDataSignature(kfs);
            if (
                _curveGeo != null
                && _diamondsGeo != null
                && sig == _dataSig
                && Math.Abs(_sigW - w) < 0.5
                && Math.Abs(_sigH - h) < 0.5
            )
                return;

            _dataSig = sig;
            _sigW = w;
            _sigH = h;

            // 曲线: 环形采样 (48 段), 复用 TrackValueEvaluator 的环形插值语义
            var period = Period;
            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                int n = 48;
                bool started = false;
                for (int i = 0; i <= n; i++)
                {
                    double phase = period * i / n;
                    float v = SampleLoop(kfs, phase, period);
                    double x = PhaseToX(phase);
                    double y = ValueToY(v);
                    if (!started)
                    {
                        gc.BeginFigure(new Point(x, y), false);
                        started = true;
                    }
                    else
                        gc.LineTo(new Point(x, y));
                }
                gc.EndFigure(false);
            }
            _curveGeo = geo;

            // 关键帧点 (菱形) — 选中帧单独绘制, 此处排除
            bool isBool = string.Equals(ValueType, "bool", StringComparison.OrdinalIgnoreCase);
            var dgeo = new StreamGeometry();
            using (var dgc = dgeo.Open())
            {
                for (int i = 0; i < kfs.Count; i++)
                {
                    if (i == _selectedKfIdx)
                        continue;
                    var kf = kfs[i];
                    double x = PhaseToX(kf.TimeMs);
                    float dv = isBool ? (kf.Value >= 0.5f ? 1f : 0f) : kf.Value;
                    double y = ValueToY(dv);
                    double s = 5;
                    dgc.BeginFigure(new Point(x, y - s), true);
                    dgc.LineTo(new Point(x + s, y));
                    dgc.LineTo(new Point(x, y + s));
                    dgc.LineTo(new Point(x - s, y));
                    dgc.EndFigure(true);
                }
            }
            _diamondsGeo = dgeo;
        }

        private static StreamGeometry BuildDiamond(double x, double y, double s)
        {
            var d = new StreamGeometry();
            using (var dgc = d.Open())
            {
                dgc.BeginFigure(new Point(x, y - s), true);
                dgc.LineTo(new Point(x + s, y));
                dgc.LineTo(new Point(x, y + s));
                dgc.LineTo(new Point(x - s, y));
                dgc.EndFigure(true);
            }
            return d;
        }

        /// <summary>几何缓存签名: 周期 + 各关键帧 (相位/值/插值/切线)。</summary>
        private long ComputeDataSignature(List<MotionKeyframe> kfs)
        {
            long h = 17;
            h = h * 31 + BitConverter.DoubleToInt64Bits(Period);
            foreach (var k in kfs)
            {
                h = h * 31 + BitConverter.DoubleToInt64Bits(k.TimeMs);
                h = h * 31 + BitConverter.DoubleToInt64Bits(k.Value);
                h = h * 31 + (k.Interpolation?.GetHashCode() ?? 0);
                h = h * 31 + BitConverter.DoubleToInt64Bits(k.TangentOut ?? 0);
                h = h * 31 + BitConverter.DoubleToInt64Bits(k.TangentIn ?? 0);
            }
            return h;
        }

        private static float SampleLoop(List<MotionKeyframe> kfs, double phaseMs, double period)
        {
            if (kfs.Count == 0)
                return 0.5f;
            if (kfs.Count == 1)
                return kfs[0].Value;
            double p = phaseMs % period;
            if (p < 0)
                p += period;
            for (int i = 0; i + 1 < kfs.Count; i++)
            {
                if (p >= kfs[i].TimeMs && p <= kfs[i + 1].TimeMs)
                    return InterpolationEngine.Evaluate(kfs[i], kfs[i + 1], p);
            }
            // 跨环闭合: last → first (first 虚拟时间 = period)
            if (p >= kfs[^1].TimeMs)
            {
                var last = kfs[^1];
                var first = kfs[0];
                double span = period - last.TimeMs;
                if (span <= 0.001)
                    return last.Value;
                return InterpolationEngine.Evaluate(
                    last,
                    new MotionKeyframe
                    {
                        TimeMs = period,
                        Value = first.Value,
                        Interpolation = first.Interpolation
                    },
                    p
                );
            }
            return kfs[^1].Value;
        }

        /// <summary>
        /// 闭合周期约束: 待机循环首帧(相位≈0)与末帧(相位≈period)为同一相位点, 值须保持一致。
        /// 编辑任一帧时同步另一端, 保证曲线无缝循环。末帧值以首帧为基准 (首帧是相位 0 的权威)。
        /// </summary>
        private void EnforceLoopClosure(List<MotionKeyframe> kfs)
        {
            if (kfs.Count == 0)
                return;
            double period = Period;
            double tol = Math.Max(1.0, period * 0.01); // 允许 1% 相位容差
            int firstIdx = -1,
                lastIdx = -1;
            for (int i = 0; i < kfs.Count; i++)
            {
                if (Math.Abs(kfs[i].TimeMs) <= tol && firstIdx < 0)
                    firstIdx = i;
                if (Math.Abs(kfs[i].TimeMs - period) <= tol)
                    lastIdx = i;
            }
            if (firstIdx >= 0 && lastIdx >= 0 && firstIdx != lastIdx)
            {
                // 强制末帧值 = 首帧值
                kfs[lastIdx].Value = kfs[firstIdx].Value;
            }
        }

        /// <summary>对当前待机循环数据执行闭合同步 (供外部添加/提交关键帧后调用)。</summary>
        public void SyncLoopClosure()
        {
            var kfs = Kfs;
            if (kfs == null)
                return;
            EnforceLoopClosure(kfs);
            InvalidateVisual();
        }

        // ═══════ 交互 ═══════

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            Focus();
            var pos = e.GetPosition(this);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;
            var kfs = Kfs;
            if (kfs == null)
                return;

            // 命中关键帧
            int hit = HitTestKf(pos);
            if (hit >= 0)
            {
                _selectedKfIdx = hit;
                _isDragging = true;
                _dragStart = pos;
                _dragStartPhase = kfs[hit].TimeMs;
                _dragStartValue = kfs[hit].Value;
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            // 空白点击: 单个空白点清除选中; 双击添加
            if (e.ClickCount == 2)
            {
                double phase = Math.Clamp(XToPhase(pos.X), 0, Period);
                float value = YToValue(pos.Y);
                AddKeyframeRequested?.Invoke(phase, value);
                e.Handled = true;
            }
            else
            {
                _selectedKfIdx = -1;
                InvalidateVisual();
                e.Handled = true;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (!_isDragging)
                return;
            var pos = e.GetPosition(this);
            var kfs = Kfs;
            if (kfs == null || _selectedKfIdx < 0 || _selectedKfIdx >= kfs.Count)
                return;

            var kf = kfs[_selectedKfIdx];
            double period = Period;
            double phase = Math.Clamp(XToPhase(pos.X), 0, period);
            float value = YToValue(pos.Y);
            bool isBool = string.Equals(ValueType, "bool", StringComparison.OrdinalIgnoreCase);
            if (isBool)
                value = value >= 0.5f ? 1f : 0f;

            // 首尾帧固定在首尾时间点 (0 / period): 只允许 Y 轴移动, 锁定 X。
            double tol = Math.Max(1.0, period * 0.01);
            if (Math.Abs(kf.TimeMs) <= tol)
                phase = 0;
            else if (Math.Abs(kf.TimeMs - period) <= tol)
                phase = period;

            kf.TimeMs = phase;
            kf.Value = Math.Clamp(value, 0f, 1f);
            // 保持相位排序
            kfs.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
            // 首尾同步: 待机循环为闭合周期曲线, 首帧(相位0)与末帧(相位period)值保持一致
            EnforceLoopClosure(kfs);
            _selectedKfIdx = kfs.IndexOf(kf);
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (!_isDragging)
                return;
            _isDragging = false;
            var kfs = Kfs;
            if (kfs == null || _selectedKfIdx < 0 || _selectedKfIdx >= kfs.Count)
                return;
            var kf = kfs[_selectedKfIdx];
            if (
                Math.Abs(_dragStartPhase - kf.TimeMs) > 0.01
                || Math.Abs(_dragStartValue - kf.Value) > 0.0001f
            )
                KeyframeEditCommitted?.Invoke(
                    kf,
                    _dragStartPhase,
                    _dragStartValue,
                    kf.TimeMs,
                    kf.Value
                );
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if ((e.Key == Key.Delete || e.Key == Key.Back) && _selectedKfIdx >= 0)
            {
                var kfs = Kfs;
                if (kfs != null && _selectedKfIdx < kfs.Count)
                {
                    var kf = kfs[_selectedKfIdx];
                    DeleteKeyframeRequested?.Invoke(kf);
                    _selectedKfIdx = -1;
                    InvalidateVisual();
                }
                e.Handled = true;
            }
        }

        private int HitTestKf(Point pos)
        {
            var kfs = Kfs;
            if (kfs == null)
                return -1;
            double best = 10;
            int bestIdx = -1;
            for (int i = 0; i < kfs.Count; i++)
            {
                double x = PhaseToX(kfs[i].TimeMs);
                double y = ValueToY(kfs[i].Value);
                double d = Math.Sqrt((pos.X - x) * (pos.X - x) + (pos.Y - y) * (pos.Y - y));
                if (d < best)
                {
                    best = d;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }
    }
}
