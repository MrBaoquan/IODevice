/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2025-7-22
 *  Description: MotionPlayer 实现 — 多 Slot 动作文件播放核心
 */

#include "MotionPlayer.h"
#include "IOStatics.h"
#include "IOLog.h"

#include "IOClock.h"
#include <cstdint>
#include <map>
#include <vector>
#include <string>
#include <algorithm>
#include <cmath>
#include <mutex>
#include <fstream>
#include <sstream>

#include "nlohmann/json.hpp"

using json = nlohmann::json;
using namespace IOToolkit;

// ========================================================================
//  内部数据结构
// ========================================================================

namespace {

    struct MotionKeyframe {
        double timeMs;
        float  value;
        std::string interpolation; // "linear", "step", "bezier", "ease"
        // Bezier 控制点 (仅当 interpolation == "bezier" 时有效)
        float cp1x = 0, cp1y = 0, cp2x = 1, cp2y = 1;
    };

    struct MotionClip {
        double startMs;
        double endMs;
        std::vector<MotionKeyframe> keyframes;
    };

    struct MotionTrack {
        std::string deviceName;
        std::string oactionName;
        std::string outputType;  // "oaxis" or "oaction"
        std::string oaxisChannel;
        bool muted = false;
        std::vector<MotionClip> clips;
    };

    struct MotionTimeline {
        double durationMs = 0;
        std::vector<MotionTrack> tracks;
    };

    // ── 插值计算 ────────────────────────────────────

    float Lerp(float a, float b, float t) {
        return a + (b - a) * t;
    }

    float Clamp01(float v) {
        return (std::max)(0.0f, (std::min)(1.0f, v));
    }

    float EvaluateBezier(float t, float p0, float p1, float p2, float p3) {
        float u = 1.0f - t;
        return u * u * u * p0 + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * p3;
    }

    float InterpolateKeyframes(const MotionKeyframe& k0, const MotionKeyframe& k1, double timeMs) {
        if (k0.timeMs >= k1.timeMs)
            return k0.value;

        float t = Clamp01(static_cast<float>((timeMs - k0.timeMs) / (k1.timeMs - k0.timeMs)));

        if (k0.interpolation == "step") {
            return k0.value;
        }
        if (k0.interpolation == "bezier") {
            // C# 编辑器将 cp2y (TangentOut) 和 cp1y (TangentIn) 存储为斜率值
            // 转换为绝对控制点: P1 = k0.value + cp2y * |span| / 3
            //                   P2 = k1.value - cp1y * |span| / 3
            float span = k1.value - k0.value;
            float absSpan = std::abs(span);
            float p1 = k0.value + k0.cp2y * absSpan / 3.0f;
            float p2 = k1.value - k1.cp1y * absSpan / 3.0f;
            float bezierVal = EvaluateBezier(t, k0.value, p1, p2, k1.value);
            return bezierVal;
        }
        if (k0.interpolation == "ease") {
            // smoothstep ease-in-out
            float eased = t * t * (3.0f - 2.0f * t);
            return Lerp(k0.value, k1.value, eased);
        }
        // default: linear
        return Lerp(k0.value, k1.value, t);
    }

    float EvaluateClip(const MotionClip& clip, double absoluteTimeMs) {
        if (clip.keyframes.empty())
            return 0.5f;

        if (clip.keyframes.size() == 1)
            return clip.keyframes[0].value;

        // 时间在第一个关键帧之前
        if (absoluteTimeMs <= clip.keyframes.front().timeMs)
            return clip.keyframes.front().value;

        // 时间在最后一个关键帧之后
        if (absoluteTimeMs >= clip.keyframes.back().timeMs)
            return clip.keyframes.back().value;

        // 查找包围区间
        for (size_t i = 0; i + 1 < clip.keyframes.size(); ++i) {
            if (absoluteTimeMs >= clip.keyframes[i].timeMs &&
                absoluteTimeMs <= clip.keyframes[i + 1].timeMs) {
                return InterpolateKeyframes(clip.keyframes[i], clip.keyframes[i + 1], absoluteTimeMs);
            }
        }
        return 0.5f;
    }

