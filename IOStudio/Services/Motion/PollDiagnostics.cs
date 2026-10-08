namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 后台设备轮询 (<c>IODeviceController.Update()</c>) 的耗时观测。
    ///
    /// <para>用途: 定位"播放卡顿 / 定时调度被饿死"这类问题 —— 播放 tick 由
    /// <c>Observable.Interval(16.67ms)</c> 驱动, 若后台轮询在同一进程里长时间占用线程/锁,
    /// interval 的触发次数就会大幅下降, 而单看 tick 内部耗时却很正常 (看不到这个瓶颈)。</para>
    /// </summary>
    public static class PollDiagnostics
    {
        /// <summary>轮询次数</summary>
        public static long UpdateCount;

        /// <summary>单次轮询最长耗时 (毫秒)</summary>
        public static long MaxUpdateMs;

        /// <summary>轮询累计耗时 (毫秒)</summary>
        public static long TotalUpdateMs;

        // ── 看门狗: 找出"哪个调用长期阻塞" ──────────────────
        //
        // 动机: 某些池线程会被同步阻塞调用长时间占用 (例如派发链路里宿主↔插件的往返),
        // 线程池只有 3 个线程, 堵一个就足以饿死 Rx 的定时通知 —— 表现为"两个定时器同时瘫",
        // 而单看每个调用点的耗时又都正常 (因为被堵的那个根本没返回, 统计不到)。
        //
        // 用法: 在候选调用点 Enter/Exit, 看门狗线程会报出超过 500ms 未返回的调用名。

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<
            int,
            (string Name, long SinceMs)
        > Active = new();

        private static int _nextId;
        private static int _watcherStarted;

        /// <summary>看门狗最近发现的长期阻塞调用名; null = 无。</summary>
        public static string? StuckSite;

        /// <summary>记录一个调用开始, 返回一个句柄给 Exit。</summary>
        public static int Enter(string name)
        {
            int id = System.Threading.Interlocked.Increment(ref _nextId);
            Active[id] = (name, System.Environment.TickCount64);
            EnsureWatcher();
            return id;
        }

        /// <summary>记录调用返回。</summary>
        public static void Exit(int id)
        {
            Active.TryRemove(id, out _);
        }

        private static void EnsureWatcher()
        {
            if (System.Threading.Interlocked.Exchange(ref _watcherStarted, 1) == 1)
                return;
            var t = new System.Threading.Thread(WatchLoop)
            {
                IsBackground = true,
                Name = "PollDiagnostics.Watch",
            };
            t.Start();
        }

        private static void WatchLoop()
        {
            while (true)
            {
                System.Threading.Thread.Sleep(200);
                long now = System.Environment.TickCount64;
                string? stuck = null;
                long stuckMs = 0;
                foreach (var kv in Active)
                {
                    long busyMs = now - kv.Value.SinceMs;
                    if (busyMs > 500 && busyMs > stuckMs)
                    {
                        stuckMs = busyMs;
                        stuck = kv.Value.Name;
                    }
                }
                StuckSite = stuck == null ? null : $"{stuck} 已阻塞 {stuckMs}ms";
            }
        }

        // ── 进程调度压力 (区分"线程耗尽"与"CPU 忙") ──

        /// <summary>采样次数</summary>
        public static long SampleCount;

        /// <summary>线程池线程数峰值</summary>
        public static int MaxThreadCount;

        /// <summary>线程池待处理工作项峰值</summary>
        public static long MaxPendingWork;

        /// <summary>首次采样时的进程 CPU 时间 (毫秒); -1 = 尚未采样</summary>
        public static double FirstCpuMs = -1;

        /// <summary>进程 CPU 时间最大值 (毫秒)</summary>
        public static double MaxCpuMs;

        /// <summary>本次运行消耗的 CPU 时间 (毫秒)</summary>
        public static double CpuDeltaMs => FirstCpuMs < 0 ? 0 : MaxCpuMs - FirstCpuMs;

        /// <summary>采样一次进程压力。由播放 tick 调用 (两种模式下都会跑)。</summary>
        public static void Sample()
        {
            SampleCount++;

            int threads = System.Threading.ThreadPool.ThreadCount;
            if (threads > MaxThreadCount)
                MaxThreadCount = threads;

            long pending = System.Threading.ThreadPool.PendingWorkItemCount;
            if (pending > MaxPendingWork)
                MaxPendingWork = pending;

            double cpuMs = System.Diagnostics.Process
                .GetCurrentProcess()
                .TotalProcessorTime.TotalMilliseconds;
            if (FirstCpuMs < 0)
                FirstCpuMs = cpuMs;
            if (cpuMs > MaxCpuMs)
                MaxCpuMs = cpuMs;
        }
    }
}
