using System;
using System.Linq;
using IOToolkit;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 设备输出分发器 — 将归一化值写入 IODevice 通道
    /// 连接 InterpolationEngine 输出 → IODevice.SetDO / SetDO(Key)
    /// </summary>
    public class DeviceDispatcher
    {
        /// <summary>
        /// 将归一化值 (0~1) 写入指定设备的 OAction 输出
        /// </summary>
        /// <param name="deviceName">设备名 (对应 IODevice.xml Device.Name)</param>
        /// <param name="oactionName">OAction 输出动作名 (对应 IODevice.xml OAction.Name)</param>
        /// <param name="normalizedValue">归一化值 (0.0 ~ 1.0)</param>
        public void Dispatch(string deviceName, string oactionName, float normalizedValue)
        {
            try
            {
                var device = IODeviceController.GetIODevice(deviceName);
                if (device != null)
                {
                    // 通过 OAction 名称设置输出, IODevice 自动分发到各 Key 并应用 Scale/InvertEvent
                    device.SetDO(oactionName, normalizedValue);
                }

                // 无论设备是否连接, 都触发值分发事件 (用于 UI 实时值同步)
                LastDispatchedValue = normalizedValue;
                ValueDispatched?.Invoke(deviceName, oactionName, normalizedValue);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[DeviceDispatcher] Dispatch failed [{deviceName}.{oactionName}]: {ex.Message}"
                );
            }
        }

        /// <summary>
        /// 将归一化值 (0~1) 写入指定设备的 OAxis 输出通道
        /// 使用 Key 类型的 SetDO 重载, 直接写入 OAxis_XX 通道
        /// </summary>
        /// <param name="deviceName">设备名 (对应 IODevice.xml Device.Name)</param>
        /// <param name="oaxisChannel">OAxis 通道名 (如 "OAxis_00", "OAxis_01")</param>
        /// <param name="normalizedValue">归一化值 (0.0 ~ 1.0)</param>
        public void DispatchOAxis(string deviceName, string oaxisChannel, float normalizedValue)
        {
            try
            {
                var device = IODeviceController.GetIODevice(deviceName);
                if (device != null)
                {
                    // 通过 Key 类型直接写入 OAxis 通道 (string → Key 隐式转换)
                    IOToolkit.Key key = oaxisChannel;
                    device.SetDO(key, normalizedValue);
                }

                // 触发值分发事件 (用于 UI 实时值同步)
                LastDispatchedValue = normalizedValue;
                ValueDispatched?.Invoke(deviceName, oaxisChannel, normalizedValue);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[DeviceDispatcher] DispatchOAxis failed [{deviceName}.{oaxisChannel}]: {ex.Message}"
                );
            }
        }

        /// <summary>
        /// 根据轨道类型自动分发 (OAction 或 OAxis)
        /// </summary>
        public void DispatchTrack(
            string deviceName,
            string oactionName,
            string outputType,
            string oaxisChannel,
            float normalizedValue
        )
        {
            if (outputType == "oaxis" && !string.IsNullOrEmpty(oaxisChannel))
                DispatchOAxis(deviceName, oaxisChannel, normalizedValue);
            else
                Dispatch(deviceName, oactionName, normalizedValue);
        }

        /// <summary>
        /// 批量写入多个 OAction 输出 (同一帧内的所有轨道输出)
        /// </summary>
        public void DispatchBatch(
            params (string deviceName, string oactionName, float value)[] outputs
        )
        {
            foreach (var (deviceName, oactionName, value) in outputs)
            {
                Dispatch(deviceName, oactionName, value);
            }
        }

        /// <summary>最后一次分发的值 (调试用)</summary>
        public float LastDispatchedValue { get; private set; }

        /// <summary>
        /// 仅触发 ValueDispatched 事件 (不写设备)。
        /// 供 NativeMotionPlaybackEngine 在 C++ 已完成设备输出后同步 UI LiveValue。
        /// </summary>
        internal void FireValueDispatched(string deviceName, string channelName, float value)
        {
            LastDispatchedValue = value;
            ValueDispatched?.Invoke(deviceName, channelName, value);
        }

        /// <summary>值分发事件 (用于 UI 显示同步)</summary>
        public event Action<string, string, float>? ValueDispatched;
    }
}