    float EvaluateTrack(const MotionTrack& track, double absoluteTimeMs) {
        for (const auto& clip : track.clips) {
            if (absoluteTimeMs >= clip.startMs && absoluteTimeMs <= clip.endMs) {
                return EvaluateClip(clip, absoluteTimeMs);
            }
        }
        return 0.5f; // 无 Clip 覆盖时返回中位值
    }

    // ── 通道键构造 ──────────────────────────────────

    std::string MakeChannelKey(const MotionTrack& track) {
        if (track.outputType == "oaxis" && !track.oaxisChannel.empty()) {
            return track.deviceName + ".oaxis." + track.oaxisChannel;
        }
        return track.deviceName + "." + track.oactionName;
    }

    // ── SafetyGuard (安全保护) ──────────────────────

    struct SafetyConfig {
        float minValue = 0.0f;
        float maxValue = 1.0f;
        float maxRatePerSecond = 5.0f; // 最大变化速率 (每秒)
    };

    class SafetyGuard {
    public:
        float Check(const std::string& channelKey, float rawValue, double deltaSec) {
            float clamped = Clamp01(rawValue);

            // 速率限制
            auto it = _lastValues.find(channelKey);
            if (it != _lastValues.end() && deltaSec > 0.0001) {
                float lastVal = it->second;
                float maxDelta = _config.maxRatePerSecond * static_cast<float>(deltaSec);
                float diff = clamped - lastVal;
                if (std::abs(diff) > maxDelta) {
                    clamped = lastVal + (diff > 0 ? maxDelta : -maxDelta);
                }
            }

            clamped = Clamp01(clamped);
            _lastValues[channelKey] = clamped;
            return clamped;
        }

        void Reset() { _lastValues.clear(); }

        void SetMaxRatePerSecond(float rate) { _config.maxRatePerSecond = rate; }
        float GetMaxRatePerSecond() const { return _config.maxRatePerSecond; }
    private:
        SafetyConfig _config;
        std::map<std::string, float> _lastValues;
    };

    // ── .motion JSON 解析 ───────────────────────────

    MotionTimeline ParseMotionJson(const json& doc) {
        MotionTimeline timeline;
        try {
            timeline.durationMs = doc.value("duration_ms", 0.0);

            if (doc.contains("tracks") && doc["tracks"].is_array()) {
                for (const auto& jTrack : doc["tracks"]) {
                    MotionTrack track;
                    track.deviceName   = jTrack.value("device", "");
                    track.oactionName  = jTrack.value("oaction", "");
                    track.outputType   = jTrack.value("output_type", "oaction");
                    track.oaxisChannel = jTrack.value("oaxis_channel", "");
                    track.muted        = jTrack.value("muted", false);

                    if (jTrack.contains("clips") && jTrack["clips"].is_array()) {
                        for (const auto& jClip : jTrack["clips"]) {
                            MotionClip clip;
                            clip.startMs = jClip.value("start_ms", 0.0);
                            clip.endMs   = jClip.value("end_ms", 0.0);

                            if (jClip.contains("keyframes") && jClip["keyframes"].is_array()) {
                                for (const auto& jKf : jClip["keyframes"]) {
                                    MotionKeyframe kf;
                                    kf.timeMs          = jKf.value("time_ms", 0.0);
                                    kf.value           = jKf.value("value", 0.5f);
                                    kf.interpolation   = jKf.value("interpolation", "linear");
                                    kf.cp1x            = jKf.value("cp1x", 0.0f);
                                    kf.cp1y            = jKf.value("cp1y", 0.0f);
                                    kf.cp2x            = jKf.value("cp2x", 1.0f);
                                    kf.cp2y            = jKf.value("cp2y", 1.0f);
                                    clip.keyframes.push_back(std::move(kf));
                                }
                            }
                            track.clips.push_back(std::move(clip));
                        }
                    }
                    timeline.tracks.push_back(std::move(track));
                }
            }
        }
        catch (const json::exception& e) {
            IOLog::Instance().Log(std::string("MotionPlayer: JSON parse error: ") + e.what() + "\n");
        }
        return timeline;
    }

