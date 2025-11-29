using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Xml.Serialization;
using IOTester.Models;
using IOToolkit;

namespace IOTester.Services
{
    public class EventForwardingService
    {
        private static EventForwardingService? _instance;
        public static EventForwardingService Instance => _instance ??= new EventForwardingService();

        private List<ProtocolGroupDto>? _config;
        private readonly Dictionary<string, UdpClient> _udpClients =
            new Dictionary<string, UdpClient>();

        public void LoadConfig(string configPath)
        {
            lock (_udpClients)
            {
                // 清理旧的 UDP 客户端
                foreach (var client in _udpClients.Values)
                {
                    client.Close();
                    client.Dispose();
                }
                _udpClients.Clear();
            }

            try
            {
                if (!File.Exists(configPath))
                {
                    Debug.WriteLine($"Config file not found: {configPath}");
                    return;
                }

                var serializer = new XmlSerializer(typeof(EventForwardConfigDto));
                using (var reader = new StreamReader(configPath))
                {
                    var root = (EventForwardConfigDto?)serializer.Deserialize(reader);
                    _config = root?.ProtocolGroups;
                    Debug.WriteLine($"Loaded {_config?.Count ?? 0} protocol groups.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load forward config: {ex.Message}");
            }
        }

        public void Start()
        {
            if (_config == null)
                return;

            foreach (var group in _config)
            {
                if (group.Mappings == null)
                    continue;

                foreach (var mapping in group.Mappings)
                {
                    if (!mapping.IsEnabled)
                        continue;
                    if (string.IsNullOrEmpty(mapping.SourceDevice))
                        continue;

                    try
                    {
                        var ioDev = IODeviceController.GetIODevice(mapping.SourceDevice);
                        if (ioDev == null)
                        {
                            Debug.WriteLine($"Device not found: {mapping.SourceDevice}");
                            continue;
                        }

                        // Bind Pressed Event
                        ioDev.BindKey(
                            mapping.SourceKey,
                            InputEvent.IE_Pressed,
                            () => HandleForwarding(group, mapping, "Pressed")
                        );

                        // Bind Released Event
                        ioDev.BindKey(
                            mapping.SourceKey,
                            InputEvent.IE_Released,
                            () => HandleForwarding(group, mapping, "Released")
                        );

                        Debug.WriteLine(
                            $"Bound {mapping.SourceDevice}.{mapping.SourceKey} -> {mapping.TargetKey}"
                        );
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error binding key {mapping.SourceKey}: {ex.Message}");
                    }
                }
            }
        }

        private void HandleForwarding(ProtocolGroupDto group, MappingDto mapping, string eventType)
        {
            try
            {
                if (group.ProtocolType == "NetIO")
                {
                    SendNetIO(group, mapping, eventType);
                }
                else if (group.ProtocolType == "Modbus-RTU")
                {
                    SendModbus(group, mapping, eventType);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Forwarding error: {ex.Message}");
            }
        }

        private void SendNetIO(ProtocolGroupDto group, MappingDto mapping, string eventType)
        {
            try
            {
                // 解析通道号 (支持 Button_xx, Axis_xxx 等格式，提取 _ 后面的数字并转为整数)
                string channel = mapping.TargetKey ?? "";
                var match = Regex.Match(channel, @"_(\d+)$");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int channelId))
                {
                    channel = channelId.ToString();
                }

                // 确定值 (Pressed -> "1", Released -> "0")
                string value = eventType == "Pressed" ? "1" : "0";

                // 构建JSON: {"evt":"SetDI","data":{"通道号":"值"}}
                // 使用 Dictionary 构建 data 对象
                var dataDict = new Dictionary<string, string> { { channel, value } };

                var payload = new { evt = "SetDI", data = dataDict };

                string jsonMessage = JsonSerializer.Serialize(payload);
                var data = Encoding.UTF8.GetBytes(jsonMessage);

                string key = $"{group.TargetIP}:{group.TargetPort}";
                UdpClient? udpClient;

                lock (_udpClients)
                {
                    if (!_udpClients.TryGetValue(key, out udpClient))
                    {
                        udpClient = new UdpClient();
                        _udpClients[key] = udpClient;
                    }
                }

                lock (udpClient)
                {
                    udpClient.Send(data, data.Length, group.TargetIP, group.TargetPort);
                }

                Debug.WriteLine(
                    $"[NetIO] Sent '{jsonMessage}' to {group.TargetIP}:{group.TargetPort}"
                );
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NetIO] Error sending: {ex.Message}");
            }
        }

        private void SendModbus(ProtocolGroupDto group, MappingDto mapping, string eventType)
        {
            // Placeholder for Modbus implementation
            Debug.WriteLine($"[Modbus] {mapping.TargetKey} {eventType} via {group.SerialPort}");
        }
    }
}
