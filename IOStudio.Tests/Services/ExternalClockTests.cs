using Xunit;
using IOStudio.Services.Motion;
using IOStudio.Tests.Helpers;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// 引擎外部时钟 (影片/音频主时钟) 测试。
    ///
    /// 影片编排必须由媒体当主时钟: 否则动作(内部 Stopwatch)与画面(LibVLC)各自自由跑,
    /// LibVLC 速率不是精确 1x, 5 分钟以上必然漂移且无人察觉。
    /// </summary>
    public class ExternalClockTests
    {
        private static NativeMotionPlaybackEngine MakeEngine(double durationMs = 10000)
        {
            var engine = new NativeMotionPlaybackEngine(new DeviceDispatcher());
            engine.EnsureTimelineLoaded(MotionTestFactory.CreateMinimalTimeline(durationMs));
            return engine;
        }

        [Fact]
        public void ExternalClock_DrivesPosition_InsteadOfInternalTimer()
        {
            var engine = MakeEngine();
            double? observed = null;
            engine.PositionChanged += t => observed = t;

            engine.Play();
            engine.SetExternalClockMs(5000);
            engine.Tick();

            Assert.NotNull(observed);
            Assert.Equal(5000, observed!.Value, precision: 1);
        }

        [Fact]
        public void ExternalClock_TracksMediaMonotonically()
        {
            var engine = MakeEngine();
            double? observed = null;
            engine.PositionChanged += t => observed = t;

            engine.Play();
            // 模拟媒体逐帧推进
            foreach (double mediaMs in new[] { 1000d, 2500d, 4000d })
            {
                engine.SetExternalClockMs(mediaMs);
                engine.Tick();
                Assert.Equal(mediaMs, observed!.Value, precision: 1);
            }
        }

        [Fact]
        public void ExternalClock_IgnoresSpeed_ToPreserveMediaSync()
        {
            var engine = MakeEngine();
            double? observed = null;
            engine.PositionChanged += t => observed = t;

            engine.Speed = 2.0; // 若被叠加, 位置会变成 6000 → 与画面错位
            engine.Play();
            engine.SetExternalClockMs(3000);
            engine.Tick();

            Assert.Equal(3000, observed!.Value, precision: 1);
        }

        [Fact]
        public void ExternalClock_Cleared_FallsBackToInternalTimer()
        {
            var engine = MakeEngine();
            double? observed = null;
            engine.PositionChanged += t => observed = t;

            engine.Play();
            engine.SetExternalClockMs(null); // 无媒体时钟 (纯动作预览)
            engine.Tick();

            Assert.NotNull(observed);
            // 内部计时驱动: 位置从 0 附近开始 (而不是某个外部值)
            Assert.InRange(observed!.Value, 0, 500);
        }

        [Fact]
        public void ExternalClock_NullAfterMedia_ResumesFromMediaPosition()
        {
            var engine = MakeEngine();
            double? observed = null;
            engine.PositionChanged += t => observed = t;

            engine.Play();
            engine.SetExternalClockMs(4000);
            engine.Tick();
            Assert.Equal(4000, observed!.Value, precision: 1);

            // 媒体时钟掉线 → 从媒体位置继续 (不回零), 保证画面/动作都不跳
            engine.SetExternalClockMs(null);
            engine.Tick();
            Assert.True(observed!.Value >= 4000);
        }

        [Fact]
        public void ExternalClock_ReachingDuration_CompletesPlayback()
        {
            var engine = MakeEngine(5000);
            bool completed = false;
            engine.PlaybackCompleted += () => completed = true;

            engine.Play();
            engine.SetExternalClockMs(5000);
            engine.Tick();

            Assert.True(completed);
        }
    }
}
