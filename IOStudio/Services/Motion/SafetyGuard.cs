using System;
using System.Collections.Generic;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// [已废弃] C# 安全保护服务 — 限位 Clamp + 速度限制 + 平滑回中。
    /// 安全保护已迁移至 C++ SafetyGuard (Phase 3.1.4)，
    /// 通过 IOToolkit.MotionPlayer.SetSafetyConfig() 配置。
    /// 保留仅用于无 C++ DLL 环境的回退。
    /// </summary>
    [Obsolete("安全保护已迁移至 C++ IODevice.dll，使用 IOToolkit.MotionPlayer.SetSafetyConfig() 配置")]
    public class SafetyGuard
    {
        /// <summary>最大速度 (归一化单位/秒), 超过此速度的输出将被截断</summary>
        public float MaxVelocityPerSecond { get; set; } = 2.0f;

        // 每个通道的上一帧值 (用于速度检查)
        private readonly Dictionary<string, float> _lastValues = new();

        /// <summary>
        /// 检查并修正输出值, 返回安全值
        /// </summary>
        /// <param name="channelKey">通道标识 (如 "Chair.OAxis_00")</param>
        /// <param name="rawValue">原始插值输出值</param>
        /// <param name="deltaTimeSec">帧间隔时间 (秒)</param>
        /// <param name="minValue">最小值限制</param>
        /// <param name="maxValue">最大值限制</param>
        /// <returns>修正后的安全值</returns>
        public float Check(
            string channelKey,
            float rawValue,
            double deltaTimeSec,
            float minValue = 0f,
            float maxValue = 1f
        )
        {
            // Step 1: Clamp 到 [min, max]
            float clamped = Math.Clamp(rawValue, minValue, maxValue);

            // Step 2: 速度限制
            if (deltaTimeSec > 0.001 && _lastValues.TryGetValue(channelKey, out float lastVal))
            {
                float velocity = Math.Abs(clamped - lastVal) / (float)deltaTimeSec;
                if (velocity > MaxVelocityPerSecond)
                {
                    // 截断到允许的最大变化量
                    float maxDelta = MaxVelocityPerSecond * (float)deltaTimeSec;
                    float direction = Math.Sign(clamped - lastVal);
                    clamped = lastVal + direction * maxDelta;

                    // 触发安全事件
                    SafetyTriggered?.Invoke(
                        $"Speed limit on [{channelKey}]: {velocity:F2}/s > {MaxVelocityPerSecond:F2}/s"
                    );
                }
            }

            // Step 3: 记录当前值
            _lastValues[channelKey] = clamped;

            return clamped;
        }

        /// <summary>
        /// 生成平滑回中序列: 当前值 → centerValue, 在 durationMs 内完成
        /// </summary>
        /// <param name="currentValue">当前值</param>
        /// <param name="centerValue">目标中位值</param>
        /// <param name="durationMs">回中总时长 (毫秒)</param>
        /// <param name="stepMs">每步间隔 (毫秒, 默认 ~60fps)</param>
        /// <returns>回中值序列</returns>
        public static IEnumerable<float> GenerateReturnToCenter(
            float currentValue,
            float centerValue = 0.5f,
            double durationMs = 1000,
            double stepMs = 16.67
        )
        {
            int steps = Math.Max(1, (int)(durationMs / stepMs));
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                // Smoothstep 缓入缓出
                float eased = t * t * (3f - 2f * t);
                yield return currentValue + (centerValue - currentValue) * eased;
            }
        }

        /// <summary>
        /// 重置所有通道的历史值 (用于 Stop/Unload 后清理)
        /// </summary>
        public void Reset()
        {
            _lastValues.Clear();
        }

        /// <summary>安全限制触发事件</summary>
        public event Action<string>? SafetyTriggered;
    }
}
