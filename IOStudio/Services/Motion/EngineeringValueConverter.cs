namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 归一化值 (0..1) → 工程量 的换算。抽成纯函数以便单测 ——
    /// 这是"内容值域 → 通道 ABI"的唯一收口，出错会让物理平台动到错误位置，
    /// 因此必须有测试锁定三级优先级。
    ///
    /// <para>优先级 (与 <c>NativeMotionPlaybackEngine.ToEngineering</c> 一致):</para>
    /// <list type="number">
    /// <item>轨道 <c>engineering_range</c> —— 绕中位的对称行程 <c>(normalized - neutral) * range</c>。
    ///   显式覆盖，代表"该轨道的工程含义与设备声明不同"，属于需要复核的偏离。</item>
    /// <item>设备声明量程 —— 绝对区间 <c>min + normalized * (max - min)</c>，支持非对称。
    ///   这是首选来源 (内容保持设备无关)。</item>
    /// <item>直通 —— 两者都未声明时原样输出，保持既有动作文件行为不变。</item>
    /// </list>
    /// </summary>
    public static class EngineeringValueConverter
    {
        /// <summary>
        /// 三级解析。参数均为值域声明，不含任何设备/IO 访问，便于测试与复用。
        /// </summary>
        /// <param name="normalized">归一化值 (0..1)</param>
        /// <param name="valueType">"float" | "bool" (用于推断默认中位值)</param>
        /// <param name="neutralValue">显式中位值 (null = 按类型默认)</param>
        /// <param name="trackRange">轨道级对称行程 (null/≤0 = 未声明)</param>
        /// <param name="declaredRange">设备声明量程 (null = 未声明)</param>
        public static float ToEngineering(
            float normalized,
            string? valueType,
            float? neutralValue,
            float? trackRange,
            (float Min, float Max)? declaredRange
        )
        {
            // ① 轨道显式覆盖: 绕中位的对称行程
            if (trackRange is { } range && range > 0f)
                return (normalized - TrackValueEvaluator.ResolveNeutral(valueType, neutralValue))
                    * range;

            // ② 设备声明量程: 一般 (可能非对称) 区间映射
            if (declaredRange is { } declared && declared.Max > declared.Min)
                return declared.Min + normalized * (declared.Max - declared.Min);

            // ③ 直通
            return normalized;
        }
    }
}
