using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace IOStudio.Models.Motion
{
    /// <summary>
    /// 时间轴中的动作实例。一个实例可以跨多个输出轨道，轨道 Clip 只是它的烘焙结果。
    /// </summary>
    public class ActionInstance
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

        [JsonPropertyName("definition_id")]
        public string DefinitionId { get; set; } = "";

        [JsonPropertyName("definition_revision")]
        public int DefinitionRevision { get; set; } = 1;

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("start_ms")]
        public double StartMs { get; set; }

        [JsonPropertyName("duration_ms")]
        public double DurationMs { get; set; }

        [JsonPropertyName("intensity")]
        public float Intensity { get; set; } = 1f;

        [JsonPropertyName("playback_rate")]
        public double PlaybackRate { get; set; } = 1.0;

        [JsonPropertyName("loop_count")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int LoopCount { get; set; } = 1;

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>预设逻辑角色到项目轨道 ID 的稳定绑定。</summary>
        [JsonPropertyName("role_track_ids")]
        public Dictionary<string, string> RoleTrackIds { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
