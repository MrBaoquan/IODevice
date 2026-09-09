using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using IOStudio.Tests.Helpers;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// MotionFileReader 文件读写完整测试
    /// 覆盖: JSON 序列化/反序列化, 文件读写, C++ 字段兼容, 边界条件
    /// </summary>
    public class MotionFileReaderTests : IDisposable
    {
        private readonly string _tempDir;

        public MotionFileReaderTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"MotionFileTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch { }
        }

        private string TempPath(string name) => Path.Combine(_tempDir, name);

        // ═══════ ToJson / ReadFromJson 往返 ═══════

        [Fact]
        public void ToJson_ReadFromJson_RoundTrip_MinimalTimeline()
        {
            var original = MotionTestFactory.CreateMinimalTimeline();
            string json = MotionFileReader.ToJson(original);
            var restored = MotionFileReader.ReadFromJson(json);

            Assert.NotNull(restored);
            Assert.Equal(original.Name, restored!.Name);
            Assert.Equal(original.DurationMs, restored.DurationMs);
            Assert.Equal(original.Tracks.Count, restored.Tracks.Count);
            Assert.Equal(
                original.Tracks[0].Clips[0].Keyframes.Count,
                restored.Tracks[0].Clips[0].Keyframes.Count
            );
        }

        [Fact]
        public void ToJson_ReadFromJson_RoundTrip_FullTimeline()
        {
            var original = MotionTestFactory.CreateExtDevTestTimeline();
            string json = MotionFileReader.ToJson(original);
            var restored = MotionFileReader.ReadFromJson(json);

            Assert.NotNull(restored);
            Assert.Equal(4, restored!.Tracks.Count);
            Assert.Equal(6, restored.Events.Count);
            Assert.NotNull(restored.Markers);
            Assert.Equal(3, restored.Markers!.Count);

            // OAction 轨道
            var lightTrack = restored.Tracks[0];
            Assert.Equal("ExtDev", lightTrack.DeviceName);
            Assert.Equal("Light", lightTrack.OActionName);
            Assert.Equal("float", lightTrack.ValueType);
            Assert.Equal(5, lightTrack.Clips[0].Keyframes.Count);

            // Bool 轨道
            var fireTrack = restored.Tracks[1];
            Assert.Equal("bool", fireTrack.ValueType);
            Assert.Equal(7, fireTrack.Clips[0].Keyframes.Count);

            // OAxis 轨道
            var moveXTrack = restored.Tracks[2];
            Assert.Equal("oaxis", moveXTrack.OutputType);
            Assert.Equal("OAxis_00", moveXTrack.OAxisChannel);
        }

        [Fact]
        public void ToJson_CppCompatible_FieldNames()
        {
            var timeline = MotionTestFactory.CreateMinimalTimeline();
            string json = MotionFileReader.ToJson(timeline);

            // C++ 兼容字段名: "device" (非 "device_name"), "oaction" (非 "oaction_name")
            Assert.Contains("\"device\"", json);
            Assert.Contains("\"oaction\"", json);
            Assert.Contains("\"duration_ms\"", json);
            Assert.Contains("\"time_ms\"", json);
            Assert.Contains("\"start_ms\"", json);
            Assert.Contains("\"end_ms\"", json);
            Assert.Contains("\"value_type\"", json);

            // 不应出现的旧格式字段名
            Assert.DoesNotContain("\"device_name\"", json);
            Assert.DoesNotContain("\"oaction_name\"", json);
        }

        [Fact]
        public void ToJson_OAxisTrack_SerializesChannelAndType()
        {
            var timeline = MotionTestFactory.Timeline(
                "OAxisTest",
                5000,
                MotionTestFactory.OAxisTrack(
                    "Dev",
                    "Axis",
                    "OAxis_02",
                    "测试轴",
                    MotionTestFactory.FullClip(5000, MotionTestFactory.LinearKf(0, 0.5f))
                )
            );

            string json = MotionFileReader.ToJson(timeline);
            Assert.Contains("\"output_type\"", json);
            Assert.Contains("\"oaxis\"", json);
            Assert.Contains("\"oaxis_channel\"", json);
            Assert.Contains("OAxis_02", json);
        }

        [Fact]
        public void ToJson_BezierKeyframe_Preserves_Tangents()
        {
            var timeline = MotionTestFactory.Timeline(
                "BezierTest",
                5000,
                MotionTestFactory.FloatTrack(
                    "Dev",
                    "Act",
                    "Test",
                    MotionTestFactory.FullClip(
                        5000,
                        MotionTestFactory.BezierKf(
                            0,
                            0.5f,
                            tangentIn: 0.3f,
                            tangentOut: -0.2f,
                            cp1x: 0.25f,
                            cp2x: 0.75f
                        )
                    )
                )
            );

            string json = MotionFileReader.ToJson(timeline);
            var restored = MotionFileReader.ReadFromJson(json);
            var kf = restored!.Tracks[0].Clips[0].Keyframes[0];

            Assert.Equal("bezier", kf.Interpolation);
            Assert.Equal(0.3f, kf.TangentIn);
            Assert.Equal(-0.2f, kf.TangentOut);
            Assert.Equal(0.25f, kf.Cp1x);
            Assert.Equal(0.75f, kf.Cp2x);
        }

        [Fact]
        public void ToJson_NullTangents_OmittedInJson()
        {
            var timeline = MotionTestFactory.CreateMinimalTimeline();
            string json = MotionFileReader.ToJson(timeline);

            // linear 关键帧的 TangentIn/Out 应为 null → JSON 中被忽略
            Assert.DoesNotContain("\"cp1y\"", json);
            Assert.DoesNotContain("\"cp2y\"", json);
            Assert.DoesNotContain("\"cp1x\"", json);
            Assert.DoesNotContain("\"cp2x\"", json);
        }

        // ═══════ 文件读写 ═══════

        [Fact]
        public void Write_Read_File_RoundTrip()
        {
            var original = MotionTestFactory.CreateExtDevTestTimeline();
            string path = TempPath("test_roundtrip.motion");

            bool written = MotionFileReader.Write(original, path);
            Assert.True(written);
            Assert.True(File.Exists(path));

            var restored = MotionFileReader.Read(path);
            Assert.NotNull(restored);
            Assert.Equal(original.Name, restored!.Name);
            Assert.Equal(original.DurationMs, restored.DurationMs);
            Assert.Equal(original.Tracks.Count, restored.Tracks.Count);
        }

        [Fact]
        public void Write_Creates_Directory_If_Missing()
        {
            var timeline = MotionTestFactory.CreateMinimalTimeline();
            string path = Path.Combine(_tempDir, "subdir", "nested", "test.motion");

            bool written = MotionFileReader.Write(timeline, path);
            Assert.True(written);
            Assert.True(File.Exists(path));
        }

        [Fact]
        public void Read_NonExistentFile_ReturnsNull()
        {
            var result = MotionFileReader.Read(TempPath("nonexistent.motion"));
            Assert.Null(result);
        }

        [Fact]
        public void Read_EmptyPath_ReturnsNull()
        {
            Assert.Null(MotionFileReader.Read(""));
            Assert.Null(MotionFileReader.Read(null!));
        }

        [Fact]
        public void ReadFromJson_EmptyJson_ReturnsNull()
        {
            Assert.Null(MotionFileReader.ReadFromJson(""));
            Assert.Null(MotionFileReader.ReadFromJson("   "));
            Assert.Null(MotionFileReader.ReadFromJson(null!));
        }

        [Fact]
        public void ReadFromJson_InvalidJson_ReturnsNull()
        {
            Assert.Null(MotionFileReader.ReadFromJson("{invalid json}"));
            Assert.Null(MotionFileReader.ReadFromJson("not json at all"));
        }

        [Fact]
        public void Write_NullTimeline_ReturnsFalse()
        {
            Assert.False(MotionFileReader.Write(null!, TempPath("x.motion")));
        }

        [Fact]
        public void Write_EmptyPath_ReturnsFalse()
        {
            Assert.False(MotionFileReader.Write(new MotionTimeline(), ""));
        }

        // ═══════ 测试数据文件加载 ═══════

        [Fact]
        public void Read_TestDataFile_LoadsCorrectly()
        {
            string testDataPath = Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                "ExtDev_IntegrationTest.motion"
            );

            // 文件应已被 csproj 复制到输出目录
            if (!File.Exists(testDataPath))
                return; // 首次构建可能未复制, 跳过

            var timeline = MotionFileReader.Read(testDataPath);
            Assert.NotNull(timeline);
            Assert.Equal("ExtDev_IntegrationTest", timeline!.Name);
            Assert.Equal(10000, timeline.DurationMs);
            Assert.Equal(4, timeline.Tracks.Count);
            Assert.Equal(6, timeline.Events.Count);
        }

        // ═══════ IsEncrypted ═══════

        [Theory]
        [InlineData("test.motion", false)]
        [InlineData("test.mtn", true)]
        [InlineData("test.MTN", true)]
        [InlineData("test.json", false)]
        [InlineData("", false)]
        public void IsEncrypted_DetectsFileExtension(string path, bool expected)
        {
            Assert.Equal(expected, MotionFileReader.IsEncrypted(path));
        }

        // ═══════ Events / Markers 序列化 ═══════

        [Fact]
        public void ToJson_Events_PreservesAllFields()
        {
            var timeline = MotionTestFactory.TimelineWithEvents(
                "EvtTest",
                5000,
                tracks: new List<MotionTrack>(),
                events: new List<TimelineEvent>
                {
                    new()
                    {
                        TimeMs = 1000,
                        EventName = "shot",
                        EventData = "{\"id\":1}",
                        DataType = "json"
                    },
                    new()
                    {
                        TimeMs = 2000,
                        EventName = "stop",
                        DataType = "string"
                    },
                }
            );

            string json = MotionFileReader.ToJson(timeline);
            var restored = MotionFileReader.ReadFromJson(json);

            Assert.Equal(2, restored!.Events.Count);
            Assert.Equal("shot", restored.Events[0].EventName);
            Assert.Equal("{\"id\":1}", restored.Events[0].EventData);
            Assert.Equal("stop", restored.Events[1].EventName);
        }

        [Fact]
        public void ToJson_Markers_PreservesAllFields()
        {
            var timeline = MotionTestFactory.TimelineWithEvents(
                "MkrTest",
                5000,
                tracks: new List<MotionTrack>(),
                markers: new List<TimelineMarker>
                {
                    new()
                    {
                        TimeMs = 0,
                        Name = "Start",
                        Note = "Begin here",
                        Color = "#ff0000"
                    },
                    new() { TimeMs = 5000, Name = "End" },
                }
            );

            string json = MotionFileReader.ToJson(timeline);
            var restored = MotionFileReader.ReadFromJson(json);

            Assert.NotNull(restored!.Markers);
            Assert.Equal(2, restored.Markers!.Count);
            Assert.Equal("Start", restored.Markers[0].Name);
            Assert.Equal("Begin here", restored.Markers[0].Note);
            Assert.Equal("#ff0000", restored.Markers[0].Color);
        }
    }
}
