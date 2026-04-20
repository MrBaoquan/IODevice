using IOToolkit.Core;
using System;
using System.Collections.Generic;

namespace IOToolkit
{
    /// <summary>
    /// 动作播放器管理器 — Slot 的加载/卸载工厂 + 批量播控。
    /// 与 IODeviceController 平级，静态类直接使用。
    /// 由 IODeviceController.Update() 内部自动驱动 Tick。
    ///
    /// 使用方式（类似 IODeviceController / IODevice 模式）:
    /// <code>
    ///   MotionSlot slot = MotionPlayer.LoadSlot("slot1", "demo.motion");
    ///   slot.Play();
    ///   slot.Pause();
    ///   float time = slot.CurrentTime;
    ///   MotionState state = slot.State;
    ///   slot.Speed = 1.5f;
    ///   slot.Unload();
    /// </code>
    /// </summary>
    public static class MotionPlayer
    {
        // ── Slot 工厂 ───────────────────────────────

        /// <summary>
        /// 加载动作文件到指定 Slot，返回 MotionSlot 实例。
        /// 若 slotId 已存在，先卸载旧 Slot 再重新加载。
        /// </summary>
        public static MotionSlot LoadSlot(
            string slotId,
            string filePath,
            int priority = 0,
            MixPolicy policy = MixPolicy.Priority
        )
        {
            IONativeWrapper.MotionLoadSlot(slotId, filePath, priority, (int)policy);
            var slot = new MotionSlot(slotId, priority, policy);
            _slots[slotId] = slot;
            return slot;
        }

        /// <summary>
        /// 从 JSON 字符串加载动作数据到指定 Slot，返回 MotionSlot 实例。
        /// 编辑器预览时使用，无需写临时文件。
        /// </summary>
        public static MotionSlot LoadSlotFromJson(
            string slotId,
            string jsonContent,
            int priority = 0,
            MixPolicy policy = MixPolicy.Priority
        )
        {
            IONativeWrapper.MotionLoadSlotFromJson(slotId, jsonContent, priority, (int)policy);
            var slot = new MotionSlot(slotId, priority, policy);
            _slots[slotId] = slot;
            return slot;
        }

        /// <summary>
        /// 获取已加载的 MotionSlot 实例，未找到返回 null。
        /// </summary>
        public static MotionSlot GetSlot(string slotId)
        {
            MotionSlot slot;
            _slots.TryGetValue(slotId, out slot);
            return slot;
        }

        /// <summary>
        /// 卸载所有 Slot 并清空缓存。
        /// </summary>
        public static void UnloadAll()
        {
            IONativeWrapper.MotionUnloadAll();
            _slots.Clear();
        }

        // ── 批量播控 ─────────────────────────────────

        public static void PlayAll() => IONativeWrapper.MotionPlayAll();

        public static void PauseAll() => IONativeWrapper.MotionPauseAll();

        public static void StopAll() => IONativeWrapper.MotionStopAll();

        // ── 全局状态 ─────────────────────────────────

        public static int SlotCount => IONativeWrapper.MotionGetSlotCount();

        // ── 事件回调 ─────────────────────────────────

        /// <summary>
        /// 设置全局事件回调。所有 Slot 的状态变更、播放完成、安全触发事件都会通知。
        /// 传入 null 取消回调。
        /// </summary>
        public static void SetEventCallback(Action<string, MotionEvent> callback)
        {
            if (callback != null)
            {
                _eventCallbackDelegate = (slotId, eventType) =>
                    callback(slotId, (MotionEvent)eventType);
                IONativeWrapper.MotionSetEventCallback(_eventCallbackDelegate);
            }
            else
            {
                _eventCallbackDelegate = null;
                IONativeWrapper.MotionSetEventCallback(null);
            }
        }

        /// <summary>
        /// 设置带事件数据的全局回调。事件触发时传递 slotId、事件类型和事件数据 (string/json/number)。
        /// 传入 null 取消回调。
        /// </summary>
        public static void SetEventDataCallback(Action<string, MotionEvent, string> callback)
        {
            if (callback != null)
            {
                _eventDataCallbackDelegate = (slotId, eventType, eventData) =>
                    callback(slotId, (MotionEvent)eventType, eventData);
                IONativeWrapper.MotionSetEventDataCallback(_eventDataCallbackDelegate);
            }
            else
            {
                _eventDataCallbackDelegate = null;
                IONativeWrapper.MotionSetEventDataCallback(null);
            }
        }

        // ── 安全参数 ─────────────────────────────────

        /// <summary>
        /// 设置全局安全保护最大变化速率 (每秒)。默认 5.0。
        /// </summary>
        public static void SetSafetyConfig(float maxRatePerSecond)
        {
            IONativeWrapper.MotionSetSafetyConfig(maxRatePerSecond);
        }

        // ── 内部方法 ─────────────────────────────────

        internal static void RemoveSlot(string slotId) => _slots.Remove(slotId);

        private static readonly Dictionary<string, MotionSlot> _slots =
            new Dictionary<string, MotionSlot>();

        // 保持 delegate 引用防止 GC 回收
        private static IONativeWrapper.MotionEventCallbackDelegate _eventCallbackDelegate;
        private static IONativeWrapper.MotionEventDataCallbackDelegate _eventDataCallbackDelegate;
    }

    public enum MotionState
    {
        Idle = 0,
        Playing = 1,
        Paused = 2,
        Stopping = 3
    }

    public enum MotionEvent
    {
        PlaybackComplete = 0,
        SafetyTriggered = 1,
        StateChanged = 2
    }

    public enum MixPolicy
    {
        Priority = 0,
        Blend = 1,
        Max = 2,
        Additive = 3,
        Override = 4
    }

    /// <summary>
    /// Slot 时钟模式。
    /// Internal: 由 IODeviceController.Update() 的 delta 时间累加驱动（默认）。
    /// External: 由外部时间源驱动，每帧通过 MotionSlot.ExternalTime 设置绝对时间。
    /// </summary>
    public enum ClockMode
    {
        Internal = 0,
        External = 1
    }
}
