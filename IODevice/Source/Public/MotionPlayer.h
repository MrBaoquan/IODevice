/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2025-7-22
 *  Description: MotionPlayer — 跨设备输出侧时序状态机
 *  与 PlayerInput (输入侧) 对称，由 IODeviceController::Update() 驱动 Tick。
 *  管理多个 MotionSlot，每个 Slot 独立加载/播放一个 .motion 文件。
 */

#pragma once
#include <functional>
#include <memory>
#include <string>
#include "IOExportsAPI.h"

namespace IOToolkit {

    enum class MotionState  { Idle = 0, Playing = 1, Paused = 2, Stopping = 3 };
    enum class MotionEvent  { PlaybackComplete = 0, SafetyTriggered = 1, StateChanged = 2 };
    enum class MixPolicy    { Priority = 0, Blend = 1, Max = 2, Additive = 3, Override = 4 };
    enum class ClockMode    { Internal = 0, External = 1 };

    // C ABI 事件回调函数指针 (可跨 C/C# Wrapper 导出)
    typedef void (*MotionEventCallback)(const char* slotId, int eventType);
    // 带事件数据的回调函数指针
    typedef void (*MotionEventDataCallback)(const char* slotId, int eventType, const char* eventData);

    class IOAPI MotionPlayer {
    public:
        static MotionPlayer& Instance();

        // ── Slot 生命周期 ────────────────────────────
        int  LoadSlot(const char* slotId, const char* filePath,
                      int priority = 0, MixPolicy policy = MixPolicy::Priority);
        int  LoadSlotFromJson(const char* slotId, const char* jsonContent,
                              int priority = 0, MixPolicy policy = MixPolicy::Priority);
        void UnloadSlot(const char* slotId);
        void UnloadAll();

        // ── 单 Slot 播控 ─────────────────────────────
        int  PlaySlot(const char* slotId);
        int  PlaySlotFrom(const char* slotId, float timeMs);
        int  PauseSlot(const char* slotId);
        int  ResumeSlot(const char* slotId);
        int  StopSlot(const char* slotId);      // 触发平滑回中 → Stopping → Idle
        int  SeekSlot(const char* slotId, float timeMs);
        void SetSlotSpeed(const char* slotId, float speed);
        void SetSlotLoop(const char* slotId, bool loop);
        void SetSlotClockMode(const char* slotId, ClockMode mode);
        void SetSlotExternalTime(const char* slotId, float timeMs);

        // ── 批量操作 ─────────────────────────────────
        void PlayAll();
        void PauseAll();
        void StopAll();
        void EnterSafeState();

        // ── 状态查询 ─────────────────────────────────
        MotionState GetSlotState(const char* slotId) const;
        float       GetSlotCurrentTime(const char* slotId) const;  // ms
        float       GetSlotDuration(const char* slotId) const;     // ms
        int         GetSlotCount() const;

        // ── 纯求值(不派发) ───────────────────────────
        int  EvaluateSlotAt(const char* slotId, float timeMs,
                            float* outValues, int maxChannels,
                            char* outKeys = nullptr, int keyBufSize = 0) const;

        // ── 通道工程量程 (归一化 0..1 → 工程量) ───────
        // 三级优先级的"解析"由宿主完成 (它才知道轨道 engineering_range 与设备声明量程),
        // 但三级**都可以表达为一个绝对区间**, 所以这里只存 (device, channel) → (min, max),
        // 换算就是一次仿射映射, 没有任何分支。查表分两层, 优先级从高到低:
        //   1) 宿主显式推送 (SetChannelRange / ClearChannelRanges 管理) —— 权威值;
        //   2) 已装载 Slot 的轨道 engineering_range 派生 —— 内容自带兜底 (宿主未推送时生效,
        //      随 Slot 装载/卸载重建, 不受 ClearChannelRanges 影响)。
        // 两层都没有的通道 → 直通 (保持既有行为)。
        //
        // 换算只在**派发收口点**执行 (DispatchChannel / EvaluateSlotAtEngineering);
        // 求值本身仍返回归一化值, 因此跨语言契约测试仍可比对归一化语义。
        void  SetChannelRange(const char* deviceName, const char* channel,
                              float minValue, float maxValue);
        void  ClearChannelRanges();
        float ToEngineering(const char* deviceName, const char* channel, float normalized) const;

