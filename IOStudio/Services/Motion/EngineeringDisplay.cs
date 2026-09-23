using System;
using System.Globalization;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 值域显示 — 把归一化值域翻译成人看得懂的东西 (工程量 + 单位 + 量程来源)。
    ///
    /// <para>为什么需要: 内容层是归一化 0..1, 作者写 "0.8" 时无法从界面知道它到底是 18°
    /// 还是 240mm。ABI v2 已把精度做到浮点无损, 但<b>人看不到</b>就等于没兑现。</para>
    ///
    /// <para>显示走与实播<b>同一个</b> <see cref="EngineeringValueConverter"/>,
    /// 保证"屏幕上看到的就是要下发的"。</para>
    ///
    /// <para>纯函数, 可单测。</para>
    /// </summary>
    public static class EngineeringDisplay
    {
        /// <summary>量程描述: (显示文本, 来源说明)。优先级与换算一致。</summary>
        public static (string Text, string Source) DescribeRange(
            float? trackRange,
            (float Min, float Max)? deviceRange,
            string? unit,
            string? valueType
        )
        {
            if (IsBool(valueType))
            {
                return (
                    "开/关",
                    trackRange is null && deviceRange is null
                        ? "bool 轨道 · 未声明量程"
                        : "bool 轨道"
                );
            }

            if (trackRange is { } tr && tr > 0f)
                return ($"±{Fmt(tr / 2f)}{Suffix(unit)}", "轨道显式覆盖 (engineering_range)");

            if (deviceRange is { } d && d.Max > d.Min)
            {
                bool symmetric = Math.Abs(d.Min + d.Max) < 1e-4 && d.Min < 0;
                string text = symmetric
                    ? $"±{Fmt(d.Max)}{Suffix(unit)}"
                    : $"{Fmt(d.Min)}..{Fmt(d.Max)}{Suffix(unit)}";
                return (text, "设备声明量程 (capabilities)");
            }

            return ("0..1", "未声明量程 · 直通 (归一化值)");
        }

        /// <summary>
        /// 归一化实时值 → 显示文本。有量程时显示工程量 (与实播同源换算), 否则显示归一化值。
        /// </summary>
        public static string FormatLive(
            float normalized,
            string? valueType,
            float? neutralValue,
            float? trackRange,
            (float Min, float Max)? deviceRange,
            string? unit
        )
        {
            if (IsBool(valueType))
                return normalized >= 0.5f ? "ON" : "OFF";

            bool hasRange =
                (trackRange is { } tr && tr > 0f)
                || (deviceRange is { } d && d.Max > d.Min);

            if (!hasRange)
                return Fmt(normalized);

            float engineering = EngineeringValueConverter.ToEngineering(
                normalized,
                valueType,
                neutralValue,
                trackRange,
                deviceRange
            );
            return $"{Fmt(engineering)}{Suffix(unit)}";
        }

        private static bool IsBool(string? valueType) =>
            string.Equals(valueType, "bool", StringComparison.OrdinalIgnoreCase);

        private static string Fmt(float value) =>
            value.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>把设备声明的单位转成人读的符号。</summary>
        private static string Suffix(string? unit) =>
            unit switch
            {
                null or "" => "",
                "deg" => "°",
                "bool" => "",
                _ => unit,
            };
    }
}
