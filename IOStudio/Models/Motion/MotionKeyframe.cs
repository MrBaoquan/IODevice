using System.Text.Json.Serialization;

namespace IOStudio.Models.Motion
{
    /// <summary>
    /// 关键帧模型 — 时间+值+插值类型+事件
    /// </summary>
    public class MotionKeyframe
    {
        /// <summary>时间偏移 (毫秒, 相对于所属 Clip 的 StartMs)</summary>
        [JsonPropertyName("time_ms")]
        public double TimeMs { get; set; }

        /// <summary>归一化输出值 (0.0 ~ 1.0)</summary>
        [JsonPropertyName("value")]
        public float Value { get; set; }

        /// <summary>
        /// 插值类型: "linear" | "bezier" | "step" | "ease_in_out"
        /// </summary>
        [JsonPropertyName("interpolation")]
        public string Interpolation { get; set; } = "linear";

        /// <summary>贝塞尔入控制点 Y (仅 bezier 类型有效, 对应 C++ cp1y)</summary>
        [JsonPropertyName("cp1y")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public float? TangentIn { get; set; }

        /// <summary>贝塞尔出控制点 Y (仅 bezier 类型有效, 对应 C++ cp2y)</summary>
        [JsonPropertyName("cp2y")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public float? TangentOut { get; set; }

        /// <summary>贝塞尔入控制点 X (仅 bezier 类型有效, 对应 C++ cp1x)</summary>
        [JsonPropertyName("cp1x")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public float? Cp1x { get; set; }

        /// <summary>贝塞尔出控制点 X (仅 bezier 类型有效, 对应 C++ cp2x)</summary>
        [JsonPropertyName("cp2x")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public float? Cp2x { get; set; }

        /// <summary>
        /// 关键帧触发事件 (可选)
        /// 播放到此关键帧时触发事件通知, 客户端可绑定接收
        /// </summary>
        [JsonPropertyName("event")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public MotionEvent? Event { get; set; }
    }
}
