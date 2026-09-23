using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// Device channel engineering range catalog — 归一化 0..1 与该设备"自己声明"的工程量区间之间的映射。
    ///
    /// <para>为什么量程属于设备: 量程是硬件属性, 不是创作意图。动作文件保持设备无关,
    /// 换平台只改平台自己的配置, 永不改 .motion。</para>
    ///
    /// <para><b>来源与优先级</b>:
    /// <list type="number">
    /// <item><b>设备自述 capabilities</b> (首选, 单一同源): 设备通过 RPC topic
    ///   <c>plugin.capabilities</c> 返回的 <c>motion.channel_range[]</c>,
    ///   由插件从其 travel 配置派生, 与三维视图软限位同源。</item>
    /// <item><b>设备配置文件兜底</b>: &lt;root&gt;/Config/&lt;DllName&gt;/config.ini 的
    ///   <c>[channel_range]</c> 段 (capabilities 不可用时使用)。</item>
    /// </list></para>
    ///
    /// <para><b>时序约束 (重要)</b>: 读 capabilities 要走 native RPC, 而设备派发本身跑在
    /// native tick 内 —— 在派发路径里发 RPC 不安全。因此本类拆成两半:
    /// <list type="bullet">
    /// <item><see cref="Resolve"/> 只查缓存 + 惰性读设备配置文件, 永不触碰 native
    ///   (可在派发路径安全调用);</item>
    /// <item><see cref="Prime"/> 在安全时机 (时间轴加载 / 开始播放) 由引擎调用, 发一次 RPC 并缓存。</item>
    /// </list></para>
    ///
    /// <para>失败一律非致命: 无量程 → 直通 (保持既有行为)。</para>
    /// </summary>
    public static class DeviceRangeCatalog
    {
        private static readonly Dictionary<string, (float Min, float Max)> Cache =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>已从 capabilities 拿到明确答复的设备 — 不再重复查询。</summary>
        private static readonly HashSet<string> Queried = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>尝试次数 — 设备迟迟不可查询时给出上限, 避免每次播放都等一次超时。</summary>
        private static readonly Dictionary<string, int> Attempts = new(
            StringComparer.OrdinalIgnoreCase
        );

        private const int MaxAttempts = 5;
        private const int QueryTimeoutMs = 500;

        private static readonly object Gate = new();
        private static bool _iniLoaded;

        /// <summary>Install as NativeMotionPlaybackEngine.DeviceRangeProvider. 只查缓存, 不碰 native。</summary>
        public static (float Min, float Max)? Resolve(string deviceName, string channelKey)
        {
            if (string.IsNullOrEmpty(deviceName) || string.IsNullOrEmpty(channelKey))
                return null;

            lock (Gate)
            {
                EnsureIniLoaded();
                return Cache.TryGetValue(deviceName + "|" + channelKey, out var range)
                    ? range
                    : null;
            }
        }

        /// <summary>
        /// Install as NativeMotionPlaybackEngine.DeviceRangePrimer.
        /// 只允许在<b>安全时机</b>调用 (时间轴加载 / Play), 不能在设备派发路径里调用。
        /// 从设备自述 capabilities 取量程 (覆盖配置兜底值)。
        /// </summary>
        public static void Prime(IEnumerable<string> deviceNames)
        {
            if (deviceNames == null)
                return;

            foreach (string name in deviceNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(name))
                    continue;
                try
                {
                    PrimeOne(name);
                }
                catch (Exception ex)
                {
                    // capabilities 是增强项: 失败回落到配置兜底。
                    Debug.WriteLine($"[DeviceRangeCatalog] {name}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        private static void PrimeOne(string deviceName)
        {
            lock (Gate)
            {
                EnsureIniLoaded();
                if (Queried.Contains(deviceName))
                    return;
                int attempt = Attempts.TryGetValue(deviceName, out var a) ? a : 0;
                if (attempt >= MaxAttempts)
                {
                    Queried.Add(deviceName);
                    return;
                }
                Attempts[deviceName] = attempt + 1;
            }

            var device = IOToolkit.IODeviceController.GetIODevice(deviceName);
            if (device == null)
                return; // 设备尚未加载 → 下次安全时机再试

            // 通用 _rpc.req/_rpc.res 通道 (插件在 RPC topic plugin.capabilities 上返回 capabilities)。
            string json = device.SendPluginRequest("plugin.capabilities", "{}", QueryTimeoutMs);
            if (string.IsNullOrEmpty(json))
                json = device.SendPluginRequest("simulator.capabilities", "{}", QueryTimeoutMs);
            if (string.IsNullOrEmpty(json))
                return; // 无响应 → 下次安全时机再试

            var map = ParseChannelRanges(json);

            lock (Gate)
            {
                Queried.Add(deviceName);
                // capabilities 是权威: 覆盖配置兜底值
                foreach (var kv in map)
                    Cache[deviceName + "|" + kv.Key] = kv.Value;
            }
        }

        /// <summary>从 capabilities JSON 抽取 motion.channel_range[]。</summary>
        private static Dictionary<string, (float Min, float Max)> ParseChannelRanges(string json)
        {
            var map = new Dictionary<string, (float Min, float Max)>(
                StringComparer.OrdinalIgnoreCase
            );
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("motion", out var motion))
                    return map;
                if (
                    !motion.TryGetProperty("channel_range", out var ranges)
                    || ranges.ValueKind != JsonValueKind.Array
                )
                    return map;

                foreach (var r in ranges.EnumerateArray())
                {
                    string? ch = r.TryGetProperty("channel", out var c) ? c.GetString() : null;
                    if (string.IsNullOrEmpty(ch))
                        continue;
                    if (!r.TryGetProperty("min", out var mn) || !r.TryGetProperty("max", out var mx))
                        continue;
                    if (mn.ValueKind != JsonValueKind.Number || mx.ValueKind != JsonValueKind.Number)
                        continue;

                    float min = (float)mn.GetDouble();
                    float max = (float)mx.GetDouble();
                    if (max <= min)
                        continue;

                    map[ch] = (min, max);
                }
            }
            catch (JsonException) { }
            return map;
        }

        /// <summary>兜底来源: 设备配置 Config/&lt;DllName&gt;/config.ini 的 [channel_range] 段 (只读一次)。</summary>
        private static void EnsureIniLoaded()
        {
            if (_iniLoaded)
                return;
            _iniLoaded = true;
            try
            {
                string root = AppContext.BaseDirectory;
                string xmlPath = Path.Combine(root, "Config", "IODevice.xml");
                if (!File.Exists(xmlPath))
                    return;

                var doc = XDocument.Load(xmlPath);
                foreach (var dev in doc.Root?.Elements("Device") ?? Enumerable.Empty<XElement>())
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

                // 不覆盖已由 capabilities 提供的值
                string cacheKey = deviceName + "|" + key;
                if (!Cache.ContainsKey(cacheKey))
                    Cache[cacheKey] = (min, max);
            }
        }
    }
}
