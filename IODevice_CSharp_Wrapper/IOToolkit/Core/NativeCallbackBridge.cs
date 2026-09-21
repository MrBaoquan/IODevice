using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// 这几个回调委托声明在 IONativeWrapper 内部，此处取别名以便正文简洁。
using ChannelRequestCallback = IOToolkit.Core.IONativeWrapper.ChannelRequestCallback;
using MotionEventCallbackDelegate = IOToolkit.Core.IONativeWrapper.MotionEventCallbackDelegate;
using MotionEventDataCallbackDelegate = IOToolkit.Core.IONativeWrapper.MotionEventDataCallbackDelegate;

// 本程序集在 Unity 之外编译（不引用 UnityEngine），但同一份产物会被 IL2CPP 转译进播放器。
// 这里提供 AOT.MonoPInvokeCallbackAttribute 的替身，用途与 UnityEngine 中同名特性一致：
// 告知 IL2CPP 该静态方法被用作原生回调入口，需要生成反向 P/Invoke 包装。
// 已在 Unity 内编译时跳过，避免与 UnityEngine 的同名类型冲突。
#if !UNITY_5_3_OR_NEWER
namespace AOT
{
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class MonoPInvokeCallbackAttribute : Attribute
    {
        public MonoPInvokeCallbackAttribute(Type type) { }
    }
}
#endif

namespace IOToolkit.Core
{
    /// <summary>
    /// 原生回调桥接层。
    ///
    /// IL2CPP 不允许把"指向实例方法的委托"（闭包即实例方法）封送到原生代码，运行时会抛
    /// <c>NotSupportedException: IL2CPP does not support marshaling delegates that point to
    /// instance methods to native code.</c>；Mono 则允许，因此同一份包装层在 Windows 正常、
    /// 在 Android/IL2CPP 上首个绑定即失败。
    ///
    /// 本层把"每次绑定传一个闭包给原生"改为"传静态入口给原生 + 在托管侧登记表里还原绑定"：
    ///   · 按键 / 动作 / 轴：原生回调不携带绑定身份 → 静态 thunk 槽位池，按槽位索引还原绑定；
    ///   · 通道 / 插件事件：原生回调携带 topic 名 → 单一静态方法按名分发，容量不受限。
    ///
    /// 登记的实际回调仍可以是闭包——它们只在托管代码内被调用，不参与原生封送。
    /// 所有交给原生侧的委托都由 static readonly 字段持有，Mono 与 IL2CPP 都不会回收或裁剪。
    /// 对外接口不变：调用方照常传 lambda，槽位分配与复用全部在内部完成。
    /// </summary>
    internal static class NativeCallbackBridge
    {
        // ==================================================================
        // 一、按键 / 动作 / 轴：静态 thunk 槽位池
        // ==================================================================

        private sealed class SlotEntry
        {
            public Delegate Handler;
        }

        /// <summary>
        /// 固定容量的槽位池。槽位按"绑定身份"分配：同一身份重复绑定（例如每次 ReCompile
        /// 的 rebindIOEvents）复用原槽位并替换回调，因此容量只与"不同绑定的条数"有关，
        /// 不会随重复绑定增长。
        /// </summary>
        private sealed class SlotPool
        {
            private readonly SlotEntry[] _slots;
            private readonly Dictionary<string, int> _indexByKey;
            private readonly Stack<int> _free;
            private readonly object _gate = new object();

            public SlotPool(int capacity, string poolName)
            {
                PoolName = poolName;
                _slots = new SlotEntry[capacity];
                _indexByKey = new Dictionary<string, int>(StringComparer.Ordinal);
                _free = new Stack<int>(capacity);
                for (int i = capacity - 1; i >= 0; i--)
                    _free.Push(i);
            }

            public string PoolName { get; private set; }

            public int Capacity
            {
                get { return _slots.Length; }
            }

