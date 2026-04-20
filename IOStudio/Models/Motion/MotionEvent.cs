using System.Text.Json.Serialization;

namespace IOStudio.Models.Motion
{
    /// <summary>
    /// 关键帧事件 — 在关键帧触发时发送的事件通知
    /// 客户端可通过 TCP/WebSocket 绑定接收, 用于触发自定义逻辑
    /// </summary>
    public class MotionEvent
    {
        /// <summary>事件名称 (客户端用此名称匹配处理程序)</summary>
        [JsonPropertyName("event_name")]
        public string EventName { get; set; } = "";

        /// <summary>
        /// 事件参数 (JSON 字符串, 客户端自行解析)
        /// 例: {"intensity": 0.8, "color": "#FF0000"}
        /// </summary>
        [JsonPropertyName("event_data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? EventData { get; set; }
    }
}