    MotionTimeline ParseMotionFile(const std::string& filePath) {
        MotionTimeline timeline;
        
        std::ifstream file(filePath);
        if (!file.is_open()) {
            IOLog::Instance().Log("MotionPlayer: Failed to open file: " + filePath + "\n");
            return timeline;
        }

        try {
            json doc = json::parse(file);
            timeline = ParseMotionJson(doc);
        }
        catch (const json::exception& e) {
            IOLog::Instance().Log(std::string("MotionPlayer: JSON parse error in ") + filePath + ": " + e.what() + "\n");
        }

        return timeline;
    }

    MotionTimeline ParseMotionString(const std::string& jsonContent) {
        MotionTimeline timeline;
        
        try {
            json doc = json::parse(jsonContent);
            timeline = ParseMotionJson(doc);
        }
        catch (const json::exception& e) {
            IOLog::Instance().Log(std::string("MotionPlayer: JSON string parse error: ") + e.what() + "\n");
        }

        return timeline;
    }

    // ── ChannelMixer (通道仲裁) ─────────────────────

    struct SlotChannelOutput {
        int priority;
        MixPolicy policy;
        std::map<std::string, float> channels; // channelKey → value
    };

    std::map<std::string, float> MixChannels(
        const std::vector<SlotChannelOutput>& outputs)
    {
        std::map<std::string, float> result;
        if (outputs.empty()) return result;

        // 收集每个通道的所有输出
        struct ChannelEntry {
            float value;
            int priority;
            MixPolicy policy;
        };
        std::map<std::string, std::vector<ChannelEntry>> channelSources;

        for (const auto& slot : outputs) {
            for (const auto& ch : slot.channels) {
                channelSources[ch.first].push_back({ ch.second, slot.priority, slot.policy });
            }
        }

        for (auto& [channelKey, entries] : channelSources) {
            if (entries.size() == 1) {
                result[channelKey] = entries[0].value;
                continue;
            }

            // 用第一个 entry 的 policy (优先级最高的)
            std::sort(entries.begin(), entries.end(),
                [](const ChannelEntry& a, const ChannelEntry& b) {
                    return a.priority < b.priority;
                });

            MixPolicy policy = entries[0].policy;
            switch (policy) {
                case MixPolicy::Priority:
                    result[channelKey] = entries[0].value;
                    break;
                case MixPolicy::Blend: {
                    float sum = 0;
                    for (auto& e : entries) sum += e.value;
                    result[channelKey] = sum / static_cast<float>(entries.size());
                    break;
                }
                case MixPolicy::Max: {
                    float maxVal = entries[0].value;
                    for (auto& e : entries) maxVal = (std::max)(maxVal, e.value);
                    result[channelKey] = maxVal;
                    break;
                }
                case MixPolicy::Additive: {
                    float sum = 0;
                    for (auto& e : entries) sum += e.value;
                    result[channelKey] = Clamp01(sum);
                    break;
                }
                case MixPolicy::Override:
                    // 最后加入的优先 (vector 最后一个)
                    result[channelKey] = entries.back().value;
                    break;
            }
        }
        return result;
    }

}  // anonymous namespace

// ========================================================================
//  MotionSlotImpl (单个 Slot 的内部状态)
// ========================================================================

namespace {

    struct MotionSlotImpl {
        std::string slotId;
        std::string filePath;
        int priority = 0;
        MixPolicy policy = MixPolicy::Priority;

        MotionState state = MotionState::Idle;
        MotionTimeline timeline;

        double currentTimeMs = 0;
        float  speed = 1.0f;
        bool   loop = false;
        ClockMode clockMode = ClockMode::Internal;
        double externalTimeMs = 0;

        // 高精度时间追踪
        std::uint64_t lastTick = 0;
        bool timerInitialized = false;

        // 回中状态
        double returnElapsedMs = 0;
        double returnDurationMs = 500.0; // 默认 500ms 回中
        std::map<std::string, float> returnStartValues;

        // 事件回调
        std::map<MotionEvent, std::function<void()>> callbacks;
        std::map<MotionEvent, std::function<void(const char*)>> dataCallbacks;

