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

        // ── 状态查询 ─────────────────────────────────
        MotionState GetSlotState(const char* slotId) const;
        float       GetSlotCurrentTime(const char* slotId) const;  // ms
        float       GetSlotDuration(const char* slotId) const;     // ms
        int         GetSlotCount() const;

        // ── 纯求值(不派发) ───────────────────────────
        int  EvaluateSlotAt(const char* slotId, float timeMs,
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
