using System.Diagnostics;
using IOTester.Models;

namespace IOTester.Services.Senders
{
    /// <summary>
    /// TCP 服务器发送器，向所有连接的客户端广播数据
    /// </summary>
    public class TcpServerSender : CustomProtocolSenderBase
    {
        public TcpServerSender(ConnectionManager connectionManager)
            : base(connectionManager) { }

        protected override void SendData(ProtocolGroupDto group, byte[] data)
        {
            string key = $"tcp-server:{group.TargetPort}";

            if (ConnectionManager.EnsureTcpServerStarted(key, group.TargetPort))
            {
                ConnectionManager.BroadcastToTcpClients(key, data);
                Debug.WriteLine(
                    $"[TCP-Server] Broadcast {data.Length} bytes on port {group.TargetPort}"
                );
            }
        }
    }
}