        MotionSlotImpl() = default;

        void StartTimer() {
            lastTick = IOClock::GetMilliseconds();
            timerInitialized = true;
        }

        void StopTimer() {
            timerInitialized = false;
        }

        // 求值当前帧所有通道
        std::map<std::string, float> EvaluateChannels() const {
            std::map<std::string, float> snapshot;
            for (const auto& track : timeline.tracks) {
                if (track.muted) continue;
                float val = EvaluateTrack(track, currentTimeMs);
                snapshot[MakeChannelKey(track)] = val;
            }
            return snapshot;
        }

        // 回中帧求值
        std::map<std::string, float> EvaluateReturnChannels() const {
            std::map<std::string, float> snapshot;
            if (returnStartValues.empty()) return snapshot;

            float t = Clamp01(static_cast<float>(returnElapsedMs / returnDurationMs));
            float eased = t * t * (3.0f - 2.0f * t); // smoothstep

            for (const auto& [key, startVal] : returnStartValues) {
                snapshot[key] = startVal + (0.5f - startVal) * eased;
            }
            return snapshot;
        }

        void FireEvent(MotionEvent evt, MotionEventCallback globalCb = nullptr,
                       MotionEventDataCallback globalDataCb = nullptr,
                       const char* eventData = nullptr) {
            // 无参回调
            auto it = callbacks.find(evt);
            if (it != callbacks.end() && it->second) {
                it->second();
            }
            // 有参回调
            auto dit = dataCallbacks.find(evt);
            if (dit != dataCallbacks.end() && dit->second) {
                dit->second(eventData ? eventData : "");
            }
            // 全局无参回调
            MotionEventCallback cb = globalCb ? globalCb : _globalCallbackRef;
            if (cb) {
                cb(slotId.c_str(), static_cast<int>(evt));
            }
            // 全局有参回调
            MotionEventDataCallback dcb = globalDataCb ? globalDataCb : _globalDataCallbackRef;
            if (dcb) {
                dcb(slotId.c_str(), static_cast<int>(evt), eventData ? eventData : "");
            }
        }

        // 指向 Impl::globalEventCallback 的引用 (由 LoadSlot 设置)
        MotionEventCallback _globalCallbackRef = nullptr;
        MotionEventDataCallback _globalDataCallbackRef = nullptr;
    };

} // anonymous namespace

// ========================================================================
//  MotionPlayer::Impl (pImpl 核心)
// ========================================================================

class MotionPlayer::Impl {
public:
    std::map<std::string, MotionSlotImpl> slots;
    SafetyGuard safetyGuard;
    std::recursive_mutex mutex;
    bool initialized = false;
    MotionEventCallback globalEventCallback = nullptr;
    MotionEventDataCallback globalEventDataCallback = nullptr;

    MotionSlotImpl* GetSlot(const char* slotId) {
        auto it = slots.find(slotId);
        return (it != slots.end()) ? &it->second : nullptr;
    }

    // 设备派发: channelKey → IODevice SetDO
    void DispatchChannel(const std::string& channelKey, float value) {
        auto dotIndex = channelKey.find('.');
        if (dotIndex == std::string::npos) return;

        std::string deviceName = channelKey.substr(0, dotIndex);
        std::string remainder = channelKey.substr(dotIndex + 1);

        if (!IODevices::HasDevice(deviceName)) return;

        IODevice& device = IODevices::GetDevice(deviceName);

        if (remainder.rfind("oaxis.", 0) == 0 && remainder.length() > 6) {
            // OAxis 模式
            std::string oaxisChannel = remainder.substr(6);
            device.SetDO(FKey(oaxisChannel.c_str()), value);
        } else {
            // OAction 模式
            device.SetDO(remainder.c_str(), value);
        }
    }

    // 分发混合后的通道值
    void DispatchMixedChannels(const std::map<std::string, float>& channels, float deltaSec) {
        for (const auto& [channelKey, rawValue] : channels) {
            float safeValue = safetyGuard.Check(channelKey, rawValue, deltaSec);
            DispatchChannel(channelKey, safeValue);
        }
    }
};

