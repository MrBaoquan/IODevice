using System.Collections.Generic;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 设备架构服务接口 — 从 IODevice.xml 解析设备/OAction/OAxis 信息。
    /// </summary>
    public interface IDeviceSchemaService
    {
        /// <summary>
        /// 加载所有可用设备的架构信息。
        /// 若配置文件缺失或解析失败, 返回默认 Platform-0 设备。
        /// </summary>
        List<Models.Motion.DeviceSchemaInfo> LoadDevices();
    }
}
