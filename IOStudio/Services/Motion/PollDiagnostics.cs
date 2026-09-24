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
