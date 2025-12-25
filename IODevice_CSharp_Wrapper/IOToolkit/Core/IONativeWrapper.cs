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
    }
}
