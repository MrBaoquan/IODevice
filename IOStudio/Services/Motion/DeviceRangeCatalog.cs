using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// Device channel engineering range catalog: maps normalized 0..1 to the range the
    /// DEVICE declares for itself.
    ///
    /// Why device-side: the range is a hardware property, not an authoring intent. Motion
    /// files stay device-agnostic; re-targeting to another platform only changes that
    /// platform's own config, never the .motion files.
    ///
    /// Sources (all owned by the device; IOStudio hardcodes no device semantics):
    ///   1. &lt;root&gt;/Config/IODevice.xml  -> &lt;Device Name="Simulator-1" DllName="Simulator"/&gt;
    ///   2. &lt;root&gt;/Config/&lt;DllName&gt;/config.ini, section [channel_range]:
    ///        OAxis_01 = -30, 30, deg
    ///      Semantics: normalized 0 -> min, 1 -> max (midpoint 0.5 -> (min+max)/2).
    ///
    /// Failures are non-fatal: without ranges everything passes through as before.
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
                if (!_loaded) { Load(); _loaded = true; }
            }
            return Cache.TryGetValue(deviceName + "|" + channelKey, out var range) ? range : null;
        }

        private static void Load()
        {
            try
            {
                string root = AppContext.BaseDirectory;
                string xmlPath = Path.Combine(root, "Config", "IODevice.xml");
                if (!File.Exists(xmlPath)) return;

                var doc = XDocument.Load(xmlPath);
                var devices = doc.Root?.Elements("Device") ?? Enumerable.Empty<XElement>();
                foreach (var dev in devices)
                {
                    string? name = dev.Attribute("Name")?.Value;
                    string? dll = dev.Attribute("DllName")?.Value;
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(dll)) continue;

                    string iniPath = Path.Combine(root, "Config", dll, "config.ini");
                    if (!File.Exists(iniPath)) continue;
                    ParseIni(iniPath, name);
                }
            }
            catch
            {
                // Ranges are an optional enhancement: on any failure we keep pass-through.
            }
        }

        private static void ParseIni(string path, string deviceName)
        {
            bool inSection = false;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                if (line[0] == '[')
                {
                    inSection = line.Equals("[channel_range]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inSection) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line[..eq].Trim();
                string[] parts = line[(eq + 1)..].Split(',');
                if (parts.Length < 2) continue;
                if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float min)) continue;
                if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float max)) continue;
                if (max <= min) continue;

                Cache[deviceName + "|" + key] = (min, max);
            }
        }
    }
}
