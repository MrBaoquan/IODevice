using IOToolkit.Core;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace IOToolkit
{
    /// <summary>
    /// IO设备
    /// </summary>
    public abstract partial class IODevice
    {
        /// <summary>
        /// 构造设备
        /// </summary>
        /// <param name="InID"></param>
        public IODevice(string InID)
        {
            this.ID = InID;
        }

        // ── 原生回调注册（IL2CPP 兼容）────────────────────────────────────
        //
        // IL2CPP 不接受把"指向实例方法的委托"（闭包即实例方法）封送到原生代码，运行时抛
        // NotSupportedException；Mono 允许，故此前只在 Android 上失效。这里统一改走
        // NativeCallbackBridge：交给原生的是静态 thunk，真实回调登记在托管侧。
        //
        // 槽位按"绑定身份"分配（设备 + 类型 + 目标名 + 事件），因此同一绑定的重复注册
        // （典型如每轮 ReCompile 都会跑的 rebindIOEvents）只刷新回调，不会持续占用新槽位。

        private string BindingKey(string kind, string target, InputEvent InEvent)
        {
            return NativeCallbackBridge.BindingKey(this.ID, kind, target, (int)InEvent);
        }

        /// <summary>调试用通道绑定的原生 handlerId → 桥接登记句柄，用于反注册。</summary>
        private Dictionary<int, int> _channelHandles;

        private void TrackChannelHandle(int nativeHandlerId, int bridgeHandle)
        {
            if (nativeHandlerId < 0 || bridgeHandle < 0)
                return;
            if (_channelHandles == null)
                _channelHandles = new Dictionary<int, int>();
            _channelHandles[nativeHandlerId] = bridgeHandle;
        }

        private int TakeChannelHandle(int nativeHandlerId)
        {
            if (_channelHandles == null || nativeHandlerId < 0)
                return -1;
            int bridgeHandle;
            if (!_channelHandles.TryGetValue(nativeHandlerId, out bridgeHandle))
                return -1;
            _channelHandles.Remove(nativeHandlerId);
            return bridgeHandle;
        }

        public string DllName()
        {
            return IONativeWrapper.DeviceDllName(this.ID);
        }

        public string IOType()
        {
            return IONativeWrapper.DeviceIOType(this.ID);
        }

        public int Index()
        {
            return IONativeWrapper.DeviceIndex(this.ID);
        }

        /// <summary>
        /// 设备是否合法  当前无效
        /// </summary>
        /// <returns></returns>

        public bool IsValid()
        {
            return IONativeWrapper.IsValid(this.ID);
        }

        /// <summary>
        /// 绑定一个Key的事件
        /// </summary>
        /// <param name="InKey">需要绑定的按键</param>
        /// <param name="InEvent">事件类型</param>
        /// <param name="InHandler">事件响应回调</param>
        public void BindKey(Key InKey, InputEvent InEvent, Action InHandler)
        {
            IONativeWrapper.BindKey(
                this.ID,
                InKey,
                (int)InEvent,
                NativeCallbackBridge.RegisterAction(BindingKey("key", InKey, InEvent), InHandler)
            );
        }

        /// <summary>
        /// 绑定一个Key的事件 回调携带Key参数
        /// </summary>
        /// <param name="InKey">需要绑定的按键</param>
        /// <param name="InEvent">事件类型</param>
        /// <param name="InHandler">事件响应回调</param>
        public void BindKey(Key InKey, InputEvent InEvent, Action<Key> InHandler)
        {
            IONativeWrapper.BindKeyWithKey(
                this.ID,
                InKey,
                (int)InEvent,
                NativeCallbackBridge.RegisterActionWithKey(
                    BindingKey("key", InKey, InEvent),
                    key => InHandler(key)
                )
            );
        }

        public void BindKey<T1>(Key InKey, InputEvent InEvent, Action<T1> InHandler, T1 _param1)
        {
            BindKey(InKey, InEvent, () => InHandler.Invoke(_param1));
        }

        /// <summary>
        /// 绑定Key事件 回调携带Key及1个自定义参数
        /// </summary>
        public void BindKey<T1>(
            Key InKey,
            InputEvent InEvent,
            Action<Key, T1> InHandler,
            T1 _param1
        )
        {
            BindKey(InKey, InEvent, (Key key) => InHandler.Invoke(key, _param1));
        }

        public void BindKey<T1, T2>(
            Key InKey,
            InputEvent InEvent,
            Action<T1, T2> InHandler,
            T1 _param1,
            T2 _param2
        )
        {
            BindKey(InKey, InEvent, () => InHandler.Invoke(_param1, _param2));
        }

        /// <summary>
        /// 绑定Key事件 回调携带Key及2个自定义参数
        /// </summary>
        public void BindKey<T1, T2>(
            Key InKey,
            InputEvent InEvent,
            Action<Key, T1, T2> InHandler,
            T1 _param1,
            T2 _param2
        )
        {
            BindKey(InKey, InEvent, (Key key) => InHandler.Invoke(key, _param1, _param2));
        }

        public void BindKey<T1, T2, T3>(
            Key InKey,
            InputEvent InEvent,
            Action<T1, T2, T3> InHandler,
            T1 _param1,
            T2 _param2,
            T3 _param3
        )
        {
            BindKey(InKey, InEvent, () => InHandler.Invoke(_param1, _param2, _param3));
        }

        /// <summary>
        /// 绑定Key事件 回调携带Key及3个自定义参数
        /// </summary>
        public void BindKey<T1, T2, T3>(
            Key InKey,
            InputEvent InEvent,
            Action<Key, T1, T2, T3> InHandler,
            T1 _param1,
            T2 _param2,
            T3 _param3
        )
        {
            BindKey(InKey, InEvent, (Key key) => InHandler.Invoke(key, _param1, _param2, _param3));
        }

        /// <summary>
        /// 绑定Action事件
        /// </summary>
        /// <param name="InActionName">Action 名称</param>
        /// <param name="InEvent">事件类型</param>
        /// <param name="InHandler">事件响应回调</param>

        public void BindAction(string InActionName, InputEvent InEvent, Action InHandler)
        {
            IONativeWrapper.BindAction(
                this.ID,
                InActionName,
                (int)InEvent,
                NativeCallbackBridge.RegisterActionWithKey(
                    BindingKey("action", InActionName, InEvent),
                    key => InHandler()
                )
            );
        }

        /// <summary>
        /// 绑定Action事件 回调携带Key参数
        /// </summary>
        /// <param name="InActionName">Action名称</param>
        /// <param name="InEvent">事件类型</param>
        /// <param name="InHandler">事件响应回调</param>
        public void BindAction(string InActionName, InputEvent InEvent, Action<Key> InHandler)
        {
            IONativeWrapper.BindAction(
                this.ID,
                InActionName,
                (int)InEvent,
                NativeCallbackBridge.RegisterActionWithKey(
                    BindingKey("action", InActionName, InEvent),
                    key => InHandler(key)
                )
            );
        }

        /// <summary>
        /// 绑定Action事件 回调携带1个自定义参数
        /// </summary>
        /// <typeparam name="T1">自定义参数类型</typeparam>
        /// <param name="InActionName">Action名称</param>
        /// <param name="InEvent">事件类型</param>
        /// <param name="InHandler">事件响应回调</param>
        /// <param name="_param1">自定义参数</param>

        public void BindAction<T1>(
            string InActionName,
            InputEvent InEvent,
            Action<T1> InHandler,
            T1 _param1
        )
        {
            BindAction(InActionName, InEvent, (Key _key) => InHandler(_param1));
        }

        /// <summary>
        /// 绑定Action事件 回调携带Key及1个自定义参数
        /// </summary>
        /// <typeparam name="T1"></typeparam>
        /// <param name="InActionName">Action名称</param>
        /// <param name="InEvent">事件类型</param>
        /// <param name="InHandler">事件响应回调</param>
        /// <param name="_param1">自定义参数</param>
        public void BindAction<T1>(
            string InActionName,
            InputEvent InEvent,
            Action<Key, T1> InHandler,
            T1 _param1
        )
        {
            BindAction(InActionName, InEvent, (Key key) => InHandler(key, _param1));
        }

        /// <summary>
        /// 绑定Action事件, 回调携带2个自定义参数
        /// </summary>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <param name="InActionName"></param>
        /// <param name="InEvent"></param>
        /// <param name="InHandler"></param>
        /// <param name="_param1"></param>
        /// <param name="_param2"></param>
        public void BindAction<T1, T2>(
            string InActionName,
            InputEvent InEvent,
            Action<T1, T2> InHandler,
            T1 _param1,
            T2 _param2
        )
        {
            BindAction(InActionName, InEvent, (Key _key) => InHandler(_param1, _param2));
        }

        /// <summary>
        /// 绑定Action事件 回调携带Key及2个自定义参数
        /// </summary>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <param name="InActionName"></param>
        /// <param name="InEvent"></param>
        /// <param name="InHandler"></param>
        /// <param name="_param1"></param>
        /// <param name="_param2"></param>
        public void BindAction<T1, T2>(
            string InActionName,
            InputEvent InEvent,
            Action<Key, T1, T2> InHandler,
            T1 _param1,
            T2 _param2
        )
        {
            BindAction(InActionName, InEvent, (Key key) => InHandler(key, _param1, _param2));
        }

        /// <summary>
        /// 绑定Action事件 回调携带3个自定义参数
        /// </summary>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <typeparam name="T3"></typeparam>
        /// <param name="InActionName"></param>
        /// <param name="InEvent"></param>
        /// <param name="InHandler"></param>
        /// <param name="_param1"></param>
        /// <param name="_param2"></param>
        /// <param name="_param3"></param>
        public void BindAction<T1, T2, T3>(
            string InActionName,
            InputEvent InEvent,
            Action<T1, T2, T3> InHandler,
            T1 _param1,
            T2 _param2,
            T3 _param3
        )
        {
            BindAction(InActionName, InEvent, (Key _key) => InHandler(_param1, _param2, _param3));
        }

        /// <summary>
        /// 绑定Action事件， 回调携带Key及3个自定义参数
        /// </summary>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <typeparam name="T3"></typeparam>
        /// <param name="InActionName"></param>
        /// <param name="InEvent"></param>
        /// <param name="InHandler"></param>
        /// <param name="_param1"></param>
        /// <param name="_param2"></param>
        /// <param name="_param3"></param>
        public void BindAction<T1, T2, T3>(
            string InActionName,
            InputEvent InEvent,
            Action<Key, T1, T2, T3> InHandler,
            T1 _param1,
            T2 _param2,
            T3 _param3
        )
        {
            BindAction(
                InActionName,
                InEvent,
                (Key key) => InHandler(key, _param1, _param2, _param3)
            );
        }

        /// <summary>
        /// 绑定Axis事件
        /// </summary>
        /// <param name="InAxisName">Axis 名称</param>
        /// <param name="InHandler">事件响应回调</param>
        public void BindAxis(string InAxisName, Action<float> InHandler)
        {
            IONativeWrapper.BindAxis(
                this.ID,
                InAxisName,
                NativeCallbackBridge.RegisterAxis(
                    BindingKey("axis", InAxisName, InputEvent.IE_Axis),
                    InHandler
                )
            );
        }

        public void BindAxisKey(Key InKey, Action<float> InHandler)
        {
            IONativeWrapper.BindAxisKey(
                this.ID,
                InKey,
                NativeCallbackBridge.RegisterAxis(
                    BindingKey("axis", InKey, InputEvent.IE_Axis),
                    InHandler
                )
            );
        }

        /// <summary>
        /// 获取一个键的输出值
        /// </summary>
        /// <param name="InKey"></param>
        /// <returns></returns>
        public float GetDO(Key InKey)
        {
            return IONativeWrapper.GetDOSingle(this.ID, InKey);
        }

        /// <summary>
        /// 获取一个OAction输出值
        /// </summary>
        /// <param name="InOAction"></param>
        /// <returns></returns>
        public float GetDO(string InOAction)
        {
            return IONativeWrapper.GetDOAction(this.ID, InOAction);
        }

        /// <summary>
        /// 获取所有通道的输出值
        /// </summary>
        /// <param name="DOStatus"></param>
        /// <returns></returns>
        public int GetDO(float[] DOStatus)
        {
            return IONativeWrapper.GetDOAll(this.ID, DOStatus);
        }

        public int RefreshStreamingData(byte[] StreamingData)
        {
            return IONativeWrapper.RefreshStreamingData(
                this.ID,
                StreamingData,
                (UInt32)StreamingData.Length
            );
        }

        /// <summary>
        /// 设置一个键的输出值
        /// </summary>
        /// <param name="InKey"></param>
        /// <param name="InStatus"></param>
        /// <returns></returns>
        public int SetDO(Key InKey, float InStatus)
        {
            return IONativeWrapper.SetDOSingle(this.ID, InKey, InStatus);
        }

        /// <summary>
        /// 设置所有键的输出值
        /// </summary>
        /// <param name="InStatus"></param>
        /// <returns></returns>
        public int SetDO(float[] InStatus)
        {
            return IONativeWrapper.SetDOAll(this.ID, InStatus);
        }

        /// <summary>
        /// 设置指定OAction的输出值
        /// </summary>
        /// <param name="OAction">OAction 名称</param>
        /// <param name="InVal"></param>
        /// <param name="bIgnoreMassage"></param>
        /// <returns></returns>
        public int SetDO(string OAction, float InVal, bool bIgnoreMassage = false)
        {
            return IONativeWrapper.SetDOAction(this.ID, OAction, InVal, bIgnoreMassage);
        }

        /// <summary>
        /// 设置OAction的输出值为1
        /// </summary>
        /// <param name="OAction"></param>
        /// <returns></returns>
        public int SetDOOn(string OAction)
        {
            return IONativeWrapper.SetDOOn(this.ID, OAction);
        }

        /// <summary>
        /// 设置OAction的输出值为0
        /// </summary>
        /// <param name="OAction"></param>
        /// <returns></returns>
        public int SetDOOff(string OAction)
        {
            return IONativeWrapper.SetDOOff(this.ID, OAction);
        }

        /// <summary>
        /// 立即执行输出动作
        /// </summary>
        /// <returns></returns>
        public int DOImmediate()
        {
            return IONativeWrapper.DOImmediate(this.ID);
        }

        /// <summary>
        /// 获取一个键是否被按下
        /// </summary>
        /// <param name="InKey"></param>
        /// <returns></returns>
        public bool GetKey(string InKey)
        {
            return IONativeWrapper.GetKey(this.ID, InKey);
        }

        /// <summary>
        /// 键被按下时响应一次
        /// </summary>
        /// <param name="InKey"></param>
        /// <returns></returns>
        public bool GetKeyDown(Key InKey)
        {
            return IONativeWrapper.GetKeyDown(this.ID, InKey);
        }

        /// <summary>
        /// 获取一个键是否弹起
        /// </summary>
        /// <param name="InKey"></param>
        /// <returns></returns>
        public bool GetKeyUp(Key InKey)
        {
            return IONativeWrapper.GetKeyUp(this.ID, InKey);
        }

        /// <summary>
        /// 获取Axis值
        /// </summary>
        /// <param name="InAxisName"></param>
        /// <returns></returns>
        public float GetAxis(string InAxisName)
        {
            return IONativeWrapper.GetAxis(this.ID, InAxisName);
        }

        /// <summary>
        /// 获取键的值
        /// </summary>
        /// <param name="InKey"></param>
        /// <returns></returns>
        public float GetAxisKey(Key InKey)
        {
            return IONativeWrapper.GetAxisKey(this.ID, InKey);
        }

        /// <summary>
        /// 获取键的原始值（未经处理）
        /// </summary>
        /// <param name="InKey"></param>
        /// <returns></returns>
        public float GetRawKeyValue(Key InKey)
        {
            return IONativeWrapper.GetRawKeyValue(this.ID, InKey);
        }

        /// <summary>
        /// 获取一个键被按下的时长
        /// </summary>
        /// <param name="InKey"></param>
        /// <returns></returns>
        public float GetKeyDownDuration(Key InKey)
        {
            return IONativeWrapper.GetKeyDownDuration(this.ID, InKey);
        }

        /// <summary>
        /// 清除本设备上已绑定的键轴
        /// </summary>
        public void ClearBindings()
        {
            IONativeWrapper.ClearBindings(this.ID);
        }

        /// <summary>
        /// 设置 Axis Key 的属性 (Scale)
        /// </summary>
        /// <param name="axisName">Axis 名称</param>
        /// <param name="keyName">Key 名称</param>
        /// <param name="scale">缩放系数</param>
        /// <returns>成功返回1 失败返回0</returns>
        public int SetAKProps(string axisName, string keyName, float scale)
        {
            return IONativeWrapper.SetAKProps(this.ID, axisName, keyName, scale);
        }

        /// <summary>
        /// 设置 OAction Key 的属性 (Scale, InvertEvent)
        /// </summary>
        /// <param name="oactionName">OAction 名称</param>
        /// <param name="keyName">Key 名称</param>
        /// <param name="scale">缩放系数</param>
        /// <param name="invertEvent">是否反转事件</param>
        /// <returns>成功返回1 失败返回0</returns>
        public int SetOKProps(string oactionName, string keyName, float scale, bool invertEvent)
        {
            return IONativeWrapper.SetOKProps(this.ID, oactionName, keyName, scale, invertEvent);
        }

        /// <summary>
        /// 设置 Property Key 的属性
        /// </summary>
        /// <param name="keyName">Key 名称</param>
        /// <param name="preOffset">预偏移值</param>
        /// <param name="preScale">预缩放系数</param>
        /// <param name="minValue">最小值</param>
        /// <param name="maxValue">最大值</param>
        /// <param name="deadZone">死区</param>
        /// <param name="sensitivity">灵敏度</param>
        /// <param name="exponent">指数曲线</param>
        /// <param name="invert">是否反转数值</param>
        /// <param name="invertEvent">是否反转事件</param>
        /// <returns>成功返回1 失败返回0</returns>
        public int SetPKProps(
            string keyName,
            float offset,
            float scale,
            float minValue,
            float maxValue,
            float deadZone,
            float sensitivity,
            float exponent,
            bool invert,
            bool invertEvent
        )
        {
            return IONativeWrapper.SetPKProps(
                this.ID,
                keyName,
                offset,
                scale,
                minValue,
                maxValue,
                deadZone,
                sensitivity,
                exponent,
                invert,
                invertEvent
            );
        }

        //  ------------- Properties -------------
        public string ID;

        // 回调委托的强引用统一由 NativeCallbackBridge 的 static 数组持有（防 GC 回收），
        // 原按设备实例挂 delegateRefs 的做法已随 IL2CPP 改造移除。

        /// <summary>
        /// 查询插件通道能力声明，返回插件通过 _capabilities 通道提供的 UTF-8 JSON。
        /// </summary>
        public string QueryPluginCapabilities(int timeoutMs = 1000, int maxBytes = 64 * 1024)
        {
            if (maxBytes <= 0)
                maxBytes = 64 * 1024;
            var buffer = new byte[maxBytes];
            int size = IONativeWrapper.QueryPluginCapabilities(
                this.ID,
                buffer,
                (uint)buffer.Length,
                (uint)Math.Max(1, timeoutMs)
            );
            if (size <= 0)
                return string.Empty;
            int count = Math.Min(size, buffer.Length);
            return System.Text.Encoding.UTF8.GetString(buffer, 0, count);
        }

        /// <summary>
        /// 通过 IODevice 通用 _rpc.req/_rpc.res 通道发送请求并等待响应。
        /// </summary>
        public string SendPluginRequest(
            string topic,
            string requestJson,
            int timeoutMs = 1000,
            int maxBytes = 1024 * 1024
        )
        {
            if (string.IsNullOrEmpty(topic))
                return string.Empty;
            if (maxBytes <= 0)
                maxBytes = 1024 * 1024;
            byte[] requestBytes = System.Text.Encoding.UTF8.GetBytes(requestJson ?? string.Empty);
            var responseBuffer = new byte[maxBytes];
            int size = IONativeWrapper.SendPluginRequest(
                this.ID,
                topic,
                requestBytes,
                (uint)requestBytes.Length,
                responseBuffer,
                (uint)responseBuffer.Length,
                (uint)Math.Max(1, timeoutMs)
            );
            if (size <= 0)
                return string.Empty;
            int count = Math.Min(size, responseBuffer.Length);
            return System.Text.Encoding.UTF8.GetString(responseBuffer, 0, count);
        }

        /// <summary>
        /// 调试工具专用: 订阅插件 `_event` 原始结构化事件。
        /// 普通业务代码应使用 <see cref="Subscribe(string, Func{ChannelContext, Task{ChannelResponse}})"/>,
        /// 它内部同时覆盖了 _event 与命名通道路径。
        /// </summary>
        public int DebugBindPluginEvent(string eventName, Action<string, byte[]> handler)
        {
            if (handler == null)
                return -1;
            PluginChannelCallback proxy = (ch, dataPtr, size) =>
            {
                byte[] buf = new byte[0];
                if (dataPtr != IntPtr.Zero && size > 0)
                {
                    buf = new byte[size];
                    Marshal.Copy(dataPtr, buf, 0, (int)size);
                }
                try
                {
                    handler(ch ?? eventName ?? string.Empty, buf);
                }
                catch { }
            };
            var name = eventName ?? string.Empty;
            int slot = NativeCallbackBridge.RegisterChannel(this.ID, name, proxy, null);
            int nativeId = IONativeWrapper.BindPluginEvent(
                this.ID,
                name,
                NativeCallbackBridge.MessageThunkAt(slot)
            );
            TrackChannelHandle(nativeId, slot);
            return nativeId;
        }

        /// <summary>
        /// 调试工具专用: 取消 <see cref="DebugBindPluginEvent"/> 返回的订阅。
        /// </summary>
        public int DebugUnbindPluginEvent(int handlerId)
        {
            if (handlerId < 0)
                return 0;
            NativeCallbackBridge.UnregisterChannel(TakeChannelHandle(handlerId));
            return IONativeWrapper.UnbindPluginEvent(this.ID, handlerId);
        }

        /// <summary>
        /// 调试工具专用：向底层 raw plugin channel 写入字节流。
        /// 普通业务代码应优先使用 <see cref="Subscribe(string, Func{ChannelContext, Task{ChannelResponse}})"/> 配套的 Channel API。
        /// </summary>
        public int DebugWritePluginRawChannel(string channelName, byte[] data)
        {
            if (string.IsNullOrEmpty(channelName) || data == null)
                return 0;
            return IONativeWrapper.WritePluginChannel(
                this.ID,
                channelName,
                data,
                (uint)data.Length
            );
        }

        /// <summary>
        /// 调试工具专用：以 UTF-8 文本写入底层 raw plugin channel。
        /// </summary>
        public int DebugWritePluginRawChannel(string channelName, string text)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty);
            return DebugWritePluginRawChannel(channelName, bytes);
        }

        /// <summary>
        /// 调试工具专用：订阅底层 raw plugin channel。
        /// 普通业务代码应优先使用 <see cref="Subscribe(string, Func{ChannelContext, Task{ChannelResponse}})"/> 或 <see cref="DebugBindPluginEvent(string, Action{string, byte[]})"/>。
        /// </summary>
        public int DebugBindPluginRawChannel(string channelName, Action<string, byte[]> handler)
        {
            if (string.IsNullOrEmpty(channelName) || handler == null)
                return -1;
            PluginChannelCallback proxy = (ch, dataPtr, size) =>
            {
                byte[] buf = new byte[0];
                if (dataPtr != IntPtr.Zero && size > 0)
                {
                    buf = new byte[size];
                    Marshal.Copy(dataPtr, buf, 0, (int)size);
                }

                try
                {
                    handler(ch ?? channelName, buf);
                }
                catch { }
            };
            int slot = NativeCallbackBridge.RegisterChannel(
                this.ID,
                channelName,
                proxy,
                null
            );
            int nativeId = IONativeWrapper.BindPluginChannel(
                this.ID,
                channelName,
                NativeCallbackBridge.MessageThunkAt(slot)
            );
            TrackChannelHandle(nativeId, slot);
            return nativeId;
        }

        /// <summary>
        /// 调试工具专用：取消底层 raw plugin channel 订阅。
        /// </summary>
        public int DebugUnbindPluginRawChannel(string channelName, int handlerId)
        {
            if (string.IsNullOrEmpty(channelName) || handlerId < 0)
                return 0;
            NativeCallbackBridge.UnregisterChannel(TakeChannelHandle(handlerId));
            return IONativeWrapper.UnbindPluginChannel(this.ID, channelName, handlerId);
        }
    }
}
