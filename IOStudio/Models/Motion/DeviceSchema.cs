using System.Collections.Generic;

namespace IOStudio.Models.Motion
{
    /// <summary>
    /// 添加轨道对话框的结果 — 包含设备名/输出名/标签/颜色等信息。
    /// </summary>
    public class AddTrackResult
    {
        public string DeviceName { get; set; } = "";
        public string OActionName { get; set; } = "";

        /// <summary>输出类型: "oaction" | "oaxis"</summary>
        public string OutputType { get; set; } = "oaction";

        /// <summary>OAxis 输出通道 (仅当 OutputType == "oaxis" 时有效, 如 "OAxis_00")</summary>
        public string OAxisChannel { get; set; } = "";
        public string Label { get; set; } = "";
        public string Color { get; set; } = "#4FC3F7";
        public string ValueType { get; set; } = "float";

        /// <summary>Idle 循环配置 (null = 不启用 idle, 空窗期输出中性值)</summary>
        public IdleLoop? IdleLoop { get; set; }

        /// <summary>值显示规则 ("percent" | "symmetric" | "engineering"; null = 自动)。</summary>
        public string? ValueDisplay { get; set; }

        /// <summary>轨道级量程覆盖 (null = 用设备声明或直通)。</summary>
        public float? EngineeringRange { get; set; }

        /// <summary>复位原点 (null = 按 value_type 默认)。</summary>
        public float? NeutralValue { get; set; }
    }

    /// <summary>OAction 信息 — 设备的输出动作定义。</summary>
    public class OActionInfo
    {
        public string DeviceName { get; set; } = "";
        public string OActionName { get; set; } = "";
        public string Label { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public List<string> KeyNames { get; set; } = new();
    }

    /// <summary>OAxis 通道信息 — 设备的直接输出通道 (OAxis_00~31)。</summary>
    public class OAxisChannelInfo
    {
        public string DeviceName { get; set; } = "";
        public string ChannelName { get; set; } = "";
        public string DisplayName { get; set; } = "";

        /// <summary>
        /// Engineering range declared BY THE DEVICE (normalized 0..1 maps to [Min, Max]).
        /// null = not declared -> pass-through. This is the single source of truth;
        /// the per-track engineering_range in a .motion is only an explicit override.
        /// </summary>
        public float? EngineeringMin { get; set; }
        public float? EngineeringMax { get; set; }

        /// <summary>Engineering unit ("deg" / "mm"), used for editor curve axis labels.</summary>
        public string Unit { get; set; } = "";
    }

    /// <summary>
    /// 设备架构信息 — 包含设备名称及其 OAction/OAxis 通道列表。
    /// 由 <see cref="IOStudio.Services.Motion.IDeviceSchemaService"/> 从 IODevice.xml 解析而来。
    /// </summary>
    public class DeviceSchemaInfo
    {
        public string DeviceName { get; set; } = "";

        /// <summary>
        /// 设备的驱动插件名 (IODevice.xml 的 <c>DllName</c> 属性)。
        ///
        /// <para>native 侧对 <c>DllName</c> 解析不出来的 External 设备是<b>直接不建</b>
        /// (见 <c>InputSettings</c>), 所以它必须是本机真实存在的插件名 —— 这也是
        /// "复制一个已在工作的设备"能保证新设备可用、而凭空捏造不能的原因。</para>
        /// </summary>
        public string DllName { get; set; } = "";

        /// <summary>设备槽位号 (IODevice.xml 的 <c>Index</c> 属性)。</summary>
        public int Index { get; set; }

        public List<OActionInfo> OActions { get; set; } = new();
        public List<OAxisChannelInfo> OAxisChannels { get; set; } = new();
    }
}
