using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 音频波形数据 — 从视频/音频文件中提取的可视化数据。
    /// </summary>
    public class WaveformData
    {
        /// <summary>每个采样点的峰值 (0.0 ~ 1.0), 用于波形绘制。</summary>
        public float[] Peaks { get; }

        /// <summary>每个采样点的 RMS 值 (0.0 ~ 1.0), 用于填充绘制。</summary>
        public float[] Rms { get; }

        /// <summary>音频总时长 (毫秒)。</summary>
        public double DurationMs { get; }

        /// <summary>采样率 (每秒采样点数, 原始音频)。</summary>
        public int SampleRate { get; }

        /// <summary>降采样后每个点对应的时长 (毫秒)。</summary>
        public double MsPerBin { get; }

        /// <summary>采样点总数。</summary>
        public int BinCount => Peaks.Length;

        public WaveformData(
            float[] peaks,
            float[] rms,
            double durationMs,
            int sampleRate,
            double msPerBin
        )
        {
            Peaks = peaks ?? throw new ArgumentNullException(nameof(peaks));
            Rms = rms ?? throw new ArgumentNullException(nameof(rms));
            DurationMs = durationMs;
            SampleRate = sampleRate;
            MsPerBin = msPerBin;
        }
    }

    /// <summary>
    /// 音频波形提取器 — 使用 LibVLC 解码视频/音频文件的音频流,
    /// 提取 PCM 数据并降采样为波形可视化数据。
    /// </summary>
    /// <remarks>
    /// 工作原理:
    /// 1. 创建独立的 LibVLC + MediaPlayer 实例 (不与播放器共享)
    /// 2. 通过 SetAudioFormat + SetAudioCallbacks 捕获 PCM 数据
    /// 3. 使用高速播放 (8x~16x) 加速提取, 同时通过渐进式回调让 UI 实时显示部分波形
    /// 4. 对原始 PCM 降采样, 每 bin 计算峰值和 RMS
    /// 5. 返回 <see cref="WaveformData"/> 供 UI 渲染
    /// </remarks>
    public class AudioWaveformExtractor : IDisposable
    {
        /// <summary>默认目标 bin 数 (降采样后的波形点数)。</summary>
        private const int DefaultTargetBins = 2000;

        /// <summary>提取音频时使用的采样率 (Hz), 降低采样率以减少数据量。</summary>
        private const int ExtractSampleRate = 11025;

        /// <summary>单声道。</summary>
        private const int ExtractChannels = 1;

        /// <summary>加速倍率 — LibVLC 支持 0.25x ~ 最高约 31.25x。</summary>
        private const float ExtractPlaybackRate = 8.0f;

        /// <summary>渐进式回调触发间隔 (毫秒)。</summary>
        private const int ProgressiveUpdateIntervalMs = 500;

        private LibVLC? _libVlc;
        private bool _disposed;

        /// <summary>波形提取完成后触发, 参数: WaveformData。</summary>
        public event Action<WaveformData>? ExtractionCompleted;

        /// <summary>提取进度 (0.0 ~ 1.0)。</summary>
        public event Action<double>? ProgressChanged;

        /// <summary>渐进式波形更新 — 在提取过程中定期返回当前已有数据的波形快照。</summary>
        public event Action<WaveformData>? ProgressiveWaveformUpdate;

        /// <summary>提取失败。</summary>
        public event Action<string>? ExtractionFailed;

        /// <summary>
        /// 从视频/音频文件异步提取波形数据。
        /// </summary>
        /// <param name="filePath">媒体文件路径。</param>
        /// <param name="targetBins">目标 bin 数 (降采样后的波形点数)。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>波形数据, 提取失败时返回 null。</returns>
        public Task<WaveformData?> ExtractAsync(
            string filePath,
            int targetBins = DefaultTargetBins,
            CancellationToken cancellationToken = default
        )
        {
            return Task.Run(
                () => ExtractCore(filePath, targetBins, cancellationToken),
                cancellationToken
            );
        }

        private WaveformData? ExtractCore(string filePath, int targetBins, CancellationToken ct)
        {
            try
            {
                EnsureLibVlc();
                if (_libVlc is null)
                    return null;

                // 收集原始 PCM 样本 — 预分配约 5 分钟 (降低采样率后更少)
                var samples = new List<float>(ExtractSampleRate * 300);
                long durationMs = 0;
                var extractionComplete = new ManualResetEventSlim(false);
                bool hasError = false;
                long lastProgressiveUpdate = 0;

                using var media = new Media(_libVlc, filePath, FromType.FromPath);
                using var player = new MediaPlayer(media);

                // 配置音频输出格式: 16-bit signed PCM, 单声道, 低采样率
                player.SetAudioFormat("S16N", (uint)ExtractSampleRate, (uint)ExtractChannels);
                player.SetAudioCallbacks(
                    (data, samplesPtr, count, pts) =>
                    {
                        if (ct.IsCancellationRequested)
                            return;

                        // 读取 16-bit PCM 样本并归一化到 [-1, 1]
                        int sampleCount = (int)count * ExtractChannels;
                        var shortBuffer = new short[sampleCount];
                        Marshal.Copy(samplesPtr, shortBuffer, 0, sampleCount);

                        lock (samples)
                        {
                            for (int i = 0; i < sampleCount; i++)
                            {
                                samples.Add(shortBuffer[i] / 32768f);
                            }
                        }

                        // 渐进式波形更新 — 定期生成快照供 UI 实时显示
                        long now = Environment.TickCount64;
                        if (
                            durationMs > 0
                            && now - lastProgressiveUpdate > ProgressiveUpdateIntervalMs
                        )
                        {
                            lastProgressiveUpdate = now;
                            WaveformData? snapshot;
                            lock (samples)
                            {
                                if (samples.Count > ExtractSampleRate) // 至少有1秒数据
                                {
                                    snapshot = DownsampleToBins(samples, targetBins, durationMs);
                                }
                                else
                                {
                                    snapshot = null;
                                }
                            }
                            if (snapshot is not null)
                            {
                                ProgressiveWaveformUpdate?.Invoke(snapshot);
                            }
                        }
                    },
                    null, // pauseCb
                    null, // resumeCb
                    null, // flushCb
                    (data) =>
                    {
                        extractionComplete.Set();
                    }
                );

                // 静音 + 不渲染视频 (仅提取音频)
                player.Mute = true;

                // 监听时长
                player.LengthChanged += (_, args) =>
                {
                    durationMs = args.Length;
                };

                player.EndReached += (_, _) =>
                {
                    extractionComplete.Set();
                };

                player.EncounteredError += (_, _) =>
                {
                    hasError = true;
                    extractionComplete.Set();
                };

                // 开始提取: 播放到结束
                if (!player.Play())
                {
                    ExtractionFailed?.Invoke("无法开始音频提取");
                    return null;
                }

                // 加速播放 — 必须在 Play() 之后设置 Rate
                player.SetRate(ExtractPlaybackRate);

                // 等待播放结束或取消
                while (!extractionComplete.Wait(300))
                {
                    if (ct.IsCancellationRequested)
                    {
                        player.Stop();
                        return null;
                    }

                    // 报告进度
                    if (durationMs > 0)
                    {
                        double progress = Math.Min(1.0, player.Time / (double)durationMs);
                        ProgressChanged?.Invoke(progress);
                    }
                }

                // 等一下让最后的 samples 写入
                Thread.Sleep(50);
                player.Stop();

                if (hasError || samples.Count == 0)
                {
                    ExtractionFailed?.Invoke("音频提取失败: 无法解码音频流");
                    return null;
                }

                if (durationMs <= 0 && samples.Count > 0)
                {
                    durationMs = (long)(samples.Count * 1000.0 / ExtractSampleRate);
                }

                ProgressChanged?.Invoke(1.0);

                // 降采样为波形 bins
                var waveform = DownsampleToBins(samples, targetBins, durationMs);
                ExtractionCompleted?.Invoke(waveform);
                return waveform;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioWaveformExtractor] Error: {ex.Message}");
                ExtractionFailed?.Invoke(ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 将原始 PCM 样本降采样为指定数量的 bin, 每个 bin 计算峰值和 RMS。
        /// </summary>
        private static WaveformData DownsampleToBins(
            List<float> samples,
            int targetBins,
            long durationMs
        )
        {
            int totalSamples = samples.Count;
            int binCount = Math.Min(targetBins, totalSamples);
            if (binCount <= 0)
                binCount = 1;

            int samplesPerBin = Math.Max(1, totalSamples / binCount);
            double msPerBin = durationMs / (double)binCount;

            var peaks = new float[binCount];
            var rms = new float[binCount];

            for (int b = 0; b < binCount; b++)
            {
                int startIdx = b * samplesPerBin;
                int endIdx = Math.Min(startIdx + samplesPerBin, totalSamples);

                float maxAbs = 0f;
                float sumSq = 0f;
                int count = endIdx - startIdx;

                for (int i = startIdx; i < endIdx; i++)
                {
                    float abs = Math.Abs(samples[i]);
                    if (abs > maxAbs)
                        maxAbs = abs;
                    sumSq += samples[i] * samples[i];
                }

                peaks[b] = Math.Min(1f, maxAbs);
                rms[b] = count > 0 ? Math.Min(1f, MathF.Sqrt(sumSq / count)) : 0f;
            }

            return new WaveformData(peaks, rms, durationMs, ExtractSampleRate, msPerBin);
        }

        private void EnsureLibVlc()
        {
            if (_libVlc is not null)
                return;

            try
            {
                Core.Initialize();
                // 禁用视频输出 + 界面元素以加速提取 (使用 dummy vout 而非 --no-video, 避免影响音频解复用)
                _libVlc = new LibVLC(
                    "--vout=none",
                    "--no-spu",
                    "--no-osd",
                    "--no-snapshot-preview",
                    "--no-stats",
                    "--no-sub-autodetect-file"
                );
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioWaveformExtractor] LibVLC init failed: {ex.Message}");
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _libVlc?.Dispose();
            _libVlc = null;
        }
    }
}
