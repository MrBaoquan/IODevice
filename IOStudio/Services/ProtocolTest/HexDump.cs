using System;
using System.Text;

namespace IOStudio.Services.ProtocolTest
{
    /// <summary>日志内容的显示方式 (主流调试台都有"HEX 显示"开关; 同一时刻只显示一种)。</summary>
    public enum ProtocolContentMode
    {
        /// <summary>仅文本 (不可读为文本的二进制帧由 HEX 兜底, 见 DisplayContent)。</summary>
        Text,

        /// <summary>文本 + HEX (旧双列模型的默认; 单列模型下等同 Text)。</summary>
        TextAndHex,

        /// <summary>仅 HEX (平铺)。</summary>
        HexOnly,

        /// <summary>HEX 转储 (经典 offset / hex / ASCII 三段式, 按 16 字节一行)。</summary>
        Dump,
    }

    /// <summary>经典 hexdump 格式化。</summary>
    public static class HexDump
    {
        public const int BytesPerLine = 16;

        /// <summary>
        /// 生成 offset / hex / ASCII 三段式转储。
        /// 二进制协议靠它才能看清结构 —— 平铺的 HEX 串在大报文上几乎没法读。
        /// </summary>
        public static string Format(byte[]? data)
        {
            if (data is null || data.Length == 0)
                return "";

            var sb = new StringBuilder();
            for (int offset = 0; offset < data.Length; offset += BytesPerLine)
            {
                var n = Math.Min(BytesPerLine, data.Length - offset);

                sb.Append(offset.ToString("X4")).Append("  ");

                for (int i = 0; i < BytesPerLine; i++)
                {
                    if (i < n)
                        sb.Append(data[offset + i].ToString("X2")).Append(' ');
                    else
                        sb.Append("   ");

                    // 8 字节处多一个空格, 便于按字节计数
                    if (i == BytesPerLine / 2 - 1)
                        sb.Append(' ');
                }

                sb.Append(' ');
                for (int i = 0; i < n; i++)
                {
                    var b = data[offset + i];
                    sb.Append(b is >= 0x20 and <= 0x7E ? (char)b : '.');
                }

                if (offset + n < data.Length)
                    sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
