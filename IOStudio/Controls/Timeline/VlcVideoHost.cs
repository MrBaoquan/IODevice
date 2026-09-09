using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace IOStudio.Controls.Timeline
{
    /// <summary>
    /// NativeControlHost 子类 — 创建 Win32 子窗口供 LibVLC 渲染视频。
    /// LibVLC 需要一个独立的 HWND 来渲染视频输出, 如果直接使用主窗口的 HWND,
    /// 视频会覆盖整个窗口。此控件通过 CreateWindowEx 创建专用子窗口,
    /// Avalonia 的 NativeControlHost 自动管理子窗口的定位和缩放。
    /// </summary>
    public class VlcVideoHost : NativeControlHost
    {
        /// <summary>原生子窗口句柄 (HWND), 供 LibVLC MediaPlayer.Hwnd 使用</summary>
        public IntPtr Hwnd { get; private set; }

        // 注册自定义窗口类 (带黑色背景画刷), 避免 STATIC 类的白色残留
        private static readonly IntPtr _wndClass;
        private const string WndClassName = "VlcVideoHostWnd";

        static VlcVideoHost()
        {
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                style = CS_HREDRAW | CS_VREDRAW,
                lpfnWndProc = DefWindowProcPtr,
                hbrBackground = GetStockObject(BLACK_BRUSH),
                lpszClassName = WndClassName,
                hInstance = GetModuleHandle(null),
            };
            _wndClass = RegisterClassEx(ref wc);
        }

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            var hwnd = CreateWindowEx(
                0,
                WndClassName,
                "",
                WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN,
                0,
                0,
                (int)Math.Max(Bounds.Width, 1),
                (int)Math.Max(Bounds.Height, 1),
                parent.Handle,
                IntPtr.Zero,
                GetModuleHandle(null),
                IntPtr.Zero
            );

            Hwnd = hwnd;
            return new WinHandle(hwnd);
        }

        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            if (control.Handle != IntPtr.Zero)
                DestroyWindow(control.Handle);
            Hwnd = IntPtr.Zero;
        }

        /// <summary>IPlatformHandle 的简单实现</summary>
        private class WinHandle : IPlatformHandle
        {
            public IntPtr Handle { get; }
            public string HandleDescriptor => "HWND";

            public WinHandle(IntPtr handle) => Handle = handle;
        }

        // ═══════ Win32 P/Invoke ═══════

        private const uint WS_CHILD = 0x40000000;
        private const uint WS_VISIBLE = 0x10000000;
        private const uint WS_CLIPCHILDREN = 0x02000000;
        private const uint CS_HREDRAW = 0x0002;
        private const uint CS_VREDRAW = 0x0001;
        private const int BLACK_BRUSH = 4;

        private static readonly IntPtr DefWindowProcPtr = GetDefWindowProcPtr();

        private static IntPtr GetDefWindowProcPtr()
        {
            var user32 = LoadLibrary("user32.dll");
            return GetProcAddress(user32, "DefWindowProcW");
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpszMenuName;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr RegisterClassEx(ref WNDCLASSEX lpWndClass);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(
            int exStyle,
            string className,
            string windowName,
            uint style,
            int x,
            int y,
            int width,
            int height,
            IntPtr parent,
            IntPtr menu,
            IntPtr instance,
            IntPtr param
        );

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("gdi32.dll")]
        private static extern IntPtr GetStockObject(int nIndex);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);
    }
}
