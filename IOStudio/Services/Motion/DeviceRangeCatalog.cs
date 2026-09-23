using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// Device channel engineering range catalog — 归一化 0..1 与该设备"自己声明"的工程量区间之间的映射。
    ///
    /// <para>为什么量程属于设备: 量程是硬件属性, 不是创作意图。动作文件保持设备无关,
    /// 换平台只改平台自己的配置, 永不改 .motion。</para>
    ///
    /// <para><b>来源</b> (全部由设备自己持有, IOStudio 不硬编码任何设备语义):
    /// <list type="number">
    /// <item>&lt;root&gt;/Config/IODevice.xml — 设备名 → DllName</item>
    /// <item>&lt;root&gt;/Config/&lt;DllName&gt;/config.ini 的 <c>[channel_range]</c> 段:
    ///   <c>OAxis_01 = -30, 30, deg</c> (归一化 0→min, 1→max)。</item>
    /// </list></para>
    ///
    /// <para><b>目标终态 (尚未打通)</b>: 由插件把量程作为 capabilities 发布
    /// (<c>motion.channel_range</c>), 宿主进程内读取, 从而让量程与 <c>motion_travel_*</c>
    /// 单一同源、无需在设备配置里再写一份。该链路目前受阻于宿主/插件通道缺陷:
    /// 经 <c>_rpc.req</c> 的 <c>plugin.capabilities</c> 调用在宿主内抛原生异常 (SEHException),
    /// 经 <c>_capabilities</c> 的 QueryChannelCapabilities 返回空 —— 两者都指向
    /// "插件→宿主" 回调未生效。详见提交说明。在此之前由本 ini 段承载。</para>
    ///
    /// <para>失败一律非致命: 无量程 → 直通 (保持既有行为)。</para>
    /// </summary>
    public static class DeviceRangeCatalog
    {
        private static readonly Dictionary<string, (float Min, float Max)?> Cache =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly object Gate = new();
        private static bool _loaded;

        /// <summary>Install as NativeMotionPlaybackEngine.DeviceRangeProvider.</summary>
        public static (float Min, float Max)? Resolve(string deviceName, string channelKey)
        {
            lock (Gate)
            {
                if (!_loaded)
                {
                    Load();
                    _loaded = true;
                }
            }
            return Cache.TryGetValue(deviceName + "|" + channelKey, out var range) ? range : null;
        }

        private static void Load()
        {
            try
            {
                string root = AppContext.BaseDirectory;
                string xmlPath = Path.Combine(root, "Config", "IODevice.xml");
                if (!File.Exists(xmlPath))
                    return;

                var doc = XDocument.Load(xmlPath);
                var devices = doc.Root?.Elements("Device") ?? Enumerable.Empty<XElement>();
                foreach (var dev in devices)
                {
                    string? name = dev.Attribute("Name")?.Value;
                    string? dll = dev.Attribute("DllName")?.Value;
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(dll))
                        continue;

                    string iniPath = Path.Combine(root, "Config", dll, "config.ini");
                    if (!File.Exists(iniPath))
                        continue;
                    ParseIni(iniPath, name);
                }
            }
            catch
            {
                // 量程是增强项: 任何失败都回落到直通, 不影响原有行为。
            }
        }

        /// <summary>解析设备配置里的 [channel_range] 段。</summary>
        private static void ParseIni(string path, string deviceName)
        {
            bool inSection = false;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                    continue;
                if (line[0] == '[')
                {
                    inSection = line.Equals("[channel_range]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inSection)
                    continue;

                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                string key = line[..eq].Trim();
                string[] parts = line[(eq + 1)..].Split(',');
                if (parts.Length < 2)
                    continue;
                if (
                    !float.TryParse(
                        parts[0].Trim(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out float min
                    )
                )
                    continue;
                if (
                    !float.TryParse(
                        parts[1].Trim(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out float max
                    )
                )
                    continue;
                if (max <= min)
                    continue;

                Cache[deviceName + "|" + key] = (min, max);
            }
        }
    }
}
