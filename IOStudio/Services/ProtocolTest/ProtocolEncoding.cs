using System;
using System.Collections.Generic;
using System.Text;

namespace IOStudio.Services.ProtocolTest
{
    /// <summary>
    /// 协议测试台的文本/十六进制编解码 —— CLI(场景) 与 UI(调试台) 共用一份。
    ///
    /// <para>为什么必须有 GBK: 国产设备与厂家软件大量使用 GBK 文本; 主流调试台
    /// (SSCOM / NetAssist) 都提供 UTF-8/GBK 选择。.NET Core 默认**不含**代码页,
    /// 必须显式注册 <see cref="CodePagesEncodingProvider"/> —— 否则
    /// <c>Encoding.GetEncoding("GBK")</c> 直接抛异常。</para>
    ///
    /// <para>放在这里而不是各调用点: 编码名→Encoding 的映射只能有一份,
    /// 否则 UI 与 CLI 迟早对同一个名字产生不同行为。</para>
    /// </summary>
    public static class ProtocolEncoding
    {
        static ProtocolEncoding()
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            }
            catch
            {
                /* 注册失败只影响 GBK, 不应让整个测试台不可用 */
            }
        }

        /// <summary>可选编码 (顺序即 UI 下拉顺序)。</summary>
        public static IReadOnlyList<string> Names { get; } = new[] { "UTF-8", "GBK", "ASCII", "HEX" };

        /// <summary>把文本按指定编码转成字节; HEX 时按十六进制串解析。</summary>
        public static byte[] Encode(string? text, string? name)
        {
            text ??= "";
            var n = (name ?? "UTF-8").Trim();

            if (string.Equals(n, "HEX", StringComparison.OrdinalIgnoreCase))
                return ParseHex(text);

            return Get(n).GetBytes(text);
        }

        /// <summary>
        /// 宽松解析十六进制串 (对齐主流调试台的手感): 自动忽略空格/换行/制表符与
        /// 逗号、分号、冒号、短横线、下划线等分隔符, 以及 <c>0x</c>/<c>0X</c> 前缀。
        /// 剩余内容必须是**偶数个**十六进制字符, 否则抛出 —— 奇数字节不该被瞎猜。
        /// </summary>
        public static byte[] ParseHex(string? text)
        {
            text ??= "";
            var sb = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];

                if (char.IsWhiteSpace(c) || c is ',' or ';' or ':' or '-' or '_')
                    continue;

                // "0x" 前缀 (0 后面紧跟 x/X) 直接跳过, 兼容从代码里粘来的写法
                if (c == '0' && i + 1 < text.Length && (text[i + 1] is 'x' or 'X'))
                {
                    i++;
                    continue;
                }

                if (!Uri.IsHexDigit(c))
                    throw new FormatException($"HEX 内容含非法字符 '{c}'");

                sb.Append(c);
            }

            if (sb.Length % 2 != 0)
                throw new FormatException("HEX 内容需要偶数个十六进制字符");

            return sb.Length == 0 ? Array.Empty<byte>() : Convert.FromHexString(sb.ToString());
        }

        /// <summary>取 Encoding; 名字不认识时退回 UTF-8 (不抛, 避免下拉里多一项就炸)。</summary>
        public static Encoding Get(string? name)
        {
            var n = (name ?? "UTF-8").Trim();
            if (string.Equals(n, "GBK", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return Encoding.GetEncoding("GBK");
                }
                catch
                {
                    return Encoding.UTF8;
                }
            }
            if (string.Equals(n, "ASCII", StringComparison.OrdinalIgnoreCase))
                return Encoding.ASCII;
            return Encoding.UTF8;
        }

        /// <summary>用于日志展示: 能否按文本显示 (含中文的设备报文要看得到原文)。</summary>
        public static string DecodeForDisplay(byte[] data)
        {
            try
            {
                var s = Encoding.UTF8.GetString(data);
                foreach (var ch in s)
                {
                    if (ch == '\uFFFD' || (char.IsControl(ch) && ch != '\r' && ch != '\n' && ch != '\t'))
                        return "";
                }
                return s;
            }
            catch
            {
                return "";
            }
        }
    }
}
