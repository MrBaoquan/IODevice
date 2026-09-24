using System.Collections.Generic;
using System.Linq;
using Xunit;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// 安全联锁预检测试 (可选规则, 需注入 <see cref="MotionSafetyProfile"/>)。
    ///
    /// <para>这类问题的共同点: 编排本身"能播", 但实机上要么不生效 (未使能)、要么危险 (急停期间仍在动),
    /// 而且不会报错 —— 正是预检要消灭的静默失效。</para>
    /// </summary>
    public class MotionSafetyValidationTests
    {
        private static readonly MotionValidationOptions WithSafety = new()
        {
            Safety = new MotionSafetyProfile(),
        };

        private static MotionTrack Track(
            string role,
            string channel,
            params MotionClip[] clips
        ) =>
            new()
            {
                Id = "trk_" + role,
                DeviceName = "Simulator-1",
                OutputType = "oaxis",
                OAxisChannel = channel,
                Label = role,
                Role = role,
                ValueType = "float",
                Clips = new List<MotionClip>(clips),
            };

        private static MotionClip Clip(double start, double end, params (double t, float v)[] kfs)
        {
            var c = new MotionClip { Id = $"c{start}", StartMs = start, EndMs = end };
            foreach (var (t, v) in kfs)
                c.Keyframes.Add(new MotionKeyframe { TimeMs = t, Value = v, Interpolation = "linear" });
            return c;
        }

        private static MotionTimeline Timeline(double durationMs, params MotionTrack[] tracks) =>
            new() { Name = "safety", DurationMs = durationMs, Tracks = new List<MotionTrack>(tracks) };

        private static List<string> Codes(MotionTimeline tl) =>
            MotionTimelineValidator
                .Validate(tl, WithSafety)
                .Select(i => i.Code)
                .ToList();

        private static MotionTrack Enable(float value = 1f) =>
            Track("motion_enable", "OAxis_11", Clip(0, 10000, (0, value), (10000, value)));

        private static MotionTrack Motion(double start = 1000, double end = 2000, float value = 0.8f) =>
            Track("pitch", "OAxis_01", Clip(start, end, (0, value), (end - start, value)));

        [Fact]
        public void MotionWithoutEnableTrack_IsFlagged()
        {
            var codes = Codes(Timeline(10000, Motion()));
            Assert.Contains("safety.missing_motion_enable", codes);
        }

        [Fact]
        public void MotionWhileNeverEnabled_IsFlagged()
        {
            var codes = Codes(Timeline(10000, Motion(), Enable(value: 0f)));
            Assert.Contains("safety.motion_never_enabled", codes);
            Assert.Contains("safety.motion_without_enable", codes);
        }

        [Fact]
        public void MotionWhileDisabled_IsFlagged()
        {
            // 使能轨存在但只在动作之后才置高 → 动作期间未使能
            var enable = Track("motion_enable", "OAxis_11", Clip(0, 10000, (0, 0f), (3000, 0f), (3100, 1f), (10000, 1f)));
            var codes = Codes(Timeline(10000, Motion(1000, 2000), enable));
            Assert.Contains("safety.motion_without_enable", codes);
        }

        [Fact]
        public void EnabledMotion_IsClean()
        {
            var codes = Codes(Timeline(10000, Motion(1000, 2000), Enable()));
            Assert.DoesNotContain(codes, c => c.StartsWith("safety."));
        }

        [Fact]
        public void MotionDuringSustainedEstop_IsFlagged()
        {
            var estop = Track("emergency_stop", "OAxis_10", Clip(0, 10000, (0, 1f), (10000, 1f)));
            var codes = Codes(Timeline(10000, Motion(2000, 3000), Enable(), estop));
            Assert.Contains("safety.motion_during_estop", codes);
        }

        [Fact]
        public void ShortDecelClipAtEstopOnset_IsNotFlagged()
        {
            // 急停触发的减速 clip (短, 与急停上升沿重合) 不应被当成"急停期间仍在动"
            var estop = Track("emergency_stop", "OAxis_10", Clip(0, 10000, (0, 0f), (2000, 0f), (2200, 1f), (10000, 1f)));
            var codes = Codes(Timeline(10000, Motion(2000, 2200), Enable(), estop));
            Assert.DoesNotContain("safety.motion_during_estop", codes);
        }

        [Fact]
        public void WithoutSafetyProfile_NoSafetyIssues()
        {
            var issues = MotionTimelineValidator.Validate(Timeline(10000, Motion()));
            Assert.DoesNotContain(issues, i => i.Code.StartsWith("safety."));
        }
    }
}
