using System.Collections.Generic;
using System.Linq;
using Xunit;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// 演出规格 → .motion 构建器测试。
    ///
    /// <para>覆盖: clip 物化与实例回填、强度绕中性缩放、idle 透传、门控、同轨重叠/未知预设/
    /// role 未映射/内容越界等**会静默失效**的规格错误, 以及"产物经序列化后能被预检判为干净"。</para>
    /// </summary>
    public class MotionShowBuilderTests
    {
        private static MotionKeyframe Kf(double t, float v, string interp = "linear") =>
            new() { TimeMs = t, Value = v, Interpolation = interp };

        private static ShowSpec MinimalSpec(
            double duration = 10000,
            float intensity = 1.0f,
            double cueTime = 1000,
            float presetValue = 1.0f
        ) =>
            new()
            {
                Name = "spec",
                DurationMs = duration,
                Device = "Simulator-1",
                Tracks = new List<ShowTrackSpec>
                {
                    new() { Role = "pitch", Channel = "OAxis_01", Label = "俯仰", Group = "主运动" },
                },
                Presets = new List<ShowPresetSpec>
                {
                    new()
                    {
                        Id = "fx_test",
                        Name = "测试",
                        Category = "测试",
                        DurationMs = 2000,
                        Channels = new List<ShowPresetChannelSpec>
                        {
                            new()
                            {
                                Role = "pitch",
                                Keyframes = new List<MotionKeyframe>
                                {
                                    Kf(0, 0.5f, "bezier"),
                                    Kf(1000, presetValue, "bezier"),
                                    Kf(2000, 0.5f, "ease_in_out"),
                                },
                            },
                        },
                    },
                },
                Cues = new List<ShowCueSpec>
                {
                    new() { TimeMs = cueTime, Preset = "fx_test", Intensity = intensity },
                },
            };

        [Fact]
        public void Build_MinimalSpec_ProducesWiredTimeline()
        {
            var result = MotionShowBuilder.Build(MinimalSpec());
            Assert.False(result.HasErrors, string.Join("; ", result.Issues.Select(i => i.ToString())));

            var tl = result.Timeline!;
            Assert.Single(tl.Tracks);
            var track = tl.Tracks[0];
            Assert.Equal("trk_pitch", track.Id);
            Assert.Equal("OAxis_01", track.OAxisChannel);
            Assert.Equal("主运动", track.Group);

            Assert.Single(track.Clips);
            var clip = track.Clips[0];
            Assert.Equal(1000, clip.StartMs);
            Assert.Equal(3000, clip.EndMs);
            Assert.Equal("fx_test", clip.SourceDefinitionId);
            Assert.Equal("pitch", clip.SourceRole);

            Assert.Single(tl.ActionInstances);
            var inst = tl.ActionInstances[0];
            Assert.Equal(clip.ActionInstanceId, inst.Id);
            Assert.Equal(2000, inst.DurationMs);
            Assert.Equal(track.Id, inst.RoleTrackIds["pitch"]);
            Assert.Equal(track.Id, tl.RoleTrackBindings["pitch"]);
            Assert.Single(tl.EmbeddedPresets);
            Assert.Single(tl.Groups);
        }

        [Fact]
        public void Build_ScalesValuesAroundNeutral()
        {
            // neutral 0.5, preset 峰值 1.0, intensity 0.5 → 0.5 + (1-0.5)*0.5 = 0.75
            var result = MotionShowBuilder.Build(MinimalSpec(intensity: 0.5f, presetValue: 1.0f));
            var mid = result.Timeline!.Tracks[0].Clips[0].Keyframes[1];
            Assert.Equal(0.75f, mid.Value, precision: 4);
        }

        [Fact]
        public void Build_CarriesIdleLoop()
        {
            var spec = MinimalSpec();
            spec.Tracks[0].Idle = new ShowIdleSpec
            {
                PeriodMs = 2000,
                BlendMs = 400,
                PhaseMode = "continuous",
                Keyframes = new List<MotionKeyframe> { Kf(0, 0.5f), Kf(1000, 0.6f), Kf(2000, 0.5f) },
            };
            var result = MotionShowBuilder.Build(spec);
            var idle = result.Timeline!.Tracks[0].IdleLoop;
            Assert.NotNull(idle);
            Assert.Equal(2000, idle!.PeriodMs);
            Assert.Equal(3, idle.Keyframes.Count);
        }

        [Fact]
        public void Build_Gate_ProducesOnOffClip()
        {
            var spec = MinimalSpec();
            spec.Tracks.Add(new ShowTrackSpec { Role = "wind", Channel = "OAxis_18", Group = "环境特效" });
            spec.Gates.Add(new ShowGateSpec { Role = "wind", StartMs = 2000, EndMs = 6000, FadeMs = 200 });
            var result = MotionShowBuilder.Build(spec);

            var wind = result.Timeline!.Tracks.Single(t => t.Role == "wind");
            Assert.Single(wind.Clips);
            var clip = wind.Clips[0];
            Assert.Equal(2000, clip.StartMs);
            Assert.Equal(6000, clip.EndMs);
            Assert.Equal(1f, clip.Keyframes[1].Value); // 淡入到 level=1
        }

        [Fact]
        public void Build_OverlappingCues_ReportError()
        {
            var spec = MinimalSpec();
            spec.Cues.Add(new ShowCueSpec { TimeMs = 2000, Preset = "fx_test", Intensity = 1f }); // 与 1000..3000 重叠
            var result = MotionShowBuilder.Build(spec);
            Assert.True(result.HasErrors);
            Assert.Null(result.Timeline);
            Assert.Contains(result.Issues, i => i.Code == "clip.overlap");
        }

        [Fact]
        public void Build_UnknownPreset_ReportError()
        {
            var spec = MinimalSpec();
            spec.Cues[0].Preset = "nope";
            var result = MotionShowBuilder.Build(spec);
            Assert.Contains(result.Issues, i => i.Code == "cue.unknown_preset");
            Assert.Null(result.Timeline);
        }

        [Fact]
        public void Build_UnmappedRole_ReportError()
        {
            var spec = MinimalSpec();
            spec.Presets[0].Channels.Add(
                new ShowPresetChannelSpec { Role = "heave", Keyframes = new List<MotionKeyframe> { Kf(0, 0.5f), Kf(2000, 0.8f) } }
            );
            var result = MotionShowBuilder.Build(spec);
            Assert.Contains(result.Issues, i => i.Code == "cue.role_unmapped");
        }

        [Fact]
        public void Build_ContentBeyondDuration_ReportError()
        {
            var spec = MinimalSpec(duration: 2000, cueTime: 1500); // 1500..3500 超出 2000
            var result = MotionShowBuilder.Build(spec);
            Assert.Contains(result.Issues, i => i.Code == "spec.content_beyond_duration");
            Assert.Null(result.Timeline);
        }

        [Fact]
        public void Build_RoundTrips_And_PassesValidation()
        {
            var spec = MinimalSpec();
            spec.Chapters.Add(new ShowChapterSpec { TimeMs = 0, Name = "开场" });
            spec.Events.Add(new ShowEventSpec { TimeMs = 1000, Name = "scene.start", Data = "{\"x\":1}", Lane = "scene" });
            spec.Tracks[0].Idle = new ShowIdleSpec
            {
                PeriodMs = 2000,
                BlendMs = 300,
                Keyframes = new List<MotionKeyframe> { Kf(0, 0.5f), Kf(1000, 0.55f), Kf(2000, 0.5f) },
            };

            var built = MotionShowBuilder.Build(spec);
            Assert.False(built.HasErrors);

            // 序列化 → 反序列化, 再走预检: 产物必须是干净的
            string json = MotionFileReader.ToJson(built.Timeline!);
            var reloaded = MotionFileReader.ReadFromJson(json);
            Assert.NotNull(reloaded);
            var issues = MotionTimelineValidator.Validate(reloaded!);
            Assert.True(
                MotionTimelineValidator.HasErrors(issues) == false,
                "构建产物不应有 Error: " + string.Join("; ", issues.Select(i => i.ToString()))
            );
            Assert.DoesNotContain(issues, i => i.Code == "clip.overlap");
            Assert.DoesNotContain(issues, i => i.Code == "instance.definition_missing");
        }
    }
}
