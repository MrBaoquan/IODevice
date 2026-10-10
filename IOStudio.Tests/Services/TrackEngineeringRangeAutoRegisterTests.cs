using System;
using Xunit;
using IOToolkit;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// 轨道自带 <c>engineering_range</c> 的「装载即登记」兜底量程 (native 侧)。
    ///
    /// <para>背景: 归一化值 (0..1) → 工程量 需要一张 <c>(device, channel) → (min, max)</c> 表。
    /// 编排端 (<c>IOStudio</c>) 会显式推送它 (<see cref="MotionPlayer.SetChannelRange"/>),
    /// 但放映端 (<c>UNIPlayer</c>) 不推送 —— 于是动作文件里的 <c>engineering_range</c> 无人消费,
    /// 电缸等通道只能拿到被钳到 <c>[0,1]</c> 的归一化值 (经 V1 <c>short</c> 截断后恒为 0/1)。</para>
    ///
    /// <para>修复: native 在 <c>LoadSlot</c> 时把每条轨道自带的 <c>engineering_range</c> 解析为
    /// 绝对区间 <c>min=(0-neutral)*range, max=(1-neutral)*range</c> 登记为兜底;
    /// 宿主之后显式推送的值覆盖同一 <c>(device, channel)</c>。本测试钉住这条兜底链路。</para>
    ///
    /// <para><b>并行说明</b>: native 的兜底量程表是由"已装载 Slot"派生的 (<c>RebuildTrackRanges</c>),
    /// 宿主的 <c>ClearChannelRanges</c> 不会影响它; 本类用随机设备名, 因此与并行运行、会清空
    /// 宿主量程表的其它用例天然隔离。</para>
    /// </summary>
    public class TrackEngineeringRangeAutoRegisterTests
    {
        private const string Channel = "OAxis_60";

        private static string UniqueDevice() =>
            "AutoRange-" + Guid.NewGuid().ToString("N").Substring(0, 8);

        private static string MotionJson(
            string device,
            float neutral,
            float keyframeValue,
            float? engineeringRange)
        {
            string range = engineeringRange.HasValue
                ? FormattableString.Invariant($"\"engineering_range\": {engineeringRange.Value},")
                : "";
            return FormattableString.Invariant(
                $@"{{
  ""duration_ms"": 1000,
  ""tracks"": [
    {{
      ""device"": ""{device}"",
      ""output_type"": ""oaxis"",
      ""oaxis_channel"": ""{Channel}"",
      ""value_type"": ""float"",
      ""neutral_value"": {neutral},
      {range}
      ""clips"": [
        {{
          ""start_ms"": 0,
          ""end_ms"": 1000,
          ""keyframes"": [
            {{ ""time_ms"": 0, ""value"": {keyframeValue}, ""interpolation"": ""linear"" }},
            {{ ""time_ms"": 1000, ""value"": {keyframeValue}, ""interpolation"": ""linear"" }}
          ]
        }}
      ]
    }}
  ]
}}");
        }

        private static void Cleanup(MotionSlot slot, string device)
        {
            try { slot.Unload(); } catch { /* ignore */ }
            // max <= min → 清除该通道量程; 只作用本用例的随机设备, 不动全局其它键
            try { MotionPlayer.SetChannelRange(device, Channel, 0f, 0f); } catch { /* ignore */ }
        }

        [Fact]
        public void EngineeringRange_NeutralZero_ConvertsNormalizedToEngineering_AtLoad()
        {
            string dev = UniqueDevice();
            var slot = MotionPlayer.LoadSlotFromJson(
                "autorange_zero_" + dev, MotionJson(dev, neutral: 0f, keyframeValue: 0.25f, engineeringRange: 1000f));
            try
            {
                // 求值本身仍返回归一化值 ...
                Assert.Equal(0.25f, slot.EvaluateAt(0f)[0], precision: 4);
                // ... 工程量 = 0 + 0.25*1000 = 250 (由装载时登记的兜底量程换算)
                Assert.Equal(250f, slot.EvaluateAtEngineering(0f)[0], precision: 2);
            }
            finally { Cleanup(slot, dev); }
        }

        [Fact]
        public void EngineeringRange_NonMidNeutral_ProducesAsymmetricRange()
        {
            string dev = UniqueDevice();
            // neutral=0.5, range=100 → 绝对区间 (-50, +50); 0.75 → -50 + 0.75*100 = 25
            var slot = MotionPlayer.LoadSlotFromJson(
                "autorange_mid_" + dev, MotionJson(dev, neutral: 0.5f, keyframeValue: 0.75f, engineeringRange: 100f));
            try
            {
                Assert.Equal(25f, slot.EvaluateAtEngineering(0f)[0], precision: 2);
            }
            finally { Cleanup(slot, dev); }
        }

        [Fact]
        public void HostPush_OverridesAutoRegisteredRange()
        {
            string dev = UniqueDevice();
            var slot = MotionPlayer.LoadSlotFromJson(
                "autorange_ovr_" + dev, MotionJson(dev, neutral: 0f, keyframeValue: 0.5f, engineeringRange: 1000f));
            try
            {
                // 兜底量程 (0, 1000) → 500
                Assert.Equal(500f, slot.EvaluateAtEngineering(0f)[0], precision: 2);
                // 宿主显式覆盖为 (-30, 30) → 0.5 → 0
                MotionPlayer.SetChannelRange(dev, Channel, -30f, 30f);
                Assert.Equal(0f, slot.EvaluateAtEngineering(0f)[0], precision: 2);
            }
            finally { Cleanup(slot, dev); }
        }

        [Fact]
        public void WithoutEngineeringRange_EngineeringEvaluation_PassesThrough()
        {
            string dev = UniqueDevice();
            var slot = MotionPlayer.LoadSlotFromJson(
                "autorange_pass_" + dev, MotionJson(dev, neutral: 0.5f, keyframeValue: 0.3f, engineeringRange: null));
            try
            {
                // 未声明量程 → 直通 (保持既有行为)
                Assert.Equal(0.3f, slot.EvaluateAtEngineering(0f)[0], precision: 4);
            }
            finally { Cleanup(slot, dev); }
        }
    }
}
