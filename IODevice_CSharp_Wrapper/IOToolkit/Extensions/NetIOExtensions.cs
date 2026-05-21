using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IOToolkit.Extension;

namespace IOToolkit.NetIO
{
    /// <summary>
    /// IODevice 的 NetIO 帧级扩展方法. 与 NETIO 插件 (protocol=ws|tcp) 对接.
    ///
    /// 插件侧固定暴露标准 PluginMessage:
    ///   "netio.frame.in"     - 对端发给宿主的 NetFrame
    ///   "netio.frame.out"    - 宿主广播给对端的 NetFrame
    ///   "netio.frame.sendto" - 宿主定向发送给对端的 NetFrame
    /// 本扩展把消息名藏在内部, 业务只用 Request / Subscribe / ChannelContext.Respond.
    ///
    /// 典型用法:
    /// <code>
    /// using IOToolkit;
    /// using IOToolkit.NetIO;
    ///
    /// var dev = IOToolkit.Devices["cinema"];  // NETIO 设备
    /// dev.Subscribe("progress", ctx => { Console.WriteLine("progress " + ctx.PayloadJson); return Task.FromResult(ChannelResponse.Handled); });
    /// await dev.Request("play");
    /// ChannelResponse films = await dev.Request("list_films");
    /// </code>
    /// </summary>
    public static class NetIOExtensions
    {
        internal const string FrameInMessage = "netio.frame.in";
        internal const string FrameOutMessage = "netio.frame.out";
        internal const string FrameSendToMessage = "netio.frame.sendto";
        internal const string SelfUpdateMessage = "netio.self.update";

        // 每台 IODevice 挂一个私有状态; GC 回收设备时自动释放.
        // (NetFrame 协议栈已下沉到 NETIO 插件, 不再需要 ack pending 表.)

        // ============ 标准 Channel 接口 (已下沉到 IODevice.Channel.cs + NETIO 插件) ============
        // 原 RequestNetIO / SubscribeNetIO / RequestWithResponse / OnInbound / NetIOState / PendingAck
        // 已删除. NetFrame 协议结帧/解帧现在由 NETIO C++ 插件完成, IODevice 只看到通用的
        // _rpc.req / _rpc.res envelope, C# 侧不再需要 ack 匹配表.

        /// <summary>
        /// 向 NETIO 插件的命名通道写入一段 UTF-8 JSON. 直接走原始 PluginChannel,
        /// 不再叠加 PluginMessage 信封 (插件已就地兼容 envelope-less 写入).
        /// </summary>
        internal static int WriteFrameChannel(IODevice device, string channelName, string json)
        {
            if (device == null || string.IsNullOrEmpty(channelName))
                return 0;
            var bytes = Encoding.UTF8.GetBytes(json ?? string.Empty);
            return IOToolkit.Core.IONativeWrapper.WritePluginChannel(
                device.ID,
                channelName,
                bytes,
                (uint)bytes.Length
            );
        }

        internal static void SendToTarget(IODevice device, string target, string frameJson)
        {
            var payload =
                "{\"target\":\"" + EscapeJsonString(target) + "\",\"frame\":" + frameJson + "}";
            WriteFrameChannel(device, FrameSendToMessage, payload);
        }

