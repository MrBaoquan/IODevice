using Xunit;
using IOStudio.Services.Motion;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// 值显示规则测试 —— 归一化 0..1 在面板上要"看得懂"。
    ///
    /// <para><b>两种原点约定</b>: 有些通道的 0 是下限 (电缸升降停在最低位),
    /// 有些的 0.5 是复位原点 (俯仰/横滚的中位)。百分比以复位原点为 0%、两端各 ±100%,
    /// 于是两者各自显示正确 —— 0 → <c>0..100%</c>, 0.5 → <c>-100%..+100%</c>。</para>
    ///
    /// <para><b>关键不变量</b>: 显示出来的区间必须与<b>实际下发</b>的换算同源
    /// (都走 <see cref="EngineeringValueConverter.ResolveRange"/>)。曾经两者各算各的 ——
    /// 显示写死 <c>±range/2</c>, 于是 <c>neutral=0</c> 的轨道显示 <c>±500</c> 而实际是 <c>0..1000</c>。
    /// 本文件末尾那条用例就是给这个 bug 上的锁。</para>
    /// </summary>
    public class EngineeringDisplayTests
    {
        // ── 默认规则: 由复位原点推导 ──────────────────────

        [Fact]
        public void Auto_NeutralAtLowerBound_IsUnipolarPercent()
        {
            // 原点在下限 (neutral=0): 0..100%
            var (text, source) = EngineeringDisplay.DescribeRange(
                null, 1000f, null, "mm", "float", 0f
            );
            Assert.Equal("0..100%", text);
            Assert.Contains("原点在下限", source);
        }

        [Fact]
        public void Auto_NeutralAtCenter_IsSymmetricPercent()
        {
            // 复位原点居中 (neutral=0.5): -100%..+100%
            var (text, source) = EngineeringDisplay.DescribeRange(
                null, null, (-30f, 30f), "deg", "float", null
            );
            Assert.Equal("-100..100%", text);
            Assert.Contains("复位原点居中", source);
        }

        [Fact]
        public void Auto_NeutralAtLowerBound_DoesNotProduceMeaninglessLowerHalf()
        {
            // neutral=0 走双向时下行程为空 —— 必须自然退化成 0..100%, 而不是凭空造负半轴
            var (text, _) = EngineeringDisplay.DescribeRange(
                "symmetric", 1000f, null, "mm", "float", 0f
            );
            Assert.Equal("0..100%", text);
        }

        // ── 显式规则覆盖 ──────────────────────────────────

        [Fact]
        public void ExplicitPercent_OverridesCenterOrigin()
        {
            // 复位原点居中, 但作者想看"从下限算起"的绝对百分比
            var (text, _) = EngineeringDisplay.DescribeRange(
                "percent", null, null, "deg", "float", 0.5f
            );
            Assert.Equal("0..100%", text);
        }

        [Fact]
        public void ExplicitEngineering_UsesDeclaredRange()
        {
            var (text, source) = EngineeringDisplay.DescribeRange(
                "engineering", null, (-30f, 30f), "deg", "float", null
            );
            Assert.Equal("±30°", text);
            Assert.Contains("设备声明量程", source);
        }

        [Fact]
        public void ExplicitEngineering_TrackOverrideWinsOverDeclared()
        {
            var (text, source) = EngineeringDisplay.DescribeRange(
                "engineering", 60f, (-300f, 300f), "mm", "float", null
            );
            Assert.Equal("±30mm", text);
            Assert.Contains("engineering_range", source);
        }

        [Fact]
        public void ExplicitEngineering_NoDeclaration_FallsBackToNormalized()
        {
            var (text, source) = EngineeringDisplay.DescribeRange(
                "engineering", null, null, "", "float", null
            );
            Assert.Equal("0..1", text);
            Assert.Contains("直通", source);
        }

        [Fact]
        public void BoolTrack_IsOnOff_RegardlessOfMode()
        {
            foreach (string? mode in new[] { null, "percent", "symmetric", "engineering" })
            {
                var (text, _) = EngineeringDisplay.DescribeRange(
                    mode, null, (0f, 1f), "bool", "bool", null
                );
                Assert.Equal("开/关", text);
            }
        }

        [Fact]
        public void UnknownModeTag_FallsBackToAuto()
        {
            // 未知标签 (手改 JSON 打错) 不能变成"没有规则", 只会回落到自动
            var (text, _) = EngineeringDisplay.DescribeRange(
                "bogus", 1000f, null, "mm", "float", 0f
            );
            Assert.Equal("0..100%", text);
        }

        // ── 实时值显示 ────────────────────────────────────

        [Theory]
        [InlineData(0.0f, "0%")]
        [InlineData(0.5f, "50%")]
        [InlineData(1.0f, "100%")]
        public void Live_UnipolarOrigin_MapsToZeroToHundred(float value, string expected)
        {
            Assert.Equal(
                expected,
                EngineeringDisplay.FormatLive(null, value, "float", 0f, 1000f, null, "mm")
            );
        }

        [Theory]
        [InlineData(0.0f, "-100%")]
        [InlineData(0.25f, "-50%")]
        [InlineData(0.5f, "0%")]
        [InlineData(0.75f, "50%")]
        [InlineData(1.0f, "100%")]
        public void Live_CenterOrigin_MapsToSymmetricPercent(float value, string expected)
        {
            Assert.Equal(
                expected,
                EngineeringDisplay.FormatLive(null, value, "float", null, null, (-30f, 30f), "deg")
            );
        }

        [Fact]
        public void Live_EngineeringMode_StillShowsEngineeringUnits()
        {
            // 工程量模式是给现场校准用的逃生口, 行为与改动前一致
            Assert.Equal(
                "18°",
                EngineeringDisplay.FormatLive("engineering", 0.8f, "float", null, null, (-30f, 30f), "deg")
            );
            Assert.Equal(
                "-12°",
                EngineeringDisplay.FormatLive("engineering", 0.3f, "float", null, 60f, null, "deg")
            );
            Assert.Equal(
                "-12",
                EngineeringDisplay.FormatLive("engineering", 0.3f, "float", null, 60f, null, "")
            );
        }

        [Theory]
        [InlineData(0.9f, "ON")]
        [InlineData(0.1f, "OFF")]
        public void Live_BoolTrack_IsOnOff(float value, string expected)
        {
            Assert.Equal(
                expected,
                EngineeringDisplay.FormatLive(null, value, "bool", null, null, (0f, 1f), "bool")
            );
        }

        // ── 回归锁: 显示区间必须与换算区间同源 ────────────

        [Theory]
        [InlineData(0f, 1000f)]      // 原点在下限 (电缸)
        [InlineData(null, null)]     // 未声明任何量程
        [InlineData(60f, 60f)]       // 轨道覆盖
        public void DescribeRange_IntervalEndpointsMatchConverter(float? trackRange, float? declaredMax)
        {
            // 这个 bug 的现象: heave (engineering_range=1000, neutral=0) 显示 "±500",
            // 而实际映射是 0..1000 —— 显示与换算各算各的。锁住: 同一组输入,
            // DescribeRange 给出的两端必须等于 ResolveRange 算出的区间端点。
            (float Min, float Max)? declared =
                declaredMax is { } mx ? ((float)(-mx), (float)mx) : null;

            var resolved = EngineeringValueConverter.ResolveRange("float", 0f, trackRange, declared);
            var (text, _) = EngineeringDisplay.DescribeRange(
                "engineering", trackRange, declared, "mm", "float", 0f
            );

            if (resolved is not { } r)
            {
                Assert.Equal("0..1", text);
                return;
            }

            // 对称区间渲染成 "±X" 而不是 "-X..X" —— 两种形态都要认
            bool symmetric = Math.Abs(r.Min + r.Max) < 1e-4f && r.Min < 0;
            if (symmetric)
            {
                Assert.Contains($"±{r.Max.ToString("0.##")}", text);
                return;
            }

            Assert.Contains(r.Min.ToString("0.##"), text);
            Assert.Contains(r.Max.ToString("0.##"), text);
        }

        [Fact]
        public void DescribeRange_HeaveCase_IsNotMisreportedAsSymmetric()
        {
            // 具体复现场景: engineering_range=1000 + neutral=0 + 无设备量程
            // → 真实区间 0..1000, 绝不能再显示成 ±500
            var (text, _) = EngineeringDisplay.DescribeRange(
                "engineering", 1000f, null, "mm", "float", 0f
            );
            Assert.Equal("0..1000mm", text);
            Assert.DoesNotContain("±", text);
        }
    }
}
