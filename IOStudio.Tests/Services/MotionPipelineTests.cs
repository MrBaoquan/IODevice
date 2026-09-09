using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using IOStudio.Tests.Helpers;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// Motion 全接口流水线测试 — 从构造到序列化到求值到导出的完整流程
    /// 模拟实际使用场景: 创建 → 保存 → 加载 → 播放 → 导出
    /// </summary>
    public class MotionPipelineTests : IDisposable
    {
        private readonly string _tempDir;

        public MotionPipelineTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"MotionPipeline_{Guid.NewGuid():N}");
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

        // ═══════ P1: 创建 → 序列化 → 反序列化 → 验证一致性 ═══════

        [Fact]
        public void Pipeline_Create_Serialize_Deserialize_Verify()
        {
            // 1. 构造 ExtDev 测试时间轴
            var original = MotionTestFactory.CreateExtDevTestTimeline(10000);
            Assert.Equal(4, original.Tracks.Count);
            Assert.Equal(6, original.Events.Count);

            // 2. 序列化
            string json = MotionFileReader.ToJson(original);
            Assert.False(string.IsNullOrEmpty(json));

            // 3. 反序列化
            var restored = MotionFileReader.ReadFromJson(json);
            Assert.NotNull(restored);

            // 4. 逐字段验证
            Assert.Equal(original.Name, restored!.Name);
            Assert.Equal(original.DurationMs, restored.DurationMs);
            Assert.Equal(original.Fps, restored.Fps);
            Assert.Equal(original.Tracks.Count, restored.Tracks.Count);
            Assert.Equal(original.Events.Count, restored.Events.Count);

            for (int t = 0; t < original.Tracks.Count; t++)
            {
                var ot = original.Tracks[t];
                var rt = restored.Tracks[t];
                Assert.Equal(ot.DeviceName, rt.DeviceName);
                Assert.Equal(ot.OActionName, rt.OActionName);
                Assert.Equal(ot.OutputType, rt.OutputType);
                Assert.Equal(ot.OAxisChannel, rt.OAxisChannel);
                Assert.Equal(ot.ValueType, rt.ValueType);
                Assert.Equal(ot.Label, rt.Label);

                Assert.Equal(ot.Clips.Count, rt.Clips.Count);
                for (int c = 0; c < ot.Clips.Count; c++)
                {
                    Assert.Equal(ot.Clips[c].StartMs, rt.Clips[c].StartMs);
                    Assert.Equal(ot.Clips[c].EndMs, rt.Clips[c].EndMs);
                    Assert.Equal(ot.Clips[c].Keyframes.Count, rt.Clips[c].Keyframes.Count);

                    for (int k = 0; k < ot.Clips[c].Keyframes.Count; k++)
                    {
                        var ok = ot.Clips[c].Keyframes[k];
                        var rk = rt.Clips[c].Keyframes[k];
                        Assert.Equal(ok.TimeMs, rk.TimeMs);
                        Assert.Equal(ok.Value, rk.Value, precision: 4);
                        Assert.Equal(ok.Interpolation, rk.Interpolation);
                        Assert.Equal(ok.TangentIn, rk.TangentIn);
                        Assert.Equal(ok.TangentOut, rk.TangentOut);
                    }
                }
            }
        }

        // ═══════ P2: 创建 → 保存文件 → 加载文件 → 引擎加载 → Seek → 验证 ═══════

        [Fact]
        public void Pipeline_SaveFile_LoadFile_EngineSeek()
        {
            // 1. 创建并保存
            var timeline = MotionTestFactory.CreateExtDevTestTimeline();
            string path = TempPath("pipeline_test.motion");
            Assert.True(MotionFileReader.Write(timeline, path));

            // 2. 从文件加载
            var loaded = MotionFileReader.Read(path);
            Assert.NotNull(loaded);

            // 3. 引擎加载 (使用 EnsureTimelineLoaded 绕过 C++ interop)
            var engine = new NativeMotionPlaybackEngine(new DeviceDispatcher());
            engine.EnsureTimelineLoaded(loaded!);

            // 4. Seek 求值 — 在关键帧精确时间点验证
            var results = new Dictionary<double, IReadOnlyList<(string, string, float)>>();
            engine.LiveValuesBatchUpdated += batch =>
            {
                // 复制一份快照
                results[results.Count] = batch.ToList();
            };

            engine.Seek(0); // t=0
            engine.Seek(5000); // t=5000 (中点)
            engine.Seek(10000); // t=10000 (终点)

            Assert.Equal(3, results.Count);
            // 每个时间点都应有 4 条轨道输出
            foreach (var (_, batch) in results)
                Assert.Equal(4, batch.Count);
        }

        // ═══════ P3: 构造 → 引擎求值 → CSV 导出 → 验证采样 ═══════

        [Fact]
        public void Pipeline_Engine_Evaluate_Then_Export_Csv()
        {
            // 1. 构造
            var timeline = MotionTestFactory.CreateMinimalTimeline(2000);

            // 2. 引擎求值验证 (确认曲线正确)
            var engine = new NativeMotionPlaybackEngine(new DeviceDispatcher());
            engine.EnsureTimelineLoaded(timeline);

            float midValue = 0f;
            engine.LiveValuesBatchUpdated += b =>
            {
                if (b.Count > 0)
                    midValue = b[0].Item3;
            };
            engine.Seek(1000); // 中点 = 1.0

            Assert.Equal(1.0f, midValue, precision: 2);

            // 3. CSV 导出
            string csvPath = TempPath("pipeline.csv");
            var exportResult = MotionExportService.Export(
                timeline,
                csvPath,
                new MotionExportService.ExportOptions
                {
                    Format = MotionExportService.ExportFormat.Csv,
                    SampleRateHz = 10,
                    ValuePrecision = 4
                }
            );
            Assert.True(exportResult.Success);

            // 4. 验证 CSV 内容与引擎求值一致
            string csv = File.ReadAllText(csvPath);
            var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // 找到 t=1000 的行
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split(',');
                if (double.TryParse(parts[0], out double t) && Math.Abs(t - 1000) < 1)
                {
                    float csvVal = float.Parse(parts[1]);
                    Assert.Equal(midValue, csvVal, precision: 2);
                    break;
                }
            }
        }

        // ═══════ P4: 混合插值全覆盖 → 引擎逐帧求值 → 值域验证 ═══════

        [Fact]
        public void Pipeline_MixedInterpolation_AllValuesInRange()
        {
            var timeline = MotionTestFactory.CreateMixedInterpolationTimeline(8000);
            var engine = new NativeMotionPlaybackEngine(new DeviceDispatcher());
            engine.EnsureTimelineLoaded(timeline);

            var allValues = new List<float>();
            engine.LiveValuesBatchUpdated += b =>
            {
                foreach (var (_, _, v) in b)
                    allValues.Add(v);
            };

            // 每 100ms 采样
            for (double t = 0; t <= 8000; t += 100)
                engine.Seek(t);

            Assert.True(allValues.Count > 0);
            foreach (var v in allValues)
                Assert.InRange(v, 0f, 1f);
        }

        // ═══════ P5: Bool 轨道完整流程 — 创建 → 保存 → 加载 → 求值 → 导出 ═══════

        [Fact]
        public void Pipeline_BoolTrack_FullCycle()
        {
            // 1. 创建 Bool 轨道
            var timeline = MotionTestFactory.Timeline(
                "BoolPipeline",
                4000,
                MotionTestFactory.BoolTrack(
                    "Dev",
                    "Switch",
                    "开关",
                    MotionTestFactory.FullClip(
                        4000,
                        MotionTestFactory.StepKf(0, 0f),
                        MotionTestFactory.StepKf(1000, 1f),
                        MotionTestFactory.StepKf(2000, 0f),
                        MotionTestFactory.StepKf(3000, 1f)
                    )
                )
            );

            // 2. 保存 & 加载
            string path = TempPath("bool_pipeline.motion");
            Assert.True(MotionFileReader.Write(timeline, path));
            var loaded = MotionFileReader.Read(path);
            Assert.NotNull(loaded);
            Assert.Equal("bool", loaded!.Tracks[0].ValueType);

            // 3. 引擎求值 — Bool 轨道强制阶梯
            var engine = new NativeMotionPlaybackEngine(new DeviceDispatcher());
            engine.EnsureTimelineLoaded(loaded);

            var seekResults = new List<(double time, float value)>();
            engine.LiveValuesBatchUpdated += b =>
            {
                if (b.Count > 0)
                    seekResults.Add((seekResults.Count, b[0].Item3));
            };

            engine.Seek(0); // 0
            engine.Seek(500); // 0 (step: 保持前一帧)
            engine.Seek(1000); // 1
            engine.Seek(1500); // 1
            engine.Seek(2000); // 0
            engine.Seek(2500); // 0
            engine.Seek(3000); // 1
            engine.Seek(3500); // 1

            float[] expected = { 0, 0, 1, 1, 0, 0, 1, 1 };
            Assert.Equal(expected.Length, seekResults.Count);
            for (int i = 0; i < expected.Length; i++)
                Assert.Equal(expected[i], seekResults[i].value, precision: 1);

            // 4. CSV 导出
            string csvPath = TempPath("bool_export.csv");
            var exportResult = MotionExportService.Export(
                loaded,
                csvPath,
                new MotionExportService.ExportOptions
                {
                    Format = MotionExportService.ExportFormat.Csv,
                    SampleRateHz = 4 // 4Hz → 每 250ms 采样
                }
            );
            Assert.True(exportResult.Success);
        }

        // ═══════ P6: OAxis 轨道完整流程 ═══════

        [Fact]
        public void Pipeline_OAxisTrack_ChannelRouting()
        {
            var timeline = MotionTestFactory.Timeline(
                "OAxisPipeline",
                2000,
                MotionTestFactory.OAxisTrack(
                    "ExtDev",
                    "MoveX",
                    "OAxis_00",
                    "左右",
                    MotionTestFactory.FullClip(
                        2000,
                        MotionTestFactory.LinearKf(0, 0.0f),
                        MotionTestFactory.LinearKf(2000, 1.0f)
                    )
                )
            );

            // 验证序列化保留 output_type 和 oaxis_channel
            string json = MotionFileReader.ToJson(timeline);
            Assert.Contains("\"output_type\"", json);
            Assert.Contains("\"oaxis\"", json);
            Assert.Contains("\"oaxis_channel\"", json);
            Assert.Contains("OAxis_00", json);

            // 引擎求值 — channelName 应为 OAxis_00 (而非 MoveX)
            var engine = new NativeMotionPlaybackEngine(new DeviceDispatcher());
            engine.EnsureTimelineLoaded(timeline);

            string? channelName = null;
            engine.LiveValuesBatchUpdated += b =>
            {
                if (b.Count > 0)
                    channelName = b[0].Item2;
            };

            engine.Seek(1000);
            Assert.Equal("OAxis_00", channelName);
        }

        // ═══════ P7: 事件触发时间验证 ═══════

        [Fact]
        public void Pipeline_Events_PreservedAfterRoundTrip()
        {
            var timeline = MotionTestFactory.CreateExtDevTestTimeline();

            // 保存 → 加载
            string path = TempPath("events_roundtrip.motion");
            MotionFileReader.Write(timeline, path);
            var loaded = MotionFileReader.Read(path);

            Assert.NotNull(loaded);
            Assert.Equal(6, loaded!.Events.Count);

            // 验证事件时间和名称
            Assert.Equal(1000, loaded.Events[0].TimeMs);
            Assert.Equal("fire_start", loaded.Events[0].EventName);
            Assert.Contains("weapon", loaded.Events[0].EventData!);

            Assert.Equal(9000, loaded.Events[5].TimeMs);
            Assert.Equal("fire_stop", loaded.Events[5].EventName);
        }

        // ═══════ P8: 标记保留验证 ═══════

        [Fact]
        public void Pipeline_Markers_PreservedAfterRoundTrip()
        {
            var timeline = MotionTestFactory.CreateExtDevTestTimeline();

            string path = TempPath("markers_roundtrip.motion");
            MotionFileReader.Write(timeline, path);
            var loaded = MotionFileReader.Read(path);

            Assert.NotNull(loaded!.Markers);
            Assert.Equal(3, loaded.Markers!.Count);
            Assert.Equal("开始", loaded.Markers[0].Name);
            Assert.Equal(0, loaded.Markers[0].TimeMs);
            Assert.Equal("中场", loaded.Markers[1].Name);
            Assert.Equal(5000, loaded.Markers[1].TimeMs);
        }

        // ═══════ P9: 多格式导出一致性 — 同一 timeline 三种格式 ═══════

        [Fact]
        public void Pipeline_MultiFormat_ExportConsistency()
        {
            var timeline = MotionTestFactory.CreateMinimalTimeline(2000);

            // MotionJson
            string jsonPath = TempPath("multi.motion");
            var r1 = MotionExportService.Export(
                timeline,
                jsonPath,
                new MotionExportService.ExportOptions
                {
                    Format = MotionExportService.ExportFormat.MotionJson
                }
            );
            Assert.True(r1.Success);

            // CSV
            string csvPath = TempPath("multi.csv");
            var r2 = MotionExportService.Export(
                timeline,
                csvPath,
                new MotionExportService.ExportOptions
                {
                    Format = MotionExportService.ExportFormat.Csv,
                    SampleRateHz = 10
                }
            );
            Assert.True(r2.Success);

            // CompactJson
            string compactPath = TempPath("multi_compact.json");
            var r3 = MotionExportService.Export(
                timeline,
                compactPath,
                new MotionExportService.ExportOptions
                {
                    Format = MotionExportService.ExportFormat.CompactJson,
                    SampleRateHz = 10
                }
            );
            Assert.True(r3.Success);

            // 三个文件都应存在且非空
            Assert.True(new FileInfo(jsonPath).Length > 0);
            Assert.True(new FileInfo(csvPath).Length > 0);
            Assert.True(new FileInfo(compactPath).Length > 0);

            // MotionJson 应该可以被重新加载
            var reloaded = MotionFileReader.Read(jsonPath);
            Assert.NotNull(reloaded);
            Assert.Equal(timeline.Tracks.Count, reloaded!.Tracks.Count);
        }

        // ═══════ P10: 端到端完整场景 — 模拟实际编辑器使用 ═══════

        [Fact]
        public void Pipeline_EndToEnd_EditorWorkflow()
        {
            // Step 1: 用户创建新项目
            var timeline = new MotionTimeline { Name = "E2E_Test", DurationMs = 5000, };

            // Step 2: 添加轨道
            timeline.Tracks.Add(
                MotionTestFactory.FloatTrack(
                    "ExtDev",
                    "Light",
                    "氛围灯",
                    MotionTestFactory.FullClip(5000, MotionTestFactory.LinearKf(0, 0.5f))
                )
            );

            timeline.Tracks.Add(
                MotionTestFactory.BoolTrack(
                    "ExtDev",
                    "Fire",
                    "开火",
                    MotionTestFactory.FullClip(5000, MotionTestFactory.StepKf(0, 0f))
                )
            );

            // Step 3: 添加关键帧 (模拟编辑)
            timeline.Tracks[0].Clips[0].Keyframes.Add(MotionTestFactory.EaseKf(2500, 1.0f));
            timeline.Tracks[0].Clips[0].Keyframes.Add(MotionTestFactory.LinearKf(5000, 0.5f));
            timeline.Tracks[1].Clips[0].Keyframes.Add(MotionTestFactory.StepKf(1000, 1f));
            timeline.Tracks[1].Clips[0].Keyframes.Add(MotionTestFactory.StepKf(3000, 0f));

            // Step 4: 添加事件
            timeline.Events.Add(MotionTestFactory.Event(1000, "fire_on"));
            timeline.Events.Add(MotionTestFactory.Event(3000, "fire_off"));

            // Step 5: 保存
            string savePath = TempPath("e2e_project.motion");
            Assert.True(MotionFileReader.Write(timeline, savePath));

            // Step 6: 重新加载 (模拟关闭再打开)
            var loaded = MotionFileReader.Read(savePath);
            Assert.NotNull(loaded);
            Assert.Equal("E2E_Test", loaded!.Name);
            Assert.Equal(2, loaded.Tracks.Count);
            Assert.Equal(2, loaded.Events.Count);

            // Step 7: 引擎播放预览
            var engine = new NativeMotionPlaybackEngine(new DeviceDispatcher());
            engine.EnsureTimelineLoaded(loaded);

            // 模拟拖拽播放头
            var seekLog = new List<(double time, string channel, float value)>();
            engine.LiveValuesBatchUpdated += batch =>
            {
                foreach (var (dev, ch, v) in batch)
                    seekLog.Add((seekLog.Count, ch, v));
            };

            for (double t = 0; t <= 5000; t += 1000)
                engine.Seek(t);

            Assert.True(seekLog.Count > 0);

            // Step 8: 导出 CSV
            string exportPath = TempPath("e2e_export.csv");
            var exportResult = MotionExportService.Export(
                loaded,
                exportPath,
                new MotionExportService.ExportOptions
                {
                    Format = MotionExportService.ExportFormat.Csv,
                    SampleRateHz = 20
                }
            );
            Assert.True(exportResult.Success);
            Assert.True(File.Exists(exportPath));
            Assert.True(new FileInfo(exportPath).Length > 100);
        }
    }
}
