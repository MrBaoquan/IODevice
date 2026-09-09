using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace IOStudio.Models.Motion
{
    /// <summary>
    /// 效果预设内的一个逻辑通道。
    /// <para>
    /// 设计约束：<see cref="Role"/> 为自由字符串（如 "heave"、"升降轴"、"OAxis_00"），
    /// 引擎与编辑器代码不解释、不校验其语义。语义由业务层 / DeviceTemplate.RoleMap 定义。
    /// </para>
    /// </summary>
    public class PresetChannel
    {
        /// <summary>
        /// 逻辑角色标签 (自由字符串) — 预设作者自定义约定名。
        /// 代码层零语义假设，跨设备复用时由 DeviceTemplate.RoleMap 映射到实际通道。
        /// </summary>
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        /// <summary>该通道幅度系数 (相对预设整体强度, 用于通道间强弱分配)</summary>
        [JsonPropertyName("scale")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public float Scale { get; set; } = 1.0f;

        /// <summary>相位偏移 (毫秒) — 通道间联动错峰, 模拟真实姿态响应时序</summary>
        [JsonPropertyName("phase_offset_ms")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double PhaseOffsetMs { get; set; }

        /// <summary>
        /// 引用曲线模板名 (可选)。当未提供内联关键帧时, 预设库按其查找模板生成关键帧。
        /// </summary>
        [JsonPropertyName("template_ref")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? TemplateRef { get; set; }

        /// <summary>
        /// 内联关键帧序列 (按 TimeMs 升序)。与 TemplateRef 二选一, 内联优先。
        /// </summary>
        [JsonPropertyName("keyframes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<MotionKeyframe>? Keyframes { get; set; }
    }
}
