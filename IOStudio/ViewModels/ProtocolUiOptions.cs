using System.Collections.Generic;
using IOStudio.Services.ProtocolTest;

namespace IOStudio.ViewModels
{
    /// <summary>传输选项。NeedsHost: 需要填目标主机 (主动连接类)。</summary>
    public sealed record ProtocolTransportOption(string Label, string Transport, bool IsListener, bool NeedsHost)
    {
        // 下拉框直接显示 ToString(): 不重写就会渲染成
        // "ProtocolTransportOption { Label = UDP 监听, Transport = udp, ... }" (用户可见的硬伤)
        public override string ToString() => Label;
    }

    /// <summary>组帧选项。</summary>
    public sealed record ProtocolFrameOption(string Label, string Mode)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// 协议测试台的静态选项集。
    ///
    /// <para>抽出来是因为"会话"与"窗口"两个 ViewModel 都要用同一份枚举 ——
    /// 两处各写一份必然漂移 (例如新增传输后忘了另一处)。</para>
    /// </summary>
    public static class ProtocolUiOptions
    {
        public static IReadOnlyList<ProtocolTransportOption> Transports { get; } =
            new[]
            {
                new ProtocolTransportOption("UDP 监听", "udp", true, false),
                new ProtocolTransportOption("UDP 连接", "udp", false, true),
                new ProtocolTransportOption("TCP 服务端", "tcp-server", true, false),
                new ProtocolTransportOption("TCP 客户端", "tcp-client", false, true),
                new ProtocolTransportOption("WebSocket 服务端", "websocket-server", true, false),
                new ProtocolTransportOption("WebSocket 客户端", "websocket-client", false, true),
                new ProtocolTransportOption("串口", "serial", true, false),
            };

        public static IReadOnlyList<ProtocolFrameOption> FrameModes { get; } =
            new[]
            {
                new ProtocolFrameOption("无组帧", "none"),
                new ProtocolFrameOption("分隔符", "delimiter"),
                new ProtocolFrameOption("长度前缀", "length-prefix"),
                new ProtocolFrameOption("定长", "fixed"),
            };

        public static IReadOnlyList<string> SendEncodings { get; } = ProtocolEncoding.Names;

        /// <summary>发送新行选项 (主流工具都有; 很多设备协议以 \r\n 结尾)。</summary>
        public static IReadOnlyList<string> NewLineOptions { get; } = new[] { "无", "\\r\\n", "\\n" };

        /// <summary>把"新行"选项转成实际后缀。</summary>
        public static string NewLineToText(string option) =>
            option switch
            {
                "\\r\\n" => "\r\n",
                "\\n" => "\n",
                _ => "",
            };

        /// <summary>匹配方式 (自动应答规则用)。</summary>
        public static IReadOnlyList<string> MatchTypes { get; } =
            new[] { "包含", "正则", "等于", "开头是", "结尾是" };

        public static string MatchTypeToMode(string label) =>
            label switch
            {
                "正则" => "regex",
                "等于" => "equals",
                "开头是" => "startsWith",
                "结尾是" => "endsWith",
                _ => "contains",
            };
    }
}
