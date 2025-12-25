using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using IOTester.Models;

namespace IOTester.Services.Senders
{
    /// <summary>
    /// 自定义协议发送器基类，提供数据格式解析等公共功能
    /// </summary>
    public abstract class CustomProtocolSenderBase : IProtocolSender
    {
        protected readonly ConnectionManager ConnectionManager;
        protected bool Disposed;

        protected CustomProtocolSenderBase(ConnectionManager connectionManager)
        {
            ConnectionManager =
                connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        }

        /// <summary>
        /// 发送数字量事件
        /// </summary>
        public void SendDigital(ProtocolGroupDto group, MappingDto mapping, string eventType)
        {
            try
            {
                string rawData =
                    eventType == "Pressed" ? mapping.PressedData : mapping.ReleasedData;

                if (string.IsNullOrWhiteSpace(rawData))
                {
                    Debug.WriteLine(
                        $"[Custom] No data configured for {mapping.SourceKey} {eventType} event, skipping."
                    );
                    return;
                }

                byte[] data = ParseRawData(mapping.DataFormat, rawData);

                if (data == null || data.Length == 0)
                {
                    Debug.WriteLine(
                        $"[Custom] Failed to parse data for {mapping.SourceKey} {eventType}"
                    );
                    return;
                }

                SendData(group, data);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Custom] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送模拟量数据
        /// </summary>
        public void SendAnalog(ProtocolGroupDto group, Dictionary<string, float> values)
        {
            foreach (var kvp in values)
            {
                try
                {
                    int intValue = (int)kvp.Value;
                    string rawData = intValue.ToString();

                    var mapping = group.Mappings?.Find(m => m.TargetKey == kvp.Key);
                    if (mapping != null)
                    {
                        byte[] data = ParseRawData(mapping.DataFormat, rawData);
                        if (data != null && data.Length > 0)
                        {
                            SendData(group, data);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Custom-Analog] Error sending {kvp.Key}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 子类实现的具体发送方法
        /// </summary>
        protected abstract void SendData(ProtocolGroupDto group, byte[] data);

        /// <summary>
        /// 解析原始数据为字节数组
        /// </summary>
        protected static byte[] ParseRawData(string format, string rawData)
        {
            if (string.IsNullOrWhiteSpace(rawData))
                return Array.Empty<byte>();

            try
            {
                if (format == "HEX")
                {
                    string hex = rawData.Replace(" ", "").Replace("-", "").Replace("0x", "");
                    if (hex.Length % 2 != 0)
                    {
                        Debug.WriteLine($"[Custom] Invalid HEX format: {rawData}");
                        return Array.Empty<byte>();
                    }

                    byte[] bytes = new byte[hex.Length / 2];
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                    }
                    return bytes;
                }
                else // ASCII
                {
                    return Encoding.UTF8.GetBytes(rawData);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Custom] Failed to parse data: {ex.Message}");
                return Array.Empty<byte>();
            }
        }

        public virtual void Dispose()
        {
            if (Disposed)
                return;
            Disposed = true;
        }
    }
}