        // ── 未送达通道统计 (播放期可观测性) ───────────
        // 派发时查不到设备, 该通道输出就被**静默**丢弃 (既无日志也无 UI, 状态仍是 playing),
        // 现场只表现为"动作在走、设备不动"。这里把丢弃按通道键累加, 由宿主在停播时读一次汇总。
        //
        // 判据是"设备实例已在注册表里", 因此它同时覆盖两种情形:
        //   1) 设备名根本不在设备表 (宿主静态预检已报错);
        //   2) 名字在表里、但设备没建出来 (预检会放过 —— 离线编排的正常态)。
        // 与静态预检互补: 静态管"作者写错了", 这里管"这一刻真的没送出去"。
        //
        // 累加**不逐帧打日志** (会淹没日志且拖慢派发); 计数按场次统计, 在起播时清零 ——
        // 不在卸载时清, 因为"停播 → 读一次"的次序里卸载先发生, 那时清零会把统计抹空。
        /// 未送达通道汇总, JSON 对象: `{"YDL-0.oaxis.OAxis_6":4320, ...}`; 无则 `{}`。
        std::string GetUnresolvedChannelsJson() const;
        /// 手动清零 (一般在起播前调用; 起播本身也会清)。
        void ResetUnresolvedCounters();

        /// 被速率上限削过的通道汇总, 与上者同形: `{"YDL-0.oaxis.OAxis_6":1234, ...}`; 无则 `{}`。
        ///
        /// <para>存在的意义是"不静默": 内容要求的速率一旦超过通道上限就会被削, 但削过的
        /// 输出看起来完全正常 (只是变慢了)。作者拿这个汇总才知道是内容太快, 而不是设备坏了。</para>
        std::string GetRateLimitedChannelsJson() const;

        /// 与 EvaluateSlotAt 相同, 但输出**工程量** —— 换算在 native 侧完成(供编排端预览使用,
        /// 使"编辑器看到的"与"放映端下发的"共用同一段换算代码)。
        int  EvaluateSlotAtEngineering(const char* slotId, float timeMs,
                                       float* outValues, int maxChannels,
                                       char* outKeys = nullptr, int keyBufSize = 0) const;

        // ── 事件回调 ─────────────────────────────────
        void BindSlotEvent(const char* slotId, MotionEvent evt,
                           std::function<void()> callback);
        void BindSlotEvent(const char* slotId, MotionEvent evt,
                           std::function<void(const char* eventData)> callback);
        void SetEventCallback(MotionEventCallback callback);
        void SetEventDataCallback(MotionEventDataCallback callback);

        // ── 安全参数配置 ─────────────────────────────
        void SetSafetyConfig(float maxRatePerSecond);

        // ── 帧循环下发节流 (运行时覆盖) ───────────────
        // .motion 的 dispatch_interval_ms 是默认值 —— Tick 派发**默认使用**它;
        // 这里提供显式运行时覆盖能力, **不改变** Motion 配置。
        //   intervalMs < 0 → 清除覆盖, 回到 .motion 配置
        //   intervalMs = 0 → 不节流 (每帧下发)
        //   intervalMs > 0 → 间隔毫秒 (0 与 8..1000 被夹取)
        void SetSlotDispatchInterval(const char* slotId, int intervalMs);
        // 返回当前**生效**的下发节流间隔 (毫秒; 已含运行时覆盖)。
        int  GetSlotDispatchInterval(const char* slotId) const;

        // ── 内部: 由 IODeviceController::Update() 驱动 ──
        void Tick(float deltaSeconds);

        // ── 初始化/反初始化 ──────────────────────────
        void Initialize();
        void UnInitialize();

    private:
        MotionPlayer();
        ~MotionPlayer();
        MotionPlayer(const MotionPlayer&) = delete;
        MotionPlayer& operator=(const MotionPlayer&) = delete;

        class Impl;
        std::unique_ptr<Impl> _impl;
    };

}  // namespace IOToolkit
