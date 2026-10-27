/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2025-7-22
 *  Description: MotionPlayer 实现 — 多 Slot 动作文件播放核心
 */

#include "MotionPlayer.h"
#include "IOStatics.h"
#include "IOLog.h"
#include "InputSettings.h"
#include "CoreTypes/OutputWriteMode.h"

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

    // ── Idle 循环 (gap 填充) ────────────────────────
    // 轨道"无 clip 覆盖"时的待机循环动作, 永不覆盖真实 clip (最低优先级填充)。
    // 语义: 求值取 phase ∈ [0, periodMs), 沿 keyframes 环形插值;
    //       keyframes 时间相对循环起始 (0 ~ periodMs)。
    struct MotionIdleLoop {
        bool enabled = false;
        double periodMs = 1000.0;   // 一个循环周期时长
        double blendMs = 0.0;       // 与相邻 clip 的交叉淡化窗口 (0=硬切换)
        std::string phaseMode = "continuous"; // "continuous" | "restart"
        double phaseOffsetMs = 0.0; // 相位偏移 (同组多轨错相编排, 毫秒)
        std::string groupId;        // 节奏组标识 (同组多轨在 continuous 下共享全局时钟同步; 空=独立)
        std::vector<MotionKeyframe> keyframes;
    };

    struct MotionTrack {
        std::string deviceName;
        std::string oactionName;
        std::string outputType;  // "oaxis" or "oaction"
        std::string oaxisChannel;
        std::string valueType;   // "float" or "bool" (用于中性值默认推断)
        bool muted = false;
        /// 禁用: 完全不参与播放 (输出与回中都不参与)。
        ///
        /// <para>必须在这里也判, 不能只靠宿主: 演示端 (UNIPlayer) 与编辑器读同一份 .motion,
        /// 若只有编辑器尊重它, 同一份内容在两处行为就不一致。</para>
        bool enabled = true;
        /// 独奏: 只要本时间轴存在"启用且独奏"的轨道, 其余轨道都不参与输出。
        bool solo = false;
        float neutralValue = 0.5f; // 无 Clip 覆盖时的输出值 (解析自 neutral_value, 缺省按 value_type)
        // 轨道级工程量程 (对称行程, 绕中位): engineering_range。>0 表示本轨道自带量程,
        // 核心在 LoadSlot 时据此登记 (device, channel) → (min, max) 的兜底;
        // 宿主显式 SetChannelRange 可覆盖。0 = 未声明 (回退设备声明量程 / 直通)。
        float engineeringRange = 0.0f;
        MotionIdleLoop idle;      // 可选待机循环 (缺省 disabled → 回退 neutralValue)
        std::vector<MotionKeyframe> overrideKeyframes; // 空窗覆盖关键帧 (绝对时间, 两点插值, 之外回 idle)
        std::vector<MotionClip> clips;
    };

    struct MotionTimeline {
        double durationMs = 0;
        // 帧循环下发节流间隔 (毫秒; 0 = 不节流)。来自 .motion 的 dispatch_interval_ms,
        // Tick 派发默认使用; 运行时可经 C ABI 覆盖 (见 MotionSlotImpl::dispatchIntervalOverrideMs)。
        int    dispatchIntervalMs = 0;
        std::vector<MotionTrack> tracks;
    };

    // ── 轨道参与输出的判据 (唯一收口) ────────────────
    //
    // 三个"关掉"位必须在这里一次判清, 否则会出现"界面看着对、设备照发"的错位 ——
    // 宿主 (IOStudio 的实时预览) 早已按三者过滤, 而本层曾经只认 muted, 于是
    // 被禁用的轨道在面板上不再跳数, 却仍在驱动真设备。
    //
    //   enabled=false  → 不参与 (禁用)
    //   muted=true     → 不参与 (静音)
    //   solo           → 只要存在"启用且独奏"的轨道, 其余一律不参与
    //
    // 禁用也参与 solo 的判定源: 被禁用的独奏轨道不应把别人压掉 (与宿主一致)。
    static bool AnySoloActive(const std::vector<MotionTrack>& tracks) {
        for (const auto& t : tracks) {
            if (t.enabled && t.solo)
                return true;
        }
        return false;
    }

    static bool IsTrackActive(const MotionTrack& track, bool anySolo) {
        if (!track.enabled) return false;
        if (track.muted) return false;
        if (anySolo && !track.solo) return false;
        return true;
    }

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
        if (k0.interpolation == "ease" || k0.interpolation == "ease_in_out") {
            // smoothstep ease-in-out (兼容 C# 编辑器写出的 ease_in_out)
            float eased = t * t * (3.0f - 2.0f * t);
            return Lerp(k0.value, k1.value, eased);
        }
        // default: linear
        return Lerp(k0.value, k1.value, t);
    }

    float EvaluateClip(const MotionClip& clip, double absoluteTimeMs, float neutralValue) {
        if (clip.keyframes.empty())
            return neutralValue;

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
        return neutralValue;
    }

    // ── Idle 循环求值 ──────────────────────────────
    // 采样 idle 循环在 phaseMs 处的值 (phase ∈ [0, periodMs))。
    // keyframes 视为环形序列: [0..last] 常规插值, [last..period] 用 last→first 闭合 (无缝循环)。
    float EvaluateIdleLoop(const MotionIdleLoop& idle, double phaseMs) {
        const auto& kfs = idle.keyframes;
        if (kfs.empty())
            return 0.5f;
        if (kfs.size() == 1)
            return kfs[0].value;

        double period = idle.periodMs > 1.0 ? idle.periodMs : 1.0;
        double p = std::fmod(phaseMs, period);
        if (p < 0.0)
            p += period;

        // 常规分段
        for (size_t i = 0; i + 1 < kfs.size(); ++i) {
            if (p >= kfs[i].timeMs && p <= kfs[i + 1].timeMs)
                return InterpolateKeyframes(kfs[i], kfs[i + 1], p);
        }
        // 跨环闭合: last → first (first 的虚拟时间 = period)
        if (p >= kfs.back().timeMs) {
            double span = period - kfs.back().timeMs;
            if (span <= 0.001)
                return kfs.back().value;
            MotionKeyframe k1v = kfs.front();
            k1v.timeMs = period; // 虚拟下一帧
            return InterpolateKeyframes(kfs.back(), k1v, p);
        }
        return kfs.back().value;
    }

    // 计算当前 gap 内的 idle 相位:
    //   "continuous" → 用全局绝对时间 (跨片段空窗保持连贯), 同组多轨共享全局时钟 → 天然同步
    //   "restart"    → 从最近一个已结束 clip 的 endMs 起算 (每个 gap 从头播)
    //   均叠加 phaseOffsetMs (同组多轨错相编排)
    double ComputeIdlePhase(const MotionTrack& track, double absoluteTimeMs) {
        double baseMs;
        if (track.idle.phaseMode == "restart") {
            double gapStartMs = 0.0;
            for (const auto& c : track.clips)
                if (c.endMs < absoluteTimeMs && c.endMs > gapStartMs)
                    gapStartMs = c.endMs;
            baseMs = absoluteTimeMs - gapStartMs;
        } else {
            baseMs = absoluteTimeMs; // continuous
        }
        return baseMs + track.idle.phaseOffsetMs;
    }

    // 空窗值: 启用 idle → 循环求值; 否则回退中性值
    float EvaluateIdleAt(const MotionTrack& track, double absoluteTimeMs) {
        if (track.idle.enabled && !track.idle.keyframes.empty())
            return EvaluateIdleLoop(track.idle, ComputeIdlePhase(track, absoluteTimeMs));
        return track.neutralValue;
    }

    // bool 轨输出量化: 所有 blend/插值结果回到 0/1 (与 C# 编辑器镜像一致, 避免 SetDO 输出 0.5 中间值)
    float QuantizeTrackValue(const MotionTrack& track, float v) {
        if (track.valueType == "bool")
            return v >= 0.5f ? 1.0f : 0.0f;
        return v;
    }

    // bool 轨专用阶梯求值 —— 镜像 C# TrackValueEvaluator.EvaluateBoolStep (编辑器亦按阶梯渲染)。
    // 开关不应在插值中点翻转: 曾用"插值后再量化", 于是 0→1 的线性段会在中点翻, 与编辑器不一致。
    float EvaluateBoolStep(const MotionClip& clip, double localTimeMs) {
        if (clip.keyframes.empty())
            return 0.0f;
        float val = clip.keyframes.front().value;
        for (const auto& kf : clip.keyframes) {
            if (kf.timeMs <= localTimeMs)
                val = kf.value;
            else
                break;
        }
        return val >= 0.5f ? 1.0f : 0.0f;
    }

    float EvaluateTrack(const MotionTrack& track, double absoluteTimeMs) {
        // 1) 真实 clip 覆盖 (最高优先级, 永不被打断)
        for (const auto& clip : track.clips) {
            if (absoluteTimeMs >= clip.startMs && absoluteTimeMs <= clip.endMs) {
                // 关键帧时间**相对 clip 起点** (与编辑器写入 .motion 的约定一致:
                // 编辑器用 `绝对时间 - clip.StartMs` 求值、拖动时也用 `clip.StartMs + 相对`)。
                // 这里曾误传绝对时间, 导致所有"起始不为 0 的 clip"在放映端取错时间点
                // (常量曲线看不出差异, 一旦关键帧有变化就会与编辑器预览不一致)。
                const double localMs = absoluteTimeMs - clip.startMs;
                // bool 轨走阶梯 (镜像 C#); float 轨走插值后钳位
                if (track.valueType == "bool")
                    return EvaluateBoolStep(clip, localMs);
                return QuantizeTrackValue(track, EvaluateClip(clip, localMs, track.neutralValue));
            }
        }

        // 2) Overlay 覆盖关键帧 (空窗自由数值点, 优先级高于 idle)
        //    语义: 两点间插值; 首点之前/末点之后回落到 idle (与底层融合);
        //          单点 = 仅该瞬间生效 (脉冲), 其余时刻回 idle。
        if (!track.overrideKeyframes.empty()) {
            const auto& ov = track.overrideKeyframes;
            if (ov.size() == 1) {
                if (std::abs(absoluteTimeMs - ov.front().timeMs) < 0.5)
                    return QuantizeTrackValue(track, ov.front().value);
                return QuantizeTrackValue(track, EvaluateIdleAt(track, absoluteTimeMs));
            }
            if (absoluteTimeMs < ov.front().timeMs)
                return QuantizeTrackValue(track, EvaluateIdleAt(track, absoluteTimeMs));
            if (absoluteTimeMs > ov.back().timeMs)
                return QuantizeTrackValue(track, EvaluateIdleAt(track, absoluteTimeMs));
            for (size_t i = 0; i + 1 < ov.size(); ++i) {
                if (absoluteTimeMs >= ov[i].timeMs && absoluteTimeMs <= ov[i + 1].timeMs) {
                    return QuantizeTrackValue(
                        track,
                        InterpolateKeyframes(ov[i], ov[i + 1], absoluteTimeMs)
                    );
                }
            }
        }

        // 3) 与临近 clip 的交叉淡化过渡带 (仅当配置了 blend_ms)
        float idleVal = EvaluateIdleAt(track, absoluteTimeMs);
        if (track.idle.blendMs > 0.0) {
            for (const auto& clip : track.clips) {
                double blend = track.idle.blendMs;
                // clip 开始前: idle → clip 首帧值 淡入
                if (absoluteTimeMs >= clip.startMs - blend && absoluteTimeMs < clip.startMs) {
                    double t = (absoluteTimeMs - (clip.startMs - blend)) / blend;
                    float clipFirstVal = clip.keyframes.empty()
                        ? track.neutralValue
                        : clip.keyframes.front().value;
                    float blended = Lerp(idleVal, clipFirstVal, Clamp01(static_cast<float>(t)));
                    return QuantizeTrackValue(track, blended);
                }
                // clip 结束后: clip 末帧值 → idle 淡出
                if (absoluteTimeMs > clip.endMs && absoluteTimeMs <= clip.endMs + blend) {
                    double t = (absoluteTimeMs - clip.endMs) / blend;
                    float clipLastVal = clip.keyframes.empty()
                        ? track.neutralValue
                        : clip.keyframes.back().value;
                    float blended = Lerp(clipLastVal, idleVal, Clamp01(static_cast<float>(t)));
                    return QuantizeTrackValue(track, blended);
                }
            }
        }

        // 4) 纯 idle 循环 / 中性值 (gap 填充)
        return QuantizeTrackValue(track, idleVal);
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
        /// 全局兜底速率 (每秒, 归一化值)。**默认 0 = 不限速**。
        ///
        /// 为什么默认不限速而不是给个宽松值: 通道的"安全速度"完全取决于硬件语义 ——
        /// 电缸是模拟量 (要限), 喷水/风扇/游戏状态宣告是开关量或命令 (必须瞬时)。
        /// 给一个全局值会无差别地把开关量也拖成斜坡 (实测会把 200ms 宽的脉冲削成半截)。
        /// 所以保护来自**显式配置** (设备表的全局 Key 属性 SlewRate); 未配置 = 保持既有行为不变。
        float maxRatePerSecond = 0.0f;
    };

    class SafetyGuard {
    public:
        /// 间隔超过该值不再视为"连续" —— 但**不是**跳过限速, 见 Check 里的处理。
        static constexpr double kDiscontinuitySec = 0.5;

        /// 通道被静音/暂停期间, 恢复后允许按多少秒的行程去"追"上次的值。
        ///
        /// 取小值是刻意的: 这个额度决定了**单步最大跳变** (rate × kResyncSeconds)。
        /// 0.5s 时电缸 (0.5/s) 一步能跳 250mm —— 实测仍被反映为"跳变过大";
        /// 收到 0.1s 后单步上限降到 50mm, 既不会一步到位, 也不会恢复得太慢。
        static constexpr double kResyncSeconds = 0.1;

        float Check(const std::string& channelKey, float rawValue, float channelRateLimit) {
            const double nowMs = static_cast<double>(IOClock::GetMilliseconds());

            auto valueIt = _lastValues.find(channelKey);
            auto timeIt = _lastTimes.find(channelKey);
            if (valueIt == _lastValues.end() || timeIt == _lastTimes.end()) {
                // 首次见到该通道: 建立基线, 不限制
                float first = Clamp01(rawValue);
                _lastValues[channelKey] = first;
                _lastTimes[channelKey] = nowMs;
                return first;
            }

            const double deltaSec = (nowMs - timeIt->second) / 1000.0;
            _lastTimes[channelKey] = nowMs;

            float clamped = Clamp01(rawValue);

            // 生效上限: 按通道配置优先, 未配置才用全局兜底。
            // rate <= 0 表示该通道明确不限速。
            float rate = channelRateLimit != 0.f ? channelRateLimit : _config.maxRatePerSecond;

            if (rate > 0.f)
            {
                // 实际允许的变化时间:
                //  - 正常连续: 用真实间隔
                //  - 间隔过大 (静音/暂停后恢复): 用 kResyncSeconds 封顶 ——
                //    关键点是**仍然限速**, 而不是像旧实现那样直接跳过限速 (那会让
                //    "停一段再跳"变成一步到位, 实测出现过 836mm 的单步跳变)。
                //
                // 注意: dt≈0 (同一毫秒内被重复调用) **不能**当作"不连续" ——
                // 那会给它整整 kResyncSeconds 的额度, 而它每秒被调用很多次,
                // 限速就完全失效了 (实测就是这个原因导致 0.5/s 配置却跑出 3/s)。
                // dt≈0 就是"没有时间流过", 允许的变化量自然是 0。
                double effectiveSec = deltaSec >= kDiscontinuitySec ? kResyncSeconds : deltaSec;

                float maxDelta = rate * static_cast<float>(effectiveSec);
                float lastVal = valueIt->second;
                float diff = clamped - lastVal;
                if (std::abs(diff) > maxDelta) {
                    clamped = lastVal + (diff > 0 ? maxDelta : -maxDelta);
                    ++_limitedCount;
                    // 按通道累计, 供宿主在停播时汇总"哪些通道被限速削过" ——
                    // 不做这个, 作者只会看到"电缸变慢了", 不知道是自己的内容太快。
                    ++_limitedByChannel[channelKey];
                }
            }

            clamped = Clamp01(clamped);
            _lastValues[channelKey] = clamped;
            return clamped;
        }

        void Reset() {
            _lastValues.clear();
            _lastTimes.clear();
            _limitedCount = 0;
            _limitedByChannel.clear();
        }

        void SetMaxRatePerSecond(float rate) { _config.maxRatePerSecond = rate; }
        float GetMaxRatePerSecond() const { return _config.maxRatePerSecond; }

        /// 本次播放以来被限速削过的次数 (供宿主摘要"内容要求了 X, 硬件限到 Y")。
        long long GetLimitedCount() const { return _limitedCount; }

        /// 按通道统计的被限速次数。空表 = 没有任何通道被削。
        const std::map<std::string, uint64_t>& GetLimitedByChannel() const {
            return _limitedByChannel;
        }

    private:
        SafetyConfig _config;
        std::map<std::string, float> _lastValues;
        std::map<std::string, double> _lastTimes;
        long long _limitedCount = 0;
        std::map<std::string, uint64_t> _limitedByChannel;
    };

    // ── .motion JSON 解析 ───────────────────────────

    MotionTimeline ParseMotionJson(const json& doc) {
        MotionTimeline timeline;
        try {
            timeline.durationMs = doc.value("duration_ms", 0.0);

            // 帧循环下发节流 (毫秒): 0=不节流; 否则夹取到 8..1000 (与旧编排端设置口径一致)。
            {
                int interval = doc.value("dispatch_interval_ms", 0);
                if (interval < 0)
                    interval = 0;
                else if (interval > 0 && interval < 8)
                    interval = 8;
                else if (interval > 1000)
                    interval = 1000;
                timeline.dispatchIntervalMs = interval;
            }

            if (doc.contains("tracks") && doc["tracks"].is_array()) {
                for (const auto& jTrack : doc["tracks"]) {
                    MotionTrack track;
                    track.deviceName   = jTrack.value("device", "");
                    track.oactionName  = jTrack.value("oaction", "");
                    track.outputType   = jTrack.value("output_type", "oaction");
                    track.oaxisChannel = jTrack.value("oaxis_channel", "");
                    track.muted        = jTrack.value("muted", false);
                    // 缺省 true: 老 .motion 没有该键时按"启用"处理。
                    // 注意写入端必须始终写出该键 —— 若被省略, "省略"与"false"就分不开了。
                    track.enabled      = jTrack.value("enabled", true);
                    track.solo         = jTrack.value("solo", false);
                    // 中性/空闲值: 显式 neutral_value 优先, 缺省按 value_type (bool=0, float=0.5)
                    track.valueType    = jTrack.value("value_type", "float");
                    if (jTrack.contains("neutral_value") && jTrack["neutral_value"].is_number()) {
                        float nv = jTrack["neutral_value"].get<float>();
                        track.neutralValue = nv < 0.0f ? 0.0f : (nv > 1.0f ? 1.0f : nv);
                    } else {
                        track.neutralValue = (track.valueType == "bool") ? 0.0f : 0.5f;
                    }

                    // 轨道级工程量程 (对称行程): >0 时供 LoadSlot 登记兜底量程
                    if (jTrack.contains("engineering_range") && jTrack["engineering_range"].is_number()) {
                        float er = jTrack["engineering_range"].get<float>();
                        track.engineeringRange = (er > 0.0f) ? er : 0.0f;
                    }

                    // Idle 循环 (可选): 无 clip 覆盖时的待机循环动作
                    if (jTrack.contains("idle") && jTrack["idle"].is_object()) {
                        const auto& jIdle = jTrack["idle"];
                        track.idle.enabled       = jIdle.value("enabled", true);
                        track.idle.periodMs      = jIdle.value("period_ms", 1000.0);
                        track.idle.blendMs       = jIdle.value("blend_ms", 0.0);
                        track.idle.phaseMode     = jIdle.value("phase_mode", "continuous");
                        track.idle.phaseOffsetMs = jIdle.value("phase_offset_ms", 0.0);
                        track.idle.groupId       = jIdle.value("group_id", "");
                        if (jIdle.contains("keyframes") && jIdle["keyframes"].is_array()) {
                            for (const auto& jKf : jIdle["keyframes"]) {
                                MotionKeyframe kf;
                                kf.timeMs          = jKf.value("time_ms", 0.0);
                                kf.value           = jKf.value("value", 0.5f);
                                kf.interpolation   = jKf.value("interpolation", "linear");
                                kf.cp1x            = jKf.value("cp1x", 0.0f);
                                kf.cp1y            = jKf.value("cp1y", 0.0f);
                                kf.cp2x            = jKf.value("cp2x", 1.0f);
                                kf.cp2y            = jKf.value("cp2y", 1.0f);
                                track.idle.keyframes.push_back(std::move(kf));
                            }
                        }
                    }

                    // Overlay 覆盖关键帧 (可选): 空窗自由数值点 (绝对时间), 优先级高于 idle
                    if (jTrack.contains("override_keyframes") && jTrack["override_keyframes"].is_array()) {
                        for (const auto& jKf : jTrack["override_keyframes"]) {
                            MotionKeyframe kf;
                            kf.timeMs          = jKf.value("time_ms", 0.0);
                            kf.value           = jKf.value("value", 0.5f);
                            kf.interpolation   = jKf.value("interpolation", "linear");
                            kf.cp1x            = jKf.value("cp1x", 0.0f);
                            kf.cp1y            = jKf.value("cp1y", 0.0f);
                            kf.cp2x            = jKf.value("cp2x", 1.0f);
                            kf.cp2y            = jKf.value("cp2y", 1.0f);
                            track.overrideKeyframes.push_back(std::move(kf));
                        }
                        std::sort(
                            track.overrideKeyframes.begin(),
                            track.overrideKeyframes.end(),
                            [](const MotionKeyframe& a, const MotionKeyframe& b) {
                                return a.timeMs < b.timeMs;
                            }
                        );
                    }

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

        // ── 帧循环下发节流 ─────────────────────────
        // 默认值来自 .motion 的 dispatch_interval_ms; C ABI 可显式设置**运行时覆盖**,
        // 覆盖只影响运行时, 不写回配置。
        int    dispatchIntervalOverrideMs = -1; // <0 = 未覆盖 (用 .motion 配置)
        double lastDispatchMs = 0;              // 上次真正下发的时刻 (IOClock 毫秒)
        bool   dispatchTimerInitialized = false;

        // 当前生效的节流间隔 (毫秒; 0 = 不节流)。
        int EffectiveDispatchIntervalMs() const {
            return dispatchIntervalOverrideMs >= 0 ? dispatchIntervalOverrideMs
                                                   : timeline.dispatchIntervalMs;
        }

        // 本帧是否到达下发时刻 (间隔<=0 恒 true; 首次未初始化恒 true → 立即下发)。
        bool ShouldDispatchNow(double nowMs) const {
            const int interval = EffectiveDispatchIntervalMs();
            if (interval <= 0) return true;
            if (!dispatchTimerInitialized) return true;
            return (nowMs - lastDispatchMs) >= static_cast<double>(interval);
        }

        void MarkDispatched(double nowMs) {
            lastDispatchMs = nowMs;
            dispatchTimerInitialized = true;
        }

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
            const bool anySolo = AnySoloActive(timeline.tracks);
            for (const auto& track : timeline.tracks) {
                if (!IsTrackActive(track, anySolo)) continue;
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

    // ── 通道工程量程表: "device\x1Fchannel" → (min, max) ──
    // 三级优先级 (轨道 engineering_range → 设备声明量程 → 直通) 的**解析**由宿主完成
    // (它才知道轨道与设备声明), 但三级都能表达为绝对区间, 所以这里只做一次仿射映射, 无分支。
    //
    // 表分两层, 优先级从高到低:
    //   1) channelRanges —— 宿主显式推送 (SetChannelRange / ClearChannelRanges 管理), 权威值;
    //   2) trackRanges   —— 由**当前已装载 Slot** 的轨道 engineering_range 派生
    //                       (RebuildTrackRanges), 是"已装载 Slots"的纯函数, 只读;
    //                       宿主的 ClearChannelRanges 不会抹掉它。
    // 这样"内容自带工程含义"的 .motion 在未推送量程的宿主 (如 UNIPlayer) 上也能得到工程量,
    // 同时宿主清空/重推量程不会与内容兜底互相踩踏。
    std::map<std::string, std::pair<float, float>> channelRanges;
    std::map<std::string, std::pair<float, float>> trackRanges;

    // ── 未送达通道统计: 完整通道键 → 丢弃次数 ──
    // DispatchChannel 里 HasDevice 失败就 `return` —— 输出被**静默**丢弃 (无日志、无 UI,
    // 状态仍是 playing), 现场只表现为"动作在走、设备不动"。
    //
    // 判据是 IODevices::HasDevice, 即"设备实例已在注册表里", 所以它同时覆盖两种情形:
    //   1) 设备名根本不在设备表 (宿主静态预检已报错);
    //   2) 名字在表里、但设备没建出来 (预检会放过 —— 离线编排的正常态)。
    // 这两层互补: 静态那层管"作者写错了", 这里管"这一刻真的没送出去"。
    //
    // **只累加、不逐帧打日志**: 逐帧打日志会淹没日志且拖慢派发。汇总由宿主在停播时读一次
    // (GetUnresolvedChannelsJson), 计数在**起播时**清零 (PlaySlot / PlaySlotFrom) —— 按"一次播放"
    // 统计, 不跨场次累加。**不**在 Unload 时清: 停播路径会先 UnloadSlot 再读汇总, 在那清会把统计抹空。
    std::map<std::string, uint64_t> unresolvedChannels;

    static std::string RangeKey(const std::string& deviceName, const std::string& channel) {
        return deviceName + "\x1F" + channel;
    }

    /// 未送达通道的汇总 (供宿主一次读取)。空表返回 `{}`。
    /// 形如 `{"YDL-0.oaxis.OAxis_6":4320,"YDL-0.oaxis.OAxis_16":720}` ——
    /// 键与 .motion 里的通道写法同形, 人一眼能对上。
    std::string BuildUnresolvedJson() const {
        json out = json::object();
        for (const auto& [key, count] : unresolvedChannels) {
            out[key] = count;
        }
        return out.dump();
    }

    /// 被速率上限削过的通道汇总 (供宿主一次读取, 停播时用)。
    /// 与 unresolvedChannels 同形: `{"YDL-0.oaxis.OAxis_6":1234}`。
    /// 用途是让作者知道"你的内容要求的速度超过了硬件上限, 被削到了多少"——
    /// 只有说清楚这一点, "电缸变慢了"才不会被误当成故障。
    std::string BuildRateLimitedJson() const {
        json out = json::object();
        for (const auto& [key, count] : safetyGuard.GetLimitedByChannel()) {
            out[key] = count;
        }
        return out.dump();
    }

    /// 归一化 → 工程量。宿主推送优先, 其次轨道自带兜底; 都无或退化区间 → 直通 (既有行为)。
    float ConvertToEngineering(const std::string& deviceName, const std::string& channel,
                               float normalized) const {
        const std::string key = RangeKey(deviceName, channel);
        const std::pair<float, float>* range = nullptr;
        auto host = channelRanges.find(key);
        if (host != channelRanges.end()) {
            range = &host->second;
        } else {
            auto track = trackRanges.find(key);
            if (track != trackRanges.end()) range = &track->second;
        }
        if (!range) return normalized;
        const float mn = range->first;
        const float mx = range->second;
        if (!(mx > mn)) return normalized;
        const float v = (std::max)(0.0f, (std::min)(1.0f, normalized));
        return mn + v * (mx - mn);
    }

    /// 从**当前已装载的所有 Slot** 重建轨道兜底量程 (内容自带量程)。
    /// 语义与宿主 EngineeringValueConverter.ResolveRange 的"轨道显式覆盖"分支一致:
    /// 绕中位的对称行程 → 绝对区间 min=(0-neutral)*range, max=(1-neutral)*range。
    /// 因为它是"已装载 Slots"的纯函数, 宿主 ClearChannelRanges / SetChannelRange 不影响它,
    /// 多个 Slot/多个宿主也不会因清表而互相丢失兜底量程。
    void RebuildTrackRanges() {
        trackRanges.clear();
        for (const auto& [id, slot] : slots) {
            const bool anySolo = AnySoloActive(slot.timeline.tracks);
            for (const auto& track : slot.timeline.tracks) {
                if (!IsTrackActive(track, anySolo)) continue;
                const float range = track.engineeringRange;
                if (!(range > 0.0f)) continue;
                if (track.deviceName.empty()) continue;
                const std::string channel =
                    (track.outputType == "oaxis" && !track.oaxisChannel.empty())
                        ? track.oaxisChannel
                        : track.oactionName;
                if (channel.empty()) continue;
                const float neutral = track.neutralValue;
                trackRanges[RangeKey(track.deviceName, channel)] =
                    std::make_pair((0.0f - neutral) * range, (1.0f - neutral) * range);
            }
        }
    }

    /// EvaluateSlotAt / EvaluateSlotAtEngineering 的共用实现 (engineering=true 时输出工程量)
    int EvaluateSlotAtImpl(const char* slotId, float timeMs, bool engineering,
                           float* outValues, int maxChannels,
                           char* outKeys, int keyBufSize);

    MotionSlotImpl* GetSlot(const char* slotId) {
        auto it = slots.find(slotId);
        return (it != slots.end()) ? &it->second : nullptr;
    }

    // 设备派发: channelKey → IODevice SetDO
    // Device dispatch: channelKey -> IODevice SetDO
    //
    // 归一化 → 工程量 的换算就在这里 (派发收口点), 使用 Impl::channelRanges 里由宿主推送的
    // (min, max) 表; 宿主未推送时回退到 Impl::trackRanges (由已装载 Slot 的轨道
    // engineering_range 派生)。求值本身仍返回归一化值, 所以跨语言契约测试比对的仍是归一化语义。
    //
    // NOTE: 编排端(编辑器)目前不驱动本路径 (它由 C# 侧驱动时间并派发); 本路径服务放映端
    // (UNIPlayer)。两端的换算必须一致 —— 这正是把换算放进 native 并让编排端也复用它的原因。
    void DispatchChannel(const std::string& channelKey, float value,
                         EOutputWriteMode mode = EOutputWriteMode::Configured) {
        auto dotIndex = channelKey.find('.');
        if (dotIndex == std::string::npos) return;

        std::string deviceName = channelKey.substr(0, dotIndex);
        std::string remainder = channelKey.substr(dotIndex + 1);

        // 查不到设备 → 这次输出**送不出去**。这里只记一笔 (不打日志, 见 unresolvedChannels 注释),
        // 由宿主在停播时汇总成"本次播放 N 个通道未送达"。
        if (!IODevices::HasDevice(deviceName)) {
            ++unresolvedChannels[channelKey];
            return;
        }

        IODevice& device = IODevices::GetDevice(deviceName);

        if (remainder.rfind("oaxis.", 0) == 0 && remainder.length() > 6) {
            // OAxis 模式
            std::string oaxisChannel = remainder.substr(6);
            device.SetDO(FKey(oaxisChannel.c_str()),
                         ConvertToEngineering(deviceName, oaxisChannel, value), mode);
        } else {
            // OAction 模式
            device.SetDO(remainder.c_str(),
                         ConvertToEngineering(deviceName, remainder, value), mode);
        }
    }

    // 分发混合后的通道值
    void DispatchMixedChannels(const std::map<std::string, float>& channels) {
        for (const auto& [channelKey, rawValue] : channels) {
            // 按通道取速率上限 (设备表全局 Key 属性的 SlewRate)。缺省 0 = 不限速。
            // 间隔由 SafetyGuard 自己用内部时钟测 (不再由调用方传 delta)
            float safeValue = safetyGuard.Check(channelKey, rawValue, RateLimitFor(channelKey));
            // MotionPlayer retains the diagnostic limiter; the physical write bypasses the
            // IODevice-layer limiter so one channel is never rate-limited twice.
            DispatchChannel(channelKey, safeValue, EOutputWriteMode::Immediate);
        }
    }

    /// 由 channelKey ("设备名.oaxis.OAxis_6" / "设备名.OAction名") 解析出设备与通道,
    /// 再取该通道配置的速率上限。
    float RateLimitFor(const std::string& channelKey) {
        auto dot = channelKey.find('.');
        if (dot == std::string::npos) return 0.f;

        std::string deviceName = channelKey.substr(0, dot);
        std::string remainder = channelKey.substr(dot + 1);
        std::string channelName = remainder.rfind("oaxis.", 0) == 0 && remainder.length() > 6
            ? remainder.substr(6)
            : remainder;

        return UInputSettings::Instance().GetChannelRateLimit(deviceName, channelName);
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

    // 轨道自带 engineering_range → 重建兜底量程 (宿主之后 SetChannelRange 仍可覆盖)
    _impl->RebuildTrackRanges();

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

    // 轨道自带 engineering_range → 重建兜底量程 (宿主之后 SetChannelRange 仍可覆盖)
    _impl->RebuildTrackRanges();

    IOLog::Instance().Log(std::string("MotionPlayer: Loaded slot '") + slotId + "' from JSON string\n");
    return 0;
}

void MotionPlayer::UnloadSlot(const char* slotId) {
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);

    auto it = _impl->slots.find(slotId);
    if (it != _impl->slots.end()) {
        it->second.StopTimer();
        _impl->slots.erase(it);
        _impl->RebuildTrackRanges();
        IOLog::Instance().Log(std::string("MotionPlayer: Unloaded slot '") + slotId + "'\n");
    }
}

void MotionPlayer::UnloadAll() {
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    for (auto& [id, slot] : _impl->slots) {
        slot.StopTimer();
    }
    _impl->slots.clear();
    _impl->RebuildTrackRanges();
    _impl->safetyGuard.Reset();
    IOLog::Instance().Log("MotionPlayer: All slots unloaded.\n");
}

// ── 单 Slot 播控 ─────────────────────────────────────

int MotionPlayer::PlaySlot(const char* slotId) {
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    auto* slot = _impl->GetSlot(slotId);
    if (!slot) return -1;

    // 新场次起算: 未送达计数按"一次播放"统计, 不跨场次累加。
    // 只在这里 (起播) 清零, **不在** Unload 时清 —— 宿主是"停播后读一次"的次序,
    // 而停播路径 (ForceStop) 会先 UnloadSlot 再读, 在那清零会把统计抹成空。
    _impl->unresolvedChannels.clear();

    slot->currentTimeMs = 0;
    slot->state = MotionState::Playing;
    slot->dispatchTimerInitialized = false; // 重新起播 → 首帧立即下发
    slot->StartTimer();
    slot->FireEvent(MotionEvent::StateChanged);
    return 0;
}

int MotionPlayer::PlaySlotFrom(const char* slotId, float timeMs) {
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    auto* slot = _impl->GetSlot(slotId);
    if (!slot) return -1;

    // 同 PlaySlot: 按场次统计, 起播清零。
    _impl->unresolvedChannels.clear();

    slot->currentTimeMs = (std::max)(0.0, (std::min)((double)timeMs, slot->timeline.durationMs));
    slot->state = MotionState::Playing;
    slot->dispatchTimerInitialized = false; // 从新时间起播 → 首帧立即下发
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
    slot->dispatchTimerInitialized = false; // 恢复播放 → 首帧立即下发
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
        const bool anySolo = AnySoloActive(slot->timeline.tracks);
        for (const auto& track : slot->timeline.tracks) {
            if (!IsTrackActive(track, anySolo)) continue;
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
    // 显式 seek (拖播放头) → 立即下发该时刻的值, 不受节流间隔限制 (与编辑器"拖拽即时反馈"一致)。
    slot->dispatchTimerInitialized = false;
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
            slot.dispatchTimerInitialized = false; // 重新起播 → 首帧立即下发
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
            const bool anySolo = AnySoloActive(slot->timeline.tracks);
            for (const auto& track : slot->timeline.tracks) {
                if (!IsTrackActive(track, anySolo)) continue;
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
    std::map<std::string, float> safeChannels;
    {
        std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
        for (auto& [id, slot] : _impl->slots) {
            if (slot.state == MotionState::Playing || slot.state == MotionState::Paused) {
                for (const auto& [key, value] : slot.EvaluateChannels()) {
                    safeChannels.emplace(key, 0.0f);
                }
            }
            else if (slot.state == MotionState::Stopping) {
                for (const auto& [key, value] : slot.EvaluateReturnChannels()) {
                    safeChannels.emplace(key, 0.0f);
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
    }

    // Dispatch outside the MotionPlayer lock. ExternalIO resolves each physical Key's
    // SafeValue/policy/rate, so OAction multi-key mappings keep per-channel safety.
    for (const auto& [key, value] : safeChannels) {
        _impl->DispatchChannel(key, value, EOutputWriteMode::SafeStop);
    }
    IOLog::Instance().Log("MotionPlayer entered lifecycle safe state through channel safety policy.\n");
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

int MotionPlayer::Impl::EvaluateSlotAtImpl(const char* slotId, float timeMs, bool engineering,
                                           float* outValues, int maxChannels,
                                           char* outKeys, int keyBufSize)
{
    auto* slot = GetSlot(slotId);
    if (!slot) return -1;

    // 求值指定时间的所有通道 (始终返回归一化值; 需要时再换算成工程量)
    std::map<std::string, float> snapshot;
    const bool anySolo = AnySoloActive(slot->timeline.tracks);
    for (const auto& track : slot->timeline.tracks) {
        if (!IsTrackActive(track, anySolo)) continue;
        float val = EvaluateTrack(track, static_cast<double>(timeMs));
        snapshot[MakeChannelKey(track)] = val;
    }

    int count = 0;
    int keyOffset = 0;
    for (const auto& [key, val] : snapshot) {
        if (count >= maxChannels) break;
        if (outValues) {
            float out = val;
            if (engineering) {
                auto dotIndex = key.find('.');
                if (dotIndex != std::string::npos) {
                    std::string deviceName = key.substr(0, dotIndex);
                    std::string remainder = key.substr(dotIndex + 1);
                    std::string channel =
                        (remainder.rfind("oaxis.", 0) == 0 && remainder.length() > 6)
                            ? remainder.substr(6)
                            : remainder;
                    out = ConvertToEngineering(deviceName, channel, val);
                }
            }
            outValues[count] = out;
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

int MotionPlayer::EvaluateSlotAt(const char* slotId, float timeMs,
                                 float* outValues, int maxChannels,
                                 char* outKeys, int keyBufSize) const
{
    return _impl->EvaluateSlotAtImpl(slotId, timeMs, false, outValues, maxChannels, outKeys, keyBufSize);
}

int MotionPlayer::EvaluateSlotAtEngineering(const char* slotId, float timeMs,
                                            float* outValues, int maxChannels,
                                            char* outKeys, int keyBufSize) const
{
    return _impl->EvaluateSlotAtImpl(slotId, timeMs, true, outValues, maxChannels, outKeys, keyBufSize);
}

// ── 通道工程量程 ─────────────────────────────────────

void MotionPlayer::SetChannelRange(const char* deviceName, const char* channel,
                                   float minValue, float maxValue)
{
    if (!deviceName || !channel) return;
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    if (!(maxValue > minValue)) {
        _impl->channelRanges.erase(Impl::RangeKey(deviceName, channel));
        return;
    }
    _impl->channelRanges[Impl::RangeKey(deviceName, channel)] = std::make_pair(minValue, maxValue);
}

void MotionPlayer::ClearChannelRanges()
{
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    _impl->channelRanges.clear();
}

float MotionPlayer::ToEngineering(const char* deviceName, const char* channel, float normalized) const
{
    if (!deviceName || !channel) return normalized;
    return _impl->ConvertToEngineering(deviceName, channel, normalized);
}

// ── 未送达通道统计 (播放期可观测性) ──────────────────

std::string MotionPlayer::GetUnresolvedChannelsJson() const
{
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    return _impl->BuildUnresolvedJson();
}

void MotionPlayer::ResetUnresolvedCounters()
{
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    _impl->unresolvedChannels.clear();
}

std::string MotionPlayer::GetRateLimitedChannelsJson() const
{
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    return _impl->BuildRateLimitedJson();
}

// ── 安全参数配置 ─────────────────────────────────────

void MotionPlayer::SetSafetyConfig(float maxRatePerSecond)
{
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    _impl->safetyGuard.SetMaxRatePerSecond(maxRatePerSecond);
    IOLog::Instance().Log("MotionPlayer: SafetyConfig updated, maxRate=" + std::to_string(maxRatePerSecond) + "/s\n");
}

// ── 帧循环下发节流 (运行时覆盖) ─────────────────────

void MotionPlayer::SetSlotDispatchInterval(const char* slotId, int intervalMs)
{
    std::lock_guard<std::recursive_mutex> lock(_impl->mutex);
    auto* slot = _impl->GetSlot(slotId);
    if (!slot) return;

    if (intervalMs < 0) {
        // 清除运行时覆盖 → 回到 .motion 配置
        slot->dispatchIntervalOverrideMs = -1;
    } else {
        // 夹取口径与 .motion 解析一致 (0=不节流; 8..1000)
        int v = intervalMs;
        if (v > 0 && v < 8) v = 8;
        else if (v > 1000) v = 1000;
        slot->dispatchIntervalOverrideMs = v;
    }
    // 立即按新间隔重新判定 (清掉旧间隔残留的下发时点)
    slot->dispatchTimerInitialized = false;

    IOLog::Instance().Log(
        std::string("MotionPlayer: slot '") + slotId +
        "' dispatch interval override = " + std::to_string(slot->dispatchIntervalOverrideMs) +
        " ms (effective " + std::to_string(slot->EffectiveDispatchIntervalMs()) + " ms)\n");
}

int MotionPlayer::GetSlotDispatchInterval(const char* slotId) const
{
    auto* slot = _impl->GetSlot(slotId);
    return slot ? slot->EffectiveDispatchIntervalMs() : 0;
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
                        const bool anySolo = AnySoloActive(slot.timeline.tracks);
                        for (const auto& track : slot.timeline.tracks) {
                            if (!IsTrackActive(track, anySolo)) continue;
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

    // Phase 2: 收集所有活跃 Slot 的通道输出。
    // 帧循环下发节流: 未到间隔的 Slot 本帧不参与混流, 其通道保持上一值不被重写 —— 这正是
    // "降低下发频率"的语义 (设备端保持上次输出)。间隔来源: .motion 的 dispatch_interval_ms,
    // 或经 C ABI 设置的运行时覆盖。
    const double nowMs = static_cast<double>(IOClock::GetMilliseconds());
    std::vector<SlotChannelOutput> slotOutputs;
    for (auto& [id, slot] : _impl->slots) {
        const bool active = (slot.state == MotionState::Playing ||
                             slot.state == MotionState::Stopping);
        if (!active) continue;
        if (!slot.ShouldDispatchNow(nowMs)) continue;

        slot.MarkDispatched(nowMs);
        SlotChannelOutput output;
        output.priority = slot.priority;
        output.policy = slot.policy;
        output.channels = (slot.state == MotionState::Playing)
            ? slot.EvaluateChannels()
            : slot.EvaluateReturnChannels();
        slotOutputs.push_back(std::move(output));
    }

    // Phase 3: 通道仲裁 (ChannelMixer)
    auto mixedChannels = MixChannels(slotOutputs);

    // Phase 4: SafetyGuard 检查 + 设备派发
    // (deltaSeconds 已不再传给 SafetyGuard —— 它用内部时钟测实际间隔, 使限速与 tick 频率无关)
    _impl->DispatchMixedChannels(mixedChannels);

    // 派发后立即下发: SetDO 只置脏, 真正推给插件在 DOImmediate (设备 Tick, ~10Hz 轮询)。
    // 不等轮询 flush, 使下发频率跟随本 Tick 的调用频率 (编辑器 60fps / 放映端各自频率),
    // 避免把逐帧派发降采样成 10fps (动作一顿一顿)。无脏数据时空操作。
    IODevices::FlushOutputs();
}
