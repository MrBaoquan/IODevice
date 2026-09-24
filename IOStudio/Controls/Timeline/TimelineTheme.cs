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
    }
}
