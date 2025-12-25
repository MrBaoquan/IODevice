using System;
using System.Collections.Generic;
using IOTester.Models;

namespace IOTester.Services.Senders
{
    /// <summary>
    /// 协议发送器接口，定义所有协议发送器的通用行为
    /// </summary>
    public interface IProtocolSender : IDisposable
    {
        /// <summary>
        /// 发送数字量事件
        /// </summary>
        /// <param name="group">协议组配置</param>
        /// <param name="mapping">映射配置</param>
        /// <param name="eventType">事件类型：Pressed 或 Released</param>
        void SendDigital(ProtocolGroupDto group, MappingDto mapping, string eventType);

        /// <summary>
        /// 发送模拟量数据
        /// </summary>
        /// <param name="group">协议组配置</param>
        /// <param name="values">通道值字典，Key为目标键名，Value为模拟量值</param>
        void SendAnalog(ProtocolGroupDto group, Dictionary<string, float> values);
    }
}
