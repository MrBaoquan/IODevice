using IOToolkit.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOToolkit
{
    /// <summary>
    /// IO设备管理
    /// </summary>
    public class IODeviceController
    {
        private class IODeviceNative : IODevice
        {
            public IODeviceNative(string InID)
                : base(InID) { }
        }

        /// <summary>
        /// 加载设备
        /// </summary>
        /// <returns></returns>
        public static int Load()
        {
            // 每轮 Load 都从干净的托管状态开始：上一轮的原生设备已随 Unload 销毁，
            // 缓存的 IODevice 与桥接槽位全都指向已不存在的设备。
            ResetManagedState();

            return IONativeWrapper.Load();
        }

        /// <summary>
        /// 卸载设备
        /// </summary>
        /// <returns></returns>

        public static int Unload()
        {
            try
            {
                return IONativeWrapper.Unload();
            }
            finally
            {
                // 原生设备已销毁，托管侧缓存与绑定随之失效，必须一并清掉，
                // 否则下一轮 Load 会复用指向已销毁设备的包装器。
                ResetManagedState();
            }
        }

        /// <summary>
        /// 清空设备缓存并归还全部桥接槽位。
        ///
        /// 只在设备整体加载/卸载的边界调用：此时原生设备已销毁，绑定已全部失效。
        /// </summary>
        private static void ResetManagedState()
        {
            devices.Clear();
            NativeCallbackBridge.ReleaseAllBindings();
        }

        public static int EnterSafeState()
        {
            return IONativeWrapper.EnterSafeState();
        }

        /// <summary>
        /// 获取设备
        /// </summary>
        /// <param name="InDeviceName">设备名称</param>
        /// <returns>查找到的设备</returns>

        public static IODevice GetIODevice(string InDeviceName)
        {
            IODevice _device;
            if (!devices.TryGetValue(InDeviceName, out _device))
            {
                _device = new IODeviceNative(InDeviceName);
                devices.Add(InDeviceName, _device);
            }
            return _device;
        }

        /// <summary>
        /// 帧调用， 需要在帧更新中持续调用
        /// </summary>
        public static void Update()
        {
            IONativeWrapper.Query();
        }

        /// <summary>
        /// 清除所有绑定
        /// </summary>
        public static void ClearAllBingings()
        {
            IONativeWrapper.ClearAllBindings();
        }

        private static Dictionary<string, IODevice> devices = new Dictionary<string, IODevice>();
    }
}
