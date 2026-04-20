using System.Text.Json.Serialization;

namespace IOStudio.Models.Motion
{
    /// <summary>
    /// 轨道分组模型 — 可将多个轨道归入同一组，支持折叠/展开、批量静音/锁定
    /// 分组作为独立实体存储在 MotionTimeline.Groups 中, 即使无子轨道也持久存在
    /// </summary>
    public class TrackGroup
    {
        /// <summary>分组唯一名称 (用作标识)</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary>分组颜色 (hex)</summary>
        [JsonPropertyName("color")]
        public string Color { get; set; } = "#7c3aed";

        /// <summary>是否折叠</summary>
        [JsonPropertyName("collapsed")]
        public bool Collapsed { get; set; }
    }
}
