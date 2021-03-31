using IOToolkit.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOToolkit
{
    public class IODeviceController
    {
        private class IODeviceNative : IODevice
        {
            public IODeviceNative(string InID) : base(InID){}
        }

        /// <summary>
        /// 加载设备
        /// </summary>
        /// <returns></returns>
        public static int Load()
        {
            return IONativeWrapper.Load();
        }

        /// <summary>
        /// 卸载设备
        /// </summary>
        /// <returns></returns>

        public static int UnLoad()
        {
            return IONativeWrapper.UnLoad();
        }

        /// <summary>
        /// 获取设备
        /// </summary>
        /// <param name="InDeviceName">设备名称</param>
        /// <returns>查找到的设备</returns>

        public static IODevice GetIODevice(string InDeviceName)
        {
            IODevice _device;
            if (!devices.TryGetValue(InDeviceName, out _device)) {
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
