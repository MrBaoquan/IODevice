using System;
using System.Globalization;
using System.Linq;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 值显示规则 —— 面板上"这个 0..1 的值该怎么给人看"。
    ///
    /// <para><b>为什么不显示原始 0..1</b>: 作者写 0.8 时无法从界面知道它到底是 18° 还是 240mm,
    /// 也不知道 0 是"下限"还是"复位中位"。<b>为什么不显示工程量</b>: 工程量要设备声明量程才有意义,
    /// 而各设备行程不同 (1000mm / 300mm…), 拿它当主显示等于让内容依赖具体硬件。</para>
    ///
    /// <para><b>百分比是本项目的主显示</b>: 它以"复位原点"为 0%, 两端各 ±100%。
    /// 于是 <c>neutral_value=0.5</c> (复位原点居中) → <c>-100%..+100%</c>;
    /// <c>neutral_value=0</c> (原点在下限) → <c>0..100%</c>。两种原点约定各自显示正确,
    /// 且都与设备行程无关 —— 同一份动作换到另一台行程不同的设备, 百分比含义不变。</para>
    ///
    /// <para>显示走与实播<b>同一个</b>量程解析 (<see cref="EngineeringValueConverter.ResolveRange"/>),
    /// 保证"屏幕上看到的区间"与"实际下发的区间"不会分叉 —— 曾经这两处各算各的
    /// (显示写死 <c>±range/2</c>), 导致 <c>neutral_value=0</c> 的轨道显示 <c>±500</c>
    /// 而实际映射是 <c>0..1000</c>。</para>
    ///
    /// <para>纯函数, 可单测。</para>
    /// </summary>
    public enum ValueDisplayMode
    {
        /// <summary>由 <c>neutral_value</c> 推导 (0 → 单向百分比; 0.5 → 双向百分比)。缺省。</summary>
        Auto,

        /// <summary>单向百分比 0..100% (原点在下限)。</summary>
        Percent,

        /// <summary>双向百分比, 以复位原点为 0% (原点居中时 -100%..+100%)。</summary>
        Symmetric,

        /// <summary>工程量 (带单位), 由轨道或设备声明的量程换算。</summary>
        Engineering,
    }

    public static class EngineeringDisplay
    {
        /// <summary>解析 JSON 里的 <c>value_display</c>; 未知或为空一律回落到 <see cref="ValueDisplayMode.Auto"/>。</summary>
        public static ValueDisplayMode ParseMode(string? raw) =>
            raw?.Trim().ToLowerInvariant() switch
            {
                "percent" => ValueDisplayMode.Percent,
                "symmetric" => ValueDisplayMode.Symmetric,
                "engineering" => ValueDisplayMode.Engineering,
                _ => ValueDisplayMode.Auto,
            };

        /// <summary>写回 JSON 用的稳定标签 (Auto 返回 null —— 缺省不落盘)。</summary>
        public static string? ModeToTag(ValueDisplayMode mode) =>
            mode switch
            {
                ValueDisplayMode.Percent => "percent",
                ValueDisplayMode.Symmetric => "symmetric",
                ValueDisplayMode.Engineering => "engineering",
                _ => null,
            };

        /// <summary>
        /// 生效模式。<see cref="ValueDisplayMode.Auto"/> 由原点约定推导:
        /// 原点在下限 (neutral=0) → 单向; 原点居中 → 双向。
        /// </summary>
        public static ValueDisplayMode ResolveMode(
            string? declared,
            string? valueType,
            float? neutralValue
        )
        {
            var mode = ParseMode(declared);
            if (mode != ValueDisplayMode.Auto)
                return mode;

            // bool 轨是开关, 双向没有意义 —— 直接用开/关 (见 DescribeRange)
            if (string.Equals(valueType, "bool", StringComparison.OrdinalIgnoreCase))
                return ValueDisplayMode.Auto;

            float neutral = TrackValueEvaluator.ResolveNeutral(valueType, neutralValue);
            return Math.Abs(neutral) < 1e-4f ? ValueDisplayMode.Percent : ValueDisplayMode.Symmetric;
        }

        /// <summary>
        /// 归一化值 → 百分比。
        ///
        /// <para>双向时以复位原点为 0%, 上行程 <c>(1-origin)</c> 映射到 +100%,
        /// 下行程 <c>origin</c> 映射到 -100% —— 于是 <c>origin=0.5</c> 恰好给出对称的 ±100%,
        /// 而 <c>origin=0</c> 时下行程为空, 自然退化成 0..100%, 不会产生无意义的下行程。</para>
        /// </summary>
        public static float ToPercent(float normalized, float neutral, bool symmetric)
        {
            if (!symmetric)
                return normalized * 100f;

            if (neutral <= 1e-4f)
                return normalized * 100f;

            return normalized >= neutral
                ? (normalized - neutral) / (1f - neutral) * 100f
                : -(neutral - normalized) / neutral * 100f;
        }

        /// <summary>量程描述: (显示文本, 来源说明)。</summary>
        public static (string Text, string Source) DescribeRange(
            string? valueDisplay,
            float? trackRange,
            (float Min, float Max)? deviceRange,
            string? unit,
            string? valueType,
            float? neutralValue
        )
        {
            if (IsBool(valueType))
                return ("开/关", "bool 轨道");

            float neutral = TrackValueEvaluator.ResolveNeutral(valueType, neutralValue);
            var mode = ResolveMode(valueDisplay, valueType, neutralValue);

            switch (mode)
            {
                case ValueDisplayMode.Percent:
                    return ("0..100%", "百分比 · 原点在下限");

                case ValueDisplayMode.Symmetric:
                {
                    // 端点由同一个换算函数算出 —— 显示与实际永远吻合
                    float lo = ToPercent(0f, neutral, true);
                    float hi = ToPercent(1f, neutral, true);
                    return ($"{Fmt(lo)}..{Fmt(hi)}%", "百分比 · 复位原点居中");
                }

                default:
                    return DescribeEngineering(trackRange, deviceRange, unit, neutral, valueType);
            }
        }

        /// <summary>工程量描述 —— 量程一律经 <see cref="EngineeringValueConverter.ResolveRange"/> 解析。</summary>
        private static (string Text, string Source) DescribeEngineering(
            float? trackRange,
            (float Min, float Max)? deviceRange,
            string? unit,
            float neutral,
            string? valueType
        )
        {
            // 必须把**实际**复位原点传进去 —— 传 null 会让 ResolveRange 回落到 float 的默认 0.5,
            // 于是 neutral=0 的轨道被算成 (-500,500) 又显示成 ±500 (本文件要修的那个 bug)。
            var resolved = EngineeringValueConverter.ResolveRange(
                valueType,
                neutral,
                trackRange,
                deviceRange
            );

            if (resolved is not { } r)
                return ("0..1", "未声明量程 · 直通 (归一化值)");

            bool symmetric = Math.Abs(r.Min + r.Max) < 1e-4f && r.Min < 0;
            string text = symmetric
                ? $"±{Fmt(r.Max)}{Suffix(unit)}"
                : $"{Fmt(r.Min)}..{Fmt(r.Max)}{Suffix(unit)}";

            string source = trackRange is { } tr && tr > 0f
                ? "工程量 · 轨道显式覆盖 (engineering_range)"
                : "工程量 · 设备声明量程";

            return (text, source);
        }

        /// <summary>
        /// 归一化实时值 → 显示文本 (与实播同源换算, 保证"看到的就是要下发的")。
        /// </summary>
        public static string FormatLive(
            string? valueDisplay,
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

            float neutral = TrackValueEvaluator.ResolveNeutral(valueType, neutralValue);
            var mode = ResolveMode(valueDisplay, valueType, neutralValue);

            if (mode == ValueDisplayMode.Engineering)
            {
                float engineering = EngineeringValueConverter.ToEngineering(
                    normalized,
                    valueType,
                    neutralValue,
                    trackRange,
                    deviceRange
                );
                return $"{Fmt(engineering)}{Suffix(unit)}";
            }

            bool symmetric = mode == ValueDisplayMode.Symmetric;
            return $"{Fmt(ToPercent(normalized, neutral, symmetric))}%";
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
