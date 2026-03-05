using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IOTester.Services
{
    /// <summary>
    /// 设备配置Schema服务，负责加载、同步和保存schema.json文件
    /// </summary>
    public class DeviceSchemaService
    {
        private static readonly Lazy<DeviceSchemaService> _instance =
            new(() => new DeviceSchemaService());
        public static DeviceSchemaService Instance => _instance.Value;

        private readonly JsonSerializerOptions _jsonOptions;
        private readonly Dictionary<string, DeviceSchema> _schemaCache =
            new(StringComparer.OrdinalIgnoreCase);

        private DeviceSchemaService()
        {
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
        }

        /// <summary>
        /// 加载设备的Schema定义
        /// </summary>
        /// <param name="dllName">DLL名称</param>
        /// <param name="forceReload">是否强制重新加载</param>
        /// <returns>Schema对象，如果不存在则返回空Schema</returns>
        public DeviceSchema LoadSchema(string dllName, bool forceReload = false)
        {
            var upperName = dllName.ToUpper();

            if (!forceReload && _schemaCache.TryGetValue(upperName, out var cached))
            {
                return cached;
            }

            var schemaPath = IniConfigService.Instance.GetSchemaPath(dllName);

            DeviceSchema schema;
            if (File.Exists(schemaPath))
            {
                try
                {
                    var json = File.ReadAllText(schemaPath);
                    schema =
                        JsonSerializer.Deserialize<DeviceSchema>(json, _jsonOptions)
                        ?? CreateDefaultSchema(dllName);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load schema: {ex.Message}");
                    schema = CreateDefaultSchema(dllName);
                }
            }
            else
            {
                // 自动从INI生成初始Schema
                schema = GenerateSchemaFromIni(dllName);
                SaveSchema(dllName, schema);
            }

            _schemaCache[upperName] = schema;
            return schema;
        }

        /// <summary>
        /// 保存Schema到文件
        /// </summary>
        /// <param name="dllName">DLL名称</param>
        /// <param name="schema">Schema对象</param>
        public void SaveSchema(string dllName, DeviceSchema schema)
        {
            var schemaPath = IniConfigService.Instance.GetSchemaPath(dllName);

            try
            {
                var json = JsonSerializer.Serialize(schema, _jsonOptions);
                var directory = Path.GetDirectoryName(schemaPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(schemaPath, json);

                _schemaCache[dllName.ToUpper()] = schema;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save schema: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 从INI文件同步Schema（添加新发现的配置项，保留用户已修改的内容）
        /// </summary>
        /// <param name="dllName">DLL名称</param>
        /// <returns>同步后的Schema</returns>
        public DeviceSchema SyncFromIni(string dllName)
        {
            var schema = LoadSchema(dllName);
            var iniKeys = IniConfigService.Instance.GetAllKeys(dllName);

            // 获取Schema中已有的所有key
            var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var section in schema.Sections)
            {
                foreach (var field in section.Fields)
                {
                    existingKeys.Add(field.Key);
                }
            }

            // 找出新增的key
            var newKeys = iniKeys.Where(k => !existingKeys.Contains(k)).ToList();

            if (newKeys.Count > 0)
            {
                // 将新key添加到"其他配置"section
                var otherSection = schema.Sections.FirstOrDefault(s => s.Name == "other");
                if (otherSection == null)
                {
                    otherSection = new SchemaSection
                    {
                        Name = "other",
                        Title = "其他配置",
                        Collapsed = true,
                        Fields = new List<SchemaField>()
                    };
                    schema.Sections.Add(otherSection);
                }

                foreach (var key in newKeys)
                {
                    otherSection.Fields.Add(
                        new SchemaField
                        {
                            Key = key,
                            Label = key,
                            Type = GuessFieldType(key),
                            Description = "自动发现的配置项"
                        }
                    );
                }

                SaveSchema(dllName, schema);
            }

            return schema;
        }

        /// <summary>
        /// 从INI文件生成初始Schema
        /// </summary>
        public DeviceSchema GenerateSchemaFromIni(string dllName)
        {
            var schema = CreateDefaultSchema(dllName);
            var iniKeys = IniConfigService.Instance.GetAllKeys(dllName);
            var defaultValues = IniConfigService.Instance.ReadSection(dllName, "default");

            // 按前缀分组配置项
            var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "connection", new List<string>() },
                { "timing", new List<string>() },
                { "di", new List<string>() },
                { "do", new List<string>() },
                { "ai", new List<string>() },
                { "other", new List<string>() }
            };

            foreach (var key in iniKeys)
            {
                var lowerKey = key.ToLower();
                if (lowerKey.StartsWith("di_"))
                    groups["di"].Add(key);
                else if (lowerKey.StartsWith("do_"))
                    groups["do"].Add(key);
                else if (lowerKey.StartsWith("ai_"))
                    groups["ai"].Add(key);
                else if (
                    lowerKey.Contains("interval")
                    || lowerKey.Contains("timeout")
                    || lowerKey.Contains("retry")
                    || lowerKey.Contains("poll")
                    || lowerKey.Contains("command")
                    || lowerKey.Contains("failure")
                )
                    groups["timing"].Add(key);
                else if (
                    lowerKey.Contains("ip")
                    || lowerKey.Contains("port")
                    || lowerKey.Contains("baud")
                    || lowerKey.Contains("driver")
                    || lowerKey.Contains("rack")
                    || lowerKey.Contains("slot")
                    || lowerKey.Contains("slave")
                    || lowerKey.Contains("parity")
                    || lowerKey.Contains("data_bit")
                    || lowerKey.Contains("stop_bit")
                )
                    groups["connection"].Add(key);
                else
                    groups["other"].Add(key);
            }

            // 生成sections
            schema.Sections = new List<SchemaSection>();

            if (groups["connection"].Count > 0)
            {
                schema.Sections.Add(
                    CreateSection("connection", "连接配置", groups["connection"], defaultValues)
                );
            }
            if (groups["timing"].Count > 0)
            {
                schema.Sections.Add(
                    CreateSection(
                        "timing",
                        "轮询时序",
                        groups["timing"],
                        defaultValues,
                        collapsed: true
                    )
                );
            }
            if (groups["di"].Count > 0)
            {
                schema.Sections.Add(
                    CreateSection("di", "DI配置（开关量输入）", groups["di"], defaultValues)
                );
            }
            if (groups["do"].Count > 0)
            {
                schema.Sections.Add(
                    CreateSection("do", "DO配置（开关量输出）", groups["do"], defaultValues)
                );
            }
            if (groups["ai"].Count > 0)
            {
                schema.Sections.Add(
                    CreateSection("ai", "AI配置（模拟量输入）", groups["ai"], defaultValues)
                );
            }
            if (groups["other"].Count > 0)
            {
                schema.Sections.Add(
                    CreateSection("other", "其他配置", groups["other"], defaultValues, collapsed: true)
                );
            }

            return schema;
        }

        private SchemaSection CreateSection(
            string name,
            string title,
            List<string> keys,
            Dictionary<string, string> defaultValues,
            bool collapsed = false
        )
        {
            return new SchemaSection
            {
                Name = name,
                Title = title,
                Collapsed = collapsed,
                Fields = keys.Select(k => CreateField(k, defaultValues)).ToList()
            };
        }

        private SchemaField CreateField(string key, Dictionary<string, string> defaultValues)
        {
            var field = new SchemaField
            {
                Key = key,
                Label = GetFriendlyLabel(key),
                Type = GuessFieldType(key),
            };

            if (defaultValues.TryGetValue(key, out var defaultValue))
            {
                field.Default = defaultValue;
            }

            // 为特定字段设置选项
            SetFieldOptions(field);

            return field;
        }

        private void SetFieldOptions(SchemaField field)
        {
            var lowerKey = field.Key.ToLower();

            // 驱动类型
            if (lowerKey == "driver")
            {
                field.Type = "select";
                field.Options = new List<SchemaOption>
                {
                    new SchemaOption { Value = "modbus_rtu", Label = "Modbus RTU（串口）" },
                    new SchemaOption { Value = "modbus_tcp", Label = "Modbus TCP（网络）" }
                };
            }
            // 校验位
            else if (lowerKey == "parity")
            {
                field.Type = "select";
                field.Options = new List<SchemaOption>
                {
                    new SchemaOption { Value = "N", Label = "无校验" },
                    new SchemaOption { Value = "E", Label = "偶校验" },
                    new SchemaOption { Value = "O", Label = "奇校验" }
                };
            }
            // 波特率
            else if (lowerKey == "baud_rate")
            {
                field.Type = "select";
                field.Options = new List<SchemaOption>
                {
                    new SchemaOption { Value = "9600", Label = "9600" },
                    new SchemaOption { Value = "19200", Label = "19200" },
                    new SchemaOption { Value = "38400", Label = "38400" },
                    new SchemaOption { Value = "57600", Label = "57600" },
                    new SchemaOption { Value = "115200", Label = "115200" }
                };
            }
            // 数据位
            else if (lowerKey == "data_bit")
            {
                field.Type = "select";
                field.Options = new List<SchemaOption>
                {
                    new SchemaOption { Value = "7", Label = "7" },
                    new SchemaOption { Value = "8", Label = "8" }
                };
            }
            // 停止位
            else if (lowerKey == "stop_bit")
            {
                field.Type = "select";
                field.Options = new List<SchemaOption>
                {
                    new SchemaOption { Value = "1", Label = "1" },
                    new SchemaOption { Value = "2", Label = "2" }
                };
            }
            // S7区域
            else if (lowerKey.EndsWith("_area"))
            {
                field.Type = "select";
                field.Options = new List<SchemaOption>
                {
                    new SchemaOption { Value = "PE", Label = "PE - 输入区(I)" },
                    new SchemaOption { Value = "PA", Label = "PA - 输出区(Q)" },
                    new SchemaOption { Value = "MK", Label = "MK - 标志区(M)" },
                    new SchemaOption { Value = "DB", Label = "DB - 数据块" }
                };
            }
            // DO模式
            else if (lowerKey == "do_mode")
            {
                field.Type = "select";
                field.Options = new List<SchemaOption>
                {
                    new SchemaOption { Value = "bit", Label = "位模式" },
                    new SchemaOption { Value = "byte", Label = "字节模式" },
                    new SchemaOption { Value = "word", Label = "字模式" },
                    new SchemaOption { Value = "dword", Label = "双字模式" }
                };
            }
            // DI/DO/AI功能码
            else if (lowerKey == "di_func")
            {
                field.Type = "select";
                field.Options = new List<SchemaOption>
                {
                    new SchemaOption { Value = "0x01", Label = "01 - 读线圈状态" },
                    new SchemaOption { Value = "0x02", Label = "02 - 读离散输入" }
                };
            }
            else if (lowerKey == "do_func")
            {
                field.Type = "select";
                field.Options = new List<SchemaOption>
                {
                    new SchemaOption { Value = "0x05", Label = "05 - 写单个线圈" },
                    new SchemaOption { Value = "0x06", Label = "06 - 写单个寄存器" }
                };
            }
            else if (lowerKey == "ai_func")
            {
                field.Type = "select";
                field.Options = new List<SchemaOption>
                {
                    new SchemaOption { Value = "0x03", Label = "03 - 读保持寄存器" },
                    new SchemaOption { Value = "0x04", Label = "04 - 读输入寄存器" }
                };
            }
        }

        private string GuessFieldType(string key)
        {
            var lowerKey = key.ToLower();

            // 布尔类型
            if (
                lowerKey.Contains("enable")
                || lowerKey.Contains("disabled")
                || lowerKey.Contains("_hold")
                || lowerKey.StartsWith("is_")
            )
                return "boolean";

            // 十六进制
            if (
                lowerKey.Contains("_addr")
                || lowerKey.Contains("_func")
                || lowerKey.Contains("_code")
                || lowerKey.Contains("_header")
                || lowerKey.Contains("_tail")
            )
                return "hex";

            // 数字类型
            if (
                lowerKey.Contains("_ms")
                || lowerKey.Contains("_num")
                || lowerKey.Contains("_bytes")
                || lowerKey.Contains("_start")
                || lowerKey.Contains("_size")
                || lowerKey.Contains("port")
                || lowerKey.Contains("slot")
                || lowerKey.Contains("rack")
                || lowerKey.Contains("index")
                || lowerKey.Contains("interval")
                || lowerKey.Contains("timeout")
                || lowerKey.Contains("failure")
                || lowerKey.Contains("_bit")
                || lowerKey.Contains("number")
            )
                return "number";

            return "text";
        }

        private string GetFriendlyLabel(string key)
        {
            // 常用配置项的友好名称映射
            var labelMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "driver", "驱动类型" },
                { "ip", "IP地址" },
                { "port", "端口/串口号" },
                { "baud_rate", "波特率" },
                { "data_bit", "数据位" },
                { "stop_bit", "停止位" },
                { "parity", "校验位" },
                { "slave_addr", "从机地址" },
                { "timeout_ms", "超时时间(ms)" },
                { "retry_wait_ms", "重试等待(ms)" },
                { "poll_interval_ms", "轮询间隔(ms)" },
                { "command_interval_ms", "命令间隔(ms)" },
                { "max_consecutive_failures", "断线检测阈值" },
                { "di_func", "DI功能码" },
                { "di_addr", "DI起始地址" },
                { "di_num", "DI通道数" },
                { "di_area", "DI区域" },
                { "di_db_number", "DI-DB块号" },
                { "di_start", "DI起始地址" },
                { "di_bytes", "DI字节数" },
                { "do_func", "DO功能码" },
                { "do_addr", "DO起始地址" },
                { "do_mode", "DO输出模式" },
                { "do_area", "DO区域" },
                { "do_db_number", "DO-DB块号" },
                { "do_start", "DO起始地址" },
                { "do_bytes", "DO字节数" },
                { "ai_func", "AI功能码" },
                { "ai_channel_*", "AI通道配置" },
                { "rack", "机架号" },
                { "slot", "槽位号" }
            };

            return labelMap.TryGetValue(key, out var label) ? label : key;
        }

        private DeviceSchema CreateDefaultSchema(string dllName)
        {
            return new DeviceSchema
            {
                DllName = dllName.ToUpper(),
                DisplayName = $"{dllName.ToUpper()} 设备",
                Version = "1.0",
                Sections = new List<SchemaSection>()
            };
        }
    }

    #region Schema Model Classes

    /// <summary>
    /// 设备配置Schema定义
    /// </summary>
    public class DeviceSchema
    {
        public string DllName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Version { get; set; } = "1.0";
        public List<SchemaSection> Sections { get; set; } = new();

        /// <summary>
        /// 预设配置列表
        /// </summary>
        public List<PresetConfig>? Presets { get; set; }

        /// <summary>
        /// 是否禁用此设备（禁用后不显示在外部设备列表中）
        /// </summary>
        public bool Disabled { get; set; } = false;

        /// <summary>
        /// 设备分类（如 "PLC", "传感器", "执行器", "通讯设备"）
        /// </summary>
        public string? Category { get; set; }

        /// <summary>
        /// 是否为常用设备（星标显示）
        /// </summary>
        public bool Starred { get; set; } = false;
    }

    /// <summary>
    /// 预设配置
    /// </summary>
    public class PresetConfig
    {
        /// <summary>
        /// 预设名称（如 "信捷 PLC"）
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 预设描述
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 预设分组（如 "PLC", "传感器", "执行器"）
        /// </summary>
        public string? Group { get; set; }

        /// <summary>
        /// 预设的配置值
        /// </summary>
        public Dictionary<string, string> Values { get; set; } = new();

        /// <summary>
        /// 应用此预设时需要隐藏的section名称列表，这些section的值会被清空
        /// </summary>
        public List<string>? HideSections { get; set; }
    }

    /// <summary>
    /// 配置分组
    /// </summary>
    public class SchemaSection
    {
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public bool Collapsed { get; set; } = false;
        public Dictionary<string, string>? VisibleWhen { get; set; }
        public List<SchemaField> Fields { get; set; } = new();

        /// <summary>
        /// INI文件中的Section名称（如"InputMapping"、"OutputMapping"），
        /// 为空则使用默认的"default"或"device_N" section
        /// </summary>
        public string? IniSection { get; set; }
    }

    /// <summary>
    /// 配置字段定义
    /// </summary>
    public class SchemaField
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";

        /// <summary>
        /// 字段类型: text, number, select, hex, boolean, uri, channel_list, array
        /// </summary>
        public string Type { get; set; } = "text";
        public string? Default { get; set; }
        public string? Description { get; set; }
        public string? Placeholder { get; set; }
        public bool Required { get; set; } = false;
        public double? Min { get; set; }
        public double? Max { get; set; }
        public List<SchemaOption>? Options { get; set; }
        public Dictionary<string, string>? VisibleWhen { get; set; }

        /// <summary>
        /// 数组类型的键前缀（如 ai_channel，实际键为 ai_channel, ai_channel_1, ai_channel_2 等）
        /// </summary>
        public string? ArrayKeyPrefix { get; set; }

        /// <summary>
        /// 数组元素的格式说明
        /// </summary>
        public string? ArrayItemFormat { get; set; }

        /// <summary>
        /// 是否在配置摘要中显示此字段
        /// </summary>
        public bool ShowInSummary { get; set; } = false;

        /// <summary>
        /// 在摘要中显示时的颜色（如 #10b981）
        /// </summary>
        public string? SummaryColor { get; set; }
    }

    /// <summary>
    /// 下拉选项
    /// </summary>
    public class SchemaOption
    {
        public string Value { get; set; } = "";
        public string Label { get; set; } = "";
    }

    /// <summary>
    /// 配置摘要项
    /// </summary>
    public class ConfigSummaryItem
    {
        public string Label { get; set; }
        public string Value { get; set; }
        public string? Color { get; set; }

        public ConfigSummaryItem(string label, string value, string? color = null)
        {
            Label = label;
            Value = value;
            Color = color;
        }
    }

    #endregion
}
