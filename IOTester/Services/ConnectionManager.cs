using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;

namespace IOTester.Services
{
    /// <summary>
    /// 连接管理器，负责管理所有网络和串口连接的生命周期
    /// </summary>
    public class ConnectionManager : IDisposable
    {
        private readonly Dictionary<string, UdpClient> _udpClients = new();
        private readonly Dictionary<string, TcpClient> _tcpClients = new();
        private readonly Dictionary<string, SerialPort> _serialPorts = new();
        private readonly Dictionary<string, TcpListener> _tcpServers = new();
        private readonly Dictionary<string, List<TcpClient>> _serverConnections = new();

        private readonly object _udpLock = new();
        private readonly object _tcpClientLock = new();
        private readonly object _serialLock = new();
        private readonly object _tcpServerLock = new();
        private readonly object _serverConnectionsLock = new();

        private bool _disposed;

        #region UDP 客户端管理

        /// <summary>
        /// 获取或创建 UDP 客户端
        /// </summary>
        public UdpClient GetOrCreateUdpClient(string key)
        {
            lock (_udpLock)
            {
                if (!_udpClients.TryGetValue(key, out var client))
                {
                    client = new UdpClient();
                    _udpClients[key] = client;
                    Debug.WriteLine($"[ConnectionManager] Created UDP client: {key}");
                }
                return client;
            }
        }

        /// <summary>
        /// 通过 UDP 发送数据
        /// </summary>
        public void SendViaUdp(string key, string targetIP, int targetPort, byte[] data)
        {
            var client = GetOrCreateUdpClient(key);
            lock (client)
            {
                client.Send(data, data.Length, targetIP, targetPort);
            }
        }

        #endregion

        #region TCP 客户端管理

        /// <summary>
        /// 获取或创建 TCP 客户端连接
        /// </summary>
        public TcpClient? GetOrCreateTcpClient(string key, string targetIP, int targetPort)
        {
            lock (_tcpClientLock)
            {
                if (!_tcpClients.TryGetValue(key, out var client) || !client.Connected)
                {
                    try
                    {
                        client?.Close();
                        client = new TcpClient();
                        client.Connect(targetIP, targetPort);
                        _tcpClients[key] = client;
                        Debug.WriteLine(
                            $"[ConnectionManager] TCP connected to {targetIP}:{targetPort}"
                        );
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ConnectionManager] TCP connection failed: {ex.Message}");
                        return null;
                    }
                }
                return client;
            }
        }

