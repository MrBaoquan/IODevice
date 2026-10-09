using Xunit;
using IOStudio.Models;
using IOStudio.Services.Motion;
using IOStudio.ViewModels.Timeline;

namespace IOStudio.Tests.ViewModels
{
    /// <summary>
    /// 设备下发节流。
    ///
    /// <para>联调时 C# 以 60Hz 逐帧把动作推给 native → 写设备; 有些设备/链路吃不下这个频率。
    /// DispatchIntervalMs &gt; 0 时只在距上次下发达到间隔才下发一次, 中间帧只更新 UI。
    /// native 的 SafetyGuard 用内部时钟测间隔, 与调用频率无关, 故降频不削弱限速保护。</para>
    /// </summary>
    public class TimelineMotionThrottleTests
    {
        [Theory]
        [InlineData(0, 0, 0, true)]      // 不节流: 恒下发
        [InlineData(5000, 0, 0, true)]
        [InlineData(10, 0, 33, false)]   // 距上次仅 10ms, 未达 33ms
        [InlineData(33, 0, 33, true)]    // 恰好达到间隔
        [InlineData(40, 0, 33, true)]
        public void ShouldDispatch_Respects_Interval(long now, long last, int interval, bool expected)
            => Assert.Equal(expected, NativeMotionPlaybackEngine.ShouldDispatch(now, last, interval));

        [Fact]
        public void Engine_Defaults_To_No_Throttle()
        {
            using var engine = new NativeMotionPlaybackEngine(new DeviceDispatcher());
            Assert.Equal(0, engine.DispatchIntervalMs); // 默认不节流: 行为与旧版逐帧下发一致
        }

        [Fact]
        public void Throttle_Command_Updates_State_Engine_And_Setting()
        {
            var settings = IOStudioSettings.Instance;
            int original = settings.MotionDispatchIntervalMs;
            try
            {
                settings.MotionDispatchIntervalMs = 0;
                var vm = new TimelineEditorViewModel(null);

                Assert.Equal("不节流", vm.MotionThrottleText);
                Assert.True(vm.MotionThrottleNone);

                vm.SetMotionThrottleCommand.Execute("50").Subscribe(); // 20 Hz
                Assert.Equal(50, vm.MotionDispatchIntervalMs);
                Assert.Equal("20 Hz", vm.MotionThrottleText);
                Assert.True(vm.MotionThrottle20);
                Assert.False(vm.MotionThrottleNone);
                Assert.Equal(50, settings.MotionDispatchIntervalMs); // 已写回设置
                Assert.Equal(50, vm.EffectiveDispatchIntervalMs);    // 已同步引擎

                vm.SetMotionThrottleCommand.Execute("0").Subscribe();
                Assert.True(vm.MotionThrottleNone);
                Assert.Equal(0, vm.EffectiveDispatchIntervalMs);
            }
            finally
            {
                settings.MotionDispatchIntervalMs = original;
                try { settings.Save(); } catch { /* 测试环境无配置路径时忽略 */ }
            }
        }
    }
}