            public int Register(string bindingKey, Delegate handler)
            {
                lock (_gate)
                {
                    int index;
                    if (_indexByKey.TryGetValue(bindingKey, out index))
                    {
                        _slots[index].Handler = handler;
                        return index;
                    }

                    if (_free.Count == 0)
                    {
                        throw new InvalidOperationException(
                            string.Format(
                                "IOToolkit {0} 原生回调槽位已用尽（容量 {1}）。"
                                    + "已登记的绑定身份数超过上限，请缩小配置中不同绑定键的数量。"
                                    + "待登记 key={2}",
                                PoolName,
                                _slots.Length,
                                bindingKey
                            )
                        );
                    }

                    index = _free.Pop();
                    _slots[index] = new SlotEntry { Handler = handler };
                    _indexByKey.Add(bindingKey, index);
                    return index;
                }
            }

            public void Release(string bindingKey)
            {
                lock (_gate)
                {
                    int index;
                    if (!_indexByKey.TryGetValue(bindingKey, out index))
                        return;
                    _indexByKey.Remove(bindingKey);
                    _slots[index] = null;
                    _free.Push(index);
                }
            }

            public Delegate Get(int index)
            {
                if (index < 0 || index >= _slots.Length)
                    return null;
                var entry = _slots[index];
                return entry == null ? null : entry.Handler;
            }

            public void Clear()
            {
                lock (_gate)
                {
                    for (int i = 0; i < _slots.Length; i++)
                        _slots[i] = null;
                    _indexByKey.Clear();
                    _free.Clear();
                    for (int i = _slots.Length - 1; i >= 0; i--)
                        _free.Push(i);
                }
            }
        }

        private const int ActionPoolCapacity = 16;
        private const int ActionWithKeyPoolCapacity = 128;
        private const int AxisPoolCapacity = 32;

        private static readonly SlotPool s_actionPool = new SlotPool(ActionPoolCapacity, "按键(无参)");
        private static readonly SlotPool s_actionWithKeyPool = new SlotPool(
            ActionWithKeyPoolCapacity,
            "按键/动作"
        );
        private static readonly SlotPool s_axisPool = new SlotPool(AxisPoolCapacity, "轴");

        /// <summary>由设备实例 + 设备内部标识拼出绑定身份，保证同设备同绑定复用槽位。</summary>
        public static string BindingKey(string device, string kind, string name, int eventType)
        {
            return device + "|" + kind + "|" + name + "|" + eventType.ToString();
        }

        public static NativeActionSignature RegisterAction(string bindingKey, Action handler)
        {
            int index = s_actionPool.Register(bindingKey, handler);
            return NativeThunks.ActionThunks[index];
        }

        public static NativeActionWithKeySignature RegisterActionWithKey(
            string bindingKey,
            Action<string> handler
        )
        {
            int index = s_actionWithKeyPool.Register(bindingKey, handler);
            return NativeThunks.ActionWithKeyThunks[index];
        }

        public static NativeAxisSignature RegisterAxis(string bindingKey, Action<float> handler)
        {
            int index = s_axisPool.Register(bindingKey, handler);
            return NativeThunks.AxisThunks[index];
        }

        public static void ReleaseBinding(string bindingKey)
        {
            s_actionPool.Release(bindingKey);
            s_actionWithKeyPool.Release(bindingKey);
            s_axisPool.Release(bindingKey);
        }

        /// <summary>释放全部槽位（设备整体卸载后，原有绑定已随原生侧失效）。</summary>
        public static void ReleaseAllBindings()
        {
            s_actionPool.Clear();
            s_actionWithKeyPool.Clear();
            s_axisPool.Clear();
            // 通道订阅同样必须归还：原生设备已随 Unload 销毁，残留槽位会在重绑时耗尽
            // （池容量 64，rebindIOEvents 一轮占 26）。
            s_channelPool.Clear();
        }

        // ── thunk 入口（由 NativeThunks 的静态方法调用）────────────────

        internal static void InvokeAction(int index)
        {
            var handler = s_actionPool.Get(index) as Action;
            if (handler == null)
                return;
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                ReportCallbackError("按键(无参)", index, ex);
            }
        }

