using Xunit;
using IOStudio.Services.Motion;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// 归一化 → 工程量 换算契约测试。
    ///
    /// 这里是"内容值域 → 通道 ABI"的唯一收口，出错会让物理平台动到错误位置，
    /// 所以三级优先级必须被锁死 (曾因量程来源缺失导致输出被整型截断成 0)。
    /// </summary>
    public class EngineeringValueConverterTests
    {
        // ── ① 轨道 engineering_range: 绕中位的对称行程 ──────────

        [Theory]
        [InlineData(0.8f, 18f)] // (0.8-0.5)*60
        [InlineData(0.3f, -12f)] // (0.3-0.5)*60
        [InlineData(0.5f, 0f)] // 中位
        [InlineData(0.0f, -30f)]
        [InlineData(1.0f, 30f)]
        public void Level1_TrackRange_IsSymmetricAboutNeutral(float normalized, float expected)
        {
            float v = EngineeringValueConverter.ToEngineering(
                normalized,
                "float",
                null,
                60f,
                null
            );
            Assert.Equal(expected, v, precision: 4);
        }

        [Fact]
        public void Level1_RespectsExplicitNeutralValue()
        {
            // neutral 0.75, range 60 → (0.8-0.75)*60 = 3
            float v = EngineeringValueConverter.ToEngineering(0.8f, "float", 0.75f, 60f, null);
            Assert.Equal(3f, v, precision: 4);
        }

        [Fact]
        public void Level1_BeatsDeclaredRange()
        {
            // 轨道显式覆盖优先于设备声明 (属于需复核的偏离)
            float v = EngineeringValueConverter.ToEngineering(
                0.8f,
                "float",
                null,
                60f,
                (-300f, 300f)
            );
            Assert.Equal(18f, v, precision: 4);
        }

        // ── ② 设备声明量程: 绝对区间 min..max ──────────────────

        [Theory]
        [InlineData(0.0f, -30f)] // min
        [InlineData(0.5f, 0f)] // 中位 (对称时落在 0)
        [InlineData(0.8f, 18f)]
        [InlineData(0.3f, -12f)]
        [InlineData(1.0f, 30f)] // max
        public void Level2_DeclaredSymmetricRange(float normalized, float expected)
        {
            float v = EngineeringValueConverter.ToEngineering(
                normalized,
                "float",
                null,
                null,
                (-30f, 30f)
            );
            Assert.Equal(expected, v, precision: 4);
        }

        [Fact]
        public void Level2_SupportsAsymmetricRange()
        {
            // (-10, 30): 归一化 0 → -10, 0.5 → 10, 1 → 30
            Assert.Equal(
                -10f,
                EngineeringValueConverter.ToEngineering(0f, "float", null, null, (-10f, 30f)),
                precision: 4
            );
            Assert.Equal(
                10f,
                EngineeringValueConverter.ToEngineering(0.5f, "float", null, null, (-10f, 30f)),
                precision: 4
            );
            Assert.Equal(
                30f,
                EngineeringValueConverter.ToEngineering(1f, "float", null, null, (-10f, 30f)),
                precision: 4
            );
        }

        [Fact]
        public void Level2_BoolChannel_MapsToUnitRange()
        {
            Assert.Equal(
                0f,
                EngineeringValueConverter.ToEngineering(0f, "bool", null, null, (0f, 1f)),
                precision: 4
            );
            Assert.Equal(
                1f,
                EngineeringValueConverter.ToEngineering(1f, "bool", null, null, (0f, 1f)),
                precision: 4
            );
        }

        [Fact]
        public void Level2_DegenerateOrInvertedRange_IsIgnored()
        {
            // max <= min 视为未声明 → 直通 (避免除零/反向映射)
            Assert.Equal(
                0.3f,
                EngineeringValueConverter.ToEngineering(0.3f, "float", null, null, (5f, 5f)),
                precision: 4
            );
            Assert.Equal(
                0.3f,
                EngineeringValueConverter.ToEngineering(0.3f, "float", null, null, (30f, -30f)),
                precision: 4
            );
        }

        // ── ③ 直通 ───────────────────────────────────────────

        [Fact]
        public void Level3_NoDeclaration_PassesThrough()
        {
            Assert.Equal(
                0.3f,
                EngineeringValueConverter.ToEngineering(0.3f, "float", null, null, null),
                precision: 4
            );
            // 轨道量程 0 / 负数 视为未声明
            Assert.Equal(
                0.3f,
                EngineeringValueConverter.ToEngineering(0.3f, "float", null, 0f, null),
                precision: 4
            );
            Assert.Equal(
                0.3f,
                EngineeringValueConverter.ToEngineering(0.3f, "float", null, -60f, null),
                precision: 4
            );
        }

        // ── 真实回归: Simulator 俯仰轴 (±30°) ─────────────────

        [Theory]
        [InlineData(0.8f, 18f)] // idle 峰值
        [InlineData(0.5f, 0f)] // idle 谷值
        [InlineData(0.3f, -12f)] // clip 值
        public void Regression_SimulatorPitchAxis(float normalized, float expected)
        {
            // 探针实测 (DO 为整型, 故实际落盘为 -12 → -11 的截断)
            float v = EngineeringValueConverter.ToEngineering(
                normalized,
                "float",
                null,
                null,
                (-30f, 30f)
            );
            Assert.Equal(expected, v, precision: 4);
        }
    }
}
