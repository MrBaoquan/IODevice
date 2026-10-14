using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IOStudio.Models
{
    /// <summary>
    /// 设备表的发现与解析 —— "加载哪一份 <c>Config\IODevice*.xml</c>"。
    ///
    /// <para><b>为什么做成可切换</b>: 现场可能同时存在多套设备配置 (影动力台 / 仿真台 / 调试台),
    /// 而它们此前只能共用一个 <c>IODevice.xml</c> —— 切换就等于互相覆盖, 谁都不敢改。
    /// 现在每套各占一份文件, 启动时加载哪份由设置决定, 运行时也能切换。</para>
    ///
    /// <para><b>切换只改"加载哪一份", 绝不改写文件</b> —— 于是不存在"切一下就把配置冲掉"的风险。</para>
    /// </summary>
    public static class DeviceTableLocator
    {
        /// <summary>缺省表名。缺了它设备服务起不来, 所以始终作为兜底。</summary>
        public const string DefaultFileName = "IODevice.xml";

        /// <summary>预设的命名约定: <c>IODevice.&lt;名字&gt;.xml</c>。</summary>
        private const string PresetPrefix = "IODevice.";
        private const string Extension = ".xml";

        /// <summary>设备表所在目录。</summary>
        public static string ConfigDir =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");

        /// <summary>按文件名拼出全路径 (只取文件名部分, 防止传入路径穿透目录)。</summary>
        public static string ResolvePath(string? fileName)
        {
            string name = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? DefaultFileName : fileName);
            if (string.IsNullOrEmpty(name))
                name = DefaultFileName;
            return Path.Combine(ConfigDir, name);
        }

        /// <summary>
        /// 生效的设备表全路径。
        ///
        /// <para>配置指向的文件不存在时<b>回落</b>到 <c>IODevice.xml</c>；连它也不存在时,
        /// 仍然返回缺省路径 —— 由 <c>IORoot.Load()</c> 去创建/报错, 调用方不必处理"没有表"这种状态。</para>
        /// </summary>
        public static string ResolveEffectivePath()
        {
            string configured = ResolvePath(IOStudioSettings.Instance.DeviceTableFile);
            if (File.Exists(configured))
                return configured;

            string fallback = ResolvePath(DefaultFileName);
            return fallback;
        }

        /// <summary>
        /// 列出现有设备表: 缺省表在前, 其余预设按名字排。
        /// 目录不存在或无任何表时返回空列表。
        /// </summary>
        public static IReadOnlyList<string> Discover()
        {
            var dir = ConfigDir;
            if (!Directory.Exists(dir))
                return Array.Empty<string>();

            try
            {
                var names = Directory
                    .EnumerateFiles(dir, "IODevice*.xml", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName)
                    .Where(n => !string.IsNullOrEmpty(n))
                    // 命名约定: IODevice.xml 或 IODevice.<名字>.xml;
                    // 排除 IOdevice.example.xml 之类历史遗留的示例 (大小写与后缀都不符约定)。
                    .Where(n =>
                        n!.Equals(DefaultFileName, StringComparison.OrdinalIgnoreCase)
                        || (
                            n.StartsWith(PresetPrefix, StringComparison.OrdinalIgnoreCase)
                            && !n.EndsWith(".example.xml", StringComparison.OrdinalIgnoreCase)
                        )
                    )
                    .OrderByDescending(n => n!.Equals(DefaultFileName, StringComparison.OrdinalIgnoreCase))
                    .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return names!;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DeviceTableLocator] 枚举设备表失败: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>把预设名转成文件名: <c>YDL</c> → <c>IODevice.YDL.xml</c>。</summary>
        public static string PresetFileNameFor(string name) =>
            PresetPrefix + (name ?? "").Trim() + Extension;

        /// <summary>
        /// 校验预设名是否可用; 不可用时给出原因。
        ///
        /// <para>只接受单层文件名片段 —— 带路径分隔符的输入会把文件写到 Config 之外,
        /// 那不是"预设"该有的行为。</para>
        /// </summary>
        public static bool IsValidPresetName(string? name, out string error)
        {
            error = "";
            string trimmed = (name ?? "").Trim();

            if (trimmed.Length == 0)
            {
                error = "名称不能为空";
                return false;
            }

            if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || trimmed.Contains('/') || trimmed.Contains('\\'))
            {
                error = "名称不能包含路径分隔符或文件名字符";
                return false;
            }

            // 叫 "IODevice" 会得到 IODevice.IODevice.xml, 不算错但很容易看混
            if (trimmed.Equals("IODevice", StringComparison.OrdinalIgnoreCase))
            {
                error = "这个名称容易和缺省表看混，请换一个";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 把<b>当前内存里的设备表</b>另存为预设。
        ///
        /// <para>用应用自己的序列化路径写 (临时把 <c>IORoot</c> 的配置路径指向目标再 Save),
        /// 于是预 preset 的格式与手工保存出来的完全一致; 写完<b>无论如何</b>都把路径还原 ——
        /// 否则后续所有保存都会写到这份预设上, 那是比写失败更坏的后果。</para>
        /// </summary>
        public static bool SaveCurrentAsPreset(string name, out string fileName, out string error)
        {
            fileName = "";
            if (!IsValidPresetName(name, out error))
                return false;

            var root = ViewModels.IORoot.Instance;
            if (root is null)
            {
                error = "设备表未加载";
                return false;
            }

            fileName = PresetFileNameFor(name);
            string target = ResolvePath(fileName);
            string previous = root.FilePath;

            try
            {
                Directory.CreateDirectory(ConfigDir);
                root.SetConfig(target).Save();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                // 必须还原: 不还原的话, 之后设备管理里的每一次保存都会写到这份预设上
                if (!string.IsNullOrEmpty(previous))
                    root.SetConfig(previous);
            }
        }

        /// <summary>把预设文件名说成人话: <c>IODevice.YDL.xml</c> → <c>YDL</c>; 缺省表 → "默认"。</summary>
        public static string DescribeLabel(string fileName)
        {
            if (fileName.Equals(DefaultFileName, StringComparison.OrdinalIgnoreCase))
                return "默认 (IODevice.xml)";

            if (
                fileName.StartsWith(PresetPrefix, StringComparison.OrdinalIgnoreCase)
                && fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
            )
            {
                return fileName[PresetPrefix.Length..^Extension.Length];
            }

            return fileName;
        }
    }
}
