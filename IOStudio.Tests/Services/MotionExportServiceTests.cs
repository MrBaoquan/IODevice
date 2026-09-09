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
    /// MotionExportService 导出测试
    /// 覆盖: MotionJson / CSV / CompactJson 三种格式, 采样精度, Mute 过滤
    /// </summary>
    public class MotionExportServiceTests : IDisposable
    {
        private readonly string _tempDir;

        public MotionExportServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"MotionExportTest_{Guid.NewGuid():N}");
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

        // ═══════ MotionJson 导出 ═══════

        [Fact]
        public void Export_MotionJson_Success()
        {
            var timeline = MotionTestFactory.CreateExtDevTestTimeline();
            string path = TempPath("export.motion");

            var result = MotionExportService.Export(
                timeline,
                path,
                new MotionExportService.ExportOptions
                {
                    Format = MotionExportService.ExportFormat.MotionJson
                }
            );

            Assert.True(result.Success);
            Assert.True(File.Exists(path));

            // 验证导出文件可被重新加载
            var reloaded = MotionFileReader.Read(path);
            Assert.NotNull(reloaded);
            Assert.Equal(timeline.Name, reloaded!.Name);
            Assert.Equal(timeline.Tracks.Count, reloaded.Tracks.Count);
        }

        // ═══════ CSV 导出 ═══════

        [Fact]
        public void Export_Csv_HasCorrectHeader()
        {
            var timeline = MotionTestFactory.CreateMinimalTimeline(1000);
            string path = TempPath("export.csv");

            var result = MotionExportService.Export(
                timeline,
                path,
                new MotionExportService.ExportOptions
                {
                    Format = MotionExportService.ExportFormat.Csv,
                    SampleRateHz = 10, // 10fps → 100ms 间隔
                    IncludeHeader = true
                }
            );

            Assert.True(result.Success);
            string content = File.ReadAllText(path);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // 表头应包含 time_ms 和轨道名
            Assert.StartsWith("time_ms", lines[0]);
            Assert.Contains("测试轨道", lines[0]);
        }

        [Fact]
        public void Export_Csv_CorrectSampleCount()
        {
            var timeline = MotionTestFactory.CreateMinimalTimeline(1000);
            string path = TempPath("samples.csv");

            var result = MotionExportService.Export(
                timeline,
                path,
                new MotionExportService.ExportOptions
                {
                    Format = MotionExportService.ExportFormat.Csv,
                    SampleRateHz = 10,
                    IncludeHeader = true
                }
            );

            Assert.True(result.Success);
            string content = File.ReadAllText(path);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // 10Hz 采样 1000ms → 11 帧 (0, 100, 200, ..., 1000) + 1 表头 = 12 行
            Assert.Equal(12, lines.Length);
        }

        [Fact]
        public void Export_Csv_ValuesInRange()
        {
            var timeline = MotionTestFactory.CreateMinimalTimeline(1000);
            string path = TempPath("range.csv");

            MotionExportService.Export(
                timeline,
                path,
                new MotionExportService.ExportOptions
                {
                    Format = MotionExportService.ExportFormat.Csv,
                    SampleRateHz = 10
                }
            );

            string content = File.ReadAllText(path);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // 跳过表头, 检查所有值在 0~1 范围内
            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(',');
                Assert.True(parts.Length >= 2, $"Line {i}: insufficient columns");
                if (float.TryParse(parts[1], out float val))
                {
                    Assert.InRange(val, 0f, 1f);
                }
            }
        }

        [Fact]
        public void Export_Csv_CustomSeparator()
        {
            var timeline = MotionTestFactory.CreateMinimalTimeline(500);
            string path = TempPath("semicolon.csv");

            MotionExportService.Export(
                timeline,
                path,
                new MotionExportService.ExportOptions
                {
                    Format = MotionExportService.ExportFormat.Csv,
                    SampleRateHz = 5,
                    CsvSeparator = ";"
                }
            );

            string content = File.ReadAllText(path);
            Assert.Contains(";", content);
        }

        // ═══════ CompactJson 导出 ═══════

        [Fact]
        public void Export_CompactJson_Success()
        {
            var timeline = MotionTestFactory.CreateMinimalTimeline(1000);
            string path = TempPath("compact.json");

            var result = MotionExportService.Export(
                timeline,
                path,
                new MotionExportService.ExportOptions
                {
                    Format = MotionExportService.ExportFormat.CompactJson,
                    SampleRateHz = 10
                }
            );

            Assert.True(result.Success);
            Assert.True(File.Exists(path));

            string content = File.ReadAllText(path);
            Assert.Contains("\"sample_rate_hz\"", content);
            Assert.Contains("\"channels\"", content);
        }

        // ═══════ Mute 过滤 ═══════

        [Fact]
        public void Export_Csv_ExcludesMutedTracks()
        {
            var timeline = MotionTestFactory.CreateSoloMuteTimeline();
            timeline.Tracks[1].Muted = true; // Mute 通道2

            string path = TempPath("muted.csv");
            MotionExportService.Export(
                timeline,
                path,
                new MotionExportService.ExportOptions
                {
                    Format = MotionExportService.ExportFormat.Csv,
                    SampleRateHz = 5,
                    ExcludeMutedTracks = true
                }
            );

            string content = File.ReadAllText(path);
            var headerLine = content.Split('\n')[0];

            Assert.Contains("通道1", headerLine);
            Assert.DoesNotContain("通道2", headerLine);
            Assert.Contains("通道3", headerLine);
        }

        [Fact]
        public void Export_Csv_IncludesMutedWhenNotExcluded()
        {
            var timeline = MotionTestFactory.CreateSoloMuteTimeline();
            timeline.Tracks[1].Muted = true;

            string path = TempPath("muted_include.csv");
            MotionExportService.Export(
                timeline,
                path,
                new MotionExportService.ExportOptions
                {
                    Format = MotionExportService.ExportFormat.Csv,
                    SampleRateHz = 5,
                    ExcludeMutedTracks = false
                }
            );

            string content = File.ReadAllText(path);
            var headerLine = content.Split('\n')[0];

            Assert.Contains("通道1", headerLine);
            Assert.Contains("通道2", headerLine);
            Assert.Contains("通道3", headerLine);
        }

        // ═══════ 时间范围导出 ═══════

        [Fact]
        public void Export_Csv_CustomTimeRange()
        {
            var timeline = MotionTestFactory.CreateMinimalTimeline(10000);
            string path = TempPath("range_export.csv");

            MotionExportService.Export(
                timeline,
                path,
                new MotionExportService.ExportOptions
                {
                    Format = MotionExportService.ExportFormat.Csv,
                    SampleRateHz = 10,
                    StartMs = 2000,
                    EndMs = 4000
                }
            );

            string content = File.ReadAllText(path);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // 表头 + 数据行: 10Hz * 2s = 21 帧 + 1 表头
            Assert.True(lines.Length >= 2, "应至少有表头和一行数据");
            // 第一个数据行时间应为 2000
            var firstDataParts = lines[1].Split(',');
            Assert.Equal("2000.0", firstDataParts[0]);
        }

        // ═══════ 边界条件 ═══════

        [Fact]
        public void Export_NullTimeline_Fails()
        {
            var result = MotionExportService.Export(
                null!,
                TempPath("x.motion"),
                new MotionExportService.ExportOptions()
            );
            Assert.False(result.Success);
        }

        [Fact]
        public void Export_EmptyPath_Fails()
        {
            var result = MotionExportService.Export(
                new MotionTimeline(),
                "",
                new MotionExportService.ExportOptions()
            );
            Assert.False(result.Success);
        }

        [Fact]
        public void Export_EmptyTimeline_Succeeds()
        {
            var timeline = new MotionTimeline { Name = "Empty", DurationMs = 1000 };
            string path = TempPath("empty.csv");

            var result = MotionExportService.Export(
                timeline,
                path,
                new MotionExportService.ExportOptions
                {
                    Format = MotionExportService.ExportFormat.Csv,
                    SampleRateHz = 10
                }
            );

            Assert.True(result.Success);
        }
    }
}
