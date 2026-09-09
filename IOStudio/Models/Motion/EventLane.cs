using System.Text.Json.Serialization;

namespace IOStudio.Models.Motion
{
    /// <summary>
    /// 事件轨道 (Event Lane) — v2.0 新增
    /// 用于按语义对事件进行分组, 每条事件轨在 UI 上独占一行, 不再与播放头重叠。
    /// 默认存在 "default" 轨, 用户可手动创建额外轨道。
    /// </summary>
    public class EventLane
    {
        /// <summary>轨道唯一 ID (稳定引用)</summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = "default";

        /// <summary>轨道显示名 (如 "场景事件" / "特效触发" / "同步标记")</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = "事件";

        /// <summary>轨道颜色 (hex)</summary>
        [JsonPropertyName("color")]
        public string Color { get; set; } = "#f59e0b";

        /// <summary>是否折叠 (仅显示色带)</summary>
        [JsonPropertyName("collapsed")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Collapsed { get; set; }
    }

    /// <summary>
    /// 标记轨道 (Marker Lane) — v2.0 新增
    /// 与事件轨独立, 用于注释/章节划分, 不触发运行时回调。
    /// </summary>
    public class MarkerLane
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "default";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "标记";

        [JsonPropertyName("color")]
        public string Color { get; set; } = "#10b981";

        [JsonPropertyName("collapsed")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Collapsed { get; set; }
    }
}
