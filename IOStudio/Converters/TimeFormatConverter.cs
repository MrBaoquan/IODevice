using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace IOStudio.Converters
{
    /// <summary>
    /// 时间格式转换器 — 毫秒 ↔ 可读时间字符串 (mm:ss.fff 或纯秒数)。
    /// 提取自 TimelineEditorWindow.axaml.cs 中 ParseTimeInput() (~40 行)。
    /// </summary>
    /// <remarks>
    /// AXAML 使用:
    /// <code>
    /// &lt;TextBlock Text="{Binding CurrentTimeMs, Converter={StaticResource TimeFormatConverter}}" /&gt;
    /// </code>
    ///
    /// 支持的输入格式 (ConvertBack):
    /// - "mm:ss.fff" → 毫秒 (如 "1:23.456" → 83456)
    /// - 纯秒数 (如 "3.5" → 3500)
    /// - 大数字自动识别为毫秒 (如 "5000" → 5000)
    /// </remarks>
    public class TimeFormatConverter : IValueConverter
    {
        /// <summary>毫秒 → 显示字符串 (mm:ss.fff)</summary>
        public object Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture
        )
        {
            if (value is double ms)
                return FormatMs(ms);
            if (value is float msF)
                return FormatMs(msF);
            return "00:00.000";
        }

        /// <summary>显示字符串 → 毫秒</summary>
        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture
        )
        {
            if (value is string input)
            {
                double? parsed = ParseTimeInput(input);
                if (parsed.HasValue)
                    return parsed.Value;
            }
            return 0d;
        }

        // ── 静态工具方法 (可直接调用) ──

        /// <summary>毫秒 → "mm:ss.fff" 格式字符串。</summary>
        public static string FormatMs(double ms)
        {
            if (ms < 0)
                ms = 0;
            var ts = TimeSpan.FromMilliseconds(ms);
            return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
        }

        /// <summary>
        /// 解析时间输入: 支持纯秒数 (如 "3.5") 或 mm:ss.fff 格式。
        /// </summary>
        /// <returns>解析后的毫秒数, 失败返回 null。</returns>
        public static double? ParseTimeInput(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            input = input.Trim();

            // mm:ss 或 mm:ss.fff 格式
            if (input.Contains(':'))
            {
                var parts = input.Split(':');
                if (
                    parts.Length == 2
                    && double.TryParse(
                        parts[0],
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out double mins
                    )
                    && double.TryParse(
                        parts[1],
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out double secs
                    )
                )
                {
                    return (mins * 60 + secs) * 1000.0;
                }
                return null;
            }

            // 纯数字: ≤100 视为秒, >100 视为毫秒
            if (
                double.TryParse(
                    input,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out double number
                )
            )
            {
                return number > 100 ? number : number * 1000.0;
            }

            return null;
        }
    }
}
