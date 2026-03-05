using IniParser;
using IniParser.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace IOTester.Services
{
    /// <summary>
    /// INI配置文件服务，负责读取和写入外部设备的配置文件
    /// </summary>
    public class IniConfigService
    {
        private static readonly Lazy<IniConfigService> _instance =
            new(() => new IniConfigService());
        public static IniConfigService Instance => _instance.Value;

        private readonly FileIniDataParser _parser;
        private readonly Encoding _utf8Encoding;

        private IniConfigService()
        {
            _parser = new FileIniDataParser();
            // 配置解析器以正确处理注释
            // 使用正则表达式同时支持 ; 和 # 作为注释标识符
            _parser.Parser.Configuration.CommentRegex = new System.Text.RegularExpressions.Regex(
                @"^[;#].*"
            );
            _parser.Parser.Configuration.AllowKeysWithoutSection = false;
            _parser.Parser.Configuration.SkipInvalidLines = true;
            // 允许重复的键（某些INI文件可能有同名键）
            _parser.Parser.Configuration.AllowDuplicateKeys = true;

            // 使用UTF-8编码（无BOM）
            _utf8Encoding = new UTF8Encoding(false);
        }

        /// <summary>
        /// 获取设备配置文件路径
        /// </summary>
        /// <param name="dllName">DLL名称（如MODBUS、SNAP7等）</param>
        /// <returns>config.ini完整路径</returns>
        public string GetIniPath(string dllName)
        {
            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(
                basePath,
                "ExternalLibraries",
                "Config",
                dllName.ToUpper(),
                "config.ini"
            );
        }

        /// <summary>
        /// 获取Schema文件路径
        /// </summary>
        /// <param name="dllName">DLL名称</param>
        /// <returns>schema.json完整路径</returns>
        public string GetSchemaPath(string dllName)
        {
            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            // Schema文件使用小写命名，存放在 Config/Schemas 目录下
            return Path.Combine(basePath, "Config", "Schemas", $"{dllName.ToLower()}.schema.json");
        }

        /// <summary>
        /// 获取配置文件(INI)路径
        /// </summary>
        /// <param name="dllName">DLL名称</param>
        /// <returns>config.ini完整路径</returns>
        public string GetConfigPath(string dllName)
        {
            return GetIniPath(dllName);
        }

        /// <summary>
        /// 读取设备配置，合并[default]和[device_N]
        /// </summary>
        /// <param name="dllName">DLL名称</param>
        /// <param name="deviceIndex">设备索引（0表示只读default，>0读取device_N覆盖default）</param>
        /// <returns>合并后的配置字典</returns>
        public Dictionary<string, string> ReadConfig(string dllName, int deviceIndex)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var iniPath = GetIniPath(dllName);

            if (!File.Exists(iniPath))
            {
                System.Diagnostics.Debug.WriteLine($"INI file not found: {iniPath}");
                return result;
            }

            try
            {
                var data = _parser.ReadFile(iniPath, _utf8Encoding);

                // 1. 先读取[default] section
                if (data.Sections.ContainsSection("default"))
                {
                    foreach (var key in data["default"])
                    {
                        result[key.KeyName] = key.Value;
                    }
                }

                // 2. 如果deviceIndex > 0，再用[device_N]覆盖
                if (deviceIndex > 0)
                {
                    var deviceSection = $"device_{deviceIndex}";
                    if (data.Sections.ContainsSection(deviceSection))
                    {
                        foreach (var key in data[deviceSection])
                        {
                            result[key.KeyName] = key.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to read INI file: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 读取指定section的原始配置（不合并）
        /// </summary>
        /// <param name="dllName">DLL名称</param>
        /// <param name="sectionName">Section名称（如"default"、"device_1"等）</param>
        /// <returns>配置字典</returns>
        public Dictionary<string, string> ReadSection(string dllName, string sectionName)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var iniPath = GetIniPath(dllName);

            if (!File.Exists(iniPath))
                return result;

            try
            {
                var data = _parser.ReadFile(iniPath, _utf8Encoding);
                if (data.Sections.ContainsSection(sectionName))
                {
                    foreach (var key in data[sectionName])
                    {
                        result[key.KeyName] = key.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to read section: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 获取所有配置项的键名（用于Schema同步）
        /// </summary>
        /// <param name="dllName">DLL名称</param>
        /// <returns>所有唯一的键名列表</returns>
        public List<string> GetAllKeys(string dllName)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var iniPath = GetIniPath(dllName);

            if (!File.Exists(iniPath))
                return keys.ToList();

            try
            {
                var data = _parser.ReadFile(iniPath, _utf8Encoding);
                foreach (var section in data.Sections)
                {
                    // 跳过expressions section（那是表达式库，不是配置项）
                    if (
                        section.SectionName.Equals(
                            "expressions",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        continue;

                    foreach (var key in section.Keys)
                    {
                        // 过滤动态通道配置（ai_channel_N等）
                        var keyName = key.KeyName;
                        if (IsChannelKey(keyName))
                        {
                            // 将 ai_channel_0, ai_channel_1 等统一为 ai_channel_*
                            keyName = GetChannelKeyPattern(keyName);
                        }
                        keys.Add(keyName);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get all keys: {ex.Message}");
            }

            return keys.OrderBy(k => k).ToList();
        }

        /// <summary>
        /// 获取所有已存在的设备Index列表
        /// </summary>
        /// <param name="dllName">DLL名称</param>
        /// <returns>设备索引列表</returns>
        public List<int> GetExistingDeviceIndexes(string dllName)
        {
            var indexes = new List<int>();
            var iniPath = GetIniPath(dllName);

            if (!File.Exists(iniPath))
                return indexes;

            try
            {
                var data = _parser.ReadFile(iniPath, _utf8Encoding);
                foreach (var section in data.Sections)
                {
                    if (
                        section.SectionName.StartsWith(
                            "device_",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        var indexStr = section.SectionName.Substring(7);
                        if (int.TryParse(indexStr, out int index))
                        {
                            indexes.Add(index);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get device indexes: {ex.Message}");
            }

            return indexes.OrderBy(i => i).ToList();
        }

        /// <summary>
        /// 保存配置到指定设备section
        /// </summary>
        /// <param name="dllName">DLL名称</param>
        /// <param name="deviceIndex">设备索引（0保存到default，>0保存到device_N）</param>
        /// <param name="values">要保存的配置项</param>
        /// <param name="defaultValues">默认值（用于判断哪些需要写入device_N）</param>
        /// <param name="deletedKeys">已删除的键列表</param>
        public void SaveConfig(
            string dllName,
            int deviceIndex,
            Dictionary<string, string> values,
            Dictionary<string, string>? defaultValues = null,
            List<string>? deletedKeys = null
        )
        {
            var iniPath = GetIniPath(dllName);

            if (!File.Exists(iniPath))
            {
                System.Diagnostics.Debug.WriteLine($"INI file not found: {iniPath}");
                return;
            }

            try
            {
                var data = _parser.ReadFile(iniPath, _utf8Encoding);
                var sectionName = deviceIndex == 0 ? "default" : $"device_{deviceIndex}";

                // 确保section存在
                if (!data.Sections.ContainsSection(sectionName))
                {
                    data.Sections.AddSection(sectionName);
                }

                var section = data[sectionName];

                // 删除已标记为删除的键
                if (deletedKeys != null)
                {
                    foreach (var key in deletedKeys)
                    {
                        if (section.ContainsKey(key))
                        {
                            section.RemoveKey(key);
                        }
                    }
                }

                foreach (var kvp in values)
                {
                    // 对于device_N section，只保存与default不同的值
                    if (deviceIndex > 0 && defaultValues != null)
                    {
                        if (defaultValues.TryGetValue(kvp.Key, out var defaultValue))
                        {
                            if (kvp.Value == defaultValue)
                            {
                                // 值与默认相同，从device_N中移除
                                if (section.ContainsKey(kvp.Key))
                                {
                                    section.RemoveKey(kvp.Key);
                                }
                                continue;
                            }
                        }
                    }

                    // 添加或更新配置项
                    if (section.ContainsKey(kvp.Key))
                    {
                        section[kvp.Key] = kvp.Value;
                    }
                    else
                    {
                        section.AddKey(kvp.Key, kvp.Value);
                    }
                }

                _parser.WriteFile(iniPath, data, _utf8Encoding);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save config: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 保存指定section的配置（用于独立section如InputMapping、OutputMapping）
        /// </summary>
        /// <param name="dllName">DLL名称</param>
        /// <param name="sectionName">Section名称</param>
        /// <param name="values">要保存的配置项</param>
        /// <param name="deletedKeys">要删除的键</param>
        public void SaveSection(
            string dllName,
            string sectionName,
            Dictionary<string, string> values,
            List<string>? deletedKeys = null
        )
        {
            var iniPath = GetIniPath(dllName);

            if (!File.Exists(iniPath))
            {
                System.Diagnostics.Debug.WriteLine($"INI file not found: {iniPath}");
                return;
            }

            try
            {
                var data = _parser.ReadFile(iniPath, _utf8Encoding);

                // 确保section存在
                if (!data.Sections.ContainsSection(sectionName))
                {
                    data.Sections.AddSection(sectionName);
                }

                var section = data[sectionName];

                // 删除已标记为删除的键
                if (deletedKeys != null)
                {
                    foreach (var key in deletedKeys)
                    {
                        if (section.ContainsKey(key))
                        {
                            section.RemoveKey(key);
                        }
                    }
                }

                // 更新/添加配置项
                foreach (var kvp in values)
                {
                    if (section.ContainsKey(kvp.Key))
                    {
                        section[kvp.Key] = kvp.Value;
                    }
                    else
                    {
                        section.AddKey(kvp.Key, kvp.Value);
                    }
                }

                _parser.WriteFile(iniPath, data, _utf8Encoding);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save section: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 判断是否为通道类型的key（如ai_channel_0, ai_channel_1等）
        /// </summary>
        private bool IsChannelKey(string keyName)
        {
            // 匹配 xxx_N 或 xxx_NN 格式
            if (keyName.Contains("_"))
            {
                var lastPart = keyName.Substring(keyName.LastIndexOf('_') + 1);
                if (int.TryParse(lastPart, out _))
                {
                    // 确认是通道类key（ai_channel, ao_channel等）
                    var prefix = keyName.Substring(0, keyName.LastIndexOf('_'));
                    return prefix.EndsWith("channel", StringComparison.OrdinalIgnoreCase);
                }
            }
            return false;
        }

        /// <summary>
        /// 获取通道key的通配模式（如 ai_channel_0 → ai_channel_*）
        /// </summary>
        private string GetChannelKeyPattern(string keyName)
        {
            var lastUnderscoreIndex = keyName.LastIndexOf('_');
            return keyName.Substring(0, lastUnderscoreIndex + 1) + "*";
        }
    }
}
