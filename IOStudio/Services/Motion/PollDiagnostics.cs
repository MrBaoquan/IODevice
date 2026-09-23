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
    }
}
