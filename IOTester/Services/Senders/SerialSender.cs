using System.Diagnostics;
using IOTester.Models;

namespace IOTester.Services.Senders
{
    /// <summary>
    /// 串口发送器
    /// </summary>
    public class SerialSender : CustomProtocolSenderBase
    {
        public SerialSender(ConnectionManager connectionManager)
            : base(connectionManager) { }

        protected override void SendData(ProtocolGroupDto group, byte[] data)
        {
            string key = $"serial:{group.SerialPort}";
            var parity = ConnectionManager.ParseParity(group.Parity);
            var stopBits = ConnectionManager.ParseStopBits(group.StopBits);

            if (
                ConnectionManager.SendViaSerial(
                    key,
                    group.SerialPort,
                    group.BaudRate,
                    group.DataBits,
                    parity,
                    stopBits,
                    data
                )
            )
            {
                Debug.WriteLine($"[Serial] Sent {data.Length} bytes to {group.SerialPort}");
            }
        }
    }
}