        internal static void InvokeActionWithKey(int index, string key)
        {
            var handler = s_actionWithKeyPool.Get(index) as Action<string>;
            if (handler == null)
                return;
            try
            {
                handler(key);
            }
            catch (Exception ex)
            {
                ReportCallbackError("按键/动作", index, ex);
            }
        }

        internal static void InvokeAxis(int index, float value)
        {
            var handler = s_axisPool.Get(index) as Action<float>;
            if (handler == null)
                return;
            try
            {
                handler(value);
            }
            catch (Exception ex)
            {
                ReportCallbackError("轴", index, ex);
            }
        }

        // ==================================================================
        // 二、通道 / 插件事件：静态 thunk 槽位池
        // ==================================================================
        //
        // 注意：这里**不能**按"原生回传的通道名"分发。原生 SubscribeChannel /
        // SubscribeChannelRequest 回调里传来的名字是插件信封层面的通道
        // （NETIO 侧为通用 _rpc.req / _rpc.res 之类），并不等于业务订阅的 topic，
        // 用名字查表会永远查不到、静默丢帧。故与按键一致，按槽位索引还原订阅。

        private sealed class ChannelSlot
        {
            public string Device;
            public string Name;
            public PluginChannelCallback Message;
            public ChannelRequestCallback Request;
        }

        private sealed class ChannelSlotPool
        {
            private readonly ChannelSlot[] _slots;

            public ChannelSlotPool(int capacity)
            {
                _slots = new ChannelSlot[capacity];
            }

            public int Capacity
            {
                get { return _slots.Length; }
            }

            public int Allocate(
                string device,
                string name,
                PluginChannelCallback message,
                ChannelRequestCallback request
            )
            {
                lock (_slots)
                {
                    for (int i = 0; i < _slots.Length; i++)
                    {
                        if (_slots[i] != null)
                            continue;
                        _slots[i] = new ChannelSlot
                        {
                            Device = device,
                            Name = name,
                            Message = message,
                            Request = request,
                        };
                        return i;
                    }
                }

                throw new InvalidOperationException(
                    string.Format(
                        "IOToolkit 通道订阅槽位已用尽（容量 {0}）。已存在 {0} 个未释放的订阅，"
                            + "请缩小业务通道数或提高 NativeThunks 的通道槽位容量。待注册 name={1}",
                        _slots.Length,
                        name
                    )
                );
            }

            public void Release(int index)
            {
                if (index < 0 || index >= _slots.Length)
                    return;
                lock (_slots)
                    _slots[index] = null;
            }

            public ChannelSlot Peek(int index)
            {
                if (index < 0 || index >= _slots.Length)
                    return null;
                return _slots[index];
            }

            public void Clear()
            {
                lock (_slots)
                {
                    for (int i = 0; i < _slots.Length; i++)
                        _slots[i] = null;
                }
            }
        }

        private const int ChannelSlotCapacity = 64;

        private static readonly ChannelSlotPool s_channelPool = new ChannelSlotPool(
            ChannelSlotCapacity
        );

        /// <summary>
        /// 登记一个 topic 的消息回调（PluginChannelCallback）与/或请求回调。
        /// 返回槽位句柄，调用方据此取静态入口，并在释放时用同一句柄注销。
        /// </summary>
        public static int RegisterChannel(
            string device,
            string name,
            PluginChannelCallback message,
            ChannelRequestCallback request
        )
        {
            return s_channelPool.Allocate(device, name, message, request);
        }

        public static void UnregisterChannel(int slot)
        {
            s_channelPool.Release(slot);
        }

        public static PluginChannelCallback MessageThunkAt(int slot)
        {
            if (slot < 0 || slot >= NativeThunks.ChannelMessageThunks.Length)
                return null;
            return NativeThunks.ChannelMessageThunks[slot];
        }

        public static ChannelRequestCallback RequestThunkAt(int slot)
        {
            if (slot < 0 || slot >= NativeThunks.ChannelRequestThunks.Length)
                return null;
            return NativeThunks.ChannelRequestThunks[slot];
        }

        // 由静态 thunk 转发进来的入口（每个封送方法均带 [AOT.MonoPInvokeCallback]）

