using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace IOStudio.Models.Motion
{
    /// <summary>
    /// 片段模型 — 轨道上的一个时间区间，包含关键帧序列
    /// </summary>
    public class MotionClip
    {
        /// <summary>片段唯一 ID (8 位 hex)</summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

        /// <summary>片段起始时间 (毫秒, 相对于时间轴 0 点)</summary>
        [JsonPropertyName("start_ms")]
        public double StartMs { get; set; }

        /// <summary>片段结束时间 (毫秒)</summary>
        [JsonPropertyName("end_ms")]
        public double EndMs { get; set; }

        /// <summary>关键帧列表 (按 TimeMs 升序)</summary>
        [JsonPropertyName("keyframes")]
        public List<MotionKeyframe> Keyframes { get; set; } = new();

        /// <summary>片段时长 (毫秒)</summary>
        [JsonIgnore]
        public double DurationMs => EndMs - StartMs;
    }
}
