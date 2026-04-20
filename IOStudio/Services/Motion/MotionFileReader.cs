using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using IOStudio.Models.Motion;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// .motion 文件读写服务
    /// 支持 JSON 明文 (.motion) 格式的读写
    /// .mtn 加密格式将在 Phase 3 (MotionCryptoService) 中实现
    /// </summary>
    public static class MotionFileReader
    {
        private static readonly JsonSerializerOptions _jsonOptions =
            new()
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };

        /// <summary>
        /// 从 .motion 文件加载时间轴
        /// </summary>
        /// <param name="filePath">.motion 文件路径</param>
        /// <returns>解析后的 MotionTimeline, 失败返回 null</returns>
        public static MotionTimeline? Read(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return null;

            try
            {
                var json = File.ReadAllText(filePath);
                return ReadFromJson(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MotionFileReader] Read failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 从 JSON 字符串解析时间轴
        /// </summary>
        public static MotionTimeline? ReadFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonSerializer.Deserialize<MotionTimeline>(json, _jsonOptions);
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MotionFileReader] JSON parse failed: {ex.Message}"
                );
                return null;
            }
        }

        /// <summary>
        /// 将时间轴写入 .motion 文件
        /// </summary>
        /// <param name="timeline">时间轴数据</param>
        /// <param name="filePath">目标文件路径</param>
        /// <returns>是否写入成功</returns>
        public static bool Write(MotionTimeline timeline, string filePath)
        {
            if (timeline == null || string.IsNullOrEmpty(filePath))
                return false;

            try
            {
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = ToJson(timeline);
                File.WriteAllText(filePath, json);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MotionFileReader] Write failed: {ex.Message}"
                );
                return false;
            }
        }

        /// <summary>
        /// 将时间轴序列化为 JSON 字符串
        /// </summary>
        public static string ToJson(MotionTimeline timeline)
        {
            return JsonSerializer.Serialize(timeline, _jsonOptions);
        }

        /// <summary>
        /// 判断文件是否为加密的 .mtn 格式
        /// </summary>
        public static bool IsEncrypted(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            return Path.GetExtension(filePath).Equals(".mtn", StringComparison.OrdinalIgnoreCase);
        }
    }
}
