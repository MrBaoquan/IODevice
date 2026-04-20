using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IOStudio.Models;

namespace IOStudio.Services.Senders
{
    /// <summary>
    /// NetIO 协议发送器，通过 UDP 发送 JSON 格式数据
    /// </summary>
    public class NetIOSender : IProtocolSender
    {
        private readonly ConnectionManager _connectionManager;
        private bool _disposed;

        public NetIOSender(ConnectionManager connectionManager)
        {
            _connectionManager =
                connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        }

        /// <summary>
        /// 发送数字量事件
        /// 格式：{"evt":"SetDI","data":{"通道号":"值"}}
        /// </summary>
        public void SendDigital(ProtocolGroupDto group, MappingDto mapping, string eventType)
        {
            try
            {
                // 解析通道号 (支持 Button_xx, Axis_xxx 等格式)
                string channel = ExtractChannelNumber(mapping.TargetKey ?? "");
                string value = eventType == "Pressed" ? "1" : "0";

                var dataDict = new Dictionary<string, string> { { channel, value } };
                var payload = new { evt = "SetDI", data = dataDict };
                string jsonMessage = JsonSerializer.Serialize(payload);
                var data = Encoding.UTF8.GetBytes(jsonMessage);

                string key = $"netio:{group.TargetIP}:{group.TargetPort}";
                _connectionManager.SendViaUdp(key, group.TargetIP, group.TargetPort, data);

                Debug.WriteLine(
                    $"[NetIO] Sent '{jsonMessage}' to {group.TargetIP}:{group.TargetPort}"
                );
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NetIO] Error sending: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送模拟量数据
        /// 格式：{"evt":"SetAD","data":{"0":"1024","1":"2048"}}
        /// </summary>
        public void SendAnalog(ProtocolGroupDto group, Dictionary<string, float> values)
        {
            try
            {
                var dataDict = new Dictionary<string, string>();

                foreach (var kvp in values)
                {
                    string channel = ExtractChannelNumber(kvp.Key);
                    dataDict[channel] = ((int)kvp.Value).ToString();
                }

                var payload = new { evt = "SetAD", data = dataDict };
                string jsonMessage = JsonSerializer.Serialize(payload);
                var data = Encoding.UTF8.GetBytes(jsonMessage);

                string key = $"netio:{group.TargetIP}:{group.TargetPort}";
                _connectionManager.SendViaUdp(key, group.TargetIP, group.TargetPort, data);

                Debug.WriteLine(
                    $"[NetIO-Analog] Sent '{jsonMessage}' to {group.TargetIP}:{group.TargetPort}"
                );
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NetIO-Analog] Error sending: {ex.Message}");
            }
        }

        /// <summary>
        /// 从键名中提取通道号
        /// 例如：Button_01 -> 1, Axis_02 -> 2
        /// </summary>
        private static string ExtractChannelNumber(string key)
        {
            var match = Regex.Match(key, @"_(\d+)$");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int channelId))
            {
                return channelId.ToString();
            }
            return key;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            // ConnectionManager 由外部管理，这里不释放
        }
    }
}
