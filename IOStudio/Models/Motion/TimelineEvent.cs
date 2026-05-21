using System.Text.Json.Serialization;

namespace IOStudio.Models.Motion
{
    /// <summary>
    /// 时间轴独立事件 — 不绑定到关键帧，在指定时间点触发
    /// 可用于触发音效、特效、同步信号等
    /// </summary>
    public class TimelineEvent
    {
        /// <summary>事件触发时间 (绝对毫秒)</summary>
        [JsonPropertyName("time_ms")]
        public double TimeMs { get; set; }

        /// <summary>事件名称</summary>
        [JsonPropertyName("event_name")]
        public string EventName { get; set; } = "";

        /// <summary>
        /// 事件参数 (JSON 字符串, 客户端自行解析)
        /// </summary>
        [JsonPropertyName("event_data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? EventData { get; set; }

        /// <summary>显示颜色 (hex)</summary>
        [JsonPropertyName("color")]
        public string Color { get; set; } = "#f59e0b";

        /// <summary>事件数据类型 (string / json / number)</summary>
        [JsonPropertyName("data_type")]
        public string DataType { get; set; } = "string";

        /// <summary>所属事件轨道 (lane) ID — 支持多事件轨。空/"default" 表示默认轨</summary>
        [JsonPropertyName("lane")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string LaneId { get; set; } = "default";
    }
}
