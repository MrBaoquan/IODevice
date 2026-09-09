using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using IOToolkit;

namespace IOStudio.ViewModels;

/// <summary>
/// 信号发生器波形类型
/// </summary>
public enum GeneratorWaveType
{
    Sine,       // 正弦
    Square,     // 方波（占空比可调）
    Triangle,   // 三角波
    Ramp,       // 斜坡（上升锯齿）
    Step,       // 阶跃（半周期循环）
    Pwm,        // PWM（占空比可调，高频）
    Sweep       // 扫频（频率 f → 10f 线性）
}

/// <summary>
/// 信号发生器：按波形参数计算 0~1 目标值，以固定步长周期输出到目标通道，
/// 并把理论值写入示波器预览缓冲。
/// </summary>
public class SignalGenerator
{
    private Func<IODevice?> _deviceProvider;
    private ScopeSampler? _previewTarget;

    public GeneratorWaveType WaveType { get; set; } = GeneratorWaveType.Sine;
    public double Frequency { get; set; } = 1.0;     // Hz
    public double Amplitude { get; set; } = 100.0;   // 峰峰值 %（0~100）
    public double Offset { get; set; } = 50.0;       // 中心偏置 %（0~100）
    public double DutyCycle { get; set; } = 50.0;    // 占空比 %（方波/PWM）
    public double PhaseDeg { get; set; } = 0.0;      // 相位 °
    public double DurationMs { get; set; } = 0.0;    // 扫频时长 ms（0=1s）
    public int CycleLimit { get; set; } = 0;         // 循环次数，0=无限

    public SignalGenerator(Func<IODevice?> deviceProvider)
    {
        _deviceProvider = deviceProvider;
    }

    public void AttachPreview(ScopeSampler sampler) => _previewTarget = sampler;

    /// <summary>t 时刻的归一化波形值（-1~1 或 0~1），再由 ComputeValue 映射到 0~1 输出。</summary>
    public double ComputeWave(double t)
    {
        double f = Frequency > 0 ? Frequency : 1.0;
        double period = 1.0 / f;
        double phase = PhaseDeg * Math.PI / 180.0;

        switch (WaveType)
        {
            case GeneratorWaveType.Sweep:
            {
                double sweepSec = DurationMs > 0 ? DurationMs / 1000.0 : 1.0;
                double tau = Math.Min(1.0, t / sweepSec);
                double instFreq = f * (1.0 + 9.0 * tau);
                // 相位积分 φ(t) = 2π * ∫ instFreq dt = 2π * f * (t + 4.5 * t² / sweepSec)
                double phi = 2.0 * Math.PI * f * (t + 4.5 * t * t / sweepSec) + phase;
                return Math.Sin(phi);
            }
            case GeneratorWaveType.Square:
            case GeneratorWaveType.Pwm:
            {
                double p = (t % period) / period;
                return p < DutyCycle / 100.0 ? 1.0 : -1.0;
            }
            case GeneratorWaveType.Triangle:
            {
                double p = (t % period) / period;
                return 4.0 * Math.Abs(p - 0.5) - 1.0;
            }
            case GeneratorWaveType.Ramp:
            {
                double p = (t % period) / period;
                return 2.0 * p - 1.0;
            }
            case GeneratorWaveType.Step:
            {
                double p = (t % period) / period;
                return p < 0.5 ? 0.0 : 1.0;
            }
            default:
                return Math.Sin(2.0 * Math.PI * f * t + phase);
        }
    }

    /// <summary>t 时刻的 0~1 输出值（幅值/偏置映射 + 钳制）。</summary>
    public double ComputeValue(double t)
    {
        double amp = Math.Clamp(Amplitude, 0, 100) / 100.0;
        double off = Math.Clamp(Offset, 0, 100) / 100.0;
        double wave = ComputeWave(t);
        double v = off + (amp / 2.0) * wave;
        return Math.Clamp(v, 0.0, 1.0);
    }

    /// <summary>
    /// 运行发生器：按 10ms 步长输出到 targets。
    /// safeSet 返回 false（未 ARM/急停）时停止输出并退出。执行 ~1.2×duration 或指定循环数后自动停止。
    /// </summary>
    public async Task RunAsync(
        CancellationToken ct,
        Func<IOToolkit.Key, float, bool> safeSet,
        IReadOnlyList<IOToolkit.Key> targets,
        Action<double>? previewTick = null)
    {
        if (targets.Count == 0 || safeSet == null)
            return;

        var sw = Stopwatch.StartNew();
        const double stepMs = 10.0;
        double next = 0;
        int completedCycles = 0;
        double lastPhase = 0;
        double period = Frequency > 0 ? 1.0 / Frequency : 1.0;

        while (!ct.IsCancellationRequested)
        {
            double t = sw.Elapsed.TotalSeconds;
            double elapsedMs = sw.Elapsed.TotalMilliseconds;
            if (elapsedMs < next)
            {
                await Task.Delay(1, ct).ConfigureAwait(false);
                continue;
            }
            next = (long)(elapsedMs / stepMs + 1) * stepMs;

            // 循环计数：跨过一个完整周期（仅对周期波形有效，Sweep 忽略）
            double phaseNow = t / period;
            if ((long)Math.Floor(phaseNow) > (long)Math.Floor(lastPhase) && WaveType != GeneratorWaveType.Sweep)
                completedCycles++;
            lastPhase = phaseNow;

            if (CycleLimit > 0 && completedCycles >= CycleLimit)
                break;

            double v = ComputeValue(t);
            previewTick?.Invoke(v);

            bool alive = true;
            foreach (var k in targets)
            {
                if (!safeSet(k, (float)v))
                {
                    alive = false;
                    break;
                }
            }
            if (!alive)
                break;
        }
    }

    public double CurrentPreviewTick(double t) => ComputeValue(t);
}