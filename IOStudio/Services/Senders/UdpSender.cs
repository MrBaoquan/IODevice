using System.Diagnostics;
using IOStudio.Models;

namespace IOStudio.Services.Senders
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
