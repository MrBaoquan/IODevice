using System.Text.Json.Serialization;

namespace IOStudio.Models.Motion
{
    /// <summary>
    /// 时间轴标记/书签 — 用于在时间轴上标记重要位置
    /// 支持命名、颜色、快速导航
    /// </summary>
    public class TimelineMarker
    {
        /// <summary>标记时间 (毫秒)</summary>
        [JsonPropertyName("time_ms")]
        public double TimeMs { get; set; }

        /// <summary>标记名称</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary>标记颜色 (hex)</summary>
        [JsonPropertyName("color")]
        public string Color { get; set; } = "#f59e0b";

        /// <summary>备注说明</summary>
        [JsonPropertyName("note")]
        public string? Note { get; set; }
    }
}
