using Xunit;
using IOStudio.Services.Motion;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// 值域显示测试 —— 归一化值域要在编辑器里"看得见"。
    ///
    /// 关键不变量: 显示出来的数字必须与<b>实际下发</b>的工程量完全一致
    /// (两者共用 <see cref="EngineeringValueConverter"/>), 否则就成了新的"所见非所播"。
    /// </summary>
    public class EngineeringDisplayTests
    {
        // ── 量程描述 ──────────────────────────────────────

        [Fact]
        public void DescribeRange_TrackOverride_IsSymmetricAboutNeutral()
        {
            var (text, source) = EngineeringDisplay.DescribeRange(60f, null, "deg", "float");
            Assert.Equal("±30°", text);
            Assert.Contains("engineering_range", source);
        }

        [Fact]
        public void DescribeRange_DeclaredSymmetric_UsesPlusMinus()
        {
            var (text, source) = EngineeringDisplay.DescribeRange(null, (-30f, 30f), "deg", "float");
            Assert.Equal("±30°", text);
            Assert.Contains("capabilities", source);
        }

        [Fact]
        public void DescribeRange_DeclaredAsymmetric_UsesRange()
        {
            var (text, _) = EngineeringDisplay.DescribeRange(null, (-10f, 30f), "mm", "float");
            Assert.Equal("-10..30mm", text);
        }

        [Fact]
        public void DescribeRange_TrackOverride_WinsOverDeclared()
        {
            var (text, source) = EngineeringDisplay.DescribeRange(
                60f,
                (-300f, 300f),
                "mm",
                "float"
            );
            Assert.Equal("±30mm", text);
            Assert.Contains("engineering_range", source);
        }

        [Fact]
        public void DescribeRange_NoDeclaration_FallsBackToNormalized()
        {
            var (text, source) = EngineeringDisplay.DescribeRange(null, null, "", "float");
            Assert.Equal("0..1", text);
            Assert.Contains("直通", source);
        }

        [Fact]
        public void DescribeRange_BoolTrack_IsOnOff()
        {
            var (text, _) = EngineeringDisplay.DescribeRange(null, (0f, 1f), "bool", "bool");
            Assert.Equal("开/关", text);
        }

        // ── 实时值显示 ────────────────────────────────────

        [Fact]
        public void FormatLive_WithDeclaredRange_ShowsEngineering()
        {
            // 0.8 → -30 + 0.8*60 = 18
            Assert.Equal("18°", EngineeringDisplay.FormatLive(0.8f, "float", null, null, (-30f, 30f), "deg"));
            // 0.3 → -12
            Assert.Equal("-12°", EngineeringDisplay.FormatLive(0.3f, "float", null, null, (-30f, 30f), "deg"));
        }

        [Fact]
        public void FormatLive_WithTrackRange_ShowsEngineering()
        {
            // (0.3-0.5)*60 = -12
            Assert.Equal("-12°", EngineeringDisplay.FormatLive(0.3f, "float", null, 60f, null, "deg"));
        }

        [Fact]
        public void FormatLive_WithoutRange_ShowsNormalized()
        {
            Assert.Equal("0.8", EngineeringDisplay.FormatLive(0.8f, "float", null, null, null, ""));
        }

        [Theory]
        [InlineData(0.9f, "ON")]
        [InlineData(0.1f, "OFF")]
        public void FormatLive_BoolTrack_IsOnOff(float value, string expected)
        {
            Assert.Equal(expected, EngineeringDisplay.FormatLive(value, "bool", null, null, (0f, 1f), "bool"));
        }

        [Fact]
        public void FormatLive_NoUnit_SuffixIsEmpty()
        {
            Assert.Equal("-12", EngineeringDisplay.FormatLive(0.3f, "float", null, 60f, null, ""));
        }

        // ── 不变量: 显示值 == 下发值 ──────────────────────

        [Theory]
        [InlineData(0.0f)]
        [InlineData(0.3f)]
        [InlineData(0.5f)]
        [InlineData(0.8f)]
        [InlineData(1.0f)]
        public void FormatLive_MatchesDispatcherConversion(float normalized)
        {
            var declared = ((float)-30f, (float)30f);
            string shown = EngineeringDisplay.FormatLive(normalized, "float", null, null, declared, "deg");
            float dispatched = EngineeringValueConverter.ToEngineering(
                normalized,
                "float",
                null,
                null,
                declared
            );
            Assert.Equal(dispatched.ToString("0.##"), shown.TrimEnd('°'));
        }
    }
}
