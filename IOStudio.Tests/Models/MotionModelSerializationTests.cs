using System.Collections.Generic;
using System.Text.Json;
using Xunit;
using IOStudio.Models.Motion;

namespace IOStudio.Tests.Models
{
    /// <summary>
    /// Motion 模型序列化 / 反序列化测试
    /// </summary>
    public class MotionModelSerializationTests
    {
        private static readonly JsonSerializerOptions Options =
            new()
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System
                    .Text
                    .Json
                    .Serialization
                    .JsonIgnoreCondition
                    .WhenWritingNull
            };

        [Fact]
        public void MotionKeyframe_RoundTrip_Preserves_Values()
        {
            var kf = new MotionKeyframe
            {
                TimeMs = 500,
                Value = 0.75f,
                Interpolation = "bezier",
                TangentIn = -0.5f,
                TangentOut = 0.5f
            };

            string json = JsonSerializer.Serialize(kf, Options);
            var deserialized = JsonSerializer.Deserialize<MotionKeyframe>(json, Options);

            Assert.NotNull(deserialized);
            Assert.Equal(500, deserialized!.TimeMs);
            Assert.Equal(0.75f, deserialized.Value);
            Assert.Equal("bezier", deserialized.Interpolation);
            Assert.Equal(-0.5f, deserialized.TangentIn);
            Assert.Equal(0.5f, deserialized.TangentOut);
        }

        [Fact]
        public void MotionTimeline_Default_Values()
        {
            var timeline = new MotionTimeline();

            Assert.Equal("1.0", timeline.Version);
            Assert.Equal(60, timeline.Fps);
            Assert.NotNull(timeline.Tracks);
            Assert.Empty(timeline.Tracks);
        }

        [Fact]
        public void MotionKeyframe_LinearDefaults()
        {
            var kf = new MotionKeyframe { TimeMs = 0, Value = 0.5f };

            Assert.Equal("linear", kf.Interpolation);
            Assert.Null(kf.TangentIn);
            Assert.Null(kf.TangentOut);
        }

        [Fact]
        public void MotionTimeline_Serialization_Uses_SnakeCase()
        {
            var timeline = new MotionTimeline { Name = "test", DurationMs = 5000 };

            string json = JsonSerializer.Serialize(timeline, Options);

            Assert.Contains("\"duration_ms\"", json);
            Assert.Contains("\"version\"", json);
            Assert.Contains("\"name\"", json);
        }

        [Fact]
        public void MotionKeyframe_Value_In_0_1_Range()
        {
            // 模型不做范围限制, 但正常值应在 0-1
            var kf = new MotionKeyframe { Value = 0.0f };
            Assert.InRange(kf.Value, 0f, 1f);

            kf.Value = 1.0f;
            Assert.InRange(kf.Value, 0f, 1f);
        }

        // ── C++/C# 格式兼容性测试 (字段名必须与 C++ MotionPlayer 解析一致) ──

        [Fact]
        public void MotionTrack_Serializes_Device_NotDeviceName()
        {
            var track = new MotionTrack { DeviceName = "Chair", OActionName = "Light" };
            string json = JsonSerializer.Serialize(track, Options);

            Assert.Contains("\"device\"", json);
            Assert.DoesNotContain("\"device_name\"", json);
        }

        [Fact]
        public void MotionTrack_Serializes_OAction_NotOActionName()
        {
            var track = new MotionTrack { DeviceName = "Chair", OActionName = "Light" };
            string json = JsonSerializer.Serialize(track, Options);

            Assert.Contains("\"oaction\"", json);
            Assert.DoesNotContain("\"oaction_name\"", json);
        }

        [Fact]
        public void MotionKeyframe_Serializes_Cp1y_NotTangentIn()
        {
            var kf = new MotionKeyframe { TangentIn = 0.5f, TangentOut = -0.5f };
            string json = JsonSerializer.Serialize(kf, Options);

            Assert.Contains("\"cp1y\"", json);
            Assert.DoesNotContain("\"tangent_in\"", json);
        }

        [Fact]
        public void MotionKeyframe_Serializes_Cp2y_NotTangentOut()
        {
            var kf = new MotionKeyframe { TangentIn = 0.5f, TangentOut = -0.5f };
            string json = JsonSerializer.Serialize(kf, Options);

            Assert.Contains("\"cp2y\"", json);
            Assert.DoesNotContain("\"tangent_out\"", json);
        }

        [Fact]
        public void MotionKeyframe_Serializes_Cp1x_Cp2x()
        {
            var kf = new MotionKeyframe { Cp1x = 0.25f, Cp2x = 0.75f };
            string json = JsonSerializer.Serialize(kf, Options);

            Assert.Contains("\"cp1x\"", json);
            Assert.Contains("\"cp2x\"", json);
        }

        [Fact]
        public void FullTimeline_RoundTrip_CppCompatible()
        {
            var timeline = new MotionTimeline
            {
                Name = "Test",
                DurationMs = 10000,
                Tracks = new List<MotionTrack>
                {
                    new()
                    {
                        DeviceName = "Platform-0",
                        OActionName = "Pitch",
                        OutputType = "oaction",
                        Muted = false,
                        Clips = new List<MotionClip>
                        {
                            new()
                            {
                                StartMs = 0,
                                EndMs = 5000,
                                Keyframes = new List<MotionKeyframe>
                                {
                                    new()
                                    {
                                        TimeMs = 0,
                                        Value = 0f,
                                        Interpolation = "linear"
                                    },
                                    new()
                                    {
                                        TimeMs = 2500,
                                        Value = 0.8f,
                                        Interpolation = "bezier",
                                        TangentIn = 0.3f,
                                        TangentOut = -0.2f,
                                        Cp1x = 0.25f,
                                        Cp2x = 0.75f
                                    },
                                    new()
                                    {
                                        TimeMs = 5000,
                                        Value = 0f,
                                        Interpolation = "ease_in_out"
                                    }
                                }
                            }
                        }
                    }
                }
            };

            string json = JsonSerializer.Serialize(timeline, Options);
            var roundTrip = JsonSerializer.Deserialize<MotionTimeline>(json, Options);

            Assert.NotNull(roundTrip);
            Assert.Equal("Test", roundTrip!.Name);
            Assert.Single(roundTrip.Tracks);

            var track = roundTrip.Tracks[0];
            Assert.Equal("Platform-0", track.DeviceName);
            Assert.Equal("Pitch", track.OActionName);

            var clip = track.Clips[0];
            Assert.Equal(3, clip.Keyframes.Count);
            Assert.Equal(0.3f, clip.Keyframes[1].TangentIn);
            Assert.Equal(-0.2f, clip.Keyframes[1].TangentOut);
            Assert.Equal(0.25f, clip.Keyframes[1].Cp1x);
            Assert.Equal(0.75f, clip.Keyframes[1].Cp2x);

            // Verify JSON uses C++ field names
            Assert.Contains("\"device\"", json);
            Assert.Contains("\"oaction\"", json);
            Assert.Contains("\"cp1y\"", json);
            Assert.Contains("\"cp2y\"", json);
            Assert.Contains("\"start_ms\"", json);
            Assert.Contains("\"end_ms\"", json);
            Assert.Contains("\"time_ms\"", json);
        }
    }
}