// ========================================================================
//  MotionPlayer 公开 API 实现
// ========================================================================

MotionPlayer::MotionPlayer() : _impl(std::make_unique<Impl>()) {}
MotionPlayer::~MotionPlayer() = default;

MotionPlayer& MotionPlayer::Instance() {
    static MotionPlayer instance;
    return instance;
}

void MotionPlayer::Initialize() {
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    if (_impl->initialized) return;
    _impl->initialized = true;
    IOLog::Instance().Log("MotionPlayer initialized.\n");
}

void MotionPlayer::UnInitialize() {
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    UnloadAll();
    _impl->initialized = false;
    IOLog::Instance().Log("MotionPlayer uninitialized.\n");
}

// ── Slot 生命周期 ────────────────────────────────────

int MotionPlayer::LoadSlot(const char* slotId, const char* filePath,
                           int priority, MixPolicy policy)
{
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);

    // 如果已存在同名 Slot, 先卸载
    auto existing = _impl->GetSlot(slotId);
    if (existing) {
        UnloadSlot(slotId);
    }

    MotionTimeline timeline = ParseMotionFile(filePath);
    if (timeline.tracks.empty()) {
        IOLog::Instance().Log(std::string("MotionPlayer: No tracks found in ") + filePath + "\n");
        return -1;
    }

    MotionSlotImpl slot;
    slot.slotId   = slotId;
    slot.filePath = filePath;
    slot.priority = priority;
    slot.policy   = policy;
    slot.timeline = std::move(timeline);
    slot.state    = MotionState::Idle;
    slot._globalCallbackRef = _impl->globalEventCallback;
    slot._globalDataCallbackRef = _impl->globalEventDataCallback;

    _impl->slots[slotId] = std::move(slot);

    IOLog::Instance().Log(std::string("MotionPlayer: Loaded slot '") + slotId + "' from " + filePath + "\n");
    return 0;
}

int MotionPlayer::LoadSlotFromJson(const char* slotId, const char* jsonContent,
                                   int priority, MixPolicy policy)
{
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);

    auto existing = _impl->GetSlot(slotId);
    if (existing) {
        UnloadSlot(slotId);
    }

    MotionTimeline timeline = ParseMotionString(jsonContent);
    if (timeline.tracks.empty()) {
        IOLog::Instance().Log(std::string("MotionPlayer: No tracks found in JSON for slot '") + slotId + "'\n");
        return -1;
    }

    MotionSlotImpl slot;
    slot.slotId   = slotId;
    slot.filePath  = "<json>";
    slot.priority = priority;
    slot.policy   = policy;
    slot.timeline = std::move(timeline);
    slot.state    = MotionState::Idle;
    slot._globalCallbackRef = _impl->globalEventCallback;
    slot._globalDataCallbackRef = _impl->globalEventDataCallback;

    _impl->slots[slotId] = std::move(slot);

    IOLog::Instance().Log(std::string("MotionPlayer: Loaded slot '") + slotId + "' from JSON string\n");
    return 0;
}

void MotionPlayer::UnloadSlot(const char* slotId) {
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);

    auto it = _impl->slots.find(slotId);
    if (it != _impl->slots.end()) {
        it->second.StopTimer();
        _impl->slots.erase(it);
        IOLog::Instance().Log(std::string("MotionPlayer: Unloaded slot '") + slotId + "'\n");
    }
}

void MotionPlayer::UnloadAll() {
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    for (auto& [id, slot] : _impl->slots) {
        slot.StopTimer();
    }
    _impl->slots.clear();
    _impl->safetyGuard.Reset();
    IOLog::Instance().Log("MotionPlayer: All slots unloaded.\n");
}

// ── 单 Slot 播控 ─────────────────────────────────────

int MotionPlayer::PlaySlot(const char* slotId) {
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    auto* slot = _impl->GetSlot(slotId);
    if (!slot) return -1;

    slot->currentTimeMs = 0;
    slot->state = MotionState::Playing;
    slot->StartTimer();
    slot->FireEvent(MotionEvent::StateChanged);
    return 0;
}

