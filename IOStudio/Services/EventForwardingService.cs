using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Xml.Serialization;
using IOStudio.Models;
using IOStudio.Services.Senders;
using IOToolkit;

namespace IOStudio.Services
{
    /// <summary>
    /// 事件转发服务，负责协调事件绑定和转发
    /// 职责：加载配置、绑定设备事件、协调各协议发送器
    /// </summary>
    public class EventForwardingService
    {
        private static EventForwardingService? _instance;
        public static EventForwardingService Instance => _instance ??= new EventForwardingService();

        // 配置
        private List<ProtocolGroupDto>? _config;

        // 组件
        private readonly ConnectionManager _connectionManager;
        private readonly AnalogValueProcessor _analogProcessor;

        // 协议发送器
        private readonly Dictionary<string, IProtocolSender> _senders = new();

        // 自定义协议子发送器
        private readonly Dictionary<string, IProtocolSender> _customSenders = new();

        // DirectOutput 模拟量值去重
        private readonly ConcurrentDictionary<string, float> _lastDirectOutputValues = new();

        private EventForwardingService()
        {
            _connectionManager = new ConnectionManager();
            _analogProcessor = new AnalogValueProcessor();
            InitializeSenders();
        }

        private void InitializeSenders()
        {
            // 主协议发送器
            _senders["NetIO"] = new NetIOSender(_connectionManager);
            _senders["Modbus-RTU"] = new ModbusSender(_connectionManager);

            // 自定义协议子发送器
            _customSenders["TCP-Client"] = new TcpClientSender(_connectionManager);
            _customSenders["TCP-Server"] = new TcpServerSender(_connectionManager);
            _customSenders["UDP"] = new UdpSender(_connectionManager);
            _customSenders["Serial"] = new SerialSender(_connectionManager);
        }

        /// <summary>
        /// 根据协议类型获取发送器
        /// </summary>
        private IProtocolSender? GetSender(ProtocolGroupDto group)
        {
            if (group.ProtocolType == "Custom" && group.IsCustomMode)
            {
                return _customSenders.TryGetValue(group.CustomProtocolType, out var sender)
                    ? sender
                    : null;
            }

            return _senders.TryGetValue(group.ProtocolType, out var s) ? s : null;
        }

        /// <summary>
        /// 加载配置文件
        /// </summary>
        public void LoadConfig(string configPath)
        {
            _connectionManager.CleanupAll();

            try
            {
                if (!File.Exists(configPath))
                {
                    Debug.WriteLine($"Config file not found: {configPath}");
                    return;
                }

                var serializer = new XmlSerializer(typeof(EventForwardConfigDto));
                using var reader = new StreamReader(configPath);
                var root = (EventForwardConfigDto?)serializer.Deserialize(reader);
                _config = root?.ProtocolGroups;
                Debug.WriteLine($"Loaded {_config?.Count ?? 0} protocol groups.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load forward config: {ex.Message}");
            }
        }

        /// <summary>
        /// 启动事件转发
        /// </summary>
        public void Start()
        {
            if (_config == null)
                return;

            // 初始化模拟量处理器
            _analogProcessor.Initialize(
                _config,
                protocolType =>
                {
                    // 根据协议类型返回发送器（用于模拟量）
                    if (protocolType == "Custom")
                        return null; // Custom 需要特殊处理
                    return _senders.TryGetValue(protocolType, out var s) ? s : null;
                }
            );

            foreach (var group in _config)
            {
                BindGroup(group);
            }

            // 启动模拟量轮询，使用可配置的刷新率
            var analogInterval = IOStudioSettings.Instance.AnalogProcessorIntervalMs;
            _analogProcessor.StartPolling(analogInterval);
        }

