using System.Collections.Generic;
using System.Linq;
using Xunit;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using IOStudio.Tests.Helpers;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// 动作编排预检测试。
    ///
    /// 预检的价值在于把"静默失效"变成明确清单, 所以每个用例都对应一种**不报错但值不对**
    /// 的真实陷阱 (遮蔽、钳位、量化、未绑定、设备未加载、内容超出总时长)。
    /// </summary>
    public class MotionTimelineValidatorTests
    {
        private static MotionTrack Track(
            string device = "Simulator-1",
            string channel = "OAxis_01",
            bool muted = false,
            bool enabled = true,
            params MotionClip[] clips
        ) =>
            new()
            {
                Id = "trk1",
                DeviceName = device,
                OutputType = "oaxis",
                OAxisChannel = channel,
                Label = "俯仰",
                ValueType = "float",
                Muted = muted,
                Enabled = enabled,
                Clips = new List<MotionClip>(clips),
            };

        private static MotionClip Clip(double start, double end, params (double t, float v)[] kfs)
        {
            var c = new MotionClip { Id = "c1", StartMs = start, EndMs = end };
            foreach (var (t, v) in kfs)
                c.Keyframes.Add(new MotionKeyframe { TimeMs = t, Value = v, Interpolation = "linear" });
            return c;
        }

        private static MotionTimeline Timeline(double durationMs, params MotionTrack[] tracks) =>
            new() { Name = "t", DurationMs = durationMs, Tracks = new List<MotionTrack>(tracks) };

        private static IEnumerable<string> Codes(IReadOnlyList<MotionValidationIssue> issues) =>
            issues.Select(i => i.Code);

        // ── 正例: 干净编排必须零问题 ──────────────────────

        [Fact]
        public void CleanTimeline_ProducesNoIssues()
        {
            var tl = Timeline(
                5000,
                Track(clips: Clip(0, 5000, (0, 0.5f), (5000, 0.8f)))
            );
            var issues = MotionTimelineValidator.Validate(tl);
            Assert.Empty(issues);
            Assert.False(MotionTimelineValidator.HasErrors(issues));
        }

        [Fact]
        public void CleanTimeline_WithDeviceCheckAndRange_StillClean()
        {
            var tl = Timeline(
                5000,
                Track(clips: Clip(0, 5000, (0, 0.5f), (5000, 0.8f)))
            );
            var issues = MotionTimelineValidator.Validate(
                tl,
                new MotionValidationOptions
                {
                    IsDeviceLoaded = _ => true,
                    DeviceRange = (_, _) => (-30f, 30f),
                }
            );
            Assert.Empty(issues);
        }

        // ── 时间轴级 ──────────────────────────────────────

        [Fact]
        public void NullTimeline_IsError()
        {
            Assert.Contains("timeline.null", Codes(MotionTimelineValidator.Validate(null)));
        }

        [Fact]
        public void NoTracks_IsError()
        {
            Assert.Contains("timeline.no_tracks", Codes(MotionTimelineValidator.Validate(Timeline(5000))));
        }

        [Fact]
        public void ZeroDuration_IsError()
        {
            var tl = Timeline(0, Track(clips: Clip(0, 1000, (0, 0.5f))));
            Assert.Contains("timeline.zero_duration", Codes(MotionTimelineValidator.Validate(tl)));
        }

        [Fact]
        public void ContentBeyondDuration_IsError_AndReportsFarEnd()
        {
            // 6 分钟影片但总时长只设了 5 秒 → 后面全部播不到 (长片最容易踩)
            var tl = Timeline(5000, Track(clips: Clip(0, 6000, (0, 0.5f), (6000, 0.8f))));
            var issues = MotionTimelineValidator.Validate(tl);
            var issue = Assert.Single(issues, i => i.Code == "timeline.content_beyond_duration");
            Assert.Equal(MotionIssueSeverity.Error, issue.Severity);
            Assert.Contains("6000", issue.Message);
        }

        [Fact]
        public void InstanceBeyondDuration_IsError()
        {
            var tl = Timeline(5000, Track(clips: Clip(0, 1000, (0, 0.5f))));
            tl.ActionInstances.Add(
                new ActionInstance { Id = "i1", StartMs = 4000, DurationMs = 3000 }
            );
            Assert.Contains(
                "timeline.content_beyond_duration",
                Codes(MotionTimelineValidator.Validate(tl))
            );
        }

        // ── 轨道级 ────────────────────────────────────────

        [Fact]
        public void OAxisTrackWithoutChannel_IsError()
        {
            var tl = Timeline(1000, Track(channel: "", clips: Clip(0, 1000, (0, 0.5f))));
            Assert.Contains("track.no_oaxis_channel", Codes(MotionTimelineValidator.Validate(tl)));
        }

        [Fact]
        public void TrackWithoutDevice_IsError()
        {
            var tl = Timeline(1000, Track(device: "", clips: Clip(0, 1000, (0, 0.5f))));
            Assert.Contains("track.no_device", Codes(MotionTimelineValidator.Validate(tl)));
        }

        [Fact]
        public void DeviceNotLoaded_IsError_OnlyWhenCheckerProvided()
        {
            var tl = Timeline(1000, Track(clips: Clip(0, 1000, (0, 0.5f))));

            // 未提供判定 → 跳过该检查
            Assert.DoesNotContain("track.device_not_loaded", Codes(MotionTimelineValidator.Validate(tl)));

            // 提供判定且返回 false → 报错
            Assert.Contains(
                "track.device_not_loaded",
                Codes(
                    MotionTimelineValidator.Validate(
                        tl,
                        new MotionValidationOptions { IsDeviceLoaded = _ => false }
                    )
                )
            );
        }

        [Fact]
        public void MutedOrDisabledTrack_IsWarning()
        {
            var tl = Timeline(
                1000,
                Track(muted: true, clips: Clip(0, 1000, (0, 0.5f))),
                Track(enabled: false, clips: Clip(0, 1000, (0, 0.5f)))
            );
            var issues = MotionTimelineValidator.Validate(tl);
            Assert.Contains("track.muted", Codes(issues));
            Assert.Contains("track.disabled", Codes(issues));
            // 是警告而非错误: 能播, 只是不输出
            Assert.False(MotionTimelineValidator.HasErrors(issues));
        }

        [Fact]
        public void TrackWithNeitherClipNorIdle_IsInfo()
        {
            var tl = Timeline(1000, Track());
            var issue = Assert.Single(
                MotionTimelineValidator.Validate(tl),
                i => i.Code == "track.always_neutral"
            );
            Assert.Equal(MotionIssueSeverity.Info, issue.Severity);
        }

        // ── clip 级 ───────────────────────────────────────

        [Fact]
        public void InvalidClipRange_IsError()
        {
            var tl = Timeline(1000, Track(clips: Clip(800, 200, (0, 0.5f))));
            Assert.Contains("clip.invalid_range", Codes(MotionTimelineValidator.Validate(tl)));
        }

        [Fact]
        public void EmptyKeyframes_IsWarning()
        {
            var tl = Timeline(1000, Track(clips: Clip(0, 1000)));
            Assert.Contains("clip.empty_keyframes", Codes(MotionTimelineValidator.Validate(tl)));
        }

        [Fact]
        public void OverlappingClips_AreWarning_WithLaterOneShadowed()
        {
            // 求值器取第一个命中的 clip → 后面的被静默遮蔽
            var tl = Timeline(
                5000,
                Track(
                    clips: new[] { Clip(0, 3000, (0, 0.5f)), Clip(2000, 5000, (0, 0.9f)) }
                )
            );
            var issue = Assert.Single(
                MotionTimelineValidator.Validate(tl),
                i => i.Code == "clip.overlap"
            );
            Assert.Equal(MotionIssueSeverity.Warning, issue.Severity);
            Assert.Contains("2000", issue.Message);
        }

        [Fact]
        public void AdjacentClips_AreNotOverlap()
        {
            var tl = Timeline(
                5000,
                Track(
                    clips: new[] { Clip(0, 2000, (0, 0.5f)), Clip(2000, 5000, (0, 0.9f)) }
                )
            );
            Assert.DoesNotContain("clip.overlap", Codes(MotionTimelineValidator.Validate(tl)));
        }

        // ── 关键帧级 ──────────────────────────────────────

        [Fact]
        public void KeyframeOutsideClip_IsWarning()
        {
            var tl = Timeline(5000, Track(clips: Clip(0, 1000, (0, 0.5f), (4000, 0.8f))));
            Assert.Contains("keyframe.out_of_clip", Codes(MotionTimelineValidator.Validate(tl)));
        }

        [Theory]
        [InlineData(1.5f)]
        [InlineData(-0.2f)]
        public void KeyframeOutOfNormalized_IsError(float value)
        {
            // 归一化越界会被求值器静默钳位到 [0,1]
            var tl = Timeline(1000, Track(clips: Clip(0, 1000, (0, value))));
            var issue = Assert.Single(
                MotionTimelineValidator.Validate(tl),
                i => i.Code == "keyframe.out_of_normalized"
            );
            Assert.Equal(MotionIssueSeverity.Error, issue.Severity);
        }

        [Fact]
        public void BoolTrackNonBinaryValue_IsWarning()
        {
            var track = Track(clips: Clip(0, 1000, (0, 0.3f)));
            track.ValueType = "bool";
            var tl = Timeline(1000, track);
            Assert.Contains("keyframe.bool_not_binary", Codes(MotionTimelineValidator.Validate(tl)));
        }

        // ── 动作实例级 ────────────────────────────────────

        [Fact]
        public void InstanceWithMissingDefinition_IsError()
        {
            var tl = Timeline(5000, Track(clips: Clip(0, 1000, (0, 0.5f))));
            tl.ActionInstances.Add(new ActionInstance { Id = "i1", DefinitionId = "fx_dive" });
            Assert.Contains("instance.definition_missing", Codes(MotionTimelineValidator.Validate(tl)));
        }

        [Fact]
        public void InstanceWithOrphanRole_IsError()
        {
            var tl = Timeline(5000, Track(clips: Clip(0, 1000, (0, 0.5f))));
            tl.EmbeddedPresets.Add(new EffectPreset { Id = "fx_dive", Revision = 1 });
            tl.ActionInstances.Add(
                new ActionInstance
                {
                    Id = "i1",
                    DefinitionId = "fx_dive",
                    RoleTrackIds = new Dictionary<string, string> { ["pitch"] = "no_such_track" },
                }
            );
            var issue = Assert.Single(
                MotionTimelineValidator.Validate(tl),
                i => i.Code == "instance.orphan_role"
            );
            Assert.Equal(MotionIssueSeverity.Error, issue.Severity);
        }

        [Fact]
        public void InstanceWithValidRole_IsClean()
        {
            var tl = Timeline(5000, Track(clips: Clip(0, 1000, (0, 0.5f))));
            tl.EmbeddedPresets.Add(new EffectPreset { Id = "fx_dive", Revision = 1 });
            tl.ActionInstances.Add(
                new ActionInstance
                {
                    Id = "i1",
                    DefinitionId = "fx_dive",
                    RoleTrackIds = new Dictionary<string, string> { ["pitch"] = "trk1" },
                }
            );
            Assert.Empty(MotionTimelineValidator.Validate(tl));
        }

        [Fact]
        public void InstanceSpanMismatchWithClips_IsWarning()
        {
            var clip = Clip(0, 1000, (0, 0.5f));
            clip.ActionInstanceId = "i1";
            var tl = Timeline(5000, Track(clips: clip));
            tl.ActionInstances.Add(
                new ActionInstance { Id = "i1", StartMs = 0, DurationMs = 3000 }
            );
            var issue = Assert.Single(
                MotionTimelineValidator.Validate(tl),
                i => i.Code == "instance.clip_span_mismatch"
            );
            Assert.Equal(MotionIssueSeverity.Warning, issue.Severity);
        }

        [Fact]
        public void ClipReferencingMissingInstance_IsWarning()
        {
            var clip = Clip(0, 1000, (0, 0.5f));
            clip.ActionInstanceId = "gone";
            var tl = Timeline(5000, Track(clips: clip));
            Assert.Contains("clip.instance_missing", Codes(MotionTimelineValidator.Validate(tl)));
        }

        // ── 门禁 ─────────────────────────────────────────

        [Fact]
        public void HasErrors_DistinguishesErrorsFromWarnings()
        {
            var clean = MotionTimelineValidator.Validate(
                Timeline(1000, Track(clips: Clip(0, 1000, (0, 0.5f))))
            );
            Assert.False(MotionTimelineValidator.HasErrors(clean));

            var warned = MotionTimelineValidator.Validate(
                Timeline(1000, Track(muted: true, clips: Clip(0, 1000, (0, 0.5f))))
            );
            Assert.False(MotionTimelineValidator.HasErrors(warned)); // 只有警告 → 不算错误

            var broken = MotionTimelineValidator.Validate(
                Timeline(1000, Track(channel: "", clips: Clip(0, 1000, (0, 0.5f))))
            );
            Assert.True(MotionTimelineValidator.HasErrors(broken));
        }
    }
}
