using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace IOStudio.Services.Config
{
    /// <summary>
    /// config.xml 后端。用于只读 XML 的插件 (MP-YDL / MP-NJLJ / MP-NJQK / RPLIDAR ...)。
    /// 字段落点由 schema 里每个 field 的 xmlPath 描述，路径末段以 @ 开头表示写属性，
    /// 否则写元素文本；路径中的 {n} 替换为 1-based 的设备节点序号。
    /// </summary>
    public sealed class XmlDeviceConfigStore : IDeviceConfigStore
    {
        private readonly string _dllName;
        private readonly DeviceSchema _schema;

        public XmlDeviceConfigStore(string dllName, DeviceSchema schema)
        {
            _dllName = dllName;
            _schema = schema;
        }

        public string GetPath(string dllName)
        {
            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            // 与 IniConfigService.GetIniPath 同布局，只把文件名换成 config.xml。
            // 插件侧 Paths::GetConfigDir() 指向 <runtimeRoot>/Config/，而 IOStudio 传入的
            // runtimeRoot 就是 <AppRoot>/ExternalLibraries，两端一致。
            return Path.Combine(
                basePath,
                "ExternalLibraries",
                "Config",
                dllName.ToUpper(),
                "config.xml"
            );
        }

        public Dictionary<string, string> ReadConfig(string dllName, int deviceIndex)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var path = GetPath(dllName);

            if (!File.Exists(path))
                return result;

            XDocument doc;
            try
            {
                doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to parse XML config: {ex.Message}");
                return result;
            }

            foreach (var field in EnumerateFields())
            {
                if (string.IsNullOrWhiteSpace(field.XmlPath))
                    continue;

                var value = ReadValue(doc, field.XmlPath, deviceIndex);
                // 取不到就不放进结果 —— 面板据此回落到 field.Default 并标「默认值」。
                if (value != null)
                    result[field.Key] = value;
            }

            return result;
        }

        public Dictionary<string, string> ReadSection(string dllName, string sectionName)
        {
            // XML 后端没有 config.ini 那种独立 section 的概念。只有 "default" 有意义：
            // 返回 schema 声明的默认值，让面板的「同默认」标记能正常工作。
            if (!string.Equals(sectionName, "default", StringComparison.OrdinalIgnoreCase))
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in EnumerateFields())
            {
                if (!string.IsNullOrEmpty(field.Default))
                    result[field.Key] = field.Default;
            }
            return result;
        }

        public void SaveConfig(
            string dllName,
            int deviceIndex,
            Dictionary<string, string> values,
            Dictionary<string, string>? defaultValues = null,
            List<string>? deletedKeys = null
        )
        {
            if (values == null || values.Count == 0)
                return;

            var path = GetPath(dllName);
            XDocument doc;
            if (File.Exists(path))
            {
                doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            }
            else
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                // 声明与根节点之间留一个换行, 免得生成物挤成 `?><MPRoot>`。
                doc = new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    new XText(Environment.NewLine),
                    new XElement("MPRoot")
                );
            }

            var byKey = EnumerateFields()
                .Where(f => !string.IsNullOrWhiteSpace(f.XmlPath))
                .ToDictionary(f => f.Key, f => f.XmlPath!, StringComparer.OrdinalIgnoreCase);

            bool changed = false;
            foreach (var kvp in values)
            {
                if (!byKey.TryGetValue(kvp.Key, out var xmlPath))
                    continue;

                // 注意: 这里不套用 INI 那套「与默认相同就跳过」的 device_N 语义 ——
                // XML 里设备节点本身就区分实例, 跳过会留下属性残缺的节点。
                if (WriteValue(doc, xmlPath, deviceIndex, kvp.Value))
                    changed = true;
            }

            if (changed)
                SavePreservingLayout(doc, path);
        }

        public void SaveSection(
            string dllName,
            string sectionName,
            Dictionary<string, string> values,
            List<string>? deletedKeys = null
        )
        {
            // XML 后端不使用独立 section，空实现。
        }

        private IEnumerable<SchemaField> EnumerateFields()
        {
            if (_schema?.Sections == null)
                yield break;

            foreach (var section in _schema.Sections)
            {
                if (section?.Fields == null)
                    continue;

                foreach (var field in section.Fields)
                    yield return field;
            }
        }

        private static string ResolvePath(string xmlPath, int deviceIndex) =>
            xmlPath.Replace("{n}", (deviceIndex + 1).ToString());

        /// <summary>求值；取不到返回 null。</summary>
        private static string? ReadValue(XDocument doc, string xmlPath, int deviceIndex)
        {
            var (elementPath, attributeName) = SplitAttribute(xmlPath);
            if (elementPath == null)
                return null;

            var element = doc.XPathSelectElement(ResolvePath(elementPath, deviceIndex));
            if (element == null)
                return null;

            if (attributeName != null)
                return element.Attribute(attributeName)?.Value;

            return element.Value;
        }

        /// <summary>写值；返回是否真的改动了文档。缺失的节点会被创建出来。</summary>
        private static bool WriteValue(XDocument doc, string xmlPath, int deviceIndex, string value)
        {
            var (elementPath, attributeName) = SplitAttribute(xmlPath);
            if (elementPath == null)
                return false;

            var resolved = ResolvePath(elementPath, deviceIndex);
            var element = doc.XPathSelectElement(resolved) ?? CreatePath(doc, resolved);
            if (element == null)
                return false;

            if (attributeName != null)
            {
                var attr = element.Attribute(attributeName);
                if (attr != null)
                {
                    if (attr.Value == value)
                        return false;
                    attr.Value = value;
                }
                else
                {
                    element.SetAttributeValue(attributeName, value);
                }
                return true;
            }

            // 元素文本: 保留既有空白节点时不能直接改 Value (会连缩进一起吃掉),
            // 只替换其中的文本节点。
            var textNode = element.Nodes().OfType<XText>().FirstOrDefault();
            if (textNode != null)
            {
                if (textNode.Value == value)
                    return false;
                textNode.Value = value;
            }
            else
            {
                element.Add(new XText(value));
            }
            return true;
        }

        /// <summary>
        /// 拆出末尾的 /@attr。返回 (元素路径, 属性名)；纯元素路径时属性名为 null。
        /// </summary>
        private static (string? ElementPath, string? AttributeName) SplitAttribute(string xmlPath)
        {
            var slash = xmlPath.LastIndexOf('/');
            if (slash >= 0 && slash + 1 < xmlPath.Length && xmlPath[slash + 1] == '@')
            {
                var elementPath = xmlPath.Substring(0, slash);
                if (elementPath.Length == 0)
                    return (null, null);
                return (elementPath, xmlPath.Substring(slash + 2));
            }
            return (xmlPath, null);
        }

        /// <summary>按路径创建缺失的元素链，返回末端元素。</summary>
        private static XElement? CreatePath(XDocument doc, string elementPath)
        {
            var segments = elementPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 || doc.Root == null)
                return null;

            var current = doc.Root;
            var start = 0;

            // 首段就是根元素名时，它已由 doc.Root 满足，不能再去 doc.Root 的子节点里找 ——
            // 否则会造出 <MPRoot><MPRoot> 这种套娃。
            var (rootName, rootIndex) = ParseSegment(segments[0]);
            if (rootIndex == 1 && rootName == doc.Root.Name.LocalName)
                start = 1;

            for (var i = start; i < segments.Length; i++)
            {
                var (name, index) = ParseSegment(segments[i]);
                var matches = current.Elements(name).ToList();
                while (matches.Count < index)
                {
                    var created = new XElement(name);
                    AppendIndented(current, created);
                    matches.Add(created);
                }
                current = matches[index - 1];
            }

            return current;
        }

        /// <summary>解析 name 或 name[i] 形式的路径段。</summary>
        private static (string Name, int Index) ParseSegment(string segment)
        {
            var bracket = segment.IndexOf('[');
            if (bracket < 0)
                return (segment, 1);

            var name = segment.Substring(0, bracket);
            var close = segment.IndexOf(']', bracket);
            var inner = close > bracket
                ? segment.Substring(bracket + 1, close - bracket - 1)
                : string.Empty;
            return int.TryParse(inner, out var index) && index >= 1 ? (name, index) : (name, 1);
        }

        /// <summary>
        /// 按父节点深度缩进插入新元素。已有尾随空白时复用它，并把子缩进插在它前面，
        /// 使结果与手工编辑的文件同形，而不是被挤成一行。
        /// </summary>
        private static void AppendIndented(XElement parent, XElement child)
        {
            var depth = parent.Ancestors().Count();
            var childIndent = new string(' ', (depth + 1) * 2);
            var parentIndent = new string(' ', depth * 2);

            var trailing = parent.Nodes().OfType<XText>().LastOrDefault();
            if (trailing != null && trailing.Value.Contains('\n'))
            {
                var newline = trailing.Value.Contains("\r\n") ? "\r\n" : "\n";
                trailing.Value = newline + parentIndent;
                trailing.AddBeforeSelf(new XText(newline + childIndent), child);
            }
            else
            {
                var newline = Environment.NewLine;
                parent.Add(
                    new XText(newline + childIndent),
                    child,
                    new XText(newline + parentIndent)
                );
            }
        }

        /// <summary>
        /// 落盘。必须保留原有缩进与注释 —— config.xml 头部那段说明端口角色的中文注释
        /// 是有效文档，不能因为一次保存就被重排或丢失。
        /// NewLineHandling=None 是必须的：默认的 Replace 会把文本节点里的 \n 改写成
        /// \r\n，等于每次保存都把整个文件的行尾翻一遍。
        /// </summary>
        private static void SavePreservingLayout(XDocument doc, string path)
        {
            var settings = new XmlWriterSettings
            {
                Indent = false,
                NewLineHandling = NewLineHandling.None,
                OmitXmlDeclaration = doc.Declaration == null,
                Encoding = new UTF8Encoding(false),
            };

            using var writer = XmlWriter.Create(path, settings);
            doc.Save(writer);
        }
    }
}
