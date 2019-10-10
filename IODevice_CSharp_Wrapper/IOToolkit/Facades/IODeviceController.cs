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

        public static int Load()
        {
            return IONativeWrapper.Load();
        }

        public static int UnLoad()
        {
            return IONativeWrapper.UnLoad();
        }

        public static IODevice GetIODevice(string InDeviceName)
        {
            IODevice _device;
            if (!devices.TryGetValue(InDeviceName, out _device)) {
                _device = new IODeviceNative(InDeviceName);
                devices.Add(InDeviceName, _device); 
            }
            return _device;
        }

        public static void Update()
        {
            IONativeWrapper.Query();
        }

        public static void ClearAllBingings()
        {
            IONativeWrapper.ClearAllBindings();
        }


        private static Dictionary<string, IODevice> devices = new Dictionary<string, IODevice>();
    }
}
