using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IOToolkit;

namespace IOStudio.ViewModels;

/// <summary>示波器触发模式。</summary>
public enum ScopeTriggerMode
{
    Free = 0,   // 自由运行（显示最新窗口）
    Rising = 1, // 上升沿触发
    Falling = 2 // 下降沿触发
}

/// <summary>
/// 示波器采样器：后台线程按采样率把选中通道的 DO 值写入环形缓冲，
/// Capture 时按触发模式截取窗口生成 ScopeFrame。
/// </summary>
public class ScopeSampler : IDisposable
{
    public const int BufferCapacity = 4096;

    private readonly object _lock = new();
    private readonly Func<IODevice?> _deviceProvider;
    private readonly List<IOToolkit.Key> _channels = new();
    private readonly List<float[]> _buffers = new();
    private readonly List<int> _heads = new();
    private readonly List<int> _counts = new();
    private readonly double[] _preview = new double[BufferCapacity];
    private int _previewHead;
    private int _previewCount;

    private CancellationTokenSource? _cts;
    private Task? _task;
    private int _sampleRate = 100;

    public double SampleIntervalMs => 1000.0 / _sampleRate;

    public ScopeSampler(Func<IODevice?> deviceProvider)
    {
        _deviceProvider = deviceProvider;
    }

    public int SampleRate
    {
        get => _sampleRate;
        set
        {
            if (value <= 0)
                return;
            _sampleRate = value;
        }
    }

    /// <summary>设置采样率 Hz。</summary>
    public void SetSampleRateHz(int hz) => SampleRate = hz;

    /// <summary>重建采样通道集合（选中变化时调用），缓冲清零。</summary>
    public void ReplaceChannels(IEnumerable<IOToolkit.Key> keys)
    {
        lock (_lock)
        {
            _channels.Clear();
            _channels.AddRange(keys);
            _buffers.Clear();
            _heads.Clear();
            _counts.Clear();
            foreach (var _ in _channels)
            {
                _buffers.Add(new float[BufferCapacity]);
                _heads.Add(0);
                _counts.Add(0);
            }
        }
    }

    public void Start()
    {
        if (_task != null)
            return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _task = Task.Run(() => SamplingLoop(ct));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _task?.Wait(500);
        }
        catch (AggregateException)
        {
        }
        _cts?.Dispose();
        _cts = null;
        _task = null;
    }

    /// <summary>清空缓冲（暂停/复位用）。</summary>
    public void Clear()
    {
        lock (_lock)
        {
            for (int i = 0; i < _counts.Count; i++)
                _counts[i] = 0;
            _previewCount = 0;
        }
    }

    private void SamplingLoop(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        double intervalMs = SampleIntervalMs;
        long nextTick = 0;
        while (!ct.IsCancellationRequested)
        {
            double now = sw.Elapsed.TotalMilliseconds;
            if (now < nextTick)
            {
                Thread.Sleep(1);
                continue;
            }
            nextTick = (long)((now / intervalMs + 1) * intervalMs);
            intervalMs = SampleIntervalMs;
            SampleOnce();
        }
    }

    private void SampleOnce()
    {
        var device = _deviceProvider();
        if (device == null)
            return;

        lock (_lock)
        {
            for (int i = 0; i < _channels.Count; i++)
            {
                float v = 0f;
                try
                {
                    v = device.GetDO(_channels[i]);
                }
                catch
                {
                    v = 0f;
                }
                _buffers[i][_heads[i]] = v;
                _heads[i] = (_heads[i] + 1) % BufferCapacity;
                if (_counts[i] < BufferCapacity)
                    _counts[i]++;
            }
        }
    }

    /// <summary>信号发生器写入目标预览值（预览曲线，供示波器叠加显示）。</summary>
    public void PushPreview(double value)
    {
        lock (_lock)
        {
            _preview[_previewHead] = value;
            _previewHead = (_previewHead + 1) % BufferCapacity;
            if (_previewCount < BufferCapacity)
                _previewCount++;
        }
    }

    public void ResetPreview()
    {
        lock (_lock)
        {
            _previewCount = 0;
        }
    }

    /// <summary>
    /// 捕获一帧。triggerMode: Free=自由(最新), Rising=上升沿, Falling=下降沿（在通道0上找最后边沿并以其为窗口尾）。
    /// </summary>
    public ScopeFrame Capture(ScopeTriggerMode triggerMode, double timeBaseMs)
    {
        lock (_lock)
        {
            int channelCount = _channels.Count;
            if (channelCount == 0)
                return new ScopeFrame();

            double intervalMs = SampleIntervalMs;
            int visible = Math.Max(1, (int)Math.Ceiling(timeBaseMs * 10.0 / intervalMs));
            if (visible > BufferCapacity)
                visible = BufferCapacity;

            int lastIndex = BufferCapacity - 1;
            int count0 = _counts.Count > 0 ? _counts[0] : 0;
            int head0 = _heads.Count > 0 ? _heads[0] : 0;

            if (count0 > 0)
            {
                int newest = (head0 - 1 + BufferCapacity) % BufferCapacity;
                int end = newest;

                if (triggerMode != ScopeTriggerMode.Free && count0 >= 2)
                {
                    var buf = _buffers[0];
                    bool rising = triggerMode == ScopeTriggerMode.Rising;
                    for (int back = 0; back < count0 - 1; back++)
                    {
                        int i = (head0 - 1 - back + BufferCapacity * 2) % BufferCapacity;
                        int prev = (i - 1 + BufferCapacity) % BufferCapacity;
                        bool hit = rising
                            ? buf[prev] < 0.5f && buf[i] >= 0.5f
                            : buf[prev] >= 0.5f && buf[i] < 0.5f;
                        if (hit)
                        {
                            end = i;
                            break;
                        }
                    }
                }

                int start = end - visible + 1;
                int validStart;
                if (start < 0)
                {
                    validStart = 0;
                }
                else
                {
                    validStart = start;
                }
                lastIndex = end;

                var channels = new double[channelCount][];
                for (int ci = 0; ci < channelCount; ci++)
                {
                    var arr = new double[BufferCapacity];
                    var src = _buffers[ci];
                    int head = _heads[ci];
                    int cnt = _counts[ci];
                    for (int j = 0; j < cnt; j++)
                        arr[j] = src[(head - cnt + j + BufferCapacity * 2) % BufferCapacity];
                    channels[ci] = arr;
                }

                int validCount = lastIndex - validStart + 1;
                double[]? preview = null;
                if (_previewCount > 0)
                {
                    int pEnd = (_previewHead - 1 + BufferCapacity) % BufferCapacity;
                    int pStart = Math.Max(0, pEnd - visible + 1);
                    int pCount = pEnd - pStart + 1;
                    preview = new double[pCount];
                    for (int j = 0; j < pCount; j++)
                        preview[j] = _preview[(pStart + j) % BufferCapacity];
                }

                return new ScopeFrame
                {
                    Channels = channels,
                    ValidStart = validStart,
                    ValidCount = validCount,
                    SampleIntervalMs = intervalMs,
                    Preview = preview
                };
            }

            // 尚无数据：返回空帧
            return new ScopeFrame { SampleIntervalMs = intervalMs };
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
