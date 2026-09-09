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

        /// <summary>
        /// 中性/空闲值 — 该轨道"无动作覆盖时"的输出值 (用于回中/默认输出)。
        /// <para>null = 使用类型默认 (bool=0 关, float=0.5 中位)。不同设备空闲语义不同,
        /// 如运动轴回中位可能是 0 或 0.5, 特效通道可配置自定义默认值。</para>
        /// </summary>
        [JsonPropertyName("neutral_value")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public float? NeutralValue { get; set; }

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

        /// <summary>
        /// 逻辑角色标签 (自由字符串, 可选)。供效果预设按角色匹配轨道。
        /// 语义由业务层定义 (如 "heave" / "pitch" / "升降轴")，代码层零解释。
        /// </summary>
        [JsonPropertyName("role")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string Role { get; set; } = "";

        /// <summary>
        /// 是否在曲线编辑器中显示 (UX-B1 焦点模式)。
        /// true = 显示, false = 隐藏但保留轨道; 默认 true.
        /// </summary>
        [JsonPropertyName("show_in_curve")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool ShowInCurve { get; set; } = true;

        /// <summary>
        /// Idle 循环 (gap 填充) — 该轨道"无 clip 覆盖"时的待机循环动作。
        /// <para>null = 无 idle, 空窗期输出 neutralValue (原语义)。启用后空窗期沿关键帧循环求值,
        /// 与相邻 clip 可交叉淡化 (BlendMs), 相位模式 continuous/restart。</para>
        /// </summary>
        [JsonPropertyName("idle")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IdleLoop? IdleLoop { get; set; }

        /// <summary>
        /// Overlay 覆盖关键帧 (空窗自由数值点, 绝对时间)。
        /// <para>优先级高于 idle: 两点之间按插值求值, 所有点之外回落到 idle 循环/中性值。
        /// 用于在待机循环上打点 (如特定时刻拉高某值), 无需建 clip。</para>
        /// </summary>
        [JsonPropertyName("override_keyframes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<MotionKeyframe>? OverrideKeyframes { get; set; }

        /// <summary>片段列表</summary>
        [JsonPropertyName("clips")]
        public List<MotionClip> Clips { get; set; } = new();
    }
}
