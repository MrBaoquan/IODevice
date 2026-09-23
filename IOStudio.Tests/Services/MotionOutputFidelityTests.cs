using System.Collections.Generic;
using Xunit;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// 产出保真 —— 写出的 .motion 必须完整承载作者编的东西。
    ///
    /// 这条链路的目标就是"产出动作文件", 所以最危险的失败不是崩溃, 而是**静默丢字段**:
    /// 写出去的文件少了一部分内容, 打开时才发现 (或更糟, 交付后才发现)。
    /// 这里用一个"富文件"覆盖各时期加入、最容易被漏掉的字段, 逐项比对往返一致性。
    /// </summary>
    public class MotionOutputFidelityTests
    {
        private static MotionKeyframe Kf(double t, float v, string interp = "linear") =>
            new() { TimeMs = t, Value = v, Interpolation = interp };

        [Fact]
        public void RichTimeline_RoundTrip_PreservesAllFields()
        {
            var idle = new IdleLoop
            {
                Enabled = true,
                PeriodMs = 3000,
                BlendMs = 250,
                PhaseMode = "continuous",
                PhaseOffsetMs = 400,
                GroupId = "breath",
                Keyframes = new List<MotionKeyframe> { Kf(0, 0.5f), Kf(1500, 0.8f), Kf(3000, 0.5f) },
            };

            var track = new MotionTrack
            {
                Id = "trk_pitch",
                DeviceName = "Simulator-1",
                OutputType = "oaxis",
                OAxisChannel = "OAxis_01",
                Label = "俯仰",
                Color = "#A78BFA",
                ValueType = "float",
                NeutralValue = 0.45f,
                EngineeringRange = 60f,
                Muted = false,
                Solo = false,
                Enabled = true,
                Locked = false,
                Group = "main",
                Role = "pitch",
                IdleLoop = idle,
                OverrideKeyframes = new List<MotionKeyframe> { Kf(12000, 0.7f), Kf(14000, 0.3f) },
                Clips = new List<MotionClip>
                {
                    new()
                    {
                        Id = "clip_dive",
                        StartMs = 6000,
                        EndMs = 9000,
                        Keyframes = new List<MotionKeyframe> { Kf(0, 0.3f, "ease_in_out"), Kf(3000, 0.3f) },
                        ActionInstanceId = "act_dive",
                        SourceDefinitionId = "fx_dive",
                        SourceRole = "pitch",
                        SourceActionName = "Dive",
                    },
                },
            };

            var preset = new EffectPreset
            {
                Id = "fx_dive",
                Revision = 2,
                Name = "俯冲",
                Category = "coaster",
                Description = "前倾后保持",
                DurationMs = 3000,
                DefaultIntensity = 1f,
                MinIntensity = 0.3f,
                MaxIntensity = 1.2f,
                Keyframes = new List<MotionKeyframe> { Kf(0, 0.3f), Kf(3000, 0.3f) },
            };

            var instance = new ActionInstance
            {
                Id = "act_dive",
                DefinitionId = "fx_dive",
                DefinitionRevision = 2,
                Name = "俯冲 1",
                StartMs = 6000,
                DurationMs = 3000,
                Intensity = 0.85f,
                PlaybackRate = 1.25,
                LoopCount = 2,
                Enabled = true,
                RoleTrackIds = new Dictionary<string, string> { ["pitch"] = "trk_pitch" },
            };

            var timeline = new MotionTimeline
            {
                Version = "3.0",
                Name = "富文件保真",
                DurationMs = 20000,
                Fps = 60,
                Tracks = new List<MotionTrack> { track },
                ActionInstances = new List<ActionInstance> { instance },
                RoleTrackBindings = new Dictionary<string, string> { ["pitch"] = "trk_pitch" },
                EmbeddedPresets = new List<EffectPreset> { preset },
                Groups = new List<TrackGroup> { new() { Name = "main", Color = "#64748B", Collapsed = true } },
                Events = new List<TimelineEvent>
                {
                    new() { TimeMs = 6000, EventName = "dive_start", EventData = "{\"n\":1}" },
                },
                Markers = new List<TimelineMarker> { new() { TimeMs = 0, Name = "开始", Note = "片头" } },
            };

            // ── 产出 ──
            string json = MotionFileReader.ToJson(timeline);
            var back = MotionFileReader.ReadFromJson(json);
            Assert.NotNull(back);

            // ── 顶层 ──
            Assert.Equal("3.0", back!.Version);
            Assert.Equal("富文件保真", back.Name);
            Assert.Equal(20000, back.DurationMs);
            Assert.Equal(60, back.Fps);
            Assert.Single(back.Tracks);
            Assert.Single(back.ActionInstances);
            Assert.Single(back.EmbeddedPresets);
            Assert.Single(back.Groups!);
            Assert.Single(back.Events);
            Assert.Single(back.Markers!);
            Assert.Equal("trk_pitch", back.RoleTrackBindings["pitch"]);

            // ── 轨道 ──
            var t = back.Tracks[0];
            Assert.Equal("trk_pitch", t.Id);
            Assert.Equal("Simulator-1", t.DeviceName);
            Assert.Equal("OAxis_01", t.OAxisChannel);
            Assert.Equal("oaxis", t.OutputType);
            Assert.Equal("float", t.ValueType);
            Assert.Equal(0.45f, t.NeutralValue!.Value, precision: 4);
            Assert.Equal(60f, t.EngineeringRange!.Value, precision: 4);
            Assert.Equal("main", t.Group);
            Assert.Equal("pitch", t.Role);

            // ── 待机循环 (gap 填充) ──
            Assert.NotNull(t.IdleLoop);
            var il = t.IdleLoop!;
            Assert.True(il.Enabled);
            Assert.Equal(3000, il.PeriodMs);
            Assert.Equal(250, il.BlendMs);
            Assert.Equal("continuous", il.PhaseMode);
            Assert.Equal(400, il.PhaseOffsetMs);
            Assert.Equal("breath", il.GroupId);
            Assert.Equal(3, il.Keyframes.Count);
            Assert.Equal(0.8f, il.Keyframes[1].Value, precision: 4);

            // ── 覆盖关键帧 ──
            Assert.NotNull(t.OverrideKeyframes);
            Assert.Equal(2, t.OverrideKeyframes!.Count);
            Assert.Equal(12000, t.OverrideKeyframes[0].TimeMs);
            Assert.Equal(0.7f, t.OverrideKeyframes[0].Value, precision: 4);

            // ── clip 及其来源标记 ──
            Assert.Single(t.Clips);
            var c = t.Clips[0];
            Assert.Equal("clip_dive", c.Id);
            Assert.Equal(6000, c.StartMs);
            Assert.Equal(9000, c.EndMs);
            Assert.Equal(2, c.Keyframes.Count);
            Assert.Equal("ease_in_out", c.Keyframes[0].Interpolation);
            Assert.Equal("act_dive", c.ActionInstanceId);
            Assert.Equal("fx_dive", c.SourceDefinitionId);
            Assert.Equal("pitch", c.SourceRole);
            Assert.Equal("Dive", c.SourceActionName);

            // ── 动作实例 ──
            var i = back.ActionInstances[0];
            Assert.Equal("act_dive", i.Id);
            Assert.Equal("fx_dive", i.DefinitionId);
            Assert.Equal(2, i.DefinitionRevision);
            Assert.Equal(6000, i.StartMs);
            Assert.Equal(3000, i.DurationMs);
            Assert.Equal(0.85f, i.Intensity, precision: 4);
            Assert.Equal(1.25, i.PlaybackRate, precision: 4);
            Assert.Equal(2, i.LoopCount);
            Assert.Equal("trk_pitch", i.RoleTrackIds["pitch"]);

            // ── 内嵌预设快照 ──
            var p = back.EmbeddedPresets[0];
            Assert.Equal("fx_dive", p.Id);
            Assert.Equal(2, p.Revision);
            Assert.Equal("俯冲", p.Name);
            Assert.Equal("coaster", p.Category);
            Assert.Equal(1.2f, p.MaxIntensity, precision: 4);
            Assert.NotNull(p.Keyframes);
            Assert.Equal(2, p.Keyframes!.Count);

            // ── 分组 / 事件 / 标记 ──
            Assert.Equal("main", back.Groups![0].Name);
            Assert.True(back.Groups[0].Collapsed);
            Assert.Equal("dive_start", back.Events[0].EventName);
            Assert.Equal(0, back.Markers![0].TimeMs);
            Assert.Equal("开始", back.Markers[0].Name);
        }

        /// <summary>
        /// 产出应当幂等: 读回再写出, 内容不变 (便于脚本化与版本管理时的稳定 diff)。
        /// </summary>
        [Fact]
        public void RoundTrip_IsStable_SecondWriteMatchesFirst()
        {
            var timeline = Helpers.MotionTestFactory.CreateExtDevTestTimeline(10000);
            string first = MotionFileReader.ToJson(timeline);
            var reloaded = MotionFileReader.ReadFromJson(first);
            string second = MotionFileReader.ToJson(reloaded!);
            Assert.Equal(first, second);
        }

        /// <summary>
        /// 产出的文件必须能通过预检 (干净内容不该产生任何问题)。
        /// </summary>
        [Fact]
        public void ProducedFile_PassesValidation()
        {
            var timeline = new MotionTimeline
            {
                Name = "produced",
                DurationMs = 10000,
                Tracks = new List<MotionTrack>
                {
                    new()
                    {
                        Id = "t1",
                        DeviceName = "Simulator-1",
                        OutputType = "oaxis",
                        OAxisChannel = "OAxis_01",
                        Label = "俯仰",
                        ValueType = "float",
                        Clips = new List<MotionClip>
                        {
                            new()
                            {
                                Id = "c1",
                                StartMs = 0,
                                EndMs = 10000,
                                Keyframes = new List<MotionKeyframe> { Kf(0, 0.5f), Kf(10000, 0.8f) },
                            },
                        },
                    },
                },
            };

            string json = MotionFileReader.ToJson(timeline);
            var back = MotionFileReader.ReadFromJson(json)!;
            var issues = MotionTimelineValidator.Validate(back);
            Assert.Empty(issues);
        }
    }
}