int MotionPlayer::PlaySlotFrom(const char* slotId, float timeMs) {
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    auto* slot = _impl->GetSlot(slotId);
    if (!slot) return -1;

    slot->currentTimeMs = (std::max)(0.0, (std::min)((double)timeMs, slot->timeline.durationMs));
    slot->state = MotionState::Playing;
    slot->StartTimer();
    slot->FireEvent(MotionEvent::StateChanged);
    return 0;
}

int MotionPlayer::PauseSlot(const char* slotId) {
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    auto* slot = _impl->GetSlot(slotId);
    if (!slot || slot->state != MotionState::Playing) return -1;

    slot->StopTimer();
    slot->state = MotionState::Paused;
    slot->FireEvent(MotionEvent::StateChanged);
    return 0;
}

int MotionPlayer::ResumeSlot(const char* slotId) {
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    auto* slot = _impl->GetSlot(slotId);
    if (!slot || slot->state != MotionState::Paused) return -1;

    slot->state = MotionState::Playing;
    slot->StartTimer();
    slot->FireEvent(MotionEvent::StateChanged);
    return 0;
}

int MotionPlayer::StopSlot(const char* slotId) {
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    auto* slot = _impl->GetSlot(slotId);
    if (!slot || slot->state == MotionState::Idle) return -1;

    if (slot->state == MotionState::Playing || slot->state == MotionState::Paused) {
        // 记录当前各通道值, 准备平滑回中
        slot->returnStartValues.clear();
        for (const auto& track : slot->timeline.tracks) {
            if (track.muted) continue;
            std::string key = MakeChannelKey(track);
            slot->returnStartValues[key] = EvaluateTrack(track, slot->currentTimeMs);
        }

        slot->returnElapsedMs = 0;
        slot->state = MotionState::Stopping;
        slot->StartTimer();
        slot->FireEvent(MotionEvent::StateChanged);
    }
    return 0;
}

int MotionPlayer::SeekSlot(const char* slotId, float timeMs) {
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    auto* slot = _impl->GetSlot(slotId);
    if (!slot) return -1;

    slot->currentTimeMs = (std::max)(0.0, (std::min)((double)timeMs, slot->timeline.durationMs));
    return 0;
}

void MotionPlayer::SetSlotSpeed(const char* slotId, float speed) {
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    auto* slot = _impl->GetSlot(slotId);
    if (slot) slot->speed = speed;
}

void MotionPlayer::SetSlotLoop(const char* slotId, bool loop) {
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    auto* slot = _impl->GetSlot(slotId);
    if (slot) slot->loop = loop;
}

void MotionPlayer::SetSlotClockMode(const char* slotId, ClockMode mode) {
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    auto* slot = _impl->GetSlot(slotId);
    if (slot) slot->clockMode = mode;
}

void MotionPlayer::SetSlotExternalTime(const char* slotId, float timeMs) {
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    auto* slot = _impl->GetSlot(slotId);
    if (slot) {
        slot->externalTimeMs = (std::max)(0.0, (std::min)((double)timeMs, slot->timeline.durationMs));
    }
}

// ── 批量操作 ─────────────────────────────────────────

void MotionPlayer::PlayAll() {
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    for (auto& [id, slot] : _impl->slots) {
        if (slot.state == MotionState::Idle || slot.state == MotionState::Paused) {
            slot.currentTimeMs = 0;
            slot.state = MotionState::Playing;
            slot.StartTimer();
            slot.FireEvent(MotionEvent::StateChanged);
        }
    }
}

void MotionPlayer::PauseAll() {
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    for (auto& [id, slot] : _impl->slots) {
        if (slot.state == MotionState::Playing) {
            slot.StopTimer();
            slot.state = MotionState::Paused;
            slot.FireEvent(MotionEvent::StateChanged);
        }
    }
}

