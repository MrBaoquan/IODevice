using System;
using System.Collections.Generic;
using Xunit;
using IOToolkit;
using IOStudio.Services.Motion;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// 「量程推送 + native 换算」契约测试 —— 让编排端与放映端共用同一段换算代码。
    ///
    /// <para>设计: 三级优先级 (轨道 engineering_range → 设备声明量程 → 直通) 都能表达为
    /// <b>绝对区间</b>, 所以职责切成两半:</para>
    /// <list type="bullet">
    /// <item><b>宿主</b>决定用哪个量程 (<see cref="EngineeringValueConverter.ResolveRange"/>,
    ///   纯配置解析) 并推给 native;</item>
    /// <item><b>native</b> 执行换算 <c>min + v*(max-min)</c> (与放映端同一段代码)。</item>
    /// </list>
    ///
    /// <para>本测试钉住两件事: (1) 解析后的仿射映射与原本的三级换算等价;
    /// (2) native 的换算结果与宿主侧完全一致 (跨语言)。</para>
    /// </summary>
    /// <remarks>
    /// 本类用的是<b>固定</b>设备名 <c>Simulator-1</c>, 且会调 <c>ClearChannelRanges()</c> 清空
    /// <b>整张</b>宿主量程表 —— 必须与其它 native 全局态用例串行, 见
    /// <see cref="NativeMotionStateCollection"/>。
    /// </remarks>
    [Collection("NativeMotionState")]
    public class ChannelRangePushContractTests
    {
        private const string Device = "Simulator-1";

        private static double Affine(float v, (float Min, float Max) range) =>
            range.Min + v * (range.Max - range.Min);

        // ── 1) 解析结果与三级换算等价 (宿主内部一致性) ──────

        [Theory]
        [InlineData(0.0f)]
        [InlineData(0.25f)]
        [InlineData(0.5f)]
        [InlineData(0.75f)]
        [InlineData(1.0f)]
        public void ResolvedRange_IsEquivalentToThreeLevelConversion_Declared(float v)
        {
            var declared = ((float)-30f, (float)30f);
            float viaLevels = EngineeringValueConverter.ToEngineering(v, "float", null, null, declared);
            var resolved = EngineeringValueConverter.ResolveRange("float", null, null, declared);
            Assert.NotNull(resolved);
            Assert.Equal(viaLevels, Affine(v, resolved!.Value), precision: 4);
        }

        [Theory]
        [InlineData(0.0f, 0.5f)]
        [InlineData(0.25f, 0.5f)]
        [InlineData(0.5f, 0.5f)]
        [InlineData(0.8f, 0.75f)] // 非中位中性值: 必须仍然等价
        [InlineData(1.0f, 0.75f)]
        public void ResolvedRange_IsEquivalentToThreeLevelConversion_TrackOverride(float v, float neutral)
        {
            float trackRange = 60f;
            float viaLevels = EngineeringValueConverter.ToEngineering(
                v, "float", neutral, trackRange, null);
            var resolved = EngineeringValueConverter.ResolveRange("float", neutral, trackRange, null);
            Assert.NotNull(resolved);
            Assert.Equal(viaLevels, Affine(v, resolved!.Value), precision: 4);
        }

        [Fact]
        public void ResolveRange_TrackOverrideBeatsDeclared()
        {
            var resolved = EngineeringValueConverter.ResolveRange(
                "float", null, 60f, (-300f, 300f));
            Assert.NotNull(resolved);
            // (0-0.5)*60 = -30, (1-0.5)*60 = +30
            Assert.Equal(-30f, resolved!.Value.Min, precision: 4);
            Assert.Equal(30f, resolved.Value.Max, precision: 4);
        }

        [Fact]
        public void ResolveRange_Undeclared_ReturnsNull()
        {
            Assert.Null(EngineeringValueConverter.ResolveRange("float", null, null, null));
            // 退化/反向区间视为未声明
            Assert.Null(EngineeringValueConverter.ResolveRange("float", null, null, (5f, 5f)));
            Assert.Null(EngineeringValueConverter.ResolveRange("float", null, null, (30f, -30f)));
        }

        // ── 2) native 换算 == 宿主换算 (跨语言) ────────────

        [Fact]
        public void Native_ToEngineering_Matches_HostAffine()
        {
            var cases = new (string Channel, float? TrackRange, float? Neutral, (float Min, float Max)? Declared)[]
            {
                ("OAxis_01", null, null, (-30f, 30f)),    // 设备声明对称
                ("OAxis_04", null, null, (-10f, 30f)),    // 设备声明非对称
                ("OAxis_07", null, null, (-360f, 360f)),
                ("OAxis_03", 60f, null, (-300f, 300f)),   // 轨道覆盖优先
                ("OAxis_06", 60f, 0.75f, null),           // 轨道覆盖 + 非中位中性值
            };

            try
            {
                MotionPlayer.ClearChannelRanges();

                var mismatches = new List<string>();
                var samples = new[] { 0f, 0.25f, 0.5f, 0.75f, 1f };

                foreach (var (channel, trackRange, neutral, declared) in cases)
                {
                    var resolved = EngineeringValueConverter.ResolveRange(
                        "float", neutral, trackRange, declared);
                    Assert.NotNull(resolved);
                    MotionPlayer.SetChannelRange(
                        Device, channel, resolved!.Value.Min, resolved.Value.Max);

                    foreach (float v in samples)
                    {
                        float native;
                        try
                        {
                            native = MotionPlayer.ToEngineering(Device, channel, v);
                        }
                        catch (Exception ex) when (
                            ex is DllNotFoundException
                            || ex is BadImageFormatException
                            || ex is EntryPointNotFoundException
                        )
                        {
                            throw new InvalidOperationException(
                                "原生 IODevice 不可用, 跨语言量程契约无法验证: " + ex.Message,
                                ex
                            );
                        }

                        double host = Affine(v, resolved.Value);
                        if (Math.Abs(native - host) > 1e-3f)
                            mismatches.Add($"{channel} v={v:F2} native={native:F4} host={host:F4}");
                    }
                }

                Assert.True(
                    mismatches.Count == 0,
                    "native 换算与宿主换算不一致:\n  " + string.Join("\n  ", mismatches)
                );
            }
            finally
            {
                MotionPlayer.ClearChannelRanges();
            }
        }

        [Fact]
        public void Native_UnregisteredChannel_PassesThrough()
        {
            MotionPlayer.ClearChannelRanges();
            // 未登记 → 原样返回 (保持既有行为)
            Assert.Equal(0.3f, MotionPlayer.ToEngineering(Device, "OAxis_99", 0.3f), precision: 4);
        }

        [Fact]
        public void Native_DegenerateRange_IsCleared_AndPassesThrough()
        {
            MotionPlayer.SetChannelRange(Device, "OAxis_50", -30f, 30f);
            Assert.Equal(0f, MotionPlayer.ToEngineering(Device, "OAxis_50", 0.5f), precision: 4);

            // max <= min 视为清除该通道
            MotionPlayer.SetChannelRange(Device, "OAxis_50", 5f, 5f);
            Assert.Equal(0.5f, MotionPlayer.ToEngineering(Device, "OAxis_50", 0.5f), precision: 4);
        }

        [Fact]
        public void Native_NormalizedInput_IsClamped()
        {
            MotionPlayer.SetChannelRange(Device, "OAxis_51", -30f, 30f);
            // 越界输入按 [0,1] 钳位 (与求值器的钳位语义一致)
            Assert.Equal(-30f, MotionPlayer.ToEngineering(Device, "OAxis_51", -0.5f), precision: 4);
            Assert.Equal(30f, MotionPlayer.ToEngineering(Device, "OAxis_51", 1.5f), precision: 4);
            MotionPlayer.ClearChannelRanges();
        }
    }
}
