using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using IOStudio.ViewModels.Timeline;

namespace IOStudio.Tests.ViewModels
{
    /// <summary>
    /// 时长/端点稳定性回归 —— 守住"打开文件不修改内容"。
    ///
    /// <para>曾经 <c>RecalculateDuration()</c> 会把每个 clip 的 EndMs 重写为
    /// <c>ceil((start+末关键帧+500)/500)*500</c> 并重算总时长，而 <c>LoadTimeline</c> 又会调用它 ——
    /// 于是"打开一个 .motion"本身就改写了内容 (每个 clip +500ms、总时长 +1000~2000)，随后只要置脏，
    /// 自动保存/保存就把这份漂移写回磁盘，还会破坏 action instance 与 clip 的跨度一致性。</para>
    /// </summary>
    public class TimelineDurationStabilityTests
    {
        private static MotionKeyframe Kf(double t, float v) =>
            new() { TimeMs = t, Value = v, Interpolation = "linear" };

        private static MotionTimeline BuildTimeline()
        {
            var longTrack = new MotionTrack
            {
                Id = "trk_long",
                DeviceName = "Simulator-1",
                OutputType = "oaxis",
                OAxisChannel = "OAxis_11",
                Role = "motion_enable",
                Label = "使能",
            };
            longTrack.Clips.Add(
                new MotionClip
                {
                    Id = "clip_long",
                    StartMs = 0,
                    EndMs = 600000,
                    Keyframes = new List<MotionKeyframe> { Kf(0, 0f), Kf(100, 1f), Kf(600000, 1f) },
                }
            );

            var shortTrack = new MotionTrack
            {
                Id = "trk_short",
                DeviceName = "Simulator-1",
                OutputType = "oaxis",
                OAxisChannel = "OAxis_01",
                Role = "pitch",
                Label = "俯仰",
            };
            shortTrack.Clips.Add(
                new MotionClip
                {
                    Id = "clip_short",
                    StartMs = 32000,
                    EndMs = 35000,
                    Keyframes = new List<MotionKeyframe> { Kf(0, 0.5f), Kf(3000, 0.8f) },
                }
            );

            return new MotionTimeline
            {
                Version = "3.0",
                Name = "stability",
                DurationMs = 600000,
                Tracks = new List<MotionTrack> { longTrack, shortTrack },
            };
        }

        [Fact]
        public void Loading_Timeline_Does_Not_Mutate_Clips_Or_Duration()
        {
            var tl = BuildTimeline();
            string path = Path.Combine(Path.GetTempPath(), $"stab_{Guid.NewGuid():N}.motion");
            try
            {
                Assert.True(MotionFileReader.Write(tl, path));

                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);

                Assert.NotNull(vm.Timeline);
                Assert.Equal(600000, vm.Timeline!.DurationMs);
                Assert.Equal(600000, vm.Timeline.Tracks[0].Clips[0].EndMs); // 不被改成 600500
                Assert.Equal(35000, vm.Timeline.Tracks[1].Clips[0].EndMs);  // 不被改成 35500
            }
            finally
            {
                try { File.Delete(path); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void RecalculateDuration_Does_Not_Rewrite_ClipEnds()
        {
            var tl = BuildTimeline();
            var vm = new TimelineEditorViewModel(null) { Timeline = tl, DurationMs = 600000 };

            vm.RecalculateDuration();

            Assert.Equal(600000, tl.Tracks[0].Clips[0].EndMs);
            Assert.Equal(35000, tl.Tracks[1].Clips[0].EndMs);
            // 内容上限 600000 → 只增不减, 时长保持 600000 (旧实现会变成 602000)
            Assert.Equal(600000, tl.DurationMs);
        }
    }
}