void MotionPlayer::StopAll() {
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    // 获取所有 slot id 的副本 (StopSlot 不会修改 map)
    std::vector<std::string> ids;
    for (auto& [id, slot] : _impl->slots) {
        if (slot.state != MotionState::Idle) {
            ids.push_back(id);
        }
    }
    for (auto& id : ids) {
        // 直接操作 slot 而非调用 StopSlot 避免重入锁
        auto* slot = _impl->GetSlot(id.c_str());
        if (!slot) continue;
        if (slot->state == MotionState::Playing || slot->state == MotionState::Paused) {
            slot->returnStartValues.clear();
            for (const auto& track : slot->timeline.tracks) {
                if (track.muted) continue;
                std::string key = MakeChannelKey(track);
                slot->returnStartValues[key] = EvaluateTrack(track, slot->currentTimeMs);
            }
            slot->returnElapsedMs = 0;
            slot->state = MotionState::Stopping;
            slot->StartTimer();
            slot->FireEvent(MotionEvent::StateChanged);
        }
    }
}

void MotionPlayer::EnterSafeState() {
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    std::map<std::string, float> safeChannels;
    for (auto& [id, slot] : _impl->slots) {
        if (slot.state == MotionState::Playing || slot.state == MotionState::Paused) {
            for (const auto& [key, value] : slot.EvaluateChannels()) {
                safeChannels[key] = 0.5f;
            }
        }
        else if (slot.state == MotionState::Stopping) {
            for (const auto& [key, value] : slot.EvaluateReturnChannels()) {
                safeChannels[key] = 0.5f;
            }
        }

        if (slot.state != MotionState::Idle) {
            slot.StopTimer();
            slot.returnStartValues.clear();
            slot.returnElapsedMs = 0;
            slot.state = MotionState::Idle;
            slot.FireEvent(MotionEvent::StateChanged);
        }
    }

    for (const auto& [key, value] : safeChannels) {
        _impl->DispatchChannel(key, value);
    }
    IOLog::Instance().Log("MotionPlayer entered lifecycle safe state.\n");
}

// ── 状态查询 ─────────────────────────────────────────

MotionState MotionPlayer::GetSlotState(const char* slotId) const {
    auto* slot = _impl->GetSlot(slotId);
    return slot ? slot->state : MotionState::Idle;
}

float MotionPlayer::GetSlotCurrentTime(const char* slotId) const {
    auto* slot = _impl->GetSlot(slotId);
    return slot ? static_cast<float>(slot->currentTimeMs) : 0.0f;
}

float MotionPlayer::GetSlotDuration(const char* slotId) const {
    auto* slot = _impl->GetSlot(slotId);
    return slot ? static_cast<float>(slot->timeline.durationMs) : 0.0f;
}

int MotionPlayer::GetSlotCount() const {
    return static_cast<int>(_impl->slots.size());
}

// ── 事件回调 ─────────────────────────────────────────

void MotionPlayer::BindSlotEvent(const char* slotId, MotionEvent evt,
                                 std::function<void()> callback)
{
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    auto* slot = _impl->GetSlot(slotId);
    if (slot) {
        slot->callbacks[evt] = std::move(callback);
    }
}

void MotionPlayer::BindSlotEvent(const char* slotId, MotionEvent evt,
                                 std::function<void(const char* eventData)> callback)
{
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    auto* slot = _impl->GetSlot(slotId);
    if (slot) {
        slot->dataCallbacks[evt] = std::move(callback);
    }
}

void MotionPlayer::SetEventCallback(MotionEventCallback callback)
{
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    _impl->globalEventCallback = callback;
    // 同步到已有 slot
    for (auto& [id, slot] : _impl->slots) {
        slot._globalCallbackRef = callback;
    }
}

void MotionPlayer::SetEventDataCallback(MotionEventDataCallback callback)
{
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    _impl->globalEventDataCallback = callback;
    for (auto& [id, slot] : _impl->slots) {
        slot._globalDataCallbackRef = callback;
    }
}

// ── 纯求值(不派发) ──────────────────────────────────

