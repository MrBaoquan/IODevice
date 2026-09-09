using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace IOStudio.Services
{
    /// <summary>
    /// 外部设备信息
    /// </summary>
    public class ExternalDeviceInfo
    {
        /// <summary>
        /// DLL名称（不含前缀和扩展名，如 MODBUS）
        /// </summary>
        public string DllName { get; set; } = string.Empty;

        /// <summary>
        /// 显示名称（如 "Modbus 串口设备"）
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// DLL完整路径
        /// </summary>
        public string DllPath { get; set; } = string.Empty;

        /// <summary>
        /// 是否有配置文件（INI）
        /// </summary>
        public bool HasConfig { get; set; }

        /// <summary>
        /// 是否有配置模式定义（Schema）
        /// </summary>
        public bool HasSchema { get; set; }

        /// <summary>
        /// 是否有文档（README.html）
        /// </summary>
        public bool HasDoc { get; set; }

        /// <summary>
        /// 配置文件夹路径
        /// </summary>
        public string? ConfigFolderPath { get; set; }

        /// <summary>
        /// 设备分类
        /// </summary>
        public string? Category { get; set; }

        /// <summary>
        /// 是否被禁用
        /// </summary>
        public bool Disabled { get; set; }

        /// <summary>
        /// 是否为常用设备（星标）
        /// </summary>
        public bool Starred { get; set; }
    }

    /// <summary>
    /// 外部设备服务 - 扫描和管理外部设备库
    /// 设备显示名称从 Schema 文件的 displayName 字段获取
    /// </summary>
    public class ExternalDeviceService
    {
        private static ExternalDeviceService? _instance;
        public static ExternalDeviceService Instance => _instance ??= new ExternalDeviceService();

        private readonly string _externalLibPath;
        private readonly string _configPath;
        private readonly string _schemaPath;
        private List<ExternalDeviceInfo> _devices = new();

        /// <summary>
        /// 获取可用的外部设备列表
        /// </summary>
        public IReadOnlyList<ExternalDeviceInfo> AvailableDevices => _devices.AsReadOnly();

        private ExternalDeviceService()
        {
            // 获取应用程序运行目录
            var appPath = AppDomain.CurrentDomain.BaseDirectory;
            _externalLibPath = Path.Combine(appPath, "ExternalLibraries");
            _configPath = Path.Combine(_externalLibPath, "Config");
            _schemaPath = Path.Combine(appPath, "Config", "Schemas");

            // 确保 Schema 目录存在
            if (!Directory.Exists(_schemaPath))
            {
                Directory.CreateDirectory(_schemaPath);
            }

            // 初始加载设备列表
            LoadDevices();
        }

        /// <summary>
        /// 加载设备列表
        /// </summary>
        public void LoadDevices()
        {
            _devices.Clear();
            ScanExternalLibraries();
        }

        /// <summary>
        /// 刷新设备列表
        /// </summary>
        public void RefreshDevices()
        {
            LoadDevices();
        }

        /// <summary>
        /// 获取可用的外部设备列表
        /// </summary>
        public List<ExternalDeviceInfo> GetAvailableDevices()
        {
            return _devices.ToList();
        }

        /// <summary>
        /// 根据 DLL 名称获取设备信息
        /// </summary>
        public ExternalDeviceInfo? GetDeviceInfo(string dllName)
        {
            return _devices.FirstOrDefault(
                d => d.DllName.Equals(dllName, StringComparison.OrdinalIgnoreCase)
            );
        }

        /// <summary>
        /// 获取设备的文档路径
        /// </summary>
        public string? GetDocumentPath(string dllName)
        {
            var device = GetDeviceInfo(dllName);
            if (device == null || !device.HasDoc || string.IsNullOrEmpty(device.ConfigFolderPath))
                return null;

            var docPath = Path.Combine(device.ConfigFolderPath, "README.html");
            return File.Exists(docPath) ? docPath : null;
        }

        /// <summary>
        /// 扫描外部设备库目录，查找所有可用的外部设备
        /// </summary>
        private void ScanExternalLibraries()
        {
            if (!Directory.Exists(_externalLibPath))
            {
                Console.WriteLine($"ExternalLibraries 目录不存在: {_externalLibPath}");
                return;
            }

            // 查找所有 IOUI-Win64-*.dll 文件
            var dllPattern = "IOUI-Win64-*.dll";
            var dllFiles = Directory.GetFiles(_externalLibPath, dllPattern);

            foreach (var dllFile in dllFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(dllFile);
                // 从 IOUI-Win64-XXXX 中提取设备名称
                var prefix = "IOUI-Win64-";
                if (fileName.StartsWith(prefix))
                {
                    var dllName = fileName.Substring(prefix.Length);

                    // 检查配置文件夹
                    var configFolder = Path.Combine(_configPath, dllName);
                    var hasConfig =
                        Directory.Exists(configFolder)
                        && Directory.GetFiles(configFolder, "*.ini").Any();

                    // 检查文档
                    var hasDoc =
                        Directory.Exists(configFolder)
                        && File.Exists(Path.Combine(configFolder, "README.html"));

                    // 检查或创建 Schema
                    var schemaFile = Path.Combine(
                        _schemaPath,
                        $"{dllName.ToLowerInvariant()}.schema.json"
                    );
                    var hasSchema = File.Exists(schemaFile);

                    // 如果没有 Schema，尝试从 INI 创建
                    if (!hasSchema && hasConfig)
                    {
                        try
                        {
                            var tempSchema = DeviceSchemaService.Instance.GenerateSchemaFromIni(
                                dllName
                            );
                            if (tempSchema != null)
                            {
                                // 设置显示名称
                                tempSchema.DisplayName = GenerateDisplayName(dllName);
                                DeviceSchemaService.Instance.SaveSchema(dllName, tempSchema);
                                hasSchema = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"为 {dllName} 创建 Schema 失败: {ex.Message}");
                        }
                    }

                    // 获取 Schema 信息
                    var deviceSchema = DeviceSchemaService.Instance.LoadSchema(dllName);

                    // 如果设备被禁用，跳过
                    if (deviceSchema?.Disabled == true)
                    {
                        continue;
                    }

                    // 获取显示名称（优先从 Schema 读取）
                    var displayName = deviceSchema?.DisplayName ?? GenerateDisplayName(dllName);

                    var deviceInfo = new ExternalDeviceInfo
                    {
                        DllName = dllName,
                        DisplayName = displayName,
                        DllPath = dllFile,
                        HasConfig = hasConfig,
                        HasSchema = hasSchema,
                        HasDoc = hasDoc,
                        ConfigFolderPath = Directory.Exists(configFolder) ? configFolder : null,
                        Category = deviceSchema?.Category,
                        Disabled = deviceSchema?.Disabled ?? false,
                        Starred = deviceSchema?.Starred ?? false
                    };

                    _devices.Add(deviceInfo);
                }
            }

            // 按显示名称排序
            _devices = _devices.OrderBy(d => d.DisplayName).ToList();
        }

        /// <summary>
        /// 从 Schema 文件获取显示名称
        /// </summary>
        private string? GetDisplayNameFromSchema(string dllName)
        {
            try
            {
                var schema = DeviceSchemaService.Instance.LoadSchema(dllName);
                return schema?.DisplayName;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 根据 DLL 名称生成友好的显示名称
        /// </summary>
        private string GenerateDisplayName(string dllName)
        {
            // 常见设备的友好名称映射
            var nameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Modbus 系列
                { "MODBUS", "Modbus 串口设备" },
                { "MODBUS_TCP", "Modbus TCP 设备" },
                { "MODBUS_UDP", "Modbus UDP 设备" },
                { "MODBUS_RTU", "Modbus RTU 设备" },
                // 西门子 PLC 系列
                { "SNAP7", "西门子 S7 PLC" },
                { "SNAP7_S1200", "西门子 S7-1200" },
                { "SNAP7_S1500", "西门子 S7-1500" },
                { "SNAP7_S200SMART", "西门子 S7-200 Smart" },
                // IO Hub
                { "IOHUB", "IO Hub 集成设备" },
                { "IOHUB_NET", "IO Hub 网络设备" },
                // 输入设备
                { "KEYBOARD", "键盘输入" },
                { "MOUSE", "鼠标输入" },
                { "JOYSTICK", "操纵杆" },
                { "GAMEPAD", "游戏手柄" },
                { "HID", "HID 设备" },
                // 串口设备
                { "SERIAL", "串口设备" },
                { "RS232", "RS232 串口" },
                { "RS485", "RS485 总线" },
                { "RS422", "RS422 接口" },
                // 网络协议
                { "TCP", "TCP 客户端" },
                { "TCPSERVER", "TCP 服务器" },
                { "UDP", "UDP 通信" },
                { "HTTP", "HTTP 客户端" },
                { "WEBSOCKET", "WebSocket" },
                { "MQTT", "MQTT 客户端" },
                { "OPC", "OPC 客户端" },
                { "OPCUA", "OPC UA 客户端" },
                { "OPCDA", "OPC DA 客户端" },
                // CAN 总线
                { "CAN", "CAN 总线" },
                { "CANOPEN", "CANopen 协议" },
                { "PCAN", "PEAK CAN" },
                { "KVASER", "Kvaser CAN" },
                { "ZLGCAN", "ZLG CAN" },
                { "USBCAN", "USB CAN" },
                // EtherCAT
                { "ECAT", "EtherCAT 主站" },
                { "ETHERCAT", "EtherCAT 设备" },
                { "SOEM", "SOEM EtherCAT" },
                // 运动控制
                { "MOTION", "运动控制器" },
                { "DMC", "运动控制卡" },
                { "AXIS", "轴控制" },
                { "SERVO", "伺服驱动" },
                { "STEPPER", "步进电机" },
                // 编码器
                { "ENCODER", "编码器" },
                { "ABSOLUTE_ENCODER", "绝对值编码器" },
                { "INCREMENTAL_ENCODER", "增量编码器" },
                // PCI 板卡
                { "PCI", "PCI 板卡" },
                { "PCIE", "PCIe 板卡" },
                { "PCI1716", "研华 PCI-1716" },
                { "PCI1710", "研华 PCI-1710" },
                { "PCI1713", "研华 PCI-1713" },
                { "PCI1730", "研华 PCI-1730" },
                { "PCI1750", "研华 PCI-1750" },
                { "PCI1751", "研华 PCI-1751" },
                { "PCI1752", "研华 PCI-1752" },
                { "PCI1753", "研华 PCI-1753" },
                { "PCI1756", "研华 PCI-1756" },
                { "PCI1760", "研华 PCI-1760" },
                { "PCI1784", "研华 PCI-1784" },
                // 研华系列
                { "ADVANTECH", "研华采集卡" },
                { "ADAM", "研华 ADAM 模块" },
                { "ADAM4000", "研华 ADAM-4000" },
                { "ADAM5000", "研华 ADAM-5000" },
                { "ADAM6000", "研华 ADAM-6000" },
                // 凌华系列
                { "ADLINK", "凌华采集卡" },
                { "DAQ2000", "凌华 DAQ-2000" },
                { "DAQ2206", "凌华 DAQ-2206" },
                // 国产控制器
                { "CCBOX", "CC-Box 控制盒" },
                { "QCBOX", "QC-Box 控制盒" },
                { "LCBOX", "LC-Box 控制盒" },
                // 传感器
                { "SENSOR", "传感器" },
                { "LOADCELL", "称重传感器" },
                { "TEMPERATURE", "温度传感器" },
                { "PRESSURE", "压力传感器" },
                { "FORCE", "力传感器" },
                { "TORQUE", "扭矩传感器" },
                { "DISPLACEMENT", "位移传感器" },
                { "LVDT", "LVDT 位移传感器" },
                // 显示与输出
                { "LED", "LED 控制" },
                { "DISPLAY", "显示屏" },
                { "INDICATOR", "指示灯" },
                { "BUZZER", "蜂鸣器" },
                // IO 模块
                { "DIO", "数字 IO 模块" },
                { "AIO", "模拟 IO 模块" },
                { "RELAY", "继电器模块" },
                { "COUNTER", "计数器模块" },
                { "PWM", "PWM 控制" },
                // 视觉系统
                { "CAMERA", "相机" },
                { "VISION", "视觉系统" },
                { "BASLER", "Basler 相机" },
                { "HIKVISION", "海康相机" },
                // 其他
                { "VIRTUAL", "虚拟设备" },
                { "SIMULATOR", "模拟器" },
                { "TEST", "测试设备" },
                { "DEBUG", "调试设备" },
                { "DEMO", "演示设备" },
                { "SAMPLE", "示例设备" },
                { "CUSTOM", "自定义设备" },
                { "EXTERNAL", "外部设备" },
            };

            if (nameMap.TryGetValue(dllName, out var displayName))
            {
                return displayName;
            }

            // 如果没有预定义名称，尝试美化 DLL 名称
            return BeautifyDllName(dllName);
        }

        /// <summary>
        /// 美化 DLL 名称为可读格式
        /// </summary>
        private string BeautifyDllName(string dllName)
        {
            if (string.IsNullOrEmpty(dllName))
                return "未知设备";

            // 将下划线替换为空格
            var result = dllName.Replace("_", " ");

            // 处理驼峰命名和全大写
            var chars = new List<char>();
            for (int i = 0; i < result.Length; i++)
            {
                var c = result[i];
                if (i > 0 && char.IsUpper(c) && char.IsLower(result[i - 1]))
                {
                    chars.Add(' ');
                }
                chars.Add(c);
            }

            result = new string(chars.ToArray());

            // 首字母大写，其余小写（对于全大写的情况）
            if (result.All(c => !char.IsLetter(c) || char.IsUpper(c)))
            {
                result = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
                    result.ToLower()
                );
            }

            return result;
        }

        /// <summary>
        /// 更新设备的显示名称（更新到 Schema）
        /// </summary>
        public void UpdateDisplayName(string dllName, string newDisplayName)
        {
            var device = GetDeviceInfo(dllName);
            if (device != null)
            {
                device.DisplayName = newDisplayName;

                // 同时更新 Schema 中的显示名称
                try
                {
                    var schema = DeviceSchemaService.Instance.LoadSchema(dllName);
                    if (schema != null)
                    {
                        schema.DisplayName = newDisplayName;
                        DeviceSchemaService.Instance.SaveSchema(dllName, schema);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"更新 Schema 显示名称失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 为设备创建 Schema（如果不存在）
        /// </summary>
        public bool EnsureSchema(string dllName)
        {
            var device = GetDeviceInfo(dllName);
            if (device == null)
                return false;

            if (device.HasSchema)
                return true;

            try
            {
                var schema = DeviceSchemaService.Instance.GenerateSchemaFromIni(dllName);
                if (schema != null)
                {
                    schema.DisplayName = device.DisplayName;
                    DeviceSchemaService.Instance.SaveSchema(dllName, schema);
                    device.HasSchema = true;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"创建 Schema 失败: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// 同步设备的 Schema（从 INI 更新）
        /// </summary>
        public bool SyncSchema(string dllName)
        {
            var device = GetDeviceInfo(dllName);
            if (device == null || !device.HasConfig)
                return false;

            try
            {
                var schema = DeviceSchemaService.Instance.SyncFromIni(dllName);
                device.HasSchema = schema != null;
                return device.HasSchema;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"同步 Schema 失败: {ex.Message}");
                return false;
            }
        }
    }
}
