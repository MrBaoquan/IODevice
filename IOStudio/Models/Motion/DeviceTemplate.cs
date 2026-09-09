using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace IOStudio.Models.Motion
{
    /// <summary>
    /// 设备模板 — 定义某类设备默认的通道角色布局。
    /// <para>
    /// RoleMap 把预设中的"逻辑角色标签"映射到该设备实际通道名 (自由字符串)。
    /// 语义完全由业务/配置层定义, 代码层零解释。
    /// 如 6DOF 平台: { "heave" → "OAxis_02", "pitch" → "OAxis_03", ... }。
    /// </para>
    /// </summary>
    public class DeviceTemplate
    {
        /// <summary>模板名称 (如 "6DOF平台" / "3DOF座椅" / "升降设备")</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary>
        /// 角色映射表: 自由字符串角色标签 → 实际通道名。
        /// 通道名可为 OAxis 通道 (如 "OAxis_02") 或 OAction 名 (如 "Heave")。
        /// </summary>
        [JsonPropertyName("role_map")]
        public Dictionary<string, string> RoleMap { get; set; } = new();
    }
}
