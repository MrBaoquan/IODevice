using Xunit;
using IOStudio.Services.Motion;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// 输出速率限制测试。
    ///
    /// 背景: C++ SafetyGuard 的 maxRatePerSecond 只存在于没人调用的原生 Tick 里,
    /// C# 实播路径此前**完全无限速** —— 编辑器可以命令任意快的变化。
    ///
    /// 两条关键语义:
    /// 1. 只在设备声明速率时才限速 (未声明 → 行为与引入前一致);
    /// 2. 时间不连续 (Seek/开始播放/长卡顿) 必须重置基线, 否则拖动播放头会被限速抹平。
    /// </summary>
    public class SafetyRateLimiterTests
    {
        private const float Rate = 5f; // 归一化单位/秒

        [Fact]
        public void FirstSample_EstablishesBaseline_NoLimit()
        {
            var limiter = new SafetyRateLimiter();
            // 首次见到该通道: 直接接受 (否则起播第一帧会被削)
            Assert.Equal(0.9f, limiter.Limit("dev|ch", 0.9f, 0.0167, Rate), precision: 4);
        }

        [Fact]
        public void SmallStepWithinLimit_PassesThrough()
        {
            var limiter = new SafetyRateLimiter();
            limiter.Limit("dev|ch", 0.5f, 0.0167, Rate); // 基线
            // 5/s × 0.0167s ≈ 0.0835 允许量; 0.5 → 0.55 在限内
            Assert.Equal(0.55f, limiter.Limit("dev|ch", 0.55f, 0.0167, Rate), precision: 4);
        }

        [Fact]
        public void LargeStep_IsClampedToMaxDelta()
        {
            var limiter = new SafetyRateLimiter();
            limiter.Limit("dev|ch", 0.5f, 0.0167, Rate); // 基线
            // 一步跳到 1.0 超出允许量 → 被削到 0.5 + 5*0.0167
            float limited = limiter.Limit("dev|ch", 1.0f, 0.0167, Rate);
            Assert.Equal(0.5f + Rate * 0.0167f, limited, precision: 4);
        }

        [Fact]
        public void Clamping_ConvergesOverMultipleFrames()
        {
            var limiter = new SafetyRateLimiter();
            limiter.Limit("dev|ch", 0f, 0.0167, Rate);
            float v = 0f;
            // 目标 1.0: 每帧最多 0.0835 → 约 12 帧到位
            for (int i = 0; i < 12; i++)
                v = limiter.Limit("dev|ch", 1f, 0.0167, Rate);
            Assert.Equal(1f, v, precision: 3);
        }

        [Fact]
        public void NegativeDirection_IsAlsoClamped()
        {
            var limiter = new SafetyRateLimiter();
            limiter.Limit("dev|ch", 0.5f, 0.0167, Rate);
            float limited = limiter.Limit("dev|ch", -0.5f, 0.0167, Rate);
            Assert.Equal(0.5f - Rate * 0.0167f, limited, precision: 4);
        }

        // ── 时间不连续 → 重置 ─────────────────────────────

        [Fact]
        public void SeekLikeTimeJump_ResetsInsteadOfClamping()
        {
            var limiter = new SafetyRateLimiter();
            limiter.Limit("dev|ch", 0.0f, 0.0167, Rate); // 基线 0
            // 播放头跳到很后面: 步长 2s 远超阈值 → 直接接受目标 (不被抹平)
            Assert.Equal(0.9f, limiter.Limit("dev|ch", 0.9f, 2.0, Rate), precision: 4);
        }

        [Fact]
        public void BackwardTime_Resets()
        {
            var limiter = new SafetyRateLimiter();
            limiter.Limit("dev|ch", 0.8f, 0.0167, Rate);
            // 步长为负 (回到过去) → 重置
            Assert.Equal(0.1f, limiter.Limit("dev|ch", 0.1f, -0.5, Rate), precision: 4);
        }

        [Fact]
        public void ZeroDelta_Resets()
        {
            var limiter = new SafetyRateLimiter();
            limiter.Limit("dev|ch", 0.8f, 0.0167, Rate);
            Assert.Equal(0.2f, limiter.Limit("dev|ch", 0.2f, 0.0, Rate), precision: 4);
        }

        [Fact]
        public void Reset_ClearsBaselines()
        {
            var limiter = new SafetyRateLimiter();
            limiter.Limit("dev|ch", 0.0f, 0.0167, Rate);
            limiter.Reset();
            // 清空后首个样本重新建立基线, 不限制
            Assert.Equal(1.0f, limiter.Limit("dev|ch", 1.0f, 0.0167, Rate), precision: 4);
        }

        // ── 关闭与边界 ────────────────────────────────────

        [Fact]
        public void UndeclaredRate_MeansNoLimiting()
        {
            var limiter = new SafetyRateLimiter();
            limiter.Limit("dev|ch", 0f, 0.0167, Rate);
            // 设备未声明 (<=0) → 不限速
            Assert.Equal(
                1.0f,
                limiter.Limit("dev|ch", 1.0f, 0.0167, 0f),
                precision: 4
            );
        }

        [Fact]
        public void Disabled_MeansNoLimiting()
        {
            var limiter = new SafetyRateLimiter { Enabled = false };
            limiter.Limit("dev|ch", 0f, 0.0167, Rate);
            Assert.Equal(1.0f, limiter.Limit("dev|ch", 1.0f, 0.0167, Rate), precision: 4);
        }

        [Fact]
        public void ChannelsAreIndependent()
        {
            var limiter = new SafetyRateLimiter();
            limiter.Limit("dev|a", 0f, 0.0167, Rate);
            limiter.Limit("dev|b", 1f, 0.0167, Rate);
            // a 被限制, b 未受影响 (以各自基线计)
            Assert.Equal(0f + Rate * 0.0167f, limiter.Limit("dev|a", 1f, 0.0167, Rate), precision: 4);
            Assert.Equal(1f, limiter.Limit("dev|b", 1f, 0.0167, Rate), precision: 4);
        }
    }
}