        internal static void InvokeChannelMessage(
            int slot,
            string channelName,
            IntPtr data,
            uint size
        )
        {
            var entry = s_channelPool.Peek(slot);
            var handler = entry == null ? null : entry.Message;
            if (handler == null)
                return;
            try
            {
                handler(channelName, data, size);
            }
            catch (Exception ex)
            {
                ReportCallbackError("channel:" + (entry.Name ?? "?"), entry.Device, ex);
            }
        }

        internal static void InvokeChannelRequest(
            int slot,
            string name,
            string requestId,
            IntPtr payloadJson,
            uint payloadSize,
            IntPtr metadataJson,
            uint metadataSize
        )
        {
            var entry = s_channelPool.Peek(slot);
            var handler = entry == null ? null : entry.Request;
            if (handler == null)
                return;
            try
            {
                handler(name, requestId, payloadJson, payloadSize, metadataJson, metadataSize);
            }
            catch (Exception ex)
            {
                ReportCallbackError("channelRequest:" + (entry.Name ?? "?"), entry.Device, ex);
            }
        }

        // ==================================================================
        // 三、Motion 全局回调（原生侧为全局单例 setter，无需槽位）
        // ==================================================================

        private static MotionEventCallbackDelegate s_motionEventThunk;
        private static MotionEventDataCallbackDelegate s_motionEventDataThunk;

        public static MotionEventCallbackDelegate MotionEventThunk
        {
            get
            {
                if (s_motionEventThunk == null)
                    s_motionEventThunk = OnMotionEvent;
                return s_motionEventThunk;
            }
        }

        public static MotionEventDataCallbackDelegate MotionEventDataThunk
        {
            get
            {
                if (s_motionEventDataThunk == null)
                    s_motionEventDataThunk = OnMotionEventData;
                return s_motionEventDataThunk;
            }
        }

        private static Action<string, int> s_motionEventHandler;
        private static Action<string, int, string> s_motionEventDataHandler;

        public static void SetMotionEventHandler(Action<string, int> handler)
        {
            s_motionEventHandler = handler;
        }

        public static void SetMotionEventDataHandler(Action<string, int, string> handler)
        {
            s_motionEventDataHandler = handler;
        }

        [AOT.MonoPInvokeCallback(typeof(MotionEventCallbackDelegate))]
        public static void OnMotionEvent(string slotId, int eventType)
        {
            var handler = s_motionEventHandler;
            if (handler == null)
                return;
            try
            {
                handler(slotId, eventType);
            }
            catch (Exception ex)
            {
                ReportCallbackError("motionEvent", slotId, ex);
            }
        }

        [AOT.MonoPInvokeCallback(typeof(MotionEventDataCallbackDelegate))]
        public static void OnMotionEventData(string slotId, int eventType, string eventData)
        {
            var handler = s_motionEventDataHandler;
            if (handler == null)
                return;
            try
            {
                handler(slotId, eventType, eventData);
            }
            catch (Exception ex)
            {
                ReportCallbackError("motionEventData", slotId, ex);
            }
        }

        // ==================================================================
        // 四、异常上报
        // ==================================================================
        //
        // 本程序集不引用 UnityEngine，无法直接 Debug.LogError。回调异常若任由其逃逸，
        // 会穿过原生栈帧造成不可预期行为，因此必须就地兜住；同时通过可选的日志钩子
        // 把错误交给宿主（Unity 侧可挂接成 Debug.LogError）。

        /// <summary>可选日志钩子，签名 (message, exception)。宿主可挂接到 Unity 日志。</summary>
        public static Action<string, Exception> ErrorSink;

        private static void ReportCallbackError(string source, object detail, Exception ex)
        {
            var sink = ErrorSink;
            if (sink != null)
            {
                try
                {
                    sink(
                        string.Format(
                            "[IOToolkit] 原生回调异常 source={0} detail={1}: {2}",
                            source,
                            detail ?? "-",
                            ex == null ? "unknown" : ex.Message
                        ),
                        ex
                    );
                }
                catch
                {
                    // 上报失败不得再次抛出，否则会在原生回调栈里形成二次异常。
                }
            }
        }
    }
}
