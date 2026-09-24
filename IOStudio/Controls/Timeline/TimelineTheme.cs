using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace IOStudio.Controls.Timeline
{
    /// <summary>
    /// 自绘控件统一取色通道。
    ///
    /// <para>时间轴里的标尺 / 轨道 / 播放头 / 曲线等都是 <see cref="Avalonia.Controls.Control"/> 直接
    /// <c>DrawingContext</c> 绘制, 无法用 <c>{DynamicResource}</c>。此前各自写死一套 hex 调色板
    /// (且部分控件自带 light/dark 分支), 导致同一界面出现多套色板、深色主题不一致 —— 这是"粗糙感"
    /// 的主要来源。</para>
    ///
    /// <para>本类把绘制用色统一收敛到 .axaml 里的 <c>Tl*</c> 令牌: 一处定义、随主题切换自动刷新。
    /// 绘制代码只依赖这里, 不再出现字面 hex (回退值仅用于令牌缺失的兜底)。</para>
    /// </summary>
    public static class TimelineTheme
    {
        private static bool _hooked;
        private static bool _dirty = true;
        private static readonly Dictionary<string, IBrush> Cache = new();

        private static void Hook()
        {
            if (_hooked)
                return;
            _hooked = true;
            if (Application.Current is { } app)
                app.ActualThemeVariantChanged += (_, _) => _dirty = true;
        }

        private static IBrush Resolve(string key, string fallback)
        {
            Hook();
            if (_dirty)
            {
                Cache.Clear();
                _dirty = false;
            }
            if (Cache.TryGetValue(key, out var cached))
                return cached;

            IBrush brush =
                Application.Current?.TryFindResource(key, out var v) == true && v is IBrush b
                    ? b
                    : new SolidColorBrush(Color.Parse(fallback));
            Cache[key] = brush;
            return brush;
        }

        // ── 标尺 ──
        public static IBrush RulerBg => Resolve("TlRulerBg", "#eef0f2");
        public static IBrush RulerLabel => Resolve("TlRulerLabel", "#4b5563");
        public static IBrush RulerMajor => Resolve("TlRulerMajor", "#6b7280");
        public static IBrush RulerMinor => Resolve("TlRulerMinor", "#d6dae0");
        public static IBrush RulerBottom => Resolve("TlRulerBottom", "#e5e7eb");

        // ── 播放头 / 参考 ──
        public static IBrush Playhead => Resolve("TlPlayhead", "#e2554c");
        public static IBrush VideoRef => Resolve("TlVideoRef", "#7c5cd6");

        // ── 语义 ──
        public static IBrush Event => Resolve("TlEvent", "#f59e0b");
        public static IBrush Marker => Resolve("TlMarker", "#7356a8");
        public static IBrush Accent => Resolve("TlAccent", "#3978d4");
        public static IBrush TextMuted => Resolve("TlTextMuted", "#747c86");
        public static IBrush TextSecondary => Resolve("TlTextSecondary", "#4f565f");
        public static IBrush ContentBg => Resolve("TlContentBg", "#ffffff");
        public static IBrush Border => Resolve("TlBorderBrush", "#d4d7db");
        public static IBrush GridLine => Resolve("TlGridLine", "#e6e9ed");
        public static IBrush CurveBg => Resolve("TlCurveBg", "#f7f8fa");
        public static IBrush WorkIn => Resolve("TlWorkIn", "#22c55e");
        public static IBrush WorkOut => Resolve("TlWorkOut", "#ef4444");
        public static IBrush SnapGuide => Resolve("TlSnapGuide", "#0ea5e9");
        public static IBrush MutedOverlay => Resolve("TlMutedOverlay", "#80ffffff");
        // 编辑画布 (恒定深色)
        public static IBrush CanvasBg => Resolve("TlCanvasBg", "#23272e");
        public static IBrush CanvasRowAlt => Resolve("TlCanvasRowAlt", "#282d34");
        public static IBrush CanvasGrid => Resolve("TlCanvasGrid", "#3a4048");
        public static IBrush CanvasText => Resolve("TlCanvasText", "#c9cfd6");
        public static IBrush Success => Resolve("TlSuccess", "#238b62");
        public static IBrush Danger => Resolve("TlDanger", "#c84f4f");
        public static IBrush Warning => Resolve("TlWarning", "#b87916");

        /// <summary>便捷构造画笔。</summary>
        public static IPen Pen(IBrush brush, double thickness, IDashStyle? dash = null) =>
            new Pen(brush, thickness, dash);

        /// <summary>取同一色的半透明变体 (用于选区/填充)。</summary>
        public static IBrush WithAlpha(IBrush brush, byte alpha)
        {
            if (brush is ISolidColorBrush s)
                return new SolidColorBrush(Color.FromArgb(alpha, s.Color.R, s.Color.G, s.Color.B));
            return brush;
        }

        /// <summary>
        /// 依据底色亮度选择高对比文字色 (亮底用黑、暗底用白), 避免"前景文字与背景太近"。
        /// 阈值用感知亮度 (ITU-R BT.601)。
        /// </summary>
        public static IBrush ContrastText(Color background)
        {
            double luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B)
                / 255.0;
            return luminance > 0.6 ? Brushes.Black : Brushes.White;
        }

        public static IBrush ContrastText(IBrush background) =>
            background is ISolidColorBrush s ? ContrastText(s.Color) : Brushes.White;
    }
}
