using System;
using System.Text;

namespace IOStudio.Services.ProtocolTest
{
    /// <summary>
    /// 场景执行的观察者 —— 让 UI 能**实时**看到每一步与每一帧, 而不是只看最终报告。
    ///
    /// <para>为什么需要: 报告是给机器/门禁用的(JSON), 但人验证协议时要看的是
    /// "到底收到了什么、哪一条不匹配"。所以把"收到的每一帧(含不匹配的)"都抛出来。</para>
    /// </summary>
    public interface IProtocolTestObserver
    {
        /// <summary>每一步执行完 (含失败原因)。</summary>
        void OnStepResult(ProtocolTestStepResult result);

        void OnSent(string via, byte[] data);

        /// <summary>收到一帧。matched = 是否满足该步的断言 (不匹配的也要看得到)。</summary>
        void OnReceived(string via, byte[] data, bool matched);
    }

    /// <summary>
    /// 端点工厂 —— CLI(执行器) 与 UI(手动监听) 共用, 避免两处各自 new 出不一致的行为。
    /// </summary>
    internal static class ProtocolEndpointFactory
    {
        public static IProtocolEndpoint Create(
            string transport,
            bool isListener,
            FrameCodec codec,
            string device = "",
            int baud = 0,
            string path = "/"
        )
        {
            var t = (transport ?? "").Trim().ToLowerInvariant();
            return t switch
            {
                "udp" => new UdpEndpoint(isListener, codec),
                "tcp-client" => new TcpClientEndpoint(codec),
                "tcp-server" => new TcpServerEndpoint(codec),
                "websocket-client" => new WebSocketClientEndpoint(path),
                "websocket-server" => new WebSocketServerEndpoint(),
                "serial" => new SerialEndpoint(codec, device, baud),
                _ => throw new InvalidOperationException($"未知 transport: {transport}"),
            };
        }

        public static FrameCodec BuildCodec(FrameSpec? spec)
        {
            if (spec is null)
                return FrameCodec.None;

            var mode = (spec.Mode ?? "none").Trim().ToLowerInvariant();
            return mode switch
            {
                "" or "none" => FrameCodec.None,
                "delimiter" => new FrameCodec
                {
                    Mode = FrameMode.Delimiter,
                    Delimiter = (spec.DelimiterEncoding ?? "utf8").Trim().ToLowerInvariant() == "hex"
                        ? Convert.FromHexString(spec.Delimiter.Replace(" ", ""))
                        : Encoding.UTF8.GetBytes(spec.Delimiter),
                },
                "length-prefix" => new FrameCodec
                {
                    Mode = FrameMode.LengthPrefix,
                    PrefixBytes = spec.PrefixBytes,
                    LittleEndian = (spec.Endian ?? "big").Trim().ToLowerInvariant() == "little",
                },
                "fixed" => new FrameCodec
                {
                    Mode = FrameMode.Fixed,
                    FixedSize = spec.Size,
                },
                _ => throw new InvalidOperationException($"未知 frame.mode: {spec.Mode}"),
            };
        }
    }
}
