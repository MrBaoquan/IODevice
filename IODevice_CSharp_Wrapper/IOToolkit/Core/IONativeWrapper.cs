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

    class IONativeWrapper
    {
        const string DllName = "IODevice_C_Wrapper";

        //[DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        //[return: MarshalAs(UnmanagedType.BStr)]
        //public static extern string GetStr([MarshalAs(UnmanagedType.BStr)] string str);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int BindKey([MarshalAs(UnmanagedType.BStr)] string InDeviceName, [MarshalAs(UnmanagedType.BStr)] string InKeyName, int InKeyEvent, NativeActionSignature InHandler);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int BindAction([MarshalAs(UnmanagedType.BStr)] string InDeviceName, [MarshalAs(UnmanagedType.BStr)] string InActionName, int InKeyEvent, NativeActionWithKeySignature InputHandler);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int BindAxis([MarshalAs(UnmanagedType.BStr)] string InDeviceName, [MarshalAs(UnmanagedType.BStr)] string InAxisName, NativeAxisSignature InHandler);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern void Query();

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern void ClearAllBindings();
    }
}
