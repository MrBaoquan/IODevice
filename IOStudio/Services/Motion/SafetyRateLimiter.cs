using System;
using System.Collections.Generic;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 输出速率限制 — 限制每个通道归一化值的变化速度, 防止编辑器命令出物理上过猛的跳变。
    ///
    /// <para>语义与 C++ <c>MotionPlayer::SafetyGuard</c> 一致 (maxRatePerSecond 作用于归一化 0..1),
    /// 但它作用在**实播路径**上 —— C++ 那份只在没人调用的原生 Tick 里生效, 等于没有被执行。</para>
    ///
    /// <para><b>关键</b>: 时间不连续 (Seek / 开始播放 / 长卡顿) 必须视为"重置基线"而不是限速,
    /// 否则拖动播放头会被限速抹平, 预览跟不上操作。</para>
    ///
    /// <para>纯逻辑, 无 IO, 可单测。</para>
    /// </summary>
    public sealed class SafetyRateLimiter
    {
        /// <summary>步长超过该阈值视为时间不连续 (秒)。</summary>
        public const double DiscontinuityThresholdSec = 0.5;

        private readonly Dictionary<string, float> _last = new(StringComparer.Ordinal);

        /// <summary>是否启用 (设备未声明速率时调用方本就应跳过, 这里是额外的总开关)。</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 限速一次。首次见到该通道只建立基线; 时间不连续时重置基线。
        /// </summary>
        /// <param name="channelKey">通道键 (设备+通道)</param>
        /// <param name="target">目标归一化值</param>
        /// <param name="deltaSec">距上次派发的时间步长 (秒)</param>
        /// <param name="maxRatePerSecond">设备声明的最大变化速率 (归一化单位/秒)</param>
        public float Limit(string channelKey, float target, double deltaSec, float maxRatePerSecond)
        {
            if (!Enabled || maxRatePerSecond <= 0f || string.IsNullOrEmpty(channelKey))
                return target;

            if (!_last.TryGetValue(channelKey, out float last))
            {
                _last[channelKey] = target; // 首次: 建立基线, 不限制
                return target;
            }

            // 时间不连续 (Seek/开始播放/长卡顿) → 重置基线, 让目标直接生效
            if (deltaSec <= 0 || deltaSec > DiscontinuityThresholdSec)
            {
                _last[channelKey] = target;
                return target;
            }

            float maxDelta = maxRatePerSecond * (float)deltaSec;
            float diff = target - last;
            if (Math.Abs(diff) <= maxDelta)
            {
                _last[channelKey] = target;
                return target;
            }

            float limited = last + Math.Sign(diff) * maxDelta;
            _last[channelKey] = limited;
            return limited;
        }

        /// <summary>清空全部通道基线 (卸载/切换时间轴时调用)。</summary>
        public void Reset() => _last.Clear();
    }
}
