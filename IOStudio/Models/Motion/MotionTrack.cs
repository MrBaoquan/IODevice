using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace IOStudio.Models.Motion
{
    /// <summary>
    /// 轨道模型 — 对应一个设备的一个输出动作 (如 "Chair / Light / 氛围灯") 或输出轴 (如 "Chair / Pitch")
    /// </summary>
    public class MotionTrack
    {
        /// <summary>轨道唯一 ID (8 位 hex)</summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

        /// <summary>IODevice.xml 中的设备名</summary>
        [JsonPropertyName("device")]
        public string DeviceName { get; set; } = "";

        /// <summary>OAction 输出动作名 (对应 IODevice.xml 中 OAction.Name, 如 "Light")</summary>
        [JsonPropertyName("oaction")]
        public string OActionName { get; set; } = "";

        /// <summary>
        /// 输出类型: "oaction" (默认, 通过 OAction 输出) | "oaxis" (通过 OAxis 通道直接输出)
        /// </summary>
        [JsonPropertyName("output_type")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string OutputType { get; set; } = "oaction";

        /// <summary>
        /// OAxis 输出通道名 (仅当 OutputType == "oaxis" 时有效, 如 "OAxis_00", "OAxis_01")
        /// </summary>
        [JsonPropertyName("oaxis_channel")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string OAxisChannel { get; set; } = "";

        /// <summary>(兼容旧版 JSON) 读取 axis_name → 写入 OAxisChannel, 不再序列化输出</summary>
        [JsonPropertyName("axis_name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string AxisName
        {
            get => ""; // 序列化时返回 default(""), 被 WhenWritingDefault 跳过
            set
            {
                if (!string.IsNullOrEmpty(value))
                {
                    OAxisChannel = value;
                    if (OutputType == "axis")
                        OutputType = "oaxis";
                }
            }
        }

        /// <summary>显示名称 (如 "俯仰")</summary>
        [JsonPropertyName("label")]
        public string Label { get; set; } = "";

        /// <summary>轨道颜色 (hex, 如 "#4FC3F7")</summary>
        [JsonPropertyName("color")]
        public string Color { get; set; } = "#4FC3F7";

        /// <summary>
        /// 值类型: "float" = 连续值 0.0~1.0, "bool" = 开关值 0/1
        /// 对于 SetDO 类操作使用 bool, SetDA/运动轴使用 float
        /// </summary>
        [JsonPropertyName("value_type")]
        public string ValueType { get; set; } = "float";

        /// <summary>是否静音 (不输出到设备)</summary>
        [JsonPropertyName("muted")]
        public bool Muted { get; set; }

        /// <summary>是否独奏 (仅输出此轨道, 其他非 Solo 轨道静音)</summary>
        [JsonPropertyName("solo")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Solo { get; set; }

        /// <summary>是否启用 (禁用时完全不参与播放和编辑)</summary>
        [JsonPropertyName("enabled")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Enabled { get; set; } = true;

        /// <summary>是否锁定 (不可编辑)</summary>
        [JsonPropertyName("locked")]
        public bool Locked { get; set; }

        /// <summary>所属分组名称 (空字符串表示未分组)</summary>
        [JsonPropertyName("group")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string Group { get; set; } = "";

        /// <summary>片段列表</summary>
        [JsonPropertyName("clips")]
        public List<MotionClip> Clips { get; set; } = new();
    }
}
