using System.Collections.Generic;
using IOStudio.Models.Motion;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 关键帧插值引擎接口 — 支持 Linear / Bezier / Step / EaseInOut 四种插值。
    /// </summary>
    public interface IInterpolationEngine
    {
        /// <summary>在给定时间点计算插值后的值。</summary>
        float Evaluate(List<MotionKeyframe> keyframes, double timeMs);
    }

    /// <summary>
    /// 插值引擎实例化包装 — 委托给静态 <see cref="InterpolationEngine"/>。
    /// </summary>
    public class InterpolationEngineInstance : IInterpolationEngine
    {
        /// <inheritdoc/>
        public float Evaluate(List<MotionKeyframe> keyframes, double timeMs) =>
            InterpolationEngine.Evaluate(keyframes, timeMs);
    }
}
