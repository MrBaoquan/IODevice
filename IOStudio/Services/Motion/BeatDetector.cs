using System;
using System.Collections.Generic;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 简易节拍检测器 — 基于能量阈值的节拍标记生成。
    /// 在波形数据上检测能量峰值, 输出节拍标记时间列表。
    /// </summary>
    /// <remarks>
    /// 算法:
    /// 1. 对 RMS 数据计算局部能量 (滑动窗口平均)
    /// 2. 当某 bin 的 RMS 超过局部平均的阈值倍数时标记为节拍
    /// 3. 最小间隔过滤 (避免连续标记)
    /// </remarks>
    public static class BeatDetector
    {
        /// <summary>默认检测灵敏度 (1.0 = 最敏感, 越大越不敏感)。</summary>
        private const float DefaultThresholdMultiplier = 1.5f;

        /// <summary>最小节拍间隔 (毫秒), 避免过于密集的标记。</summary>
        private const double MinBeatIntervalMs = 200;

        /// <summary>滑动窗口大小 (bin 数)。</summary>
        private const int WindowSize = 43; // ~约 0.5 秒 (取决于 msPerBin)

        /// <summary>
        /// 从波形数据中检测节拍标记。
        /// </summary>
        /// <param name="waveform">波形数据。</param>
        /// <param name="sensitivity">灵敏度倍数 (越小越敏感, 建议 1.2 ~ 2.0)。</param>
        /// <returns>节拍标记时间列表 (毫秒), 按时间排序。</returns>
        public static List<double> DetectBeats(
            WaveformData waveform,
            float sensitivity = DefaultThresholdMultiplier
        )
        {
            if (waveform is null)
                throw new ArgumentNullException(nameof(waveform));

            var beats = new List<double>();
            int binCount = waveform.BinCount;

            if (binCount < WindowSize * 2)
                return beats; // 数据太少, 无法有效检测

            float[] rms = waveform.Rms;
            int halfWindow = WindowSize / 2;

            // 计算滑动窗口平均 RMS
            float[] localAvg = new float[binCount];
            float windowSum = 0;
            int windowCount = 0;

            // 初始化窗口
            for (int i = 0; i < Math.Min(WindowSize, binCount); i++)
            {
                windowSum += rms[i];
                windowCount++;
            }

            for (int b = 0; b < binCount; b++)
            {
                // 窗口中心 = b, 范围 = [b - halfWindow, b + halfWindow]
                int left = b - halfWindow;
                int right = b + halfWindow;

                // 扩展窗口右端
                while (right < binCount - 1 && right < b + halfWindow)
                {
                    right++;
                    windowSum += rms[right];
                    windowCount++;
                }

                // 收缩窗口左端
                while (left < b - halfWindow && windowCount > 0)
                {
                    windowSum -= rms[Math.Max(0, left)];
                    windowCount--;
                    left++;
                }

                localAvg[b] = windowCount > 0 ? windowSum / windowCount : 0;
            }

            // 使用更简单的方式重新计算
            Array.Clear(localAvg, 0, localAvg.Length);
            for (int b = 0; b < binCount; b++)
            {
                float sum = 0;
                int count = 0;
                int lo = Math.Max(0, b - halfWindow);
                int hi = Math.Min(binCount - 1, b + halfWindow);

                for (int i = lo; i <= hi; i++)
                {
                    sum += rms[i];
                    count++;
                }

                localAvg[b] = count > 0 ? sum / count : 0;
            }

            // 检测节拍: RMS 超过局部平均 × 灵敏度
            double lastBeatMs = -MinBeatIntervalMs * 2;

            for (int b = 1; b < binCount - 1; b++)
            {
                double timeMs = b * waveform.MsPerBin;
                float threshold = localAvg[b] * sensitivity;

                // 峰值检测: 当前 bin > 阈值 且 是局部最大值
                if (
                    rms[b] > threshold
                    && rms[b] > 0.05f
                    && rms[b] >= rms[b - 1]
                    && rms[b] >= rms[b + 1]
                )
                {
                    // 最小间隔过滤
                    if (timeMs - lastBeatMs >= MinBeatIntervalMs)
                    {
                        beats.Add(timeMs);
                        lastBeatMs = timeMs;
                    }
                }
            }

            return beats;
        }
    }
}
