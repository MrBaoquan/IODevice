using System.Diagnostics;
using IOStudio.Models;

namespace IOStudio.Services.Senders
{
    /// <summary>
    /// TCP 客户端发送器
    /// </summary>
    public class TcpClientSender : CustomProtocolSenderBase
    {
        public TcpClientSender(ConnectionManager connectionManager)
            : base(connectionManager) { }

        protected override void SendData(ProtocolGroupDto group, byte[] data)
        {
            string key = $"tcp-client:{group.TargetIP}:{group.TargetPort}";
            if (ConnectionManager.SendViaTcpClient(key, group.TargetIP, group.TargetPort, data))
            {
                Debug.WriteLine(
                    $"[TCP-Client] Sent {data.Length} bytes to {group.TargetIP}:{group.TargetPort}"
                );
            }
        }
    }
}
