using System;
using System.Collections.Generic;
using IOStudio.Models.Motion;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 轨道"任一时刻值"统一求值器 — 镜像 IODevice C++ MotionPlayer 运行时语义, 保证编辑器所见即实播。
    /// <para>
    /// 语义 (与 C++ MotionPlayer::EvaluateTrack / EvaluateClip 一致):
    /// 1. 时刻落在某个 Clip 内 → 该 Clip 关键帧插值
    ///    (linear/step/bezier/ease; 首关键帧前取首值, 末关键帧后取末值; 空关键帧=中性值, 单帧=该值; Bool 轨用阶梯)。
    /// 2. 时刻无任何 Clip 覆盖 → Idle 循环求值 (若轨道配置了 idle): 环形关键帧按相位采样,
    ///    与相邻 clip 可交叉淡化 (BlendMs), 相位模式 continuous/restart。
    /// 3. 无 idle → 轨道"中性/空闲值" (默认为 Bool=0, Float=0.5, 可通过 MotionTrack.NeutralValue 配置)。
    /// </para>
    /// </summary>
    public static class TrackValueEvaluator
    {
        /// <summary>解析轨道中性/空闲值: 显式配置优先, null 时按类型默认 (bool=0, float=0.5)。</summary>
        public static float ResolveNeutral(string? valueType, float? neutralValue)
        {
            if (neutralValue.HasValue)
                return Math.Clamp(neutralValue.Value, 0f, 1f);
            return string.Equals(valueType, "bool", StringComparison.OrdinalIgnoreCase) ? 0f : 0.5f;
        }

        /// <summary>求值: 轨道在 timeMs 时刻的输出值 (0~1 / bool 0|1), 无 idle/overlay (原语义)。</summary>
        public static float Evaluate(
            IReadOnlyList<MotionClip> clips,
            string? valueType,
            float? neutralValue,
            double timeMs
        ) => Evaluate(clips, valueType, neutralValue, null, null, timeMs);

        /// <summary>求值: 轨道在 timeMs 时刻的输出值, 含 idle (无 overlay)。</summary>
        public static float Evaluate(
            IReadOnlyList<MotionClip> clips,
            string? valueType,
            float? neutralValue,
            IdleLoop? idle,
            double timeMs
        ) => Evaluate(clips, valueType, neutralValue, idle, null, timeMs);

        /// <summary>求值: 轨道在 timeMs 时刻的输出值 (0~1 / bool 0|1)。与 C++ 运行时一致。</summary>
        public static float Evaluate(
            IReadOnlyList<MotionClip> clips,
            string? valueType,
            float? neutralValue,
            IdleLoop? idle,
            IReadOnlyList<MotionKeyframe>? overrides,
            double timeMs
        )
        {
            bool isBool = string.Equals(valueType, "bool", StringComparison.OrdinalIgnoreCase);
            // bool → 量化 0/1; float → clamp [0,1] (镜像 C++ SafetyGuard 输出钳位 + QuantizeTrackValue)
            static float ClampQuantize(bool b, float v) =>
                b ? (v >= 0.5f ? 1f : 0f) : Math.Clamp(v, 0f, 1f);

            if (clips != null)
            {
                for (int c = 0; c < clips.Count; c++)
                {
                    var clip = clips[c];
                    if (timeMs >= clip.StartMs && timeMs <= clip.EndMs)
                    {
                        if (clip.Keyframes.Count == 0)
                            return ClampQuantize(isBool, ResolveNeutral(valueType, neutralValue));
                        if (isBool)
                            return EvaluateBoolStep(clip, timeMs);
                        // float: 结果 clamp 到 [0,1] (镜像 C++ SafetyGuard 输出钳位)
                        return Math.Clamp(
                            InterpolationEngine.Evaluate(clip.Keyframes, timeMs - clip.StartMs),
                            0f,
                            1f
                        );
                    }
                }
            }

            // Overlay 覆盖关键帧 (空窗自由数值点, 优先级高于 idle)。
            // 语义 (镜像 C++): 两点间插值; 首点之前/末点之后回落到 idle; 单点 = 仅该瞬间生效 (脉冲)。
            if (overrides != null && overrides.Count > 0)
            {
                if (overrides.Count == 1)
                {
                    var o = overrides[0];
                    if (Math.Abs(timeMs - o.TimeMs) < 0.5)
                        return ClampQuantize(isBool, o.Value);
                    return ClampQuantize(
                        isBool,
                        EvaluateIdleAt(clips, valueType, neutralValue, idle, timeMs)
                    );
                }
                if (timeMs < overrides[0].TimeMs)
                    return ClampQuantize(
                        isBool,
                        EvaluateIdleAt(clips, valueType, neutralValue, idle, timeMs)
                    );
                if (timeMs > overrides[^1].TimeMs)
                    return ClampQuantize(
                        isBool,
                        EvaluateIdleAt(clips, valueType, neutralValue, idle, timeMs)
                    );
                for (int i = 0; i + 1 < overrides.Count; i++)
                {
                    if (timeMs >= overrides[i].TimeMs && timeMs <= overrides[i + 1].TimeMs)
                        return ClampQuantize(
                            isBool,
                            EvalSegment(overrides[i], overrides[i + 1], timeMs, null)
                        );
                }
            }

            // 无 Clip/Overlay 覆盖 → idle 循环 / 中性值 (gap 填充)
            float idleVal = EvaluateIdleAt(clips, valueType, neutralValue, idle, timeMs);

            // 与相邻 clip 交叉淡化 (仅当配置了 blend_ms, 镜像 C++ EvaluateTrack 的 blend 分支)
            if (idle != null && idle.BlendMs > 0 && clips != null)
            {
                double blend = idle.BlendMs;
                for (int c = 0; c < clips.Count; c++)
                {
                    var clip = clips[c];
                    // clip 开始前: idle → clip 首帧值 淡入
                    if (timeMs >= clip.StartMs - blend && timeMs < clip.StartMs)
                    {
                        double t = (timeMs - (clip.StartMs - blend)) / blend;
                        float clipFirstVal =
                            clip.Keyframes.Count > 0
                                ? clip.Keyframes[0].Value
                                : ResolveNeutral(valueType, neutralValue);
                        return ClampQuantize(
                            isBool,
                            InterpolationEngine.Linear(
                                idleVal,
                                clipFirstVal,
                                Math.Clamp((float)t, 0f, 1f)
                            )
                        );
                    }
                    // clip 结束后: clip 末帧值 → idle 淡出
                    if (timeMs > clip.EndMs && timeMs <= clip.EndMs + blend)
                    {
                        double t = (timeMs - clip.EndMs) / blend;
                        float clipLastVal =
                            clip.Keyframes.Count > 0
                                ? clip.Keyframes[^1].Value
                                : ResolveNeutral(valueType, neutralValue);
                        return ClampQuantize(
                            isBool,
                            InterpolationEngine.Linear(
                                clipLastVal,
                                idleVal,
                                Math.Clamp((float)t, 0f, 1f)
                            )
                        );
                    }
                }
            }

            return ClampQuantize(isBool, idleVal);
        }

        // ═══════ Idle 循环求值 (镜像 C++ EvaluateIdleLoop / EvaluateIdleAt / ComputeIdlePhase) ═══════

        private static float EvaluateIdleAt(
            IReadOnlyList<MotionClip>? clips,
            string? valueType,
            float? neutralValue,
            IdleLoop? idle,
            double timeMs
        )
        {
            if (idle != null && idle.Enabled && idle.Keyframes != null && idle.Keyframes.Count > 0)
                return EvaluateIdleLoop(idle, ComputeIdlePhase(clips, idle, timeMs));
            return ResolveNeutral(valueType, neutralValue);
        }

        private static double ComputeIdlePhase(
            IReadOnlyList<MotionClip>? clips,
            IdleLoop idle,
            double timeMs
        )
        {
            double baseMs;
            if (idle.PhaseMode == "restart" && clips != null)
            {
                double gapStartMs = 0;
                foreach (var c in clips)
                    if (c.EndMs < timeMs && c.EndMs > gapStartMs)
                        gapStartMs = c.EndMs;
                baseMs = timeMs - gapStartMs;
            }
            else
            {
                baseMs = timeMs; // continuous
            }
            return baseMs + idle.PhaseOffsetMs;
        }

        /// <summary>采样 idle 循环在 phaseMs 处的值 (phase ∈ [0, periodMs))。环形闭合: [last..period] 用 last→first。</summary>
        private static float EvaluateIdleLoop(IdleLoop idle, double phaseMs)
        {
            var kfs = idle.Keyframes;
            if (kfs == null || kfs.Count == 0)
                return 0.5f;
            if (kfs.Count == 1)
                return kfs[0].Value;

            double period = idle.PeriodMs > 1.0 ? idle.PeriodMs : 1.0;
            double p = phaseMs % period;
            if (p < 0)
                p += period;

            for (int i = 0; i + 1 < kfs.Count; i++)
            {
                if (p >= kfs[i].TimeMs && p <= kfs[i + 1].TimeMs)
                    return EvalSegment(kfs[i], kfs[i + 1], p, null);
            }
            // 跨环闭合: last → first (first 的虚拟时间 = period)
            if (p >= kfs[^1].TimeMs)
            {
                double span = period - kfs[^1].TimeMs;
                if (span <= 0.001)
                    return kfs[^1].Value;
                return EvalSegment(kfs[^1], kfs[0], p, period);
            }
            return kfs[^1].Value;
        }

        /// <summary>两关键帧单段插值 (镜像 C++ InterpolateKeyframes), 支持 k1 时间覆盖 (跨环闭合用)。</summary>
        private static float EvalSegment(
            MotionKeyframe k0,
            MotionKeyframe k1,
            double timeMs,
            double? k1TimeOverride
        )
        {
            double k1t = k1TimeOverride ?? k1.TimeMs;
            if (k0.TimeMs >= k1t)
                return k0.Value;

            float t = Math.Clamp((float)((timeMs - k0.TimeMs) / (k1t - k0.TimeMs)), 0f, 1f);

            return k0.Interpolation?.ToLowerInvariant() switch
            {
                "step" => InterpolationEngine.Step(k0.Value, k1.Value, t),
                "bezier"
                    => InterpolationEngine.Bezier(
                        k0.Value,
                        k1.Value,
                        t,
                        k0.TangentOut ?? 0f,
                        k1.TangentIn ?? 0f,
                        k0.Cp2x ?? 1f,
                        k1.Cp1x ?? 1f
                    ),
                "ease_in_out" or "ease" => InterpolationEngine.EaseInOut(k0.Value, k1.Value, t),
                _ => InterpolationEngine.Linear(k0.Value, k1.Value, t),
            };
        }

        /// <summary>Bool 轨阶梯求值: 保持前一关键帧值直到下一个关键帧。</summary>
        public static float EvaluateBoolStep(MotionClip clip, double absoluteTimeMs)
        {
            var kfs = clip.Keyframes;
            if (kfs == null || kfs.Count == 0)
                return 0f;

            double localMs = absoluteTimeMs - clip.StartMs;
            float val = kfs[0].Value;
            for (int k = 0; k < kfs.Count; k++)
            {
                if (kfs[k].TimeMs <= localMs)
                    val = kfs[k].Value;
                else
                    break;
            }
            return val >= 0.5f ? 1f : 0f;
        }
    }
}
