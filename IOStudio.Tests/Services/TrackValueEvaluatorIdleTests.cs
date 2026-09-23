using System.Collections.Generic;
using Xunit;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// −30 缺陷定位测试 (2026-09-23)。
    ///
    /// 现象: 一个"只覆盖部分时间轴"的 clip (6000–9000ms, 关键帧值 0.3) 与 idle 同时存在时,
    /// 实播在 clip 窗口内输出归一化 0.0 (工程量 = 区间下限), 而不是 clip 的 0.3。
    /// 对照: 同样的值、同样 60 量程, 如果 clip 覆盖整条时间轴 (起始 = 0), 则正确输出 -12。
    ///
    /// 三组用例分别覆盖三层, 用来把缺陷收窄到具体某一层:
    ///   A. 求值器直调 (纯语义)
    ///   B. JSON 往返 (读文件 → 模型)
    ///   C. 播放引擎 Seek (编辑器实播路径)
    /// </summary>
    public class TrackValueEvaluatorIdleTests
    {
        private const double ClipStart = 6000;
        private const double ClipEnd = 9000;
        private const float ClipValue = 0.3f;

        // ── 构造与 idle_probe.motion 完全等价的模型 ──────────

        private static MotionClip PartialClip() => new()
        {
            StartMs = ClipStart,
            EndMs = ClipEnd,
            Keyframes = new List<MotionKeyframe>
            {
                new() { TimeMs = 0, Value = ClipValue, Interpolation = "ease_in_out" },
                new() { TimeMs = 3000, Value = ClipValue, Interpolation = "ease_in_out" },
            },
        };

        private static IdleLoop Idle() => new()
        {
            Enabled = true,
            PeriodMs = 3000,
            BlendMs = 300,
            PhaseMode = "continuous",
            Keyframes = new List<MotionKeyframe>
            {
                new() { TimeMs = 0, Value = 0.5f, Interpolation = "ease_in_out" },
                new() { TimeMs = 1000, Value = 0.8f, Interpolation = "ease_in_out" },
                new() { TimeMs = 2000, Value = 0.5f, Interpolation = "ease_in_out" },
                new() { TimeMs = 3000, Value = 0.5f, Interpolation = "ease_in_out" },
            },
        };

        private static MotionTrack PitchTrack(IdleLoop? idle) => new()
        {
            DeviceName = "Simulator-1",
            OutputType = "oaxis",
            OAxisChannel = "OAxis_01",
            Label = "Pitch",
            ValueType = "float",
            EngineeringRange = 60,
            IdleLoop = idle,
            Clips = new List<MotionClip> { PartialClip() },
        };

        // ═══ A. 求值器直调 ═══════════════════════════════════

        [Theory]
        // clip 窗口内 → 必须是 clip 值 (0.3), idle 不得介入
        [InlineData(6000, 0.3)]
        [InlineData(6500, 0.3)]
        [InlineData(7500, 0.3)]
        [InlineData(8800, 0.3)]
        [InlineData(9000, 0.3)]
        public void A_Evaluator_PartialClip_InWindow_ReturnsClipValue(double t, double expected)
        {
            float v = TrackValueEvaluator.Evaluate(
                new List<MotionClip> { PartialClip() },
                "float",
                null,
                Idle(),
                null,
                t
            );
            Assert.Equal(expected, v, precision: 3);
        }

        [Fact]
        public void A_Evaluator_PartialClip_WithoutIdle_InWindow_ReturnsClipValue()
        {
            // 对照: 无 idle 时同样应得 0.3 → 若这里过而上面不过, 缺陷在 idle 分支
            float v = TrackValueEvaluator.Evaluate(
                new List<MotionClip> { PartialClip() },
                "float",
                null,
                null,
                null,
                7500
            );
            Assert.Equal(0.3f, v, precision: 3);
        }

        [Fact]
        public void A_Evaluator_PartialClip_ClipStartNotZero_IsStillMatched()
        {
            // 收窄: 把 clip 平移到 0 起点 (整条时间轴覆盖的等价形态), 必须仍为 0.3
            var clip = new MotionClip
            {
                StartMs = 0,
                EndMs = 3000,
                Keyframes = new List<MotionKeyframe>
                {
                    new() { TimeMs = 0, Value = ClipValue, Interpolation = "ease_in_out" },
                    new() { TimeMs = 3000, Value = ClipValue, Interpolation = "ease_in_out" },
                },
            };
            float v = TrackValueEvaluator.Evaluate(
                new List<MotionClip> { clip },
                "float",
                null,
                Idle(),
                null,
                1500
            );
            Assert.Equal(0.3f, v, precision: 3);
        }

        // ═══ B. JSON 往返 ════════════════════════════════════

        internal const string ProbeJson = """
        {
          "version": "3.0",
          "name": "Idle loop + preset-clip probe",
          "duration_ms": 15000,
          "fps": 60,
          "tracks": [
            {
              "id": "trk_pitch",
              "device": "Simulator-1",
              "oaction": "",
              "output_type": "oaxis",
              "oaxis_channel": "OAxis_01",
              "label": "Pitch",
              "color": "#A78BFA",
              "value_type": "float",
              "muted": false,
              "enabled": true,
              "locked": false,
              "group": "main",
              "role": "pitch",
              "show_in_curve": true,
              "engineering_range": 60,
              "idle": {
                "enabled": true,
                "period_ms": 3000,
                "blend_ms": 300,
                "phase_mode": "continuous",
                "keyframes": [
                  { "time_ms": 0, "value": 0.5, "interpolation": "ease_in_out" },
                  { "time_ms": 1000, "value": 0.8, "interpolation": "ease_in_out" },
                  { "time_ms": 2000, "value": 0.5, "interpolation": "ease_in_out" },
                  { "time_ms": 3000, "value": 0.5, "interpolation": "ease_in_out" }
                ]
              },
              "clips": [
                {
                  "id": "clip_dive",
                  "start_ms": 6000,
                  "end_ms": 9000,
                  "keyframes": [
                    { "time_ms": 0, "value": 0.3, "interpolation": "ease_in_out" },
                    { "time_ms": 3000, "value": 0.3, "interpolation": "ease_in_out" }
                  ],
                  "action_instance_id": "act_dive_01",
                  "source_definition_id": "fx_dive",
                  "source_role": "pitch",
                  "source_action_name": "Dive"
                }
              ]
            }
          ],
          "action_instances": [
            {
              "id": "act_dive_01",
              "definition_id": "fx_dive",
              "definition_revision": 1,
              "name": "Dive",
              "start_ms": 6000,
              "duration_ms": 3000,
              "intensity": 1,
              "playback_rate": 1,
              "loop_count": 1,
              "enabled": true,
              "role_track_ids": { "pitch": "trk_pitch" }
            }
          ],
          "role_track_bindings": { "pitch": "trk_pitch" },
          "embedded_presets": [
            {
              "id": "fx_dive",
              "revision": 1,
              "name": "Dive",
              "category": "coaster",
              "description": "pitch forward then hold",
              "duration_ms": 3000,
              "default_intensity": 1,
              "min_intensity": 0.3,
              "max_intensity": 1.2,
              "channels": [
                {
                  "role": "pitch",
                  "scale": 1,
                  "keyframes": [
                    { "time_ms": 0, "value": 0.3, "interpolation": "ease_in_out" },
                    { "time_ms": 3000, "value": 0.3, "interpolation": "ease_in_out" }
                  ]
                }
              ]
            }
          ],
          "groups": [ { "name": "main", "color": "#64748B", "collapsed": false } ],
          "events": [],
          "markers": [],
          "event_lanes": [],
          "marker_lanes": []
        }
        """;

        [Fact]
        public void B_RoundTrip_ClipGeometryAndValues_Survive()
        {
            var tl = MotionFileReader.ReadFromJson(ProbeJson);
            Assert.NotNull(tl);
            Assert.Single(tl!.Tracks);

            var track = tl.Tracks[0];
            Assert.Equal("float", track.ValueType);
            Assert.Equal(60f, track.EngineeringRange!.Value);
            Assert.NotNull(track.IdleLoop);
            Assert.True(track.IdleLoop!.Enabled);
            Assert.Equal(3000, track.IdleLoop.PeriodMs);
            Assert.Equal(300, track.IdleLoop.BlendMs);
            Assert.Equal(4, track.IdleLoop.Keyframes.Count);

            // ← 关键: clip 几何与关键帧值是否原样保留
            Assert.Single(track.Clips);
            var clip = track.Clips[0];
            Assert.Equal(6000, clip.StartMs);
            Assert.Equal(9000, clip.EndMs);
            Assert.Equal(2, clip.Keyframes.Count);
            Assert.Equal(0, clip.Keyframes[0].TimeMs);
            Assert.Equal(0.3f, clip.Keyframes[0].Value, precision: 3);
            Assert.Equal(3000, clip.Keyframes[1].TimeMs);
            Assert.Equal(0.3f, clip.Keyframes[1].Value, precision: 3);
        }

        [Fact]
        public void B_RoundTrip_Evaluate_InWindow_ReturnsClipValue()
        {
            var tl = MotionFileReader.ReadFromJson(ProbeJson)!;
            var track = tl.Tracks[0];
            float v = TrackValueEvaluator.Evaluate(
                track.Clips,
                track.ValueType,
                track.NeutralValue,
                track.IdleLoop,
                track.OverrideKeyframes,
                7500
            );
            Assert.Equal(0.3f, v, precision: 3);
        }

        // ═══ C. 引擎实播路径 ═════════════════════════════════

        [Fact]
        public void C_Engine_Seek_InClipWindow_EmitsClipValue()
        {
            var tl = MotionFileReader.ReadFromJson(ProbeJson)!;
            var engine = new NativeMotionPlaybackEngine(new DeviceDispatcher());
            engine.EnsureTimelineLoaded(tl);

            var bySeek = new List<float>();
            engine.LiveValuesBatchUpdated += b =>
            {
                if (b.Count > 0)
                    bySeek.Add(b[0].Item3);
            };

            double[] times = { 0d, 3000d, 7500d, 10500d };
            foreach (double t in times)
                engine.Seek(t);

            Assert.Equal(times.Length, bySeek.Count);

            // clip 窗口内 → 0.3 (而不是 0.0)
            Assert.Equal(0.3f, bySeek[2], precision: 3);
        }
    }
}
