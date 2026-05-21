using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IOToolkit.Extension
{
    /// <summary>
    /// 高精度周期性脉冲任务. 以固定 tick 频率异步调用 <c>updateAction</c>,
    /// 使用 Stopwatch 做漂移补偿, 适合驱动 DOImmediate / AD 扫描等.
    /// 线程安全: Start / Stop / SetUpdatesPerSecond 均可并发调用.
    /// </summary>
    public class PulseTask
    {
        private CancellationTokenSource _cts;
        private Task _task;
        private int _updatesPerSecond;
        private double _updateIntervalMilliseconds;
        private readonly object _lock = new object();
        private readonly Action _updateAction;

        public PulseTask(int updatesPerSecond, Action updateAction)
        {
            if (updatesPerSecond <= 0)
                throw new ArgumentException("updatesPerSecond must be greater than zero.");
            _updatesPerSecond = updatesPerSecond;
            _updateIntervalMilliseconds = 1000.0 / _updatesPerSecond;

            _updateAction = updateAction ?? throw new ArgumentNullException(nameof(updateAction));
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_task != null && !_task.IsCompleted)
                    return;

                _cts = new CancellationTokenSource();
                CancellationToken token = _cts.Token;

                _task = Task.Run(
                    async () =>
                    {
                        var stopwatch = Stopwatch.StartNew();
                        double nextTick = stopwatch.Elapsed.TotalMilliseconds;

                        try
                        {
                            while (!token.IsCancellationRequested)
                            {
                                _updateAction.Invoke();

                                nextTick += _updateIntervalMilliseconds;
                                var sleepTime = nextTick - stopwatch.Elapsed.TotalMilliseconds;

                                if (sleepTime > 1)
                                {
                                    try
                                    {
                                        await Task.Delay(
                                            TimeSpan.FromMilliseconds(sleepTime),
                                            token
                                        );
                                    }
                                    catch (TaskCanceledException)
                                    {
                                        break;
                                    }
                                }
                                else
                                {
                                    await Task.Yield();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Trace.TraceError("PulseTask异常: " + ex.Message);
                        }
                    },
                    token
                );
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (_cts == null)
                    return;

                _cts.Cancel();

                try
                {
                    _task?.Wait();
                }
                catch (AggregateException ae)
                {
                    ae.Handle(e => e is TaskCanceledException);
                }
                finally
                {
                    _cts.Dispose();
                    _cts = null;
                    _task = null;
                }
            }
        }

        public void SetUpdatesPerSecond(int updatesPerSecond)
        {
            if (updatesPerSecond <= 0)
                throw new ArgumentException("updatesPerSecond must be greater than zero.");
            lock (_lock)
            {
                _updatesPerSecond = updatesPerSecond;
                _updateIntervalMilliseconds = 1000.0 / _updatesPerSecond;
            }
        }
    }
}
