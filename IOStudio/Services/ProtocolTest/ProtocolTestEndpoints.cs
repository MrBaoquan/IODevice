using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IOStudio.Services.ProtocolTest
{
    /// <summary>
    /// 协议测试的传输端抽象。
    ///
    /// <para>只做“收发字节”这一层, 不理解任何业务协议 —— 业务断言交给场景里的 match,
    /// 这样同一套传输适配可以复用给任意协议。</para>
    /// </summary>
    internal interface IProtocolEndpoint : IDisposable
    {
        string Kind { get; }

        /// <summary>实际端口 (listen 使用 0 自动分配时, 这里是分配到的真实端口)。</summary>
        int Port { get; }

        Task OpenAsync(string host, int port, CancellationToken ct);

        Task SendAsync(byte[] data, CancellationToken ct);

        /// <summary>收到一帧数据; 超时返回 null。</summary>
        Task<byte[]?> ReceiveAsync(TimeSpan timeout, CancellationToken ct);
    }

    internal static class TimeoutHelper
    {
        /// <summary>
        /// 带超时等待。返回 (是否在超时前完成, 结果)。
        /// 用显式元组而非可空值: UdpReceiveResult 是结构体, `?.` 不适用。
        /// </summary>
        public static async Task<(bool Ok, T Value)> TryWithTimeout<T>(
            Task<T> task,
            TimeSpan timeout,
            CancellationToken ct
        )
        {
            var done = await Task.WhenAny(task, Task.Delay(timeout, ct)).ConfigureAwait(false);
            if (done != task)
                return (false, default!);
            return (true, await task.ConfigureAwait(false));
        }
    }

    /// <summary>UDP 端: listen = 绑定本地端口; connect = 连到对端 (发送定向)。</summary>
    internal sealed class UdpEndpoint : IProtocolEndpoint
    {
        private readonly bool _isListener;
        private UdpClient? _client;
        private IPEndPoint? _lastSender;

        public UdpEndpoint(bool isListener) => _isListener = isListener;

        public string Kind => _isListener ? "udp(listen)" : "udp(connect)";

        public int Port { get; private set; }

        public Task OpenAsync(string host, int port, CancellationToken ct)
        {
            if (_isListener)
            {
                // 始终绑 0.0.0.0, 便于同时接受 127.0.0.1 与外部对端
                _client = new UdpClient(new IPEndPoint(IPAddress.Any, port));
                Port = ((IPEndPoint)_client.Client.LocalEndPoint!).Port;
            }
            else
            {
                _client = new UdpClient(0);
                _client.Connect(host, port);
                Port = port;
            }
            return Task.CompletedTask;
        }

        public async Task SendAsync(byte[] data, CancellationToken ct)
        {
            if (_client is null)
                throw new InvalidOperationException("UDP 端未打开");
            if (_isListener)
            {
                // 监听端是"未连接"的 socket, 必须显式给目的地址 ——
                // 回包给最近一次收到的来源 (自测里就是那个 connect 端)。
                if (_lastSender is null)
                    throw new InvalidOperationException("UDP 监听端尚未收到过数据, 无法确定回包地址");
                await _client.SendAsync(data, _lastSender, ct).ConfigureAwait(false);
            }
            else
            {
                await _client.SendAsync(data, ct).ConfigureAwait(false);
            }
        }

        public async Task<byte[]?> ReceiveAsync(TimeSpan timeout, CancellationToken ct)
        {
            if (_client is null)
                throw new InvalidOperationException("UDP 端未打开");
            var task = _client.ReceiveAsync(ct).AsTask();
            var (ok, result) = await TimeoutHelper.TryWithTimeout(task, timeout, ct).ConfigureAwait(false);
            if (!ok)
                return null;
            _lastSender = result.RemoteEndPoint;
            return result.Buffer;
        }

        public void Dispose() => _client?.Dispose();
    }

    /// <summary>TCP 客户端: 主动连接。</summary>
    internal sealed class TcpClientEndpoint : IProtocolEndpoint
    {
        private TcpClient? _tcp;
        private NetworkStream? _stream;

        public string Kind => "tcp-client";
        public int Port { get; private set; }

        public async Task OpenAsync(string host, int port, CancellationToken ct)
        {
            _tcp = new TcpClient { NoDelay = true };
            await _tcp.ConnectAsync(host, port, ct).ConfigureAwait(false);
            _stream = _tcp.GetStream();
            Port = port;
        }

        public async Task SendAsync(byte[] data, CancellationToken ct)
        {
            if (_stream is null)
                throw new InvalidOperationException("TCP 客户端未连接");
            await _stream.WriteAsync(data, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
        }

        public async Task<byte[]?> ReceiveAsync(TimeSpan timeout, CancellationToken ct)
        {
            if (_stream is null)
                throw new InvalidOperationException("TCP 客户端未连接");
            var buf = new byte[8192];
            var (ok, read) = await TimeoutHelper
                .TryWithTimeout(_stream.ReadAsync(buf, ct).AsTask(), timeout, ct)
                .ConfigureAwait(false);
            if (!ok || read <= 0)
                return null;
            return buf[..read];
        }

        public void Dispose()
        {
            _stream?.Dispose();
            _tcp?.Dispose();
        }
    }

    /// <summary>TCP 服务端: 监听并在首次收发时接受一个连接 (单连接, 够自测用)。</summary>
    internal sealed class TcpServerEndpoint : IProtocolEndpoint
    {
        private TcpListener? _listener;
        private TcpClient? _accepted;
        private NetworkStream? _stream;

        public string Kind => "tcp-server";
        public int Port { get; private set; }

        public Task OpenAsync(string host, int port, CancellationToken ct)
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            return Task.CompletedTask;
        }

        private async Task<bool> EnsureAcceptedAsync(CancellationToken ct)
        {
            if (_stream is not null)
                return true;
            if (_listener is null)
                return false;
            _accepted = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            _accepted.NoDelay = true;
            _stream = _accepted.GetStream();
            return true;
        }

        public async Task SendAsync(byte[] data, CancellationToken ct)
        {
            if (!await EnsureAcceptedAsync(ct).ConfigureAwait(false) || _stream is null)
                throw new InvalidOperationException("TCP 服务端未被连接");
            await _stream.WriteAsync(data, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
        }

        public async Task<byte[]?> ReceiveAsync(TimeSpan timeout, CancellationToken ct)
        {
            var (accepted, _) = await TimeoutHelper
                .TryWithTimeout(EnsureAcceptedAsync(ct), timeout, ct)
                .ConfigureAwait(false);
            if (!accepted || _stream is null)
                return null;

            var buf = new byte[8192];
            var (ok, read) = await TimeoutHelper
                .TryWithTimeout(_stream.ReadAsync(buf, ct).AsTask(), timeout, ct)
                .ConfigureAwait(false);
            if (!ok || read <= 0)
                return null;
            return buf[..read];
        }

        public void Dispose()
        {
            _stream?.Dispose();
            _accepted?.Dispose();
            _listener?.Stop();
        }
    }
}
