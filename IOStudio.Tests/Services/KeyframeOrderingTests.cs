using System.Collections.Generic;
using Xunit;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using IOStudio.Tests.Helpers;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// 关键帧有序性 —— 求值<b>不得改写调用方的数据</b>。
    ///
    /// 背景: InterpolationEngine.Evaluate 曾对乱序输入就地 <c>Sort()</c>, 而它跑在播放/曲线
    /// 渲染热路径上 —— 等于在播放期间静默改写用户文档 (破坏选择与撤销快照, 并与渲染竞争)。
    /// 现在改为"乱序退化为线性扫描且不改数据", 乱序本身由预检暴露。
    /// </summary>
    public class KeyframeOrderingTests
    {
        private static MotionKeyframe Kf(double t, float v) =>
            new() { TimeMs = t, Value = v, Interpolation = "linear" };

        [Fact]
        public void Evaluate_DoesNotMutateUnsortedInput()
        {
            var kfs = new List<MotionKeyframe> { Kf(1000, 0.9f), Kf(0, 0.1f), Kf(500, 0.5f) };
            var before = new List<MotionKeyframe>(kfs);

            InterpolationEngine.Evaluate(kfs, 250);

            Assert.Equal(before, kfs); // 顺序未被改动 (引用相等即可)
        }

        [Fact]
        public void Evaluate_SortedInput_IsUnaffected()
        {
            var kfs = new List<MotionKeyframe> { Kf(0, 0.0f), Kf(1000, 1.0f) };
            // 有序: 走二分路径, 中点应为 0.5
            Assert.Equal(0.5f, InterpolationEngine.Evaluate(kfs, 500), precision: 3);
        }

        [Fact]
        public void Evaluate_UnsortedInput_StillReturnsDeterministicValue()
        {
            var kfs = new List<MotionKeyframe> { Kf(1000, 0.9f), Kf(0, 0.1f) };
            float a = InterpolationEngine.Evaluate(kfs, 500);
            float b = InterpolationEngine.Evaluate(kfs, 500);
            Assert.Equal(a, b, precision: 4); // 确定 (不因排序副作用而变)
        }

        [Fact]
        public void Evaluate_Boundaries_ReturnFirstAndLastValues()
        {
            var kfs = new List<MotionKeyframe> { Kf(100, 0.2f), Kf(900, 0.8f) };
            Assert.Equal(0.2f, InterpolationEngine.Evaluate(kfs, 0), precision: 3);
            Assert.Equal(0.8f, InterpolationEngine.Evaluate(kfs, 5000), precision: 3);
        }

        // ── 预检暴露乱序 ──────────────────────────────────

        [Fact]
        public void Validator_ReportsUnsortedKeyframes()
        {
            var clip = new MotionClip { Id = "c", StartMs = 0, EndMs = 2000 };
            clip.Keyframes.Add(Kf(1000, 0.9f)); // 乱序: 先 1000
            clip.Keyframes.Add(Kf(0, 0.1f));
            var track = new MotionTrack
            {
                Id = "t",
                DeviceName = "Simulator-1",
                OutputType = "oaxis",
                OAxisChannel = "OAxis_01",
                ValueType = "float",
                Clips = new List<MotionClip> { clip },
            };
            var timeline = new MotionTimeline { DurationMs = 2000, Tracks = new List<MotionTrack> { track } };

            var issues = MotionTimelineValidator.Validate(timeline);
            Assert.Contains(issues, i => i.Code == "keyframe.unsorted");
            // 是警告 (能播但结果不可靠), 不是错误
            Assert.False(MotionTimelineValidator.HasErrors(issues));
        }

        [Fact]
        public void Validator_SortedKeyframes_NoWarning()
        {
            var clip = new MotionClip { Id = "c", StartMs = 0, EndMs = 2000 };
            clip.Keyframes.Add(Kf(0, 0.1f));
            clip.Keyframes.Add(Kf(1000, 0.9f));
            var track = new MotionTrack
            {
                Id = "t",
                DeviceName = "Simulator-1",
                OutputType = "oaxis",
                OAxisChannel = "OAxis_01",
                ValueType = "float",
                Clips = new List<MotionClip> { clip },
            };
            var timeline = new MotionTimeline { DurationMs = 2000, Tracks = new List<MotionTrack> { track } };

            Assert.Empty(MotionTimelineValidator.Validate(timeline));
        }
    }
}
