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
        public static extern int UnLoad();

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int BindKey([MarshalAs(UnmanagedType.BStr)] string InDeviceName, [MarshalAs(UnmanagedType.BStr)] string InKeyName, int InKeyEvent, NativeActionSignature InHandler);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int BindAction([MarshalAs(UnmanagedType.BStr)] string InDeviceName, [MarshalAs(UnmanagedType.BStr)] string InActionName, int InKeyEvent, NativeActionWithKeySignature InputHandler);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int BindAxis([MarshalAs(UnmanagedType.BStr)] string InDeviceName, [MarshalAs(UnmanagedType.BStr)] string InAxisName, NativeAxisSignature InHandler);

        [DllImport(DllName,CallingConvention = CallingConvention.StdCall)]
        public static extern short GetDOSingle([MarshalAs(UnmanagedType.BStr)] string InDeviceName, [MarshalAs(UnmanagedType.BStr)] string InKeyName);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int GetDOAll([MarshalAs(UnmanagedType.BStr)] string InDeviceName, [MarshalAs(UnmanagedType.LPArray)] short[] DOStatus);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int SetDOSingle([MarshalAs(UnmanagedType.BStr)] string InDeviceName, [MarshalAs(UnmanagedType.BStr)] string InKeyName, short InStatus);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int SetDOAll([MarshalAs(UnmanagedType.BStr)] string InDeviceName, [MarshalAs(UnmanagedType.LPArray)] short[] DOStatus);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int SetDOAction([MarshalAs(UnmanagedType.BStr)] string InDeviceName, [MarshalAs(UnmanagedType.BStr)] string InActionName, short InVal);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int SetDOOn([MarshalAs(UnmanagedType.BStr)] string InDeviceName, [MarshalAs(UnmanagedType.BStr)] string InActionName);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int SetDOOff([MarshalAs(UnmanagedType.BStr)] string InDeviceName, [MarshalAs(UnmanagedType.BStr)] string InActionName);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern void Query();

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern void ClearAllBindings();

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern bool GetKey([MarshalAs(UnmanagedType.BStr)] string InDeviceName,[MarshalAs(UnmanagedType.BStr)] string InKey);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern bool GetKeyDown([MarshalAs(UnmanagedType.BStr)] string InDeviceName, [MarshalAs(UnmanagedType.BStr)] string InKey);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern  bool GetKeyUp([MarshalAs(UnmanagedType.BStr)] string InDeviceName, [MarshalAs(UnmanagedType.BStr)] string InKey);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern  float GetAxis([MarshalAs(UnmanagedType.BStr)] string InDeviceName, [MarshalAs(UnmanagedType.BStr)] string InAxisName);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern  float GetAxisKey([MarshalAs(UnmanagedType.BStr)] string InDeviceName, [MarshalAs(UnmanagedType.BStr)] string InKey);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern  float GetKeyDownDuration([MarshalAs(UnmanagedType.BStr)] string InDeviceName, [MarshalAs(UnmanagedType.BStr)] string InKey);
    }
}