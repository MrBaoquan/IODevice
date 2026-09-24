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
            lock (_slotsLock) { _slots[slotId] = slot; }
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
            lock (_slotsLock) { _slots[slotId] = slot; }
            return slot;
        }

        /// <summary>
        /// 获取已加载的 MotionSlot 实例，未找到返回 null。
        /// </summary>
        public static MotionSlot GetSlot(string slotId)
        {
            lock (_slotsLock) { _slots.TryGetValue(slotId, out var slot); return slot; }
        }

        /// <summary>
        /// 卸载所有 Slot 并清空缓存。
        /// </summary>
        public static void UnloadAll()
        {
            IONativeWrapper.MotionUnloadAll();
            lock (_slotsLock) { _slots.Clear(); }
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
                // 原生回调统一走 NativeCallbackBridge 的静态入口（IL2CPP 不接受闭包委托封送）。
                NativeCallbackBridge.SetMotionEventHandler(
                    (slotId, eventType) => callback(slotId, (MotionEvent)eventType)
                );
                IONativeWrapper.MotionSetEventCallback(NativeCallbackBridge.MotionEventThunk);
            }
            else
            {
                NativeCallbackBridge.SetMotionEventHandler(null);
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
                NativeCallbackBridge.SetMotionEventDataHandler(
                    (slotId, eventType, eventData) =>
                        callback(slotId, (MotionEvent)eventType, eventData)
                );
                IONativeWrapper.MotionSetEventDataCallback(
                    NativeCallbackBridge.MotionEventDataThunk
                );
            }
            else
            {
                NativeCallbackBridge.SetMotionEventDataHandler(null);
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

        // ── 通道工程量程 (归一化 0..1 → 工程量) ───────
        // 三级优先级 (轨道 engineering_range → 设备声明量程 → 直通) 的**解析**在宿主侧完成
        // (只有宿主知道轨道与设备声明), 但三级都能表达为绝对区间, 所以推给 native 的是一张
        // (device, channel) → (min, max) 表; 换算则由 native 执行, 与放映端共用同一段代码。
        //
        // 未登记或退化区间 → 直通 (保持既有行为)。

        /// <summary>登记一个通道的工程量程。max &lt;= min 视为清除该通道。</summary>
        public static void SetChannelRange(
            string deviceName,
            string channel,
            float minValue,
            float maxValue
        )
        {
            IONativeWrapper.MotionSetChannelRange(deviceName, channel, minValue, maxValue);
        }

        /// <summary>清空全部通道量程 (卸载时间轴/切换设备时调用)。</summary>
        public static void ClearChannelRanges()
        {
            IONativeWrapper.MotionClearChannelRanges();
        }

        /// <summary>归一化 → 工程量 (未登记量程时原样返回)。</summary>
        public static float ToEngineering(string deviceName, string channel, float normalized)
        {
            return IONativeWrapper.MotionToEngineering(deviceName, channel, normalized);
        }

        // ── native Tick 驱动 ─────────────────────────

        /// <summary>
        /// 驱动 native Tick (单位: 秒)。
        ///
        /// <para>平时由 <c>IODeviceController.Update()</c> 以状态轮询频率 (约 10Hz) 驱动;
        /// 编排端播放时可额外按帧调用它, 让 native 承担派发 (量程换算 + SafetyGuard 限速都在
        /// native 侧)。限速强度与调用频率无关 —— SafetyGuard 用内部时钟测实际间隔。</para>
        /// </summary>
        public static void Tick(float deltaSeconds)
        {
            IONativeWrapper.MotionTick(deltaSeconds);
        }

        // ── 内部方法 ─────────────────────────────────

        internal static void RemoveSlot(string slotId) { lock (_slotsLock) { _slots.Remove(slotId); } }

        /// <summary>
        /// Slot 缓存字典。会同时被 UI 线程与播放 tick 线程访问 (加载/查询/卸载),
        /// Dictionary 非线程安全, 无锁并发会破坏内部结构 (曾表现为 IndexOutOfRangeException),
        /// 因此所有访问点统一用 _slotsLock 串行化。
        /// </summary>
        private static readonly Dictionary<string, MotionSlot> _slots =
            new Dictionary<string, MotionSlot>();
        private static readonly object _slotsLock = new object();

        // 事件委托的强引用由 NativeCallbackBridge 的 static 字段持有（防 GC 回收）。
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
