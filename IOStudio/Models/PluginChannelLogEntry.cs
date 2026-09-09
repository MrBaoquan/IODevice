using System;

namespace IOStudio.Models
{
    /// <summary>
    /// 插件通道日志条目。
    /// 一条 = 一次通道读/写事件（常用于 NETIO 的 netio.udp.in / netio.udp.out）。
    /// </summary>
    public sealed class PluginChannelLogEntry
    {
        public DateTime Timestamp { get; init; } = DateTime.Now;

        /// <summary>方向：IN（来自设备/外部）或 OUT（写向设备/外部）</summary>
        public string Direction { get; init; } = "IN";

        /// <summary>设备名（对应 IODevice.xml 中的 Name）</summary>
        public string DeviceName { get; init; } = string.Empty;

        /// <summary>通道名（例：netio.udp.in、netio.udp.out）</summary>
        public string ChannelName { get; init; } = string.Empty;

        /// <summary>字节长度</summary>
        public int Size { get; init; }

        /// <summary>尝试作为 UTF-8 文本解码后的内容（非文本时退化为 16 进制）</summary>
        public string Text { get; init; } = string.Empty;

        // —— 以下为 UI 绑定便捷属性 ——
        public string TimestampText => Timestamp.ToString("HH:mm:ss.fff");

        public bool IsIn => Direction == "IN";
        public bool IsOut => Direction == "OUT";
    }
}