        private static string EscapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s ?? string.Empty;
            var sb = new StringBuilder(s.Length + 4);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                            sb.AppendFormat("\\u{0:x4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }

    // =====================================================================
    // NetIO DI/AD 通道扩展 (三种 protocol 下都可用)
    //
    // 协议统一 v2 (2026-04, P5 重构):
    //   IOToolkit 出站远程 IO 控制统一走 NetFrame 新协议. 下面的 SetRemoteKeyDown/Up,
    //   SetRemoteAxis, EmitNetEvent, ZeroRemoteDI/AD 不再写 OAxis_240-248, 而是通过
    //   内部 helper `WriteFrameChannel(...)` (其底层走 IODevice.RequestChannel
    //   non-wait 分支 → NETIO 插件编码 NetFrame `evt`/`req`) 发送形如:
    //     {"type":"evt","name":"io.set_di","data":{"channels":{"<ch>":<val>}}}
    //   legacy 协议 (SetDI/SetAD/ZeroDI/ZeroAD JSON) 仅在入站方向由插件转换兼容, 出站不再保证.
    //
    //   SetRemoteAddress(ip, port) 会写入内部默认目标地址, 后续发帧自动改走 _sendto 定向.
    //   留空 (未调用 SetRemoteAddress) 时走 out 通道广播, 与 ws/tcp 原有语义一致.
    //
    //   SetEventMode / GetEventMode / (internal) SetRemoteChannel 保留签名仅用于
    //   兼容显式 OAxis 写法 (SetEventMode + SetDO(OAxis_xx) + DOImmediate), 不再被新 API 使用.
    // =====================================================================

    /// <summary>NetIO 常用按键映射 (Button_00 - Button_17).</summary>
    public struct NetIOKeyCode
    {
        public static readonly Key Ctrl = IOKeyCode.Button_00;
        public static readonly Key Home = IOKeyCode.Button_01;
        public static readonly Key Menu = IOKeyCode.Button_02;
        public static readonly Key Up = IOKeyCode.Button_03;
        public static readonly Key Down = IOKeyCode.Button_04;
        public static readonly Key Left = IOKeyCode.Button_05;
        public static readonly Key Right = IOKeyCode.Button_06;
        public static readonly Key Confirm = IOKeyCode.Button_07;
        public static readonly Key Back = IOKeyCode.Button_08;
        public static readonly Key VolumeUp = IOKeyCode.Button_09;
        public static readonly Key VolumeDown = IOKeyCode.Button_10;
        public static readonly Key Mute = IOKeyCode.Button_11;
        public static readonly Key Loop = IOKeyCode.Button_15;
        public static readonly Key Play = IOKeyCode.Button_16;
        public static readonly Key Pause = IOKeyCode.Button_17;
    }

    /// <summary>DI/AD 通道事件模式 (写入 OAxis_240).</summary>
    public enum EventMode
    {
        SetAll = 0,
        SetDI = 1,
        SetAD = 2,
        ZeroAll = 10,
        ZeroDI = 11,
        ZeroAD = 12
    }

    /// <summary>
    /// NetIO DI/AD 通道扩展方法. 在 udp/ws/tcp 三种协议下均生效 —
    /// 由 NETIO 插件根据实例 protocol 选择具体传输方式.
    /// </summary>
    public static class NetIOChannelExtensions
    {
        // Legacy OAxis 保留位 (仅为兼容显式写法). 新 API 不再走这些通道.
        private static readonly Key chFunc = "OAxis_240";
        private static readonly Key chPort = "OAxis_245";
        private static readonly Key chSetKeyFlag = "OAxis_246";
        private static readonly Key chChannel = "OAxis_247";
        private static readonly Key chValue = "OAxis_248";

        // ── 每台 IODevice 挂一份私有状态 (默认目标地址 + 最近事件模式). ──
        private sealed class IoCtrlState
        {
            public string DefaultTarget; // "ip:port" 或 null
            public EventMode LastMode; // GetEventMode 兼容返回值
        }

        private static readonly ConditionalWeakTable<IODevice, IoCtrlState> _ctrlStates =
            new ConditionalWeakTable<IODevice, IoCtrlState>();

        private static IoCtrlState GetCtrl(IODevice dev) =>
            _ctrlStates.GetValue(dev, _ => new IoCtrlState());

        // ── 构造 {"type":"evt","name":"<ioName>","data":{"channels":{...}}} 并下发. ──
        //    有默认目标地址时走 "_sendto" (定向), 否则走 "out" (广播/扇出).
        private static void SendIoFrame(
            IODevice dev,
            string ioName,
            Dictionary<Key, float> channels
        )
        {
            var sb = new StringBuilder(64);
            sb.Append("{\"type\":\"evt\",\"name\":\"")
                .Append(ioName)
                .Append("\",\"data\":{\"channels\":{");
            bool first = true;
            if (channels != null)
            {
                foreach (var kv in channels)
                {
                    int ch = kv.Key.GetIntValue();
                    if (ch < 0)
                    {
                        continue;
                    }
                    if (!first)
                    {
                        sb.Append(',');
                    }
                    first = false;
                    // 整数尽量整数输出, 避免 "1.0" 这种噪音.
                    float v = kv.Value;
                    sb.Append('"').Append(ch).Append("\":");
                    if (v == (int)v)
                        sb.Append((int)v);
                    else
                        sb.Append(v.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            sb.Append("}}}");
            var frame = sb.ToString();

            var state = GetCtrl(dev);
            if (!string.IsNullOrEmpty(state.DefaultTarget))
            {
                NetIOExtensions.SendToTarget(dev, state.DefaultTarget, frame);
            }
            else
            {
                NetIOExtensions.WriteFrameChannel(dev, NetIOExtensions.FrameOutMessage, frame);
            }
        }

        /// <summary>
        /// 设置 NetIO 设备的远端目标地址. 设置后本设备上的 SetRemoteKeyDown/Up/Axis/
        /// EmitNetEvent/ZeroRemoteDI/AD 都会自动改走 _sendto 定向到该地址;
        /// 未设置 (或 ip 无效) 时走 out 广播/扇出.
        /// 三种 protocol (udp/ws/tcp) 语义一致.
        /// </summary>
        public static void SetRemoteAddress(this IODevice netDev, string ip, int port)
        {
            if (netDev == null)
            {
                return;
            }
            var state = GetCtrl(netDev);
            if (string.IsNullOrEmpty(ip) || port <= 0)
            {
                state.DefaultTarget = null;
                return;
            }
            if (!IPAddress.TryParse(ip, out _))
            {
                System.Diagnostics.Trace.TraceWarning("Invalid IP address: " + ip);
                return;
            }
            state.DefaultTarget = ip + ":" + port;
        }

        /// <summary>
        /// [Legacy] 仅写入 OAxis_240 以兼容显式 OAxis 写法. 新 API 已不依赖该通道.
        /// </summary>
        public static void SetEventMode(this IODevice netDev, EventMode func)
        {
            GetCtrl(netDev).LastMode = func;
            netDev.SetDO(chFunc, (int)func);
        }

        /// <summary>[Legacy] 返回最近一次 SetEventMode 设置的模式.</summary>
        public static EventMode GetEventMode(this IODevice netDev) => GetCtrl(netDev).LastMode;

        /// <summary>DI 通道置零. 发送 io.zero_di 帧.</summary>
        public static void ZeroRemoteDI(this IODevice netDev)
        {
            SendIoFrame(netDev, "io.zero_di", null);
        }

        /// <summary>AD 通道置零. 发送 io.zero_ad 帧.</summary>
        public static void ZeroRemoteAD(this IODevice netDev)
        {
            SendIoFrame(netDev, "io.zero_ad", null);
        }

        /// <summary>把 extDevice 的 [startChannel, startChannel+count) 按键事件转发到 netDevice 对应的 OAxis.</summary>
        public static void PropagateDIEvents(
            this IODevice netDevice,
            IODevice extDevice,
            int startChannel,
            int count
        )
        {
            for (int i = 0; i < count; i++)
            {
                int idx = startChannel + i;
                Key outputKey = $"OAxis_{idx:D2}";
                Key inputKey = $"Button_{idx:D2}";

                extDevice.BindKey(
                    inputKey,
                    InputEvent.IE_Pressed,
                    () => netDevice.SetRemoteKeyDown(outputKey)
                );
                extDevice.BindKey(
                    inputKey,
                    InputEvent.IE_Released,
                    () => netDevice.SetRemoteKeyUp(outputKey)
                );
            }
        }

        /// <summary>发射一次瞬时 DI 事件 (按下后立即抬起). 连发两帧 io.set_di.</summary>
        public static void EmitNetEvent(this IODevice netDev, Key evtKey)
        {
            var dict = new Dictionary<Key, float>(1);
            dict[evtKey] = 1;
            SendIoFrame(netDev, "io.set_di", dict);
            dict[evtKey] = 0;
            SendIoFrame(netDev, "io.set_di", dict);
        }

        public static void SetRemoteKeyDown(this IODevice netDev, Key button)
        {
            var dict = new Dictionary<Key, float>(1);
            dict[button] = 1;
            SendIoFrame(netDev, "io.set_di", dict);
        }

        public static void SetRemoteKeyUp(this IODevice netDev, Key button)
        {
            var dict = new Dictionary<Key, float>(1);
            dict[button] = 0;
            SendIoFrame(netDev, "io.set_di", dict);
        }

        /// <summary>
        /// 写入 AD 值. value 原样下发 (协议统一 v2 起不再做 *1000 放大,
        /// 与对端 GetDeviceAD 取到的 short 值语义一致).
        /// </summary>
        public static void SetRemoteAxis(this IODevice netDev, Key axisKey, float value)
        {
            var dict = new Dictionary<Key, float>(1);
            dict[axisKey] = value;
            SendIoFrame(netDev, "io.set_ad", dict);
        }

        /// <summary>[Legacy] 写入 OAxis_246/247/248. 新 API 不再使用.</summary>
        private static void SetRemoteChannel(this IODevice netDev, Key chKey, float val)
        {
            netDev.SetDO(chSetKeyFlag, 1);
            netDev.SetDO(chChannel, chKey.GetIntValue());
            netDev.SetDO(chValue, val);
        }
    }

    // =====================================================================
    // Peer 发现 + 广播
    //
    // 插件保留通道 "_peers" 提供当前已连接对端的快照:
    //   宿主写入 "_peers" 空数据 → 插件立即通过 PluginChannelDispatcher 回推一条
    //   以 UTF-8 JSON 数组形式的 peers 快照.
    //
    // 典型用法:
    //   var peers = await device.GetPeersAsync();      // 或 device.GetPeers()
    //   foreach (var p in peers) Console.WriteLine(p);
    //   device.BroadcastRequest("status", "{\"ok\":true}"); // 无差别广播给所有 inbound 对端
    // =====================================================================

    /// <summary>已连接对端信息 (来自 NETIO 插件 _peers 通道).</summary>
    public sealed class NetPeer
    {
        /// <summary>对端会话 ID (由插件内部分配; ws/tcp 下用于 RequestToSid; udp 下为 "udp:ip:port").</summary>
        public string Sid { get; set; }

        /// <summary>传输协议: "ws" | "tcp" | "udp".</summary>
        public string Protocol { get; set; }

        /// <summary>对端地址 "ip:port".</summary>
        public string Addr { get; set; }

        /// <summary>"inbound" (对方连入) | "outbound" (本节点连出) | "udp".</summary>
        public string Role { get; set; }

        /// <summary>对端自报名 (未握手时为空).</summary>
        public string Name { get; set; }

        /// <summary>最近一次收到报文的时刻 (毫秒 steady_clock).</summary>
        public long LastSeenMs { get; set; }

        /// <summary>首次见到的时刻 (毫秒 steady_clock).</summary>
        public long SinceMs { get; set; }

        public override string ToString() =>
            $"[{Protocol}/{Role}] {(string.IsNullOrEmpty(Name) ? Addr : Name + "@" + Addr)} (sid={Sid})";
    }

    public static class NetIOPeerExtensions
    {
        /// <summary>获取当前对端列表 (JSON 原文). 默认 1 秒超时, 未响应返回 "[]".</summary>
        public static Task<string> GetPeersJsonAsync(this IODevice device, int timeoutMs = 1000)
        {
            if (device == null)
                throw new ArgumentNullException(nameof(device));
            try
            {
                var json = device.SendPluginRequest("netio.peers", "{}", timeoutMs, 256 * 1024);
                return Task.FromResult(string.IsNullOrEmpty(json) ? "[]" : json);
            }
            catch
            {
                return Task.FromResult("[]");
            }
        }

        /// <summary>获取当前对端列表. 阻塞等待插件响应 (最多 timeoutMs).</summary>
        public static IReadOnlyList<NetPeer> GetPeers(this IODevice device, int timeoutMs = 1000)
        {
            var task = device.GetPeersJsonAsync(timeoutMs);
            if (!task.Wait(Math.Max(50, timeoutMs + 200)))
                return new NetPeer[0];
            return ParsePeers(task.Result);
        }

        /// <summary>异步获取强类型 peer 列表.</summary>
        public static async Task<IReadOnlyList<NetPeer>> GetPeersAsync(
            this IODevice device,
            int timeoutMs = 1000
        )
        {
            var json = await device.GetPeersJsonAsync(timeoutMs).ConfigureAwait(false);
            return ParsePeers(json);
        }

        /// <summary>
        /// 无差别广播一条 evt 帧给所有 inbound 对端 (ws/tcp) 或当前 remote (udp).
        /// 用于不关心目标的通知场景, 如 "_peer_join" / 广播式状态推送.
        /// </summary>
        public static void BroadcastRequest(
            this IODevice device,
            string name,
            string dataJson = null
        )
        {
            if (device == null)
                throw new ArgumentNullException(nameof(device));
            device
                .Request(name, dataJson, ChannelRequestOptions.NoResponse)
                .GetAwaiter()
                .GetResult();
        }

        // —— 极小 JSON 数组解析器, 只支持我们 _peers 负载的扁平结构 —— //
        internal static IReadOnlyList<NetPeer> ParsePeers(string json)
        {
            var list = new List<NetPeer>();
            if (string.IsNullOrEmpty(json))
                return list;
            int i = 0,
                n = json.Length;
            SkipWs(json, ref i);
            if (i >= n || json[i] != '[')
                return list;
            i++;
            while (i < n)
            {
                SkipWs(json, ref i);
                if (i < n && json[i] == ']')
                {
                    i++;
                    break;
                }
                if (i < n && json[i] == ',')
                {
                    i++;
                    continue;
                }
                if (i < n && json[i] == '{')
                {
                    int objStart = i;
                    int depth = 0;
                    bool inStr = false,
                        esc = false;
                    for (; i < n; i++)
                    {
                        char c = json[i];
                        if (inStr)
                        {
                            if (esc)
                            {
                                esc = false;
                            }
                            else if (c == '\\')
                                esc = true;
                            else if (c == '"')
                                inStr = false;
                        }
                        else
                        {
                            if (c == '"')
                                inStr = true;
                            else if (c == '{')
                                depth++;
                            else if (c == '}')
                            {
                                depth--;
                                if (depth == 0)
                                {
                                    i++;
                                    break;
                                }
                            }
                        }
                    }
                    string objText = json.Substring(objStart, i - objStart);
                    var p = ParsePeerObject(objText);
                    if (p != null)
                        list.Add(p);
                }
                else
                {
                    i++;
                }
            }
            return list;
        }

        private static NetPeer ParsePeerObject(string obj)
        {
            var p = new NetPeer();
            p.Sid = ExtractStringField(obj, "sid");
            p.Protocol = ExtractStringField(obj, "protocol");
            p.Addr = ExtractStringField(obj, "addr");
            p.Role = ExtractStringField(obj, "role");
            p.Name = ExtractStringField(obj, "name");
            long v;
            if (TryExtractLongField(obj, "sinceMs", out v))
                p.SinceMs = v;
            if (TryExtractLongField(obj, "lastSeenMs", out v))
                p.LastSeenMs = v;
            return p;
        }

        /// <summary>为姊妹扩展类暴露的单对象解析 (仅内部调用).</summary>
        internal static NetPeer ParsePeerObjectPublic(string obj) => ParsePeerObject(obj);

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n'))
                i++;
        }

        internal static string ExtractStringField(string obj, string field)
        {
            string key = "\"" + field + "\"";
            int idx = obj.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0)
                return string.Empty;
            int i = idx + key.Length;
            while (i < obj.Length && (obj[i] == ' ' || obj[i] == ':'))
                i++;
            if (i >= obj.Length || obj[i] != '"')
                return string.Empty;
            i++;
            var sb = new StringBuilder();
            bool esc = false;
            for (; i < obj.Length; i++)
            {
                char c = obj[i];
                if (esc)
                {
                    sb.Append(c);
                    esc = false;
                    continue;
                }
                if (c == '\\')
                {
                    esc = true;
                    continue;
                }
                if (c == '"')
                    break;
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static bool TryExtractLongField(string obj, string field, out long value)
        {
            value = 0;
            string key = "\"" + field + "\"";
            int idx = obj.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0)
                return false;
            int i = idx + key.Length;
            while (i < obj.Length && (obj[i] == ' ' || obj[i] == ':'))
                i++;
            int start = i;
            while (i < obj.Length && (char.IsDigit(obj[i]) || obj[i] == '-'))
                i++;
            if (i == start)
                return false;
            return long.TryParse(obj.Substring(start, i - start), out value);
        }
    }

    // =====================================================================
    // 阶段 3: 命名、握手、对端事件、定向发送
    //   保留通道:
    //     "_self"  宿主→插件  JSON {"name","role","tags":[...]}   设置自身身份
    //     "_sendto" 宿主→插件  "addr\n<frame>" 或 "sid:<sid>\n<frame>" 定向发送
    //     "_event"  插件→宿主  JSON {"evt":"peer_join"|"peer_leave"|"peer_update","data":{peer}}
    // =====================================================================

    public sealed class PeerEventArgs : EventArgs
    {
        public NetPeer Peer { get; }

        public PeerEventArgs(NetPeer p)
        {
            Peer = p;
        }
    }

    public static class NetIONamingExtensions
    {
        internal const string SelfMessage = NetIOExtensions.SelfUpdateMessage;
        internal const string SendToMessage = NetIOExtensions.FrameSendToMessage;

        private sealed class NamingState
        {
            public EventHandler<PeerEventArgs> JoinHandlers;
            public EventHandler<PeerEventArgs> LeaveHandlers;
            public EventHandler<PeerEventArgs> UpdateHandlers;
            public int JoinHandlerId = -1;
            public int LeaveHandlerId = -1;
            public int UpdateHandlerId = -1;
            public readonly object Lock = new object();
        }

        private static readonly ConditionalWeakTable<IODevice, NamingState> _states =
            new ConditionalWeakTable<IODevice, NamingState>();

        private static NamingState GetState(IODevice device)
        {
            return _states.GetValue(
                device,
                d =>
                {
                    var s = new NamingState();
                    s.JoinHandlerId = d.DebugBindPluginEvent(
                        "peer_join",
                        (evt, bytes) => DispatchPeerEvent(d, s, bytes, PeerEventKind.Join)
                    );
                    s.LeaveHandlerId = d.DebugBindPluginEvent(
                        "peer_leave",
                        (evt, bytes) => DispatchPeerEvent(d, s, bytes, PeerEventKind.Leave)
                    );
                    s.UpdateHandlerId = d.DebugBindPluginEvent(
                        "peer_update",
                        (evt, bytes) => DispatchPeerEvent(d, s, bytes, PeerEventKind.Update)
                    );
                    return s;
                }
            );
        }

        private enum PeerEventKind
        {
            Join,
            Leave,
            Update
        }

        private static void DispatchPeerEvent(
            IODevice device,
            NamingState state,
            byte[] bytes,
            PeerEventKind kind
        )
        {
            if (bytes == null || bytes.Length == 0)
                return;

            string json;
            try
            {
                json = Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return;
            }

            var peer = NetIOPeerExtensions.ParsePeerObjectPublic(json);
            if (peer == null)
                return;

            var args = new PeerEventArgs(peer);
            EventHandler<PeerEventArgs> handler = null;
            lock (state.Lock)
            {
                if (kind == PeerEventKind.Join)
                    handler = state.JoinHandlers;
                else if (kind == PeerEventKind.Leave)
                    handler = state.LeaveHandlers;
                else
                    handler = state.UpdateHandlers;
            }
            handler?.Invoke(device, args);
        }

        /// <summary>订阅对端加入事件 (ws/tcp 入站连入 / 出站连上; udp 首次收到报文).</summary>
        public static void OnPeerJoin(this IODevice device, EventHandler<PeerEventArgs> handler)
        {
            if (device == null)
                throw new ArgumentNullException(nameof(device));
            if (handler == null)
                return;
            var s = GetState(device);
            lock (s.Lock)
                s.JoinHandlers =
                    (EventHandler<PeerEventArgs>)Delegate.Combine(s.JoinHandlers, handler);
        }

        /// <summary>订阅对端离线事件 (ws/tcp 断连; udp 超时).</summary>
        public static void OnPeerLeave(this IODevice device, EventHandler<PeerEventArgs> handler)
        {
            if (device == null)
                throw new ArgumentNullException(nameof(device));
            if (handler == null)
                return;
            var s = GetState(device);
            lock (s.Lock)
                s.LeaveHandlers =
                    (EventHandler<PeerEventArgs>)Delegate.Combine(s.LeaveHandlers, handler);
        }

        /// <summary>订阅对端 _hello 更新事件 (收到对端自报名/tags 时).</summary>
        public static void OnPeerUpdate(this IODevice device, EventHandler<PeerEventArgs> handler)
        {
            if (device == null)
                throw new ArgumentNullException(nameof(device));
            if (handler == null)
                return;
            var s = GetState(device);
            lock (s.Lock)
                s.UpdateHandlers =
                    (EventHandler<PeerEventArgs>)Delegate.Combine(s.UpdateHandlers, handler);
        }

        /// <summary>设置本节点的 name/tags/role. 设置后会向所有已连对端重发一次 _hello 帧.</summary>
        public static void SetSelfName(
            this IODevice device,
            string name,
            IEnumerable<string> tags = null,
            string role = null
        )
        {
            if (device == null)
                throw new ArgumentNullException(nameof(device));
            var sb = new StringBuilder();
            sb.Append('{');
            bool first = true;
            if (name != null)
            {
                sb.Append("\"name\":\"").Append(Esc(name)).Append('"');
                first = false;
            }
            if (role != null)
            {
                if (!first)
                    sb.Append(',');
                sb.Append("\"role\":\"").Append(Esc(role)).Append('"');
                first = false;
            }
            if (tags != null)
            {
                if (!first)
                    sb.Append(',');
                sb.Append("\"tags\":[");
                bool ft = true;
                foreach (var t in tags)
                {
                    if (t == null)
                        continue;
                    if (!ft)
                        sb.Append(',');
                    sb.Append('"').Append(Esc(t)).Append('"');
                    ft = false;
                }
                sb.Append(']');
            }
            sb.Append('}');
            // 确保 peer 事件绑定已初始化，避免首次使用时状态表尚未建立。
            GetState(device);
            NetIOExtensions.WriteFrameChannel(device, SelfMessage, sb.ToString());
        }

        /// <summary>定向发送一个 evt 帧到 "ip:port" 对端 (出站池 → 入站会话 → 懒拨号).</summary>
        public static void RequestTo(
            this IODevice device,
            string addr,
            string name,
            string dataJson = null
        )
        {
            if (device == null)
                throw new ArgumentNullException(nameof(device));
            if (string.IsNullOrEmpty(addr))
                throw new ArgumentNullException(nameof(addr));
            SendToTarget(device, addr, BuildEventJson(name, dataJson));
        }

        /// <summary>定向发送一个 evt 帧到指定 sid 的对端.</summary>
        public static void RequestToSid(
            this IODevice device,
            string sid,
            string name,
            string dataJson = null
        )
        {
            if (device == null)
                throw new ArgumentNullException(nameof(device));
            if (string.IsNullOrEmpty(sid))
                throw new ArgumentNullException(nameof(sid));
            SendToTarget(device, "sid:" + sid, BuildEventJson(name, dataJson));
        }

        /// <summary>按对端自报名定向发送. 查不到则静默丢弃. 首次调用建议先 await GetPeersAsync.</summary>
        public static void RequestToName(
            this IODevice device,
            string peerName,
            string name,
            string dataJson = null
        )
        {
            if (device == null)
                throw new ArgumentNullException(nameof(device));
            if (string.IsNullOrEmpty(peerName))
                throw new ArgumentNullException(nameof(peerName));
            var peers = device.GetPeers(500);
            foreach (var p in peers)
            {
                if (p.Name == peerName)
                {
                    SendToTarget(device, "sid:" + p.Sid, BuildEventJson(name, dataJson));
                    return;
                }
            }
        }

        private static void SendToTarget(IODevice device, string target, string frameJson)
        {
            var payload = "{\"target\":\"" + Esc(target) + "\",\"frame\":" + frameJson + "}";
            NetIOExtensions.WriteFrameChannel(device, SendToMessage, payload);
        }

        private static string BuildEventJson(string name, string dataJson)
        {
            var sb = new StringBuilder();
            sb.Append("{\"type\":\"evt\",\"name\":\"").Append(Esc(name)).Append('"');
            if (!string.IsNullOrEmpty(dataJson))
                sb.Append(",\"data\":").Append(dataJson);
            sb.Append('}');
            return sb.ToString();
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s ?? string.Empty;
            var sb = new StringBuilder(s.Length + 4);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                            sb.AppendFormat("\\u{0:x4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