        /// <summary>
        /// 通过 TCP 客户端发送数据
        /// </summary>
        public bool SendViaTcpClient(string key, string targetIP, int targetPort, byte[] data)
        {
            var client = GetOrCreateTcpClient(key, targetIP, targetPort);
            if (client == null)
                return false;

            try
            {
                var stream = client.GetStream();
                stream.Write(data, 0, data.Length);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConnectionManager] TCP send failed: {ex.Message}");
                lock (_tcpClientLock)
                {
                    _tcpClients.Remove(key);
                    client?.Close();
                }
                return false;
            }
        }

        #endregion

        #region TCP 服务器管理

        /// <summary>
        /// 获取或创建 TCP 服务器
        /// </summary>
        public bool EnsureTcpServerStarted(string key, int port)
        {
            lock (_tcpServerLock)
            {
                if (_tcpServers.ContainsKey(key))
                    return true;

                try
                {
                    var listener = new TcpListener(IPAddress.Any, port);
                    listener.Start();
                    _tcpServers[key] = listener;

                    lock (_serverConnectionsLock)
                    {
                        _serverConnections[key] = new List<TcpClient>();
                    }

                    StartAcceptingConnections(listener, key);
                    Debug.WriteLine($"[ConnectionManager] TCP server listening on port {port}");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ConnectionManager] TCP server start failed: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// 广播数据到所有连接的客户端
        /// </summary>
        public void BroadcastToTcpClients(string key, byte[] data)
        {
            lock (_serverConnectionsLock)
            {
                if (!_serverConnections.TryGetValue(key, out var connections))
                    return;

                var disconnected = new List<TcpClient>();
                foreach (var client in connections)
                {
                    try
                    {
                        if (client.Connected)
                        {
                            var stream = client.GetStream();
                            stream.Write(data, 0, data.Length);
                        }
                        else
                        {
                            disconnected.Add(client);
                        }
                    }
                    catch
                    {
                        disconnected.Add(client);
                    }
                }

                // 清理断开的连接
                foreach (var client in disconnected)
                {
                    connections.Remove(client);
                    try
                    {
                        client.Close();
                    }
                    catch { }
                }

                Debug.WriteLine(
                    $"[ConnectionManager] Broadcast {data.Length} bytes to {connections.Count} clients"
                );
            }
        }

        private void StartAcceptingConnections(TcpListener listener, string key)
        {
            listener.BeginAcceptTcpClient(
                ar =>
                {
                    try
                    {
                        var client = listener.EndAcceptTcpClient(ar);
                        lock (_serverConnectionsLock)
                        {
                            if (_serverConnections.TryGetValue(key, out var connections))
                            {
                                connections.Add(client);
                                Debug.WriteLine(
                                    $"[ConnectionManager] Client connected: {client.Client.RemoteEndPoint}"
                                );
                            }
                        }
                        StartAcceptingConnections(listener, key);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ConnectionManager] Accept error: {ex.Message}");
                    }
                },
                null
            );
        }

        #endregion

        #region 串口管理

        /// <summary>
        /// 获取或创建串口连接
        /// </summary>
        public SerialPort? GetOrCreateSerialPort(
            string key,
            string portName,
            int baudRate,
            int dataBits,
            Parity parity,
            StopBits stopBits
        )
        {
            lock (_serialLock)
            {
                if (!_serialPorts.TryGetValue(key, out var port) || !port.IsOpen)
                {
                    try
                    {
                        port?.Close();
                        port = new SerialPort(portName)
                        {
                            BaudRate = baudRate,
                            DataBits = dataBits,
                            Parity = parity,
                            StopBits = stopBits
                        };
                        port.Open();
                        _serialPorts[key] = port;
                        Debug.WriteLine(
                            $"[ConnectionManager] Serial port opened: {portName} at {baudRate} baud"
                        );
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            $"[ConnectionManager] Serial port open failed: {ex.Message}"
                        );
                        return null;
                    }
                }
                return port;
            }
        }

        /// <summary>
        /// 通过串口发送数据
        /// </summary>
        public bool SendViaSerial(
            string key,
            string portName,
            int baudRate,
            int dataBits,
            Parity parity,
            StopBits stopBits,
            byte[] data
        )
        {
            var port = GetOrCreateSerialPort(key, portName, baudRate, dataBits, parity, stopBits);
            if (port == null)
                return false;

            try
            {
                port.Write(data, 0, data.Length);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConnectionManager] Serial send failed: {ex.Message}");
                lock (_serialLock)
                {
                    _serialPorts.Remove(key);
                    try
                    {
                        port?.Close();
                    }
                    catch { }
                }
                return false;
            }
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 解析校验位字符串为 Parity 枚举
        /// </summary>
        public static Parity ParseParity(string parity)
        {
            return parity?.ToUpper() switch
            {
                "NONE" => Parity.None,
                "ODD" => Parity.Odd,
                "EVEN" => Parity.Even,
                "MARK" => Parity.Mark,
                "SPACE" => Parity.Space,
                _ => Parity.None
            };
        }

        /// <summary>
        /// 解析停止位整数为 StopBits 枚举
        /// </summary>
        public static StopBits ParseStopBits(int stopBits)
        {
            return stopBits switch
            {
                1 => StopBits.One,
                2 => StopBits.Two,
                _ => StopBits.One
            };
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// 清理所有连接
        /// </summary>
        public void CleanupAll()
        {
            lock (_udpLock)
            {
                foreach (var client in _udpClients.Values)
                {
                    try
                    {
                        client.Close();
                    }
                    catch { }
                }
                _udpClients.Clear();
            }

            lock (_tcpClientLock)
            {
                foreach (var client in _tcpClients.Values)
                {
                    try
                    {
                        client.Close();
                    }
                    catch { }
                }
                _tcpClients.Clear();
            }

            lock (_serialLock)
            {
                foreach (var port in _serialPorts.Values)
                {
                    try
                    {
                        if (port.IsOpen)
                            port.Close();
                        port.Dispose();
                    }
                    catch { }
                }
                _serialPorts.Clear();
            }

            lock (_tcpServerLock)
            {
                foreach (var listener in _tcpServers.Values)
                {
                    try
                    {
                        listener.Stop();
                    }
                    catch { }
                }
                _tcpServers.Clear();
            }

            lock (_serverConnectionsLock)
            {
                foreach (var connections in _serverConnections.Values)
                {
                    foreach (var client in connections)
                    {
                        try
                        {
                            client.Close();
                        }
                        catch { }
                    }
                }
                _serverConnections.Clear();
            }

            Debug.WriteLine("[ConnectionManager] All connections cleaned up");
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            CleanupAll();
        }

        #endregion
    }
}
