using System.Diagnostics;
using IOTester.Models;

namespace IOTester.Services.Senders
{
    /// <summary>
    /// UDP 发送器
    /// </summary>
    public class UdpSender : CustomProtocolSenderBase
    {
        public UdpSender(ConnectionManager connectionManager)
            : base(connectionManager) { }

        protected override void SendData(ProtocolGroupDto group, byte[] data)
        {
            string key = $"udp:{group.TargetIP}:{group.TargetPort}";
            ConnectionManager.SendViaUdp(key, group.TargetIP, group.TargetPort, data);
            Debug.WriteLine(
                $"[UDP] Sent {data.Length} bytes to {group.TargetIP}:{group.TargetPort}"
            );
        }
    }
}
