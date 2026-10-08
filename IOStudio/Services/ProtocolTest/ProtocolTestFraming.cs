using System;
using System.Collections.Generic;
using System.Linq;

namespace IOStudio.Services.ProtocolTest
{
    /// <summary>组帧方式。流式传输(TCP/串口)必须有帧边界, 否则"收到多少算一条"无从判定。</summary>
    internal enum FrameMode
    {
        /// <summary>不组帧: 流式传输下"本次读到多少就是多少"(仅适合自测/已知对端行为)。</summary>
        None,

        /// <summary>分隔符: 帧以指定字节序列结束 (常见 \n / \r\n / 0x03)。</summary>
        Delimiter,

        /// <summary>长度前缀: 先 N 字节长度, 再 N 字节负载 (工控/二进制协议最常见)。</summary>
        LengthPrefix,

        /// <summary>定长: 每帧固定字节数。</summary>
        Fixed,
    }

    /// <summary>
    /// 组帧编解码。
    ///
    /// <para>为什么需要: 之前 P1 的 TCP 收发是"读一次算一帧"——只有在自测(发送方一次写完、
    /// 接收方一次读到)时才成立。真实对端可能粘包/拆包, 断言会随机失败。这里把帧边界
    /// 显式化, 让 TCP/串口的测试有确定性。</para>
    /// </summary>
    internal sealed class FrameCodec
    {
        public FrameMode Mode { get; init; } = FrameMode.None;
        public byte[] Delimiter { get; init; } = Array.Empty<byte>();
        public int PrefixBytes { get; init; } = 4;
        public bool LittleEndian { get; init; }
        public int FixedSize { get; init; }

        public static readonly FrameCodec None = new();

        /// <summary>把负载编码成一帧。</summary>
        public byte[] Encode(byte[] payload)
        {
            switch (Mode)
            {
                case FrameMode.Delimiter:
                    var withDelim = new byte[payload.Length + Delimiter.Length];
                    Buffer.BlockCopy(payload, 0, withDelim, 0, payload.Length);
                    Buffer.BlockCopy(Delimiter, 0, withDelim, payload.Length, Delimiter.Length);
                    return withDelim;

                case FrameMode.LengthPrefix:
                    if (PrefixBytes is < 1 or > 4)
                        throw new InvalidOperationException($"prefix_bytes 必须在 1..4, 实际 {PrefixBytes}");
                    var prefix = new byte[PrefixBytes];
                    WriteLength(prefix, payload.Length);
                    var framed = new byte[PrefixBytes + payload.Length];
                    Buffer.BlockCopy(prefix, 0, framed, 0, PrefixBytes);
                    Buffer.BlockCopy(payload, 0, framed, PrefixBytes, payload.Length);
                    return framed;

                case FrameMode.Fixed:
                    if (payload.Length != FixedSize)
                        throw new InvalidOperationException(
                            $"fixed 帧要求负载恰好 {FixedSize} 字节, 实际 {payload.Length}"
                        );
                    return payload;

                default:
                    return payload;
            }
        }

        /// <summary>
        /// 从缓冲区头部尝试切出一帧。成功则把已消费的字节移出缓冲区。
        /// 数据不完整时返回 false (调用方继续读)。
        /// </summary>
        public bool TryExtract(List<byte> pending, out byte[] frame)
        {
            frame = Array.Empty<byte>();
            switch (Mode)
            {
                case FrameMode.None:
                    if (pending.Count == 0)
                        return false;
                    frame = pending.ToArray();
                    pending.Clear();
                    return true;

                case FrameMode.Delimiter:
                {
                    int at = IndexOf(pending, Delimiter);
                    if (at < 0)
                        return false;
                    frame = pending.Take(at).ToArray();
                    pending.RemoveRange(0, at + Delimiter.Length);
                    return true;
                }

                case FrameMode.LengthPrefix:
                {
                    if (pending.Count < PrefixBytes)
                        return false;
                    int len = ReadLength(pending);
                    if (len < 0)
                        throw new InvalidOperationException("长度前缀为负, 报文可能不是本组帧格式");
                    if (pending.Count < PrefixBytes + len)
                        return false;
                    frame = pending.Skip(PrefixBytes).Take(len).ToArray();
                    pending.RemoveRange(0, PrefixBytes + len);
                    return true;
                }

                case FrameMode.Fixed:
                    if (pending.Count < FixedSize)
                        return false;
                    frame = pending.Take(FixedSize).ToArray();
                    pending.RemoveRange(0, FixedSize);
                    return true;

                default:
                    return false;
            }
        }

        private void WriteLength(byte[] dst, int value)
        {
            if (LittleEndian)
            {
                for (int i = 0; i < PrefixBytes; i++)
                    dst[i] = (byte)((value >> (8 * i)) & 0xFF);
            }
            else
            {
                for (int i = 0; i < PrefixBytes; i++)
                    dst[i] = (byte)((value >> (8 * (PrefixBytes - 1 - i))) & 0xFF);
            }
        }

        private int ReadLength(List<byte> src)
        {
            int value = 0;
            if (LittleEndian)
            {
                for (int i = PrefixBytes - 1; i >= 0; i--)
                    value = (value << 8) | src[i];
            }
            else
            {
                for (int i = 0; i < PrefixBytes; i++)
                    value = (value << 8) | src[i];
            }
            return value;
        }

        private static int IndexOf(List<byte> haystack, byte[] needle)
        {
            if (needle.Length == 0 || haystack.Count < needle.Length)
                return -1;
            for (int i = 0; i + needle.Length <= haystack.Count; i++)
            {
                bool hit = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        hit = false;
                        break;
                    }
                }
                if (hit)
                    return i;
            }
            return -1;
        }
    }

    /// <summary>带缓冲的收帧器: 端点把原始字节喂进来, 由它切出完整帧。</summary>
    internal sealed class FramedReceiver
    {
        private readonly FrameCodec _codec;
        private readonly List<byte> _pending = new();

        public FramedReceiver(FrameCodec codec) => _codec = codec;

        public void Append(ReadOnlySpan<byte> data) => _pending.AddRange(data.ToArray());

        public bool TryTake(out byte[] frame) => _codec.TryExtract(_pending, out frame);
    }
}