        /// <summary>
        /// 绑定协议组的所有映射
        /// </summary>
        private void BindGroup(ProtocolGroupDto group)
        {
            if (group.Mappings == null || string.IsNullOrEmpty(group.SourceDevice))
                return;

            var ioDev = IODeviceController.GetIODevice(group.SourceDevice);
            if (ioDev == null)
            {
                Debug.WriteLine($"Device not found: {group.SourceDevice}");
                return;
            }

            // 设备直出模式
            IODevice? targetDev = null;
            if (group.ProtocolType == "DirectOutput" && !string.IsNullOrEmpty(group.TargetDevice))
            {
                targetDev = IODeviceController.GetIODevice(group.TargetDevice);
                if (targetDev == null)
                {
                    Debug.WriteLine($"Target device not found: {group.TargetDevice}");
                    return;
                }
            }

            foreach (var mapping in group.Mappings)
            {
                if (!mapping.IsEnabled)
                    continue;

                try
                {
                    if (group.ProtocolType == "DirectOutput" && targetDev != null)
                    {
                        BindDirectOutput(ioDev, targetDev, group, mapping);
                    }
                    else
                    {
                        BindProtocolForwarding(ioDev, group, mapping);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error binding key {mapping.SourceKey}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 绑定设备直出模式
        /// </summary>
        private void BindDirectOutput(
            IODevice sourceDev,
            IODevice targetDev,
            ProtocolGroupDto group,
            MappingDto mapping
        )
        {
            var outputKey = ConvertToOutputKey(mapping.TargetKey);

            if (mapping.MappingType == MappingType.Digital)
            {
                // 数字量
                sourceDev.BindKey(
                    mapping.SourceKey,
                    InputEvent.IE_Pressed,
                    () => targetDev.SetDO(outputKey, 1)
                );
                sourceDev.BindKey(
                    mapping.SourceKey,
                    InputEvent.IE_Released,
                    () => targetDev.SetDO(outputKey, 0)
                );

                Debug.WriteLine(
                    $"[DirectOutput] Bound Digital {group.SourceDevice}.{mapping.SourceKey} -> {group.TargetDevice}.{outputKey}"
                );
            }
            else
            {
                // 模拟量（带去重）
                var deduplicationKey = $"{group.Id}:{outputKey}";
                var capturedTargetDev = targetDev;

                sourceDev.BindAxisKey(
                    mapping.SourceKey,
                    value =>
                    {
                        if (
                            !_lastDirectOutputValues.TryGetValue(
                                deduplicationKey,
                                out var lastValue
                            )
                            || Math.Abs(lastValue - value) > float.Epsilon
                        )
                        {
                            _lastDirectOutputValues[deduplicationKey] = value;
                            capturedTargetDev.SetDO(outputKey, value);
                        }
                    }
                );

                Debug.WriteLine(
                    $"[DirectOutput] Bound Analog {group.SourceDevice}.{mapping.SourceKey} -> {group.TargetDevice}.{outputKey}"
                );
            }
        }

        /// <summary>
        /// 绑定协议转发模式
        /// </summary>
        private void BindProtocolForwarding(
            IODevice ioDev,
            ProtocolGroupDto group,
            MappingDto mapping
        )
        {
            if (mapping.MappingType == MappingType.Digital)
            {
                // 数字量
                var sender = GetSender(group);
                if (sender == null)
                {
                    Debug.WriteLine($"No sender found for protocol: {group.ProtocolType}");
                    return;
                }

                ioDev.BindKey(
                    mapping.SourceKey,
                    InputEvent.IE_Pressed,
                    () => sender.SendDigital(group, mapping, "Pressed")
                );
                ioDev.BindKey(
                    mapping.SourceKey,
                    InputEvent.IE_Released,
                    () => sender.SendDigital(group, mapping, "Released")
                );

                Debug.WriteLine(
                    $"Bound Digital {group.SourceDevice}.{mapping.SourceKey} -> {mapping.TargetKey}"
                );
            }
            else
            {
                // 模拟量
                var groupId = group.Id;
                var targetKey = mapping.TargetKey;
                _analogProcessor.MarkHasAnalogMappings();

                ioDev.BindAxisKey(
                    mapping.SourceKey,
                    value =>
                    {
                        _analogProcessor.UpdateValue(groupId, targetKey, value);
                    }
                );

                Debug.WriteLine(
                    $"Bound Analog {group.SourceDevice}.{mapping.SourceKey} -> {mapping.TargetKey}"
                );
            }
        }

        /// <summary>
        /// 将输入Key转换为输出Key格式
        /// 规则：将 xx_yy 格式转换为 OAxis_yy
        /// </summary>
        private static Key ConvertToOutputKey(string inputKey)
        {
            if (string.IsNullOrEmpty(inputKey))
                return inputKey;

            int underscoreIndex = inputKey.IndexOf('_');
            if (underscoreIndex > 0 && underscoreIndex < inputKey.Length - 1)
            {
                string suffix = inputKey.Substring(underscoreIndex + 1);
                return $"OAxis_{suffix}";
            }

            return inputKey;
        }

        /// <summary>
        /// 停止事件转发
        /// </summary>
        public void Stop()
        {
            _analogProcessor.Clear();
            _lastDirectOutputValues.Clear();
            _connectionManager.CleanupAll();
        }
    }
}
