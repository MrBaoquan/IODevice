using System.Collections.Generic;
using System.Diagnostics;
using IOTester.Models;

namespace IOTester.Services.Senders
{
    /// <summary>
    /// Modbus-RTU 协议发送器（占位实现）
    /// </summary>
    public class ModbusSender : IProtocolSender
    {
        private readonly ConnectionManager _connectionManager;
        private bool _disposed;

        public ModbusSender(ConnectionManager connectionManager)
        {
            _connectionManager = connectionManager;
        }

        public void SendDigital(ProtocolGroupDto group, MappingDto mapping, string eventType)
        {
            // TODO: 实现 Modbus-RTU 数字量发送
            Debug.WriteLine($"[Modbus] {mapping.TargetKey} {eventType} via {group.SerialPort}");
        }

        public void SendAnalog(ProtocolGroupDto group, Dictionary<string, float> values)
        {
            // TODO: 实现 Modbus-RTU 模拟量发送
            Debug.WriteLine(
                $"[Modbus-Analog] Sending {values.Count} analog values via {group.SerialPort}"
            );
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
        }
    }
}
