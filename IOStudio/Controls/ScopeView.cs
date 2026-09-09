using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using IOStudio.ViewModels;

namespace IOStudio.Controls;

/// <summary>
/// 自绘示波器控件：网格/坐标轴/多通道波形叠加/目标预览虚线/A-B 游标测量。
/// 数据由外部通过 SetFrame 推送（与渲染解耦），时基/缩放/偏移为可绑定样式属性。
/// </summary>
public class ScopeView : Control
{
    private ScopeFrame? _frame;
    private double _cursorAX = -1;
    private double _cursorBX = -1;
    private bool _dragA;
    private bool _dragB;

    public static readonly StyledProperty<double> TimeBaseMsProperty =
        AvaloniaProperty.Register<ScopeView, double>(nameof(TimeBaseMs), 100);

    public static readonly StyledProperty<double> VerticalScalePercentProperty =
        AvaloniaProperty.Register<ScopeView, double>(nameof(VerticalScalePercent), 100);

    public static readonly StyledProperty<double> VerticalOffsetPercentProperty =
        AvaloniaProperty.Register<ScopeView, double>(nameof(VerticalOffsetPercent), 0);

    public double TimeBaseMs
    {
        get => GetValue(TimeBaseMsProperty);
        set => SetValue(TimeBaseMsProperty, value);
    }

    public double VerticalScalePercent
    {
        get => GetValue(VerticalScalePercentProperty);
        set => SetValue(VerticalScalePercentProperty, value);
    }

    public double VerticalOffsetPercent
    {
        get => GetValue(VerticalOffsetPercentProperty);
        set => SetValue(VerticalOffsetPercentProperty, value);
    }

    static ScopeView()
    {
        AffectsRender<ScopeView>(
            TimeBaseMsProperty,
            VerticalScalePercentProperty,
            VerticalOffsetPercentProperty);
    }

    public void SetFrame(ScopeFrame? frame)
    {
        _frame = frame;
        InvalidateVisual();
    }

    private static (double Left, double Right, double Top, double Bottom) PlotMargins(double w, double h)
    {
        const double top = 30, bottom = 24, left = 48, right = 12;
        return (left, w - right, top, h - bottom);
    }

    private IBrush? GetResBrush(string key)
    {
        if (this is IResourceHost host && host.TryFindResource(key, out var v) && v is IBrush b)
            return b;
        return null;
    }

