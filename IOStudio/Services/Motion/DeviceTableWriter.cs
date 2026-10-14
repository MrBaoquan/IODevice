using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using IOStudio.Models.Motion;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// <c>Config/IODevice.xml</c> 的写入 —— 只做<b>最小结点插入</b>, 不整份重写。
    ///
    /// <para><b>为什么不走 <c>IORoot.Instance.Save()</c></b>: 那条路是 <c>XmlSerializer</c> +
    /// <c>StreamWriter</c> 的全文件重写, 会抹掉注释、缩进与行尾。IODevice.xml 是用户会用任意
    /// 编辑器手工维护的文件 ("用首选编辑器打开")。同理也<b>不</b>用 <c>XmlDocument</c> 往返:
    /// XML 规范要求解析器把 CRLF 归一成 LF, 存回去就把整份文件的行尾换掉了。</para>
    ///
    /// <para>所以这里是纯文本插入: 按行定位目标设备的结点块, 用该文件自己的缩进与行尾插进去,
    /// 其余字节原样保留; 写入前先用 <see cref="XmlDocument"/> 重新解析一遍确认没写坏。</para>
    /// </summary>
    public static class DeviceTableWriter
    {
        /// <summary>设备表路径 (与 <see cref="DeviceSchemaService"/> 读的是同一份)。</summary>
        public static string TablePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "IODevice.xml");

        private static readonly Regex OpenTagRegex = new(
            @"<Device\b[^>]*?>",
            RegexOptions.IgnoreCase
        );

        private static readonly Regex NameAttrRegex = new(
            @"\bName\s*=\s*""([^""]*)""",
            RegexOptions.IgnoreCase
        );

        /// <summary>
        /// 读整份文件快照 (原样字节, 供撤销还原)。文件不存在返回 null。
        /// 用字节而不是字符串: 还原要做到逐字节相同, BOM 也算在内。
        /// </summary>
        public static byte[]? ReadSnapshot()
        {
            try
            {
                return File.Exists(TablePath) ? File.ReadAllBytes(TablePath) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>把快照原样写回 (撤销用)。</summary>
        public static bool WriteSnapshot(byte[] snapshot)
        {
            if (snapshot is null)
                return false;

            try
            {
                File.WriteAllBytes(TablePath, snapshot);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DeviceTableWriter] 还原设备表失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 往设备表里插一台**新设备** (含它要用的 OAction 结点)。返回是否真的写入了。
        ///
        /// <para>与 <see cref="TryAddOAction"/> 同样是纯文本最小插入: 保留注释、缩进与行尾。
        /// 计划由 <see cref="DeviceCreationPlanner"/> 生成 —— 这里只负责把计划落成文本。</para>
        /// </summary>
        public static bool TryAddDevice(DeviceCreationPlan plan, out string error)
        {
            error = "";

            if (plan is null || string.IsNullOrWhiteSpace(plan.DeviceName))
            {
                error = "设备名为空";
                return false;
            }
            if (string.IsNullOrWhiteSpace(plan.DllName))
            {
                error = "驱动插件名为空 —— native 侧对 DllName 无效的 External 设备会直接不建";
                return false;
            }

            // 动作可以为空 (冷启动时推不出通道): 只建出设备骨架。
            var actions = plan.Actions ?? new List<OActionCompletion>();

            byte[]? original;
            try
            {
                if (!File.Exists(TablePath))
                {
                    error = $"设备表不存在: {TablePath}";
                    return false;
                }
                original = File.ReadAllBytes(TablePath);
            }
            catch (Exception ex)
            {
                error = $"读取设备表失败: {ex.Message}";
                return false;
            }

            bool hasBom =
                original.Length >= 3
                && original[0] == 0xEF
                && original[1] == 0xBB
                && original[2] == 0xBF;
            string text = new UTF8Encoding(false).GetString(
                original,
                hasBom ? 3 : 0,
                original.Length - (hasBom ? 3 : 0)
            );

            if (!TryInsertDevice(text, plan, out string updated, out error))
                return false;

            try
            {
                var check = new XmlDocument();
                check.LoadXml(updated);
            }
            catch (Exception ex)
            {
                error = $"插入后 XML 非法, 已放弃写入: {ex.Message}";
                return false;
            }

            try
            {
                var bytes = new UTF8Encoding(false).GetBytes(updated);
                if (hasBom)
                {
                    var withBom = new byte[bytes.Length + 3];
                    withBom[0] = 0xEF;
                    withBom[1] = 0xBB;
                    withBom[2] = 0xBF;
                    Array.Copy(bytes, 0, withBom, 3, bytes.Length);
                    bytes = withBom;
                }
                File.WriteAllBytes(TablePath, bytes);
                return true;
            }
            catch (Exception ex)
            {
                error = $"写入设备表失败: {ex.Message}";
                return false;
            }
        }

        /// <summary>纯文本层面的设备插入 (无 IO, 可单测)。</summary>
        internal static bool TryInsertDevice(
            string text,
            DeviceCreationPlan plan,
            out string updated,
            out string error
        )
        {
            updated = text;
            error = "";

            // 重名设备不容许: native 侧遇到重名会报错并跳过, 那是"看起来加了、其实没加"。
            foreach (Match m in OpenTagRegex.Matches(text))
            {
                var nm = NameAttrRegex.Match(m.Value);
                if (
                    nm.Success
                    && string.Equals(nm.Groups[1].Value, plan.DeviceName, StringComparison.OrdinalIgnoreCase)
                )
                {
                    error = $"设备表中已存在设备 '{plan.DeviceName}'";
                    return false;
                }
            }

            var rootOpen = Regex.Match(text, @"<IORoot\b[^>]*?>", RegexOptions.IgnoreCase);
            if (!rootOpen.Success)
            {
                error = "设备表里找不到 <IORoot> 根结点";
                return false;
            }

            string eol = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            bool rootSelfClosing = rootOpen.Value.EndsWith("/>", StringComparison.Ordinal);

            int blockEnd;
            if (rootSelfClosing)
            {
                blockEnd = rootOpen.Index + rootOpen.Length;
            }
            else
            {
                int close = text.IndexOf("</IORoot>", rootOpen.Index + rootOpen.Length, StringComparison.OrdinalIgnoreCase);
                if (close < 0)
                {
                    error = "<IORoot> 未闭合";
                    return false;
                }
                blockEnd = close;
            }

            // 缩进照抄第一个既有 Device; 表里一台设备都没有时用 2 空格 (与既有表一致)
            string deviceIndent =
                OpenTagRegex.Matches(text) is { Count: > 0 } devices
                && devices[0].Index > 0
                    ? LineIndentAt(text, devices[0].Index)
                    : "  ";
            string childIndent = deviceIndent + "  ";

            var node = new StringBuilder();
            node.Append(deviceIndent)
                .Append("<Device Name=\"")
                .Append(Escape(plan.DeviceName))
                .Append("\" Type=\"External\" DllName=\"")
                .Append(Escape(plan.DllName))
                .Append("\" Index=\"")
                .Append(plan.Index.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append("\">")
                .Append(eol);
            node.Append(childIndent).Append("<Properties />").Append(eol);

            foreach (var action in plan.Actions)
            {
                node.Append(childIndent)
                    .Append("<OAction Name=\"")
                    .Append(Escape(action.OActionName))
                    .Append("\">")
                    .Append(eol);
                foreach (var key in action.Channels)
                {
                    node.Append(childIndent)
                        .Append("  <Key Name=\"")
                        .Append(Escape(key))
                        .Append("\" />")
                        .Append(eol);
                }
                node.Append(childIndent).Append("</OAction>").Append(eol);
            }

            node.Append(deviceIndent).Append("</Device>").Append(eol);

            if (rootSelfClosing)
            {
                // <IORoot /> → <IORoot> + 设备 + </IORoot>
                var opened = rootOpen.Value[..^2].TrimEnd() + ">";
                updated = text[..rootOpen.Index] + opened + eol + node + "</IORoot>" + text[(rootOpen.Index + rootOpen.Length)..];
                return true;
            }

            int lineStart = blockEnd;
            while (lineStart > 0 && text[lineStart - 1] != '\n')
                lineStart--;

            updated = text[..lineStart] + node + text[lineStart..];
            return true;
        }

        /// <summary>
        /// 往指定设备里插一个 <c>&lt;OAction&gt;</c>。返回是否真的写入了。
        ///
        /// <para><b>channelKeys 不允许为空</b>: <c>OActionMappings</c> 是
        /// <c>map&lt;名字, 通道列表&gt;</c>, 一个没有 <c>&lt;Key&gt;</c> 的 OAction 会让
        /// <c>SetDO</c> 通过存在性判断却一个通道都不下发 —— 那比不写更能骗人
        /// (预检不再报警, 输出照样丢), 所以宁可拒绝。</para>
        /// </summary>
        public static bool TryAddOAction(
            string deviceName,
            string oActionName,
            string? label,
            IReadOnlyList<string>? channelKeys,
            out string error
        )
        {
            error = "";

            if (string.IsNullOrWhiteSpace(deviceName))
            {
                error = "设备名为空";
                return false;
            }
            if (string.IsNullOrWhiteSpace(oActionName))
            {
                error = "动作名为空";
                return false;
            }

            var keys = (channelKeys ?? Array.Empty<string>())
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (keys.Count == 0)
            {
                error = "必须指定至少一个 Key 通道 —— 没有 Key 的 OAction 不会下发任何通道";
                return false;
            }

            byte[]? original;
            try
            {
                if (!File.Exists(TablePath))
                {
                    error = $"设备表不存在: {TablePath}";
                    return false;
                }
                original = File.ReadAllBytes(TablePath);
            }
            catch (Exception ex)
            {
                error = $"读取设备表失败: {ex.Message}";
                return false;
            }

            bool hasBom =
                original.Length >= 3
                && original[0] == 0xEF
                && original[1] == 0xBB
                && original[2] == 0xBF;
            string text = new UTF8Encoding(false).GetString(
                original,
                hasBom ? 3 : 0,
                original.Length - (hasBom ? 3 : 0)
            );

            if (!TryInsertOAction(text, deviceName, oActionName, label, keys, out string updated, out error))
                return false;

            // 写盘前先确认插出来的还是合法 XML —— 宁可失败也不要把用户的设备表写坏。
            try
            {
                var check = new XmlDocument();
                check.LoadXml(updated);
            }
            catch (Exception ex)
            {
                error = $"插入后 XML 非法, 已放弃写入: {ex.Message}";
                return false;
            }

            try
            {
                var bytes = new UTF8Encoding(false).GetBytes(updated);
                if (hasBom)
                {
                    var withBom = new byte[bytes.Length + 3];
                    withBom[0] = 0xEF;
                    withBom[1] = 0xBB;
                    withBom[2] = 0xBF;
                    Array.Copy(bytes, 0, withBom, 3, bytes.Length);
                    bytes = withBom;
                }
                File.WriteAllBytes(TablePath, bytes);
                return true;
            }
            catch (Exception ex)
            {
                error = $"写入设备表失败: {ex.Message}";
                return false;
            }
        }

        /// <summary>纯文本层面的插入 (无 IO, 可单测)。</summary>
        internal static bool TryInsertOAction(
            string text,
            string deviceName,
            string oActionName,
            string? label,
            IReadOnlyList<string> channelKeys,
            out string updated,
            out string error
        )
        {
            updated = text;
            error = "";

            var openTags = OpenTagRegex.Matches(text);
            Match? target = null;
            foreach (Match m in openTags)
            {
                var nameMatch = NameAttrRegex.Match(m.Value);
                if (
                    nameMatch.Success
                    && string.Equals(nameMatch.Groups[1].Value, deviceName, StringComparison.OrdinalIgnoreCase)
                )
                {
                    target = m;
                    break;
                }
            }

            if (target is null)
            {
                error = $"设备表中找不到设备 '{deviceName}'";
                return false;
            }

            bool selfClosing = target.Value.EndsWith("/>", StringComparison.Ordinal);
            int blockEnd;
            if (selfClosing)
            {
                blockEnd = target.Index + target.Length;
            }
            else
            {
                int close = text.IndexOf("</Device>", target.Index + target.Length, StringComparison.OrdinalIgnoreCase);
                if (close < 0)
                {
                    error = $"设备 '{deviceName}' 的结点块未闭合";
                    return false;
                }
                blockEnd = close;
            }

            string block = text.Substring(target.Index, blockEnd - target.Index);

            // 已有同名 OAction → 不重复插 (幂等, 也是防呆)
            foreach (Match oa in Regex.Matches(block, @"<OAction\b[^>]*?>", RegexOptions.IgnoreCase))
            {
                var nm = NameAttrRegex.Match(oa.Value);
                if (
                    nm.Success
                    && string.Equals(nm.Groups[1].Value, oActionName, StringComparison.OrdinalIgnoreCase)
                )
                {
                    error = $"设备 '{deviceName}' 已有动作 '{oActionName}'";
                    return false;
                }
            }

            string eol = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            string deviceIndent = LineIndentAt(text, target.Index);
            string childIndent = deviceIndent + "  ";

            var node = new StringBuilder();
            node.Append(childIndent).Append("<OAction Name=\"").Append(Escape(oActionName)).Append('"');
            if (!string.IsNullOrWhiteSpace(label))
                node.Append(" Label=\"").Append(Escape(label.Trim())).Append('"');
            node.Append('>').Append(eol);
            foreach (var key in channelKeys)
                node.Append(childIndent).Append("  <Key Name=\"").Append(Escape(key)).Append("\" />").Append(eol);
            node.Append(childIndent).Append("</OAction>").Append(eol);

            if (selfClosing)
            {
                // <Device ... /> → <Device ...> + 新结点 + </Device>
                var openedTag = target.Value[..^2].TrimEnd() + ">";
                updated =
                    text[..target.Index]
                    + openedTag
                    + eol
                    + node
                    + deviceIndent
                    + "</Device>"
                    + text[(target.Index + target.Length)..];
                return true;
            }

            // 插在 </Device> 那一行之前 (保留它自己的缩进)
            int lineStart = blockEnd;
            while (lineStart > 0 && text[lineStart - 1] != '\n')
                lineStart--;

            updated = text[..lineStart] + node + text[lineStart..];
            return true;
        }

        /// <summary>取某个下标所在行的行首缩进, 用于让插入的结点与邻居对齐。</summary>
        private static string LineIndentAt(string text, int index)
        {
            int lineStart = index;
            while (lineStart > 0 && text[lineStart - 1] != '\n')
                lineStart--;

            int i = lineStart;
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t'))
                i++;

            return text[lineStart..i];
        }

        private static string Escape(string value) =>
            value.Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
    }
}
