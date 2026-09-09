using System;
using System.Collections.Generic;
using IOStudio.Models.Motion;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 关键帧插值引擎 — 支持 Linear / Bezier / Step / EaseInOut 四种插值
    /// </summary>
    public static class InterpolationEngine
    {
        /// <summary>
        /// 给定时间 (相对于 Clip 的 StartMs), 在关键帧列表中计算插值后的值
        /// </summary>
        /// <param name="keyframes">关键帧列表 (按 TimeMs 升序)</param>
        /// <param name="timeMs">当前时间 (毫秒, 相对于 Clip.StartMs)</param>
        /// <returns>插值后的归一化值 (0.0 ~ 1.0)</returns>
        public static float Evaluate(List<MotionKeyframe> keyframes, double timeMs)
        {
            if (keyframes == null || keyframes.Count == 0)
                return 0.5f; // 默认中位
            if (keyframes.Count == 1)
                return keyframes[0].Value;

            // 在第一个关键帧之前, 返回第一个关键帧的值
            if (timeMs <= keyframes[0].TimeMs)
                return keyframes[0].Value;

            // 在最后一个关键帧之后, 返回最后一个关键帧的值
            if (timeMs >= keyframes[^1].TimeMs)
                return keyframes[^1].Value;

            // 防御: 若关键帧非严格升序, 二分查找会返回错误区间导致锯齿毛刺。
            // 就地做一次检测, 仅当乱序时才重排 (正常有序路径零开销)。
            bool needSort = false;
            for (int i = 1; i < keyframes.Count; i++)
            {
                if (keyframes[i].TimeMs < keyframes[i - 1].TimeMs)
                {
                    needSort = true;
                    break;
                }
            }
            if (needSort)
                keyframes.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));

            // 二分查找当前时间所处的两个关键帧
            int left = 0;
            int right = keyframes.Count - 1;

            while (right - left > 1)
            {
                int mid = (left + right) / 2;
                if (keyframes[mid].TimeMs <= timeMs)
                    left = mid;
                else
                    right = mid;
            }

            return Evaluate(keyframes[left], keyframes[right], timeMs);
        }

        /// <summary>
        /// 免分配的两关键帧求值 — 供渲染热路径 (曲线采样) 使用, 避免每采样点 new List。
        /// </summary>
        public static float Evaluate(MotionKeyframe kfA, MotionKeyframe kfB, double timeMs)
        {
            // 计算归一化 t [0, 1]
            double span = kfB.TimeMs - kfA.TimeMs;
            if (span <= 0)
                return kfA.Value;

            float t = (float)((timeMs - kfA.TimeMs) / span);
            t = Math.Clamp(t, 0f, 1f);

            // 根据 kfA 的插值类型选择算法 (从 kfA → kfB 的过渡)
            return kfA.Interpolation?.ToLowerInvariant() switch
            {
                "bezier"
                    => Bezier(
                        kfA.Value,
                        kfB.Value,
                        t,
                        kfA.TangentOut ?? 0f,
                        kfB.TangentIn ?? 0f,
                        kfA.Cp2x ?? 1f,
                        kfB.Cp1x ?? 1f
                    ),
                "step" => Step(kfA.Value, kfB.Value, t),
                "ease_in_out" or "ease" => EaseInOut(kfA.Value, kfB.Value, t),
                _ => Linear(kfA.Value, kfB.Value, t),
            };
        }

        /// <summary>线性插值</summary>
        public static float Linear(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        /// <summary>
        /// 三次贝塞尔插值 (无水平控制, 快速路径)
        /// P0 = a, P3 = b
        /// P1 = a + tangentOut * span / 3
        /// P2 = b - tangentIn * span / 3
        /// P(t) = (1-t)³P0 + 3(1-t)²tP1 + 3(1-t)t²P2 + t³P3
        /// </summary>
        public static float Bezier(float a, float b, float t, float tangentOut, float tangentIn)
        {
            return Bezier(a, b, t, tangentOut, tangentIn, 1f, 1f);
        }

        /// <summary>
        /// 三次贝塞尔插值 (支持 2D 控制点)
        /// cp2x: kfA 的出控制点水平距离系数 (默认 1.0), 范围 [0.2, 3.0]
        /// cp1x: kfB 的入控制点水平距离系数 (默认 1.0), 范围 [0.2, 3.0]
        /// 系数为 1.0 时 Bx(t)=t (线性时间), 等价于传统纯垂直切线调整。
        /// </summary>
        public static float Bezier(
            float a,
            float b,
            float normalizedTime,
            float tangentOut,
            float tangentIn,
            float cp2x,
            float cp1x
        )
        {
            // 控制点 Y
            float span = b - a;
            float p1y = a + tangentOut * Math.Abs(span) / 3f;
            float p2y = b - tangentIn * Math.Abs(span) / 3f;

            // 控制点 X (归一化 [0,1] 时间空间)
            float p1xNorm = cp2x / 3f; // kfA OUT: 从 0 出发
            float p2xNorm = 1f - cp1x / 3f; // kfB IN:  从 1 回看

            // 快速路径: 当两端 cpx ≈ 1.0 时, Bx(t)=t, 无需求解
            bool isIdentityX = Math.Abs(cp2x - 1f) < 0.001f && Math.Abs(cp1x - 1f) < 0.001f;
            float t = isIdentityX ? normalizedTime : SolveBezierX(p1xNorm, p2xNorm, normalizedTime);

            // 评估 Y
            float u = 1f - t;
            float u2 = u * u;
            float u3 = u2 * u;
            float t2 = t * t;
            float t3 = t2 * t;

            return u3 * a + 3f * u2 * t * p1y + 3f * u * t2 * p2y + t3 * b;
        }

        /// <summary>
        /// Newton 法求解 Bx(t) = x 的参数 t。
        /// Bx(t) = 3(1-t)²t·cx1 + 3(1-t)t²·cx2 + t³
        /// </summary>
        private static float SolveBezierX(float cx1, float cx2, float x)
        {
            float t = x; // 初始猜测
            for (int i = 0; i < 8; i++)
            {
                float u = 1f - t;
                float xt = 3f * u * u * t * cx1 + 3f * u * t * t * cx2 + t * t * t;
                float dxt = 3f * u * u * cx1 + 6f * u * t * (cx2 - cx1) + 3f * t * t * (1f - cx2);
                if (Math.Abs(dxt) < 1e-7f)
                    break;
                t -= (xt - x) / dxt;
                t = Math.Clamp(t, 0f, 1f);
            }
            return Math.Clamp(t, 0f, 1f);
        }

        /// <summary>阶梯插值 (保持前一帧值直到下一帧)</summary>
        public static float Step(float a, float b, float t)
        {
            return t < 1.0f ? a : b;
        }

        /// <summary>
        /// 缓入缓出 (Smoothstep)
        /// t' = t² × (3 - 2t)
        /// </summary>
        public static float EaseInOut(float a, float b, float t)
        {
            float s = t * t * (3f - 2f * t);
            return a + (b - a) * s;
        }

        /// <summary>
        /// 对整个 Clip 的关键帧序列在指定时间采样
        /// </summary>
        /// <param name="clip">片段</param>
        /// <param name="absoluteTimeMs">绝对时间 (相对于时间轴 0 点)</param>
        /// <returns>插值后的归一化值</returns>
        public static float EvaluateClip(MotionClip clip, double absoluteTimeMs)
        {
            if (clip == null || clip.Keyframes.Count == 0)
                return 0.5f;

            double localTime = absoluteTimeMs - clip.StartMs;
            return Evaluate(clip.Keyframes, localTime);
        }
    }
}