    private void DrawText(DrawingContext dc, string text, double size, double x, double y, IBrush? brush)
    {
        if (brush == null || string.IsNullOrEmpty(text))
            return;
        var ft = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Consolas", FontStyle.Normal, FontWeight.Normal),
            size,
            brush);
        dc.DrawText(ft, new Point(x, y));
    }

    /// <summary>像素 x → 通道 ci 在 x 处的采样值（百分比 0~100），越界返回 null。</summary>
    private double? ValueAt(double x, int ci)
    {
        if (_frame == null || !_frame.HasValidData || ci < 0 || ci >= _frame.Channels.Length)
            return null;
        var (left, right, top, bottom) = PlotMargins(Bounds.Width, Bounds.Height);
        double plotW = right - left;
        if (plotW <= 0)
            return null;
        double totalMs = TimeBaseMs * 10.0;
        double samplesPerScreen = totalMs / _frame.SampleIntervalMs;
        int lastIndex = _frame.ValidStart + _frame.ValidCount - 1;
        double d = samplesPerScreen - 1 - (x - left) / (plotW / samplesPerScreen);
        int p = lastIndex - (int)Math.Round(d);
        if (p < _frame.ValidStart || p > lastIndex)
            return null;
        return _frame.Channels[ci][p] * 100.0;
    }

    public override void Render(DrawingContext dc)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 1 || h <= 1)
            return;

        var bg = GetResBrush("TlContentBg") ?? Brushes.White;
        dc.DrawRectangle(bg, null, new Rect(0, 0, w, h));

        var (left, right, top, bottom) = PlotMargins(w, h);
        double plotW = right - left;
        double plotH = bottom - top;
        if (plotW <= 10 || plotH <= 10)
            return;

        var gridPen = new Pen(GetResBrush("TlBorderLight") ?? Brushes.Silver, 1);
        var gridSubPen = new Pen(GetResBrush("TlBorderLight") ?? Brushes.Silver, 0.5);
        var textBrush = GetResBrush("TlTextMuted") ?? Brushes.Gray;
        var textPrimary = GetResBrush("TlTextPrimary") ?? Brushes.Black;

        double vRange = 100.0 / (VerticalScalePercent / 100.0);
        double vCenter = 50.0 + VerticalOffsetPercent;
        double totalMs = TimeBaseMs * 10.0;

        // 网格（垂直 10 格，水平 5 格）
        for (int i = 0; i <= 10; i++)
        {
            double x = left + i * plotW / 10.0;
            dc.DrawLine(i % 5 == 0 ? gridPen : gridSubPen, new Point(x, top), new Point(x, bottom));
            double t = i * TimeBaseMs;
            string label = t >= 1000 ? (t / 1000).ToString("0.##") + "s" : t.ToString("0") + "ms";
            DrawText(dc, label, 9, x - 14, bottom + 4, textBrush);
        }
        for (int j = 0; j <= 5; j++)
        {
            double y = top + j * plotH / 5.0;
            dc.DrawLine(gridPen, new Point(left, y), new Point(right, y));
            double val = vCenter + vRange / 2.0 - j * vRange / 5.0;
            DrawText(dc, val.ToString("F0") + "%", 9, 2, y - 7, textBrush);
        }

        if (_frame != null && _frame.HasValidData)
        {
            double samplesPerScreen = totalMs / _frame.SampleIntervalMs;
            int lastIndex = _frame.ValidStart + _frame.ValidCount - 1;
            double pxPerSample = plotW / samplesPerScreen;
            int step = Math.Max(1, (int)Math.Ceiling(1.0 / Math.Max(0.4, pxPerSample)));

            for (int ci = 0; ci < _frame.Channels.Length; ci++)
            {
                if (!_frame.IsChannelVisible(ci))
                    continue;
                var color = (_frame.Colors != null && ci < _frame.Colors.Count) ? _frame.Colors[ci] : Brushes.Gray;
                var pen = new Pen(color, 1.5);
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    bool started = false;
                    for (int p = _frame.ValidStart; p <= lastIndex; p += step)
                    {
                        double d = lastIndex - p;
                        double x = left + (samplesPerScreen - 1 - d) * pxPerSample;
                        if (x < left - 2)
                            break;
                        double v = _frame.Channels[ci][p] * 100.0;
                        double y = top + (vCenter + vRange / 2.0 - v) / vRange * plotH;
                        if (!started)
                        {
                            ctx.BeginFigure(new Point(x, y), false);
                            started = true;
                        }
                        else
                        {
                            ctx.LineTo(new Point(x, y));
                        }
                    }
                }
                dc.DrawGeometry(null, pen, geo);
            }

            // 目标预览虚线（信号发生器理论曲线）
            if (_frame.Preview != null && _frame.Preview.Length > 0)
            {
                var previewPen = new Pen(GetResBrush("TlWarning") ?? Brushes.Orange, 1.2)
                {
                    DashStyle = new DashStyle(new double[] { 4, 3 }, 0)
                };
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    bool started = false;
                    int n = _frame.Preview.Length;
                    for (int p = 0; p < n; p++)
                    {
                        double d = n - 1 - p;
                        double x = left + (samplesPerScreen - 1 - d) * pxPerSample;
                        if (x < left - 2)
                            break;
                        double v = _frame.Preview[p] * 100.0;
                        double y = top + (vCenter + vRange / 2.0 - v) / vRange * plotH;
                        if (!started)
                        {
                            ctx.BeginFigure(new Point(x, y), false);
                            started = true;
                        }
                        else
                        {
                            ctx.LineTo(new Point(x, y));
                        }
                    }
                }
                dc.DrawGeometry(null, previewPen, geo);
            }
        }

        // 游标 A/B
        if (_cursorAX < 0)
            _cursorAX = left + plotW * 0.25;
        if (_cursorBX < 0)
            _cursorBX = left + plotW * 0.75;
        _cursorAX = Math.Clamp(_cursorAX, left, right);
        _cursorBX = Math.Clamp(_cursorBX, left, right);

        var accent = GetResBrush("TlAccent") ?? Brushes.DodgerBlue;
        var cursorPen = new Pen(accent, 1) { DashStyle = new DashStyle(new double[] { 3, 2 }, 0) };
        dc.DrawLine(cursorPen, new Point(_cursorAX, top), new Point(_cursorAX, bottom));
        dc.DrawLine(cursorPen, new Point(_cursorBX, top), new Point(_cursorBX, bottom));
        DrawText(dc, "A", 10, _cursorAX + 2, top - 16, accent);
        DrawText(dc, "B", 10, _cursorBX + 2, top - 16, accent);

        // 读数条
        double dtMs = Math.Abs(_cursorBX - _cursorAX) * totalMs / plotW;
        double freq = dtMs > 0 ? 1000.0 / dtMs : 0;
        string readout = $"ΔT {dtMs:0.##} ms    F {freq:0.###} Hz";
        int measureCi = _frame != null && _frame.Channels.Length > 0 ? 0 : -1;
        if (measureCi >= 0)
        {
            var va = ValueAt(_cursorAX, measureCi);
            var vb = ValueAt(_cursorBX, measureCi);
            if (va.HasValue && vb.HasValue)
                readout += $"    Vpp {Math.Abs(vb.Value - va.Value):0.#}%  (CH{measureCi + 1}: {va.Value:0.#}% → {vb.Value:0.#}%)";
        }
        DrawText(dc, readout, 10, 6, 5, textPrimary);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _cursorAX) < 8)
        {
            _dragA = true;
            e.Handled = true;
        }
        else if (Math.Abs(p.X - _cursorBX) < 8)
        {
            _dragB = true;
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_dragA)
        {
            _cursorAX = Math.Clamp(p.X, 0, Bounds.Width);
            InvalidateVisual();
            e.Handled = true;
        }
        else if (_dragB)
        {
            _cursorBX = Math.Clamp(p.X, 0, Bounds.Width);
            InvalidateVisual();
            e.Handled = true;
        }
        else
        {
            bool near = Math.Abs(p.X - _cursorAX) < 8 || Math.Abs(p.X - _cursorBX) < 8;
            Cursor = near ? new Cursor(StandardCursorType.SizeWestEast) : Cursor.Default;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragA = false;
        _dragB = false;
    }
}
