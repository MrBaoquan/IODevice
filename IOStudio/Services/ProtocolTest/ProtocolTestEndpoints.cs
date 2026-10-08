using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
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

        /// <summary>
        /// 已连接的远端列表 —— 只有多客户端服务端才有; 其它传输为空。
        /// 主流调试台都会把"谁连进来了"列出来并允许定向发送。
        /// </summary>
        IReadOnlyList<string> Peers => Array.Empty<string>();

        /// <summary>定向发送目标; null = 广播(服务端) 或唯一对端。</summary>
        string? SelectedPeer { get => null; set { } }

        /// <summary>最近一次收帧的来源 (UDP / 多客户端服务端)。</summary>
        string? LastReceivedPeer => null;

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

    /// <summary>
    /// 流式传输端基类 (TCP / 串口)。
    ///
    /// <para>关键点: 超时**不能丢弃在途的读**。若超时后把 ReadAsync 丢掉, 之后到达的字节
    /// 会被那次被遗忘的读吃掉并丢失 —— 表现为"数据偶发丢失/断言随机失败"。
    /// 因此这里把在途读与收帧缓冲都保留下来, 超时只是"这一轮不等了"。</para>
    /// </summary>
    internal abstract class StreamEndpointBase : IProtocolEndpoint
    {
        private readonly FrameCodec _codec;
        private readonly FramedReceiver _receiver;
        private Task<int>? _readTask;
        private byte[]? _readBuf;

        protected StreamEndpointBase(FrameCodec codec)
        {
            _codec = codec;
            _receiver = new FramedReceiver(codec);
        }

        public abstract string Kind { get; }

        /// <summary>实际端口 (listen 使用 0 自动分配时, 这里是分配到的真实端口)。</summary>
        public int Port { get; protected set; }

        public abstract Task OpenAsync(string host, int port, CancellationToken ct);

        /// <summary>从底层读一批字节 (0 表示连接关闭)。</summary>
        protected abstract Task<int> ReadRawAsync(byte[] buffer, CancellationToken ct);

        protected abstract Task WriteRawAsync(byte[] data, CancellationToken ct);

        /// <summary>发送时必须套用组帧 —— 否则对端按帧解析会永远等不到边界。</summary>
        public Task SendAsync(byte[] data, CancellationToken ct) =>
            WriteRawAsync(_codec.Encode(data), ct);

        public async Task<byte[]?> ReceiveAsync(TimeSpan timeout, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                if (_receiver.TryTake(out var frame))
                    return frame;

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    return null;

                if (!await FillAsync(remaining, ct).ConfigureAwait(false))
                    return null;
            }
        }

        private async Task<bool> FillAsync(TimeSpan remaining, CancellationToken ct)
        {
            if (_readTask is null)
            {
                _readBuf = new byte[8192];
                _readTask = ReadRawAsync(_readBuf, ct);
            }

            var (ok, n) = await TimeoutHelper.TryWithTimeout(_readTask, remaining, ct).ConfigureAwait(false);
            if (!ok)
                return false; // 超时: 保留在途读, 下次继续等同一批

            var buf = _readBuf!;
            _readTask = null;
            _readBuf = null;
            if (n <= 0)
                return false; // 连接关闭

            _receiver.Append(buf.AsSpan(0, n));
            return true;
        }

        public abstract void Dispose();
    }

    /// <summary>UDP 端: listen = 绑定本地端口; connect = 连到对端 (发送定向)。</summary>
    internal sealed class UdpEndpoint : IProtocolEndpoint
    {
        private readonly bool _isListener;
        private readonly FrameCodec _codec;
        private readonly FramedReceiver _receiver;
        private UdpClient? _client;
        private IPEndPoint? _lastSender;
        private Task<UdpReceiveResult>? _readTask;

        public UdpEndpoint(bool isListener, FrameCodec codec)
        {
            _isListener = isListener;
            _codec = codec;
            _receiver = new FramedReceiver(codec);
        }

        public string Kind => _isListener ? "udp(listen)" : "udp(connect)";

        public int Port { get; private set; }

        /// <summary>定向发送目标 "ip:port"; null = 回给最近一次收到的来源。</summary>
        public string? SelectedPeer { get; set; }

        public string? LastReceivedPeer => _lastSender?.ToString();

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
            var framed = _codec.Encode(data);

            if (_isListener)
            {
                // 监听端是"未连接"的 socket, 必须显式给目的地址 ——
                // 优先用户指定, 否则回给最近一次收到的来源。
                var target = ParsePeer(SelectedPeer) ?? _lastSender;
                if (target is null)
                    throw new InvalidOperationException(
                        "UDP 监听端尚未收到过数据, 无法确定目的地址 (可先填目标对端)"
                    );
                await _client.SendAsync(framed, target, ct).ConfigureAwait(false);
                return;
            }

            // 已连接模式: UdpClient **不允许**在 SendAsync 里再指定地址
            // (否则抛 "Cannot send packets to an arbitrary host while connected")。
            // 若用户显式换了目标, 必须重新 Connect。
            var peer = ParsePeer(SelectedPeer);
            if (peer is not null)
                _client.Connect(peer);

            await _client.SendAsync(framed, ct).ConfigureAwait(false);
        }

        /// <summary>解析 "ip:port" (填错返回 null, 让上层回退到默认行为)。</summary>
        private static IPEndPoint? ParsePeer(string? peer)
        {
            if (string.IsNullOrWhiteSpace(peer))
                return null;
            var s = peer.Trim();
            int i = s.LastIndexOf(':');
            if (i <= 0 || i == s.Length - 1)
                return null;
            return IPAddress.TryParse(s[..i], out var ip)
                && int.TryParse(s[(i + 1)..], out var port)
                && port is > 0 and <= 65535
                ? new IPEndPoint(ip, port)
                : null;
        }

        public async Task<byte[]?> ReceiveAsync(TimeSpan timeout, CancellationToken ct)
        {
            if (_client is null)
                throw new InvalidOperationException("UDP 端未打开");

            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                if (_receiver.TryTake(out var frame))
                    return frame;

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    return null;

                // 保留在途接收: 超时后不能把 ReceiveAsync 丢掉, 否则下一份数据报会被它吃掉。
                _readTask ??= _client.ReceiveAsync(ct).AsTask();
                var (ok, result) = await TimeoutHelper.TryWithTimeout(_readTask, remaining, ct)
                    .ConfigureAwait(false);
                if (!ok)
                    return null;

                _readTask = null;
                _lastSender = result.RemoteEndPoint;
                _receiver.Append(result.Buffer);
            }
        }

        public void Dispose() => _client?.Dispose();
    }

    /// <summary>TCP 客户端: 主动连接。</summary>
    internal sealed class TcpClientEndpoint : StreamEndpointBase
    {
        private TcpClient? _tcp;
        private NetworkStream? _stream;

        public TcpClientEndpoint(FrameCodec codec) : base(codec) { }

        public override string Kind => "tcp-client";

        public override async Task OpenAsync(string host, int port, CancellationToken ct)
        {
            _tcp = new TcpClient { NoDelay = true };
            await _tcp.ConnectAsync(host, port, ct).ConfigureAwait(false);
            _stream = _tcp.GetStream();
            Port = port;
        }

        protected override async Task<int> ReadRawAsync(byte[] buffer, CancellationToken ct)
        {
            if (_stream is null)
                throw new InvalidOperationException("TCP 客户端未连接");
            return await _stream.ReadAsync(buffer, ct).ConfigureAwait(false);
        }

        protected override async Task WriteRawAsync(byte[] data, CancellationToken ct)
        {
            if (_stream is null)
                throw new InvalidOperationException("TCP 客户端未连接");
            await _stream.WriteAsync(data, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
        }

        public override void Dispose()
        {
            _stream?.Dispose();
            _tcp?.Dispose();
        }
    }

    /// <summary>
    /// TCP 服务端: **多客户端**。
    ///
    /// <para>为什么必须多客户端: 主流调试台 (NetAssist / Hercules) 的服务端都能同时接多个客户端、
    /// 列出"谁连进来了", 并支持定向发送与广播。单连接服务端无法验证"多端并发"这类真实场景
    /// —— 而多端并发恰恰是服务端最常见的排错对象。</para>
    ///
    /// <para>每个客户端各自持有组帧缓冲: 粘包/拆包是**按连接**独立的, 共用一份缓冲会串帧。</para>
    /// </summary>
    internal sealed class TcpServerEndpoint : IProtocolEndpoint
    {
        private sealed class Client
        {
            public Client(string id, TcpClient tcp, FrameCodec codec)
            {
                Id = id;
                Tcp = tcp;
                Stream = tcp.GetStream();
                Receiver = new FramedReceiver(codec);
            }

            public string Id { get; }
            public TcpClient Tcp { get; }
            public NetworkStream Stream { get; }
            public FramedReceiver Receiver { get; }
            public Task<int>? ReadTask { get; set; }
            public byte[] Buf { get; } = new byte[8192];
        }

        private readonly FrameCodec _codec;
        private readonly object _lock = new();
        private readonly List<Client> _clients = new();
        private TcpListener? _listener;
        private string? _selected;
        private string? _lastReceived;

        public TcpServerEndpoint(FrameCodec codec) => _codec = codec;

        public string Kind => "tcp-server";
        public int Port { get; private set; }

        public IReadOnlyList<string> Peers
        {
            get
            {
                lock (_lock)
                    return _clients.Select(c => c.Id).ToArray();
            }
        }

        /// <summary>null = 广播给所有客户端。</summary>
        public string? SelectedPeer
        {
            get => _selected;
            set => _selected = value;
        }

        public string? LastReceivedPeer => _lastReceived;

        public Task OpenAsync(string host, int port, CancellationToken ct)
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            // 主动 accept: 客户端要能立刻连上并出现在列表里 (否则"连上了却看不到"很反直觉)
            _ = AcceptLoopAsync(ct);
            return Task.CompletedTask;
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var tcp = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                    tcp.NoDelay = true;
                    var ep = (IPEndPoint)tcp.Client.RemoteEndPoint!;
                    lock (_lock)
                        _clients.Add(new Client($"{ep.Address}:{ep.Port}", tcp, _codec));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break; // 正常停止
                }
                catch
                {
                    break;
                }
            }
        }

        public async Task<byte[]?> ReceiveAsync(TimeSpan timeout, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                if (ct.IsCancellationRequested)
                    return null;

                if (TryTakeBuffered(out var frame, out var from))
                {
                    _lastReceived = from;
                    return frame;
                }

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    return null;

                await PumpOnceAsync(remaining, ct).ConfigureAwait(false);
            }
        }

        private bool TryTakeBuffered(out byte[] frame, out string? from)
        {
            lock (_lock)
            {
                foreach (var c in _clients)
                {
                    if (c.Receiver.TryTake(out var f))
                    {
                        frame = f;
                        from = c.Id;
                        return true;
                    }
                }
            }
            frame = Array.Empty<byte>();
            from = null;
            return false;
        }

        /// <summary>等任一客户端来数据, 收一批进它自己的组帧缓冲。</summary>
        private async Task PumpOnceAsync(TimeSpan remaining, CancellationToken ct)
        {
            Client[] snapshot;
            lock (_lock)
            {
                snapshot = _clients.ToArray();
                foreach (var c in snapshot)
                    c.ReadTask ??= c.Stream.ReadAsync(c.Buf, ct).AsTask();
            }

            if (snapshot.Length == 0)
            {
                try
                {
                    await Task.Delay(remaining, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    /* 停止中 */
                }
                return;
            }

            var reads = snapshot.Select(c => (Task)c.ReadTask!).ToList();
            reads.Add(Task.Delay(remaining, ct));
            var done = await Task.WhenAny(reads).ConfigureAwait(false);

            var idx = Array.FindIndex(snapshot, c => ReferenceEquals(c.ReadTask, done));
            if (idx < 0)
                return; // 超时 (或停止)

            var client = snapshot[idx];
            int n;
            try
            {
                n = await client.ReadTask!.ConfigureAwait(false);
            }
            catch
            {
                RemoveClient(client);
                return;
            }

            lock (_lock)
            {
                client.ReadTask = null;
                if (n > 0)
                    client.Receiver.Append(client.Buf.AsSpan(0, n));
            }

            if (n <= 0)
                RemoveClient(client); // 对端关闭
        }

        public async Task SendAsync(byte[] data, CancellationToken ct)
        {
            var framed = _codec.Encode(data);

            Client[] targets;
            string? sel;
            lock (_lock)
            {
                sel = _selected;
                targets = sel is null
                    ? _clients.ToArray()
                    : _clients.Where(c => c.Id == sel).ToArray();
            }

            if (targets.Length == 0)
                throw new InvalidOperationException(
                    sel is null ? "TCP 服务端尚无客户端连接" : $"目标客户端 {sel} 已断开"
                );

            foreach (var c in targets)
            {
                try
                {
                    await c.Stream.WriteAsync(framed, ct).ConfigureAwait(false);
                    await c.Stream.FlushAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    RemoveClient(c);
                }
            }
        }

        private void RemoveClient(Client c)
        {
            lock (_lock)
            {
                if (!_clients.Remove(c))
                    return;
            }
            try
            {
                c.Stream.Dispose();
            }
            catch
            {
                /* ignore */
            }
            try
            {
                c.Tcp.Dispose();
            }
            catch
            {
                /* ignore */
            }
        }

        public void Dispose()
        {
            Client[] snapshot;
            lock (_lock)
            {
                snapshot = _clients.ToArray();
                _clients.Clear();
            }
            foreach (var c in snapshot)
            {
                try
                {
                    c.Stream.Dispose();
                }
                catch
                {
                    /* ignore */
                }
                try
                {
                    c.Tcp.Dispose();
                }
                catch
                {
                    /* ignore */
                }
            }
            try
            {
                _listener?.Stop();
            }
            catch
            {
                /* ignore */
            }
        }
    }

    /// <summary>串口端: host 参数忽略, 端口名与波特率来自场景 (device / baud)。</summary>
    internal sealed class SerialEndpoint : StreamEndpointBase
    {
        private readonly string _device;
        private readonly int _baud;
        private SerialPort? _port;

        public SerialEndpoint(FrameCodec codec, string device, int baud) : base(codec)
        {
            _device = device;
            _baud = baud > 0 ? baud : 115200;
        }

        public override string Kind => $"serial({_device})";

        public override Task OpenAsync(string host, int port, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_device))
                throw new InvalidOperationException("串口场景必须提供 device (例如 COM3)");
            _port = new SerialPort(_device, _baud, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 2000,
                WriteTimeout = 2000,
                DtrEnable = true,
                RtsEnable = true,
            };
            _port.Open();
            return Task.CompletedTask;
        }

        protected override async Task<int> ReadRawAsync(byte[] buffer, CancellationToken ct)
        {
            if (_port is null || !_port.IsOpen)
                throw new InvalidOperationException($"串口未打开: {_device}");
            return await _port.BaseStream.ReadAsync(buffer, ct).ConfigureAwait(false);
        }

        protected override async Task WriteRawAsync(byte[] data, CancellationToken ct)
        {
            if (_port is null || !_port.IsOpen)
                throw new InvalidOperationException($"串口未打开: {_device}");
            await _port.BaseStream.WriteAsync(data, ct).ConfigureAwait(false);
            await _port.BaseStream.FlushAsync(ct).ConfigureAwait(false);
        }

        public override void Dispose()
        {
            try
            {
                if (_port is { IsOpen: true })
                    _port.Close();
            }
            catch
            {
                /* 关闭失败不应影响测试结论 */
            }
            _port?.Dispose();
        }
    }

    /// <summary>WebSocket 客户端。URL 由 host/port/path 拼出 (ws://host:port/path)。</summary>
    internal sealed class WebSocketClientEndpoint : IProtocolEndpoint
    {
        private readonly string _path;
        private ClientWebSocket? _ws;
        private Task<byte[]>? _inflight;

        public WebSocketClientEndpoint(string path) =>
            _path = string.IsNullOrWhiteSpace(path) ? "/" : path;

        public string Kind => "websocket-client";
        public int Port { get; private set; }

        public async Task OpenAsync(string host, int port, CancellationToken ct)
        {
            _ws = new ClientWebSocket();
            var uri = new Uri($"ws://{host}:{port}{_path}");
            await _ws.ConnectAsync(uri, ct).ConfigureAwait(false);
            Port = port;
        }

        public async Task SendAsync(byte[] data, CancellationToken ct)
        {
            if (_ws is null)
                throw new InvalidOperationException("WebSocket 客户端未连接");
            // 统一发二进制帧: 内容仍是 UTF-8 文本, 但不受对端文本帧处理差异影响
            await _ws.SendAsync(data, WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
        }

        public async Task<byte[]?> ReceiveAsync(TimeSpan timeout, CancellationToken ct)
        {
            if (_ws is null)
                throw new InvalidOperationException("WebSocket 客户端未连接");
            // 保留在途接收 (同流式传输的理由): 超时后不能丢掉, 否则丢一帧
            _inflight ??= ReadMessageAsync(_ws, ct);
            var (ok, data) = await TimeoutHelper.TryWithTimeout(_inflight, timeout, ct)
                .ConfigureAwait(false);
            if (!ok)
                return null;
            _inflight = null;
            return data;
        }

        internal static async Task<byte[]> ReadMessageAsync(WebSocket ws, CancellationToken ct)
        {
            var buf = new byte[8192];
            using var ms = new MemoryStream();
            while (true)
            {
                var r = await ws.ReceiveAsync(buf, ct).ConfigureAwait(false);
                if (r.MessageType == WebSocketMessageType.Close)
                    throw new IOException("WebSocket 对端已关闭连接");
                if (r.Count > 0)
                    ms.Write(buf, 0, r.Count);
                if (r.EndOfMessage)
                    break;
            }
            return ms.ToArray();
        }

        public void Dispose() => _ws?.Dispose();
    }

    /// <summary>
    /// WebSocket 服务端。
    ///
    /// <para>用 TcpListener + 自实现 HTTP Upgrade 握手 + WebSocket.CreateFromStream,
    /// 而不是 HttpListener —— 后者在 Windows 上需要 URL ACL/管理员权限, 会让测试
    /// 在普通账户下直接失败, 不利于 CI 与现场使用。</para>
    /// </summary>
    internal sealed class WebSocketServerEndpoint : IProtocolEndpoint
    {
        private const string WsGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        private TcpListener? _listener;
        private TcpClient? _client;
        private WebSocket? _ws;
        private Task<bool>? _acceptTask;
        private Task<byte[]>? _inflight;

        public string Kind => "websocket-server";
        public int Port { get; private set; }

        public Task OpenAsync(string host, int port, CancellationToken ct)
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            // 必须**主动** accept: WebSocket 客户端 ConnectAsync 要等服务端的 101 握手响应,
            // 而握手只在 accept 之后才发生。若等到首次收发才 accept, 客户端会一直等 → 死锁。
            _acceptTask ??= DoAcceptAsync(ct);
            return Task.CompletedTask;
        }

        private Task<bool> EnsureAcceptedAsync(CancellationToken ct)
        {
            if (_ws is not null)
                return Task.FromResult(true);
            _acceptTask ??= DoAcceptAsync(ct);
            return _acceptTask;
        }

        private async Task<bool> DoAcceptAsync(CancellationToken ct)
        {
            if (_listener is null)
                return false;
            _client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            _client.NoDelay = true;
            var stream = _client.GetStream();
            await HandshakeAsync(stream, ct).ConfigureAwait(false);
            _ws = WebSocket.CreateFromStream(
                stream,
                isServer: true,
                subProtocol: null,
                keepAliveInterval: TimeSpan.Zero
            );
            return true;
        }

        private static async Task HandshakeAsync(NetworkStream stream, CancellationToken ct)
        {
            var buf = new byte[8192];
            int total = 0;
            while (total < buf.Length)
            {
                int n = await stream.ReadAsync(buf.AsMemory(total), ct).ConfigureAwait(false);
                if (n <= 0)
                    throw new IOException("WebSocket 握手失败: 连接在收到请求头前关闭");
                total += n;
                if (ContainsDoubleCrlf(buf, total))
                    break;
            }

            var text = Encoding.ASCII.GetString(buf, 0, total);
            var key = ExtractHeader(text, "Sec-WebSocket-Key");
            if (string.IsNullOrEmpty(key))
                throw new IOException("WebSocket 握手失败: 缺少 Sec-WebSocket-Key");

            var accept = Convert.ToBase64String(
                SHA1.HashData(Encoding.ASCII.GetBytes(key + WsGuid))
            );
            var response =
                "HTTP/1.1 101 Switching Protocols\r\n"
                + "Upgrade: websocket\r\n"
                + "Connection: Upgrade\r\n"
                + $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response), ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        private static bool ContainsDoubleCrlf(byte[] buf, int len)
        {
            for (int i = 0; i + 3 < len; i++)
            {
                if (buf[i] == 13 && buf[i + 1] == 10 && buf[i + 2] == 13 && buf[i + 3] == 10)
                    return true;
            }
            return false;
        }

        private static string? ExtractHeader(string request, string name)
        {
            foreach (var line in request.Split("\r\n"))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0)
                    continue;
                if (line[..colon].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                    return line[(colon + 1)..].Trim();
            }
            return null;
        }

        public async Task SendAsync(byte[] data, CancellationToken ct)
        {
            if (!await EnsureAcceptedAsync(ct).ConfigureAwait(false) || _ws is null)
                throw new InvalidOperationException("WebSocket 服务端未被连接");
            await _ws.SendAsync(data, WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
        }

        public async Task<byte[]?> ReceiveAsync(TimeSpan timeout, CancellationToken ct)
        {
            var (accepted, _) = await TimeoutHelper
                .TryWithTimeout(EnsureAcceptedAsync(ct), timeout, ct)
                .ConfigureAwait(false);
            if (!accepted || _ws is null)
                return null;

            _inflight ??= WebSocketClientEndpoint.ReadMessageAsync(_ws, ct);
            var (ok, data) = await TimeoutHelper.TryWithTimeout(_inflight, timeout, ct)
                .ConfigureAwait(false);
            if (!ok)
                return null;
            _inflight = null;
            return data;
        }

        public void Dispose()
        {
            _ws?.Dispose();
            _client?.Dispose();
            _listener?.Stop();
        }
    }
}
