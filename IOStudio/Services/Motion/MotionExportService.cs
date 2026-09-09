using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IOStudio.Models.Motion;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// Motion 时间轴导出服务
    /// 支持多种导出格式:
    /// - .motion (JSON 明文, 标准格式)
    /// - .csv (通道值逐帧导出)
    /// - .json (精简 JSON, 仅含通道数据)
    /// </summary>
    public static class MotionExportService
    {
        /// <summary>导出格式枚举</summary>
        public enum ExportFormat
        {
            /// <summary>标准 .motion JSON 格式</summary>
            MotionJson,

            /// <summary>逐帧 CSV 格式 (时间, 通道1, 通道2, ...)</summary>
            Csv,

            /// <summary>精简 JSON (仅通道采样数据)</summary>
            CompactJson
        }

        /// <summary>导出配置</summary>
        public class ExportOptions
        {
            /// <summary>导出格式</summary>
            public ExportFormat Format { get; set; } = ExportFormat.MotionJson;

            /// <summary>采样率 (Hz), 仅 CSV/CompactJson 格式使用</summary>
            public int SampleRateHz { get; set; } = 60;

            /// <summary>起始时间 (ms)</summary>
            public double StartMs { get; set; } = 0;

            /// <summary>结束时间 (ms), 0 表示使用时间轴总时长</summary>
            public double EndMs { get; set; } = 0;

            /// <summary>值精度 (小数位数)</summary>
            public int ValuePrecision { get; set; } = 4;

            /// <summary>是否仅导出未静音的轨道</summary>
            public bool ExcludeMutedTracks { get; set; } = true;

            /// <summary>CSV 分隔符</summary>
            public string CsvSeparator { get; set; } = ",";

            /// <summary>是否包含表头 (CSV)</summary>
            public bool IncludeHeader { get; set; } = true;
        }

        /// <summary>导出结果</summary>
        public class ExportResult
        {
            public bool Success { get; set; }
            public string? FilePath { get; set; }
            public string? Error { get; set; }
            public int FrameCount { get; set; }
            public int TrackCount { get; set; }
        }

        /// <summary>
        /// 导出时间轴到文件
        /// </summary>
        public static ExportResult Export(
            MotionTimeline timeline,
            string filePath,
            ExportOptions options
        )
        {
            if (timeline == null)
                return new ExportResult { Success = false, Error = "时间轴为空" };
            if (string.IsNullOrEmpty(filePath))
                return new ExportResult { Success = false, Error = "文件路径为空" };

            try
            {
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string content = options.Format switch
                {
                    ExportFormat.MotionJson => ExportAsMotionJson(timeline),
                    ExportFormat.Csv => ExportAsCsv(timeline, options),
                    ExportFormat.CompactJson => ExportAsCompactJson(timeline, options),
                    _ => throw new ArgumentException($"不支持的导出格式: {options.Format}")
                };

                File.WriteAllText(filePath, content, Encoding.UTF8);

                return new ExportResult
                {
                    Success = true,
                    FilePath = filePath,
                    TrackCount = GetExportTracks(timeline, options).Count
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MotionExportService] Export failed: {ex.Message}"
                );
                return new ExportResult { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// 导出为标准 .motion JSON 格式
        /// </summary>
        private static string ExportAsMotionJson(MotionTimeline timeline)
        {
            return MotionFileReader.ToJson(timeline);
        }

        /// <summary>
        /// 导出为 CSV 格式 (逐帧采样)
        /// </summary>
        private static string ExportAsCsv(MotionTimeline timeline, ExportOptions options)
        {
            var tracks = GetExportTracks(timeline, options);
            double endMs = options.EndMs > 0 ? options.EndMs : timeline.DurationMs;
            double startMs = options.StartMs;
            double stepMs = 1000.0 / options.SampleRateHz;
            string sep = options.CsvSeparator;
            string fmt = $"F{options.ValuePrecision}";

            var sb = new StringBuilder();

            // 表头
            if (options.IncludeHeader)
            {
                sb.Append("time_ms");
                foreach (var track in tracks)
                {
                    string label = !string.IsNullOrEmpty(track.Label)
                        ? track.Label
                        : track.OActionName;
                    sb.Append(sep);
                    sb.Append(EscapeCsvField(label, sep));
                }
                sb.AppendLine();
            }

            // 逐帧采样
            int frameCount = 0;
            for (double t = startMs; t <= endMs; t += stepMs)
            {
                sb.Append(t.ToString("F1"));
                foreach (var track in tracks)
                {
                    float value = EvaluateTrackAtTime(track, t);
                    sb.Append(sep);
                    sb.Append(value.ToString(fmt));
                }
                sb.AppendLine();
                frameCount++;
            }

            return sb.ToString();
        }

        /// <summary>
        /// 导出为精简 JSON (采样数据)
        /// </summary>
        private static string ExportAsCompactJson(MotionTimeline timeline, ExportOptions options)
        {
            var tracks = GetExportTracks(timeline, options);
            double endMs = options.EndMs > 0 ? options.EndMs : timeline.DurationMs;
            double startMs = options.StartMs;
            double stepMs = 1000.0 / options.SampleRateHz;
            int precision = options.ValuePrecision;

            var result = new Dictionary<string, object>
            {
                ["version"] = timeline.Version ?? "1.0",
                ["name"] = timeline.Name ?? "Untitled",
                ["duration_ms"] = timeline.DurationMs,
                ["sample_rate_hz"] = options.SampleRateHz,
                ["start_ms"] = startMs,
                ["end_ms"] = endMs
            };

            var channelList = new List<Dictionary<string, object>>();
            foreach (var track in tracks)
            {
                var samples = new List<double>();
                for (double t = startMs; t <= endMs; t += stepMs)
                {
                    float val = EvaluateTrackAtTime(track, t);
                    samples.Add(Math.Round(val, precision));
                }

                channelList.Add(
                    new Dictionary<string, object>
                    {
                        ["device"] = track.DeviceName ?? "",
                        ["action"] = track.OActionName ?? "",
                        ["label"] = track.Label ?? "",
                        ["value_type"] = track.ValueType ?? "float",
                        ["samples"] = samples
                    }
                );
            }

            result["channels"] = channelList;

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            return JsonSerializer.Serialize(result, jsonOptions);
        }

        // ─── 辅助方法 ───

        /// <summary>获取需要导出的轨道列表</summary>
        private static List<MotionTrack> GetExportTracks(
            MotionTimeline timeline,
            ExportOptions options
        )
        {
            if (options.ExcludeMutedTracks)
                return timeline.Tracks.Where(t => !t.Muted).ToList();
            return timeline.Tracks.ToList();
        }

        /// <summary>在指定时间评估轨道值</summary>
        private static float EvaluateTrackAtTime(MotionTrack track, double timeMs)
        {
            foreach (var clip in track.Clips)
            {
                if (timeMs >= clip.StartMs && timeMs <= clip.EndMs)
                {
                    double localMs = timeMs - clip.StartMs;
                    return InterpolationEngine.Evaluate(clip.Keyframes, localMs);
                }
            }
            return 0.5f; // 默认中间值
        }

        /// <summary>转义 CSV 字段</summary>
        private static string EscapeCsvField(string field, string separator)
        {
            if (field.Contains(separator) || field.Contains("\"") || field.Contains("\n"))
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            return field;
        }

        /// <summary>
        /// 获取导出格式对应的文件扩展名
        /// </summary>
        public static string GetFileExtension(ExportFormat format) =>
            format switch
            {
                ExportFormat.MotionJson => ".motion",
                ExportFormat.Csv => ".csv",
                ExportFormat.CompactJson => ".json",
                _ => ".motion"
            };

        /// <summary>
        /// 获取文件对话框过滤器
        /// </summary>
        public static string GetFileFilter(ExportFormat format) =>
            format switch
            {
                ExportFormat.MotionJson => "Motion 时间轴|*.motion",
                ExportFormat.Csv => "CSV 逐帧数据|*.csv",
                ExportFormat.CompactJson => "精简 JSON|*.json",
                _ => "所有文件|*.*"
            };
    }
}
