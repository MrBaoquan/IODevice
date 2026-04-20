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
    }

    /// <summary>
    /// 设备架构信息 — 包含设备名称及其 OAction/OAxis 通道列表。
    /// 由 <see cref="IOStudio.Services.Motion.IDeviceSchemaService"/> 从 IODevice.xml 解析而来。
    /// </summary>
    public class DeviceSchemaInfo
    {
        public string DeviceName { get; set; } = "";
        public List<OActionInfo> OActions { get; set; } = new();
        public List<OAxisChannelInfo> OAxisChannels { get; set; } = new();
    }
}
