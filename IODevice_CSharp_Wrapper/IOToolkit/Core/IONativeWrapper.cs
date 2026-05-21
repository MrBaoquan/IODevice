using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace IOToolkit.Core
{
    public delegate void NativeActionSignature();
    public delegate void NativeActionWithKeySignature([MarshalAs(UnmanagedType.BStr)] string _ptr);
    public delegate void NativeAxisSignature(float _val);

    /// <summary>
    /// 插件通道回调（字节流原样下发）。
    /// 注意：channelName 为 ANSI char*，data 为非托管缓冲区，长度由 size 指定。
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void PluginChannelCallback(
        [MarshalAs(UnmanagedType.LPStr)] string channelName,
        IntPtr data,
        uint size
    );

    internal class IONativeWrapper
    {
        const string DllName = "IODevice_C_Wrapper";

        //[DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        //[return: MarshalAs(UnmanagedType.BStr)]
        //public static extern string GetStr([MarshalAs(UnmanagedType.BStr)] string str);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int Load();

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int Unload();

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern bool IsValid([MarshalAs(UnmanagedType.BStr)] string InDeviceName);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.BStr)]
        public static extern string DeviceDllName(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.BStr)]
        public static extern string DeviceIOType(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int DeviceIndex([MarshalAs(UnmanagedType.BStr)] string InDeviceName);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int SetIOConfigPath([MarshalAs(UnmanagedType.BStr)] string InFilePath);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int SetIOLogDir([MarshalAs(UnmanagedType.BStr)] string InFilePath);

        [DllImport(
            DllName,
            CallingConvention = CallingConvention.StdCall,
            EntryPoint = "BindKeyWithKey"
        )]
        public static extern int BindKeyWithKey(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.BStr)] string InKeyName,
            int InKeyEvent,
            NativeActionWithKeySignature InHandler
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int BindKey(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.BStr)] string InKeyName,
            int InKeyEvent,
            NativeActionSignature InHandler
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int BindAxisKey(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.BStr)] string InAxisKeyName,
            NativeAxisSignature InHandler
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int BindAction(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.BStr)] string InActionName,
            int InKeyEvent,
            NativeActionWithKeySignature InputHandler
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int BindAxis(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.BStr)] string InAxisName,
            NativeAxisSignature InHandler
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern float GetDOSingle(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.BStr)] string InKeyName
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern float GetDOAction(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.BStr)] string InOAction
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int GetDOAll(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.LPArray)] float[] DOStatus
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int RefreshStreamingData(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.LPArray)] byte[] StreamingData,
            UInt32 DataSize
        );

        // ── Plugin Channel (原始字节流通道) ─────────────────

        /// <summary>
        /// 向设备插件通道写入字节流（如 netio.udp.out）。
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int WritePluginChannel(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.LPStr)] string channelName,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] byte[] data,
            uint size
        );

        /// <summary>
        /// 订阅设备插件通道（如 netio.udp.in）。返回 handlerId（>=0 成功，&lt;0 失败）。
        /// 调用方须保持 callback 委托引用，防止被 GC 回收。
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int BindPluginChannel(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.LPStr)] string channelName,
            PluginChannelCallback callback
        );

        /// <summary>
        /// 取消订阅设备插件通道。
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int UnbindPluginChannel(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.LPStr)] string channelName,
            int handlerId
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int QueryPluginCapabilities(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] outJson,
            uint capacity,
            uint timeoutMs
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int SendPluginRequest(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.LPStr)] string topic,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] byte[] requestJson,
            uint requestSize,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 5)] byte[] responseJson,
            uint capacity,
            uint timeoutMs
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int BindPluginEvent(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.LPStr)] string eventName,
            PluginChannelCallback callback
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int UnbindPluginEvent(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            int handlerId
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int RequestChannel(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.LPStr)] string name,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] byte[] requestJson,
            uint requestSize,
            int waitResponse,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 6)] byte[] metadataJson,
            uint metadataSize,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 8)] byte[] responseJson,
            uint capacity,
            uint timeoutMs
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int SubscribeChannel(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.LPStr)] string name,
            PluginChannelCallback callback
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int UnsubscribeChannel(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            int handlerId
        );

        /// <summary>
        /// 插件→宿主 RPC 请求回调. requestId/payloadJson/metadataJson 均为非托管字节缓冲;
        /// 通过 Marshal.Copy + UTF8 解码读取.
        /// </summary>
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void ChannelRequestCallback(
            [MarshalAs(UnmanagedType.LPStr)] string name,
            [MarshalAs(UnmanagedType.LPStr)] string requestId,
            IntPtr payloadJson,
            uint payloadSize,
            IntPtr metadataJson,
            uint metadataSize
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int SubscribeChannelRequest(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.LPStr)] string name,
            ChannelRequestCallback callback
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int UnsubscribeChannelRequest(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            int handlerId
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int RespondChannelRequest(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.LPStr)] string requestId,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] byte[] payloadJson,
            uint payloadSize,
            int ok,
            [MarshalAs(UnmanagedType.LPStr)] string errorMessage
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int QueryChannelCapabilities(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] outJson,
            uint capacity,
            uint timeoutMs
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int SetDOSingle(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.BStr)] string InKeyName,
            float InStatus
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int SetDOAll(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.LPArray)] float[] DOStatus
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int SetDOAction(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.BStr)] string InActionName,
            float InVal,
            bool bIngoreMassage = false
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int SetDOOn(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.BStr)] string InActionName
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int SetDOOff(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.BStr)] string InActionName
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int DOImmediate([MarshalAs(UnmanagedType.BStr)] string InDeviceName);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern void Query();

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern void ClearBindings(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern void ClearAllBindings();

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool GetKey(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.BStr)] string InKey
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool GetKeyDown(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.BStr)] string InKey
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool GetKeyUp(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.BStr)] string InKey
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern float GetAxis(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.BStr)] string InAxisName
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern float GetAxisKey(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.BStr)] string InKey
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern float GetRawKeyValue(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.BStr)] string InKey
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern float GetKeyDownDuration(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.BStr)] string InKey
        );

        /// <summary>
        /// 设置 Axis Key 的属性 (Scale)
        /// </summary>
        /// <param name="InDeviceName">设备名称</param>
        /// <param name="InAxisName">Axis 名称</param>
        /// <param name="InKeyName">Key 名称</param>
        /// <param name="InScale">缩放系数</param>
        /// <returns>成功返回1 失败返回0</returns>
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int SetAKProps(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.BStr)] string InAxisName,
            [MarshalAs(UnmanagedType.BStr)] string InKeyName,
            float InScale
        );

        /// <summary>
        /// 设置 OAction Key 的属性 (Scale, InvertEvent)
        /// </summary>
        /// <param name="InDeviceName">设备名称</param>
        /// <param name="InOActionName">OAction 名称</param>
        /// <param name="InKeyName">Key 名称</param>
        /// <param name="InScale">缩放系数</param>
        /// <param name="InInvertEvent">是否反转事件</param>
        /// <returns>成功返回1 失败返回0</returns>
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int SetOKProps(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.BStr)] string InOActionName,
            [MarshalAs(UnmanagedType.BStr)] string InKeyName,
            float InScale,
            [MarshalAs(UnmanagedType.I1)] bool InInvertEvent
        );

        /// <summary>
        /// 设置 Property Key 的属性
        /// </summary>
        /// <param name="InDeviceName">设备名称</param>
        /// <param name="InKeyName">Key 名称</param>
        /// <param name="InOffset">偏移值（校准零点）</param>
        /// <param name="InScale">缩放系数（映射输入范围）</param>
        /// <param name="InMinValue">最小值</param>
        /// <param name="InMaxValue">最大值</param>
        /// <param name="InDeadZone">死区</param>
        /// <param name="InSensitivity">灵敏度</param>
        /// <param name="InExponent">指数曲线</param>
        /// <param name="InInvert">是否反转数值</param>
        /// <param name="InInvertEvent">是否反转事件</param>
        /// <returns>成功返回1 失败返回0</returns>
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int SetPKProps(
            [MarshalAs(UnmanagedType.BStr)] string InDeviceName,
            [MarshalAs(UnmanagedType.BStr)] string InKeyName,
            float InOffset,
            float InScale,
            float InMinValue,
            float InMaxValue,
            float InDeadZone,
            float InSensitivity,
            float InExponent,
            [MarshalAs(UnmanagedType.I1)] bool InInvert,
            [MarshalAs(UnmanagedType.I1)] bool InInvertEvent
        );

        // ── MotionPlayer ────────────────────────────────────

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int MotionLoadSlot(
            [MarshalAs(UnmanagedType.BStr)] string InSlotId,
            [MarshalAs(UnmanagedType.BStr)] string InFilePath,
            int InPriority,
            int InMixPolicy
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern void MotionUnloadSlot([MarshalAs(UnmanagedType.BStr)] string InSlotId);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern void MotionUnloadAll();

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int MotionPlaySlot([MarshalAs(UnmanagedType.BStr)] string InSlotId);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int MotionPlaySlotFrom(
            [MarshalAs(UnmanagedType.BStr)] string InSlotId,
            float InTimeMs
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int MotionPauseSlot([MarshalAs(UnmanagedType.BStr)] string InSlotId);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int MotionResumeSlot([MarshalAs(UnmanagedType.BStr)] string InSlotId);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int MotionStopSlot([MarshalAs(UnmanagedType.BStr)] string InSlotId);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int MotionSeekSlot(
            [MarshalAs(UnmanagedType.BStr)] string InSlotId,
            float InTimeMs
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern void MotionSetSlotSpeed(
            [MarshalAs(UnmanagedType.BStr)] string InSlotId,
            float InSpeed
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern void MotionSetSlotLoop(
            [MarshalAs(UnmanagedType.BStr)] string InSlotId,
            [MarshalAs(UnmanagedType.I1)] bool InLoop
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern void MotionSetSlotClockMode(
            [MarshalAs(UnmanagedType.BStr)] string InSlotId,
            int InMode
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern void MotionSetSlotExternalTime(
            [MarshalAs(UnmanagedType.BStr)] string InSlotId,
            float InTimeMs
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int MotionGetSlotState(
            [MarshalAs(UnmanagedType.BStr)] string InSlotId
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern float MotionGetSlotCurrentTime(
            [MarshalAs(UnmanagedType.BStr)] string InSlotId
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern float MotionGetSlotDuration(
            [MarshalAs(UnmanagedType.BStr)] string InSlotId
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int MotionGetSlotCount();

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern void MotionPlayAll();

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern void MotionPauseAll();

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern void MotionStopAll();

        // ── Phase 3 新增 API ────────────────────────────

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int MotionLoadSlotFromJson(
            [MarshalAs(UnmanagedType.BStr)] string InSlotId,
            [MarshalAs(UnmanagedType.BStr)] string InJsonContent,
            int InPriority,
            int InMixPolicy
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int MotionEvaluateSlotAt(
            [MarshalAs(UnmanagedType.BStr)] string InSlotId,
            float InTimeMs,
            [Out] float[] OutValues,
            int InMaxChannels
        );

        public delegate void MotionEventCallbackDelegate(
            [MarshalAs(UnmanagedType.BStr)] string slotId,
            int eventType
        );

        public delegate void MotionEventDataCallbackDelegate(
            [MarshalAs(UnmanagedType.BStr)] string slotId,
            int eventType,
            [MarshalAs(UnmanagedType.BStr)] string eventData
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern void MotionSetEventCallback(MotionEventCallbackDelegate InCallback);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern void MotionSetEventDataCallback(
            MotionEventDataCallbackDelegate InCallback
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern void MotionSetSafetyConfig(float InMaxRatePerSecond);
    }
}