int MotionPlayer::EvaluateSlotAt(const char* slotId, float timeMs,
                                 float* outValues, int maxChannels,
                                 char* outKeys, int keyBufSize) const
{
    auto* slot = _impl->GetSlot(slotId);
    if (!slot) return -1;

    // 求值指定时间的所有通道
    std::map<std::string, float> snapshot;
    for (const auto& track : slot->timeline.tracks) {
        if (track.muted) continue;
        float val = EvaluateTrack(track, static_cast<double>(timeMs));
        snapshot[MakeChannelKey(track)] = val;
    }

    int count = 0;
    int keyOffset = 0;
    for (const auto& [key, val] : snapshot) {
        if (count >= maxChannels) break;
        if (outValues) {
            outValues[count] = val;
        }
        // 写入通道键名 (以 \0 分隔)
        if (outKeys && keyBufSize > 0) {
            int needed = static_cast<int>(key.size()) + 1;
            if (keyOffset + needed <= keyBufSize) {
                memcpy(outKeys + keyOffset, key.c_str(), needed);
                keyOffset += needed;
            }
        }
        ++count;
    }
    return count;
}

// ── 安全参数配置 ─────────────────────────────────────

void MotionPlayer::SetSafetyConfig(float maxRatePerSecond)
{
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    _impl->safetyGuard.SetMaxRatePerSecond(maxRatePerSecond);
    IOLog::Instance().Log("MotionPlayer: SafetyConfig updated, maxRate=" + std::to_string(maxRatePerSecond) + "/s\n");
}

// ── Tick — 核心驱动 ──────────────────────────────────

void MotionPlayer::Tick(float deltaSeconds) {
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    if (_impl->slots.empty()) return;

    double deltaMs = deltaSeconds * 1000.0;

    // Phase 1: 更新每个 Slot 的时间状态
    for (auto& [id, slot] : _impl->slots) {
        switch (slot.state) {
            case MotionState::Playing: {
                if (slot.clockMode == ClockMode::External) {
                    slot.currentTimeMs = slot.externalTimeMs;
                } else {
                    slot.currentTimeMs += deltaMs * slot.speed;
                }

                if (slot.currentTimeMs >= slot.timeline.durationMs) {
                    if (slot.loop) {
                        slot.currentTimeMs = std::fmod(slot.currentTimeMs, slot.timeline.durationMs);
                    } else {
                        slot.currentTimeMs = slot.timeline.durationMs;
                        // 播放完成 → 开始回中
                        slot.returnStartValues.clear();
                        for (const auto& track : slot.timeline.tracks) {
                            if (track.muted) continue;
                            std::string key = MakeChannelKey(track);
                            slot.returnStartValues[key] = EvaluateTrack(track, slot.currentTimeMs);
                        }
                        slot.returnElapsedMs = 0;
                        slot.state = MotionState::Stopping;
                        slot.FireEvent(MotionEvent::PlaybackComplete);
                        slot.FireEvent(MotionEvent::StateChanged);
                    }
                }
                break;
            }
            case MotionState::Stopping: {
                slot.returnElapsedMs += deltaMs;
                if (slot.returnElapsedMs >= slot.returnDurationMs) {
                    slot.StopTimer();
                    slot.returnStartValues.clear();
                    slot.state = MotionState::Idle;
                    slot.FireEvent(MotionEvent::StateChanged);
                }
                break;
            }
            default:
                break;
        }
    }

    // Phase 2: 收集所有活跃 Slot 的通道输出
    std::vector<SlotChannelOutput> slotOutputs;
    for (auto& [id, slot] : _impl->slots) {
        if (slot.state == MotionState::Playing) {
            SlotChannelOutput output;
            output.priority = slot.priority;
            output.policy = slot.policy;
            output.channels = slot.EvaluateChannels();
            slotOutputs.push_back(std::move(output));
        }
        else if (slot.state == MotionState::Stopping) {
            SlotChannelOutput output;
            output.priority = slot.priority;
            output.policy = slot.policy;
            output.channels = slot.EvaluateReturnChannels();
            slotOutputs.push_back(std::move(output));
        }
    }

    // Phase 3: 通道仲裁 (ChannelMixer)
    auto mixedChannels = MixChannels(slotOutputs);

    // Phase 4: SafetyGuard 检查 + 设备派发
    _impl->DispatchMixedChannels(mixedChannels, deltaSeconds);
}
