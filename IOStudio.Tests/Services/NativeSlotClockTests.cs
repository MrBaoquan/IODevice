using System;
using System.Collections.Generic;
using Xunit;
using IOToolkit;
using IOStudio.Services.Motion;
using IOStudio.Tests.Helpers;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// native Slot 的时钟驱动测试 —— 隔离验证"外部时钟 + Tick 能否真正推进 slot 时间"。
    ///
    /// <para>背景: 让编排端改用 native 派发时, 观测到 slot 停在 Playing/External 且外部时间在推,
    /// 但内部时间似乎不动。这里不经 VM/Rx, 直接驱动 native, 把问题收窄到 native 或驱动层。</para>
    /// </summary>
    /// <remarks>装载 native Slot 属于进程级共享状态, 见 <see cref="NativeMotionStateCollection"/>。</remarks>
    [Collection("NativeMotionState")]
    public class NativeSlotClockTests
    {
        private static MotionSlot LoadSlot(string slotId, double durationMs = 10000)
        {
            var timeline = MotionTestFactory.CreateMinimalTimeline(durationMs);
            string json = MotionFileReader.ToJson(timeline);
            return MotionPlayer.LoadSlotFromJson(slotId, json);
        }

        [Fact]
        public void ExternalClock_DrivesSlotTime()
        {
            var slot = LoadSlot("clock_external_probe");
            try
            {
                slot.ClockMode = ClockMode.External;
                slot.Play();

                foreach (double t in new[] { 0d, 250d, 500d, 1000d, 3000d })
                {
                    slot.SetExternalTime((float)t);
                    MotionPlayer.Tick(1f / 60f);
                    Assert.Equal(t, slot.CurrentTime, precision: 1);
                }
            }
            finally
            {
                slot.Unload();
            }
        }

        [Fact]
        public void InternalClock_AdvancesWithTick()
        {
            var slot = LoadSlot("clock_internal_probe");
            try
            {
                slot.ClockMode = ClockMode.Internal;
                slot.Play();

                MotionPlayer.Tick(0.5f); // 500ms
                Assert.True(
                    slot.CurrentTime >= 400f,
                    $"内部时钟应当随 Tick 推进, 实际 {slot.CurrentTime}"
                );
            }
            finally
            {
                slot.Unload();
            }
        }

        [Fact]
        public void ExternalTime_IsClampedToDuration()
        {
            var slot = LoadSlot("clock_clamp_probe", durationMs: 5000);
            try
            {
                slot.ClockMode = ClockMode.External;
                slot.Play();

                slot.SetExternalTime(99999f);
                MotionPlayer.Tick(1f / 60f);

                Assert.True(slot.CurrentTime <= slot.Duration + 1f);
            }
            finally
            {
                slot.Unload();
            }
        }
    }
}
