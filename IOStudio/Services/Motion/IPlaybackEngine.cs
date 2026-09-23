using System;
using System.Collections.Generic;
using IOStudio.Models.Motion;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 播放引擎接口 — 抽象 Timeline 播放控制
    /// 支持播放/暂停/停止/恢复、速度控制、循环、Tick 驱动
    /// </summary>
    public interface IPlaybackEngine : IDisposable
    {
        /// <summary>当前播放状态</summary>
        PlaybackState State { get; }

        /// <summary>是否循环播放</summary>
        bool Loop { get; set; }

        /// <summary>播放速度 (1.0 = 正常)</summary>
        double Speed { get; set; }

        /// <summary>是否将预览值实时输出到物理设备</summary>
        bool LiveOutputToDevice { get; set; }

        /// <summary>
        /// 是否由 native 承担派发 (量程换算 + 限速 + 写设备都在 native, 与放映端同源)。
        /// 默认 false = 由 C# 侧驱动并直接写设备。
        /// </summary>
        bool UseNativeDispatch { get; set; }

        /// <summary>
        /// 最近一次内部错误 (如 native 派发失败); null = 无。
        ///
        /// <para>为什么需要: 播放 tick 里抛出的异常会被 Rx 订阅吞掉并**终止 tick**
        /// (表现为"播放不动了"却毫无提示)。把错误留在引擎上、由 CLI/UI 暴露出来,
        /// 才不会又变成一次静默失效。</para>
        /// </summary>
        string? LastError { get; }

        /// <summary>
        /// 统一通知事件 — 所有引擎通知通过此单一事件发出。
        /// 使用 pattern matching 处理不同类型:
        /// <code>
        /// engine.Notified += n => { switch(n) { case PlaybackNotification.Position p: ... } };
        /// </code>
        /// </summary>
        event Action<PlaybackNotification>? Notified;

        /// <summary>位置变化事件 (timeMs)</summary>
        event Action<double>? PositionChanged;

        /// <summary>状态变化事件 (oldState, newState)</summary>
        event Action<PlaybackState, PlaybackState>? StateChanged;

        /// <summary>播放完成事件</summary>
        event Action? PlaybackCompleted;

        /// <summary>Timeline 加载完成事件</summary>
        event Action<MotionTimeline>? TimelineLoaded;

        /// <summary>批量 LiveValue 更新事件 (每 Tick 触发一次, 替代逐轨道 ValueDispatched)</summary>
        event Action<
            IReadOnlyList<(string deviceName, string channelName, float value)>
        >? LiveValuesBatchUpdated;

        /// <summary>设备值分发器</summary>
        DeviceDispatcher Dispatcher { get; }

        /// <summary>加载 Timeline 数据</summary>
        bool LoadTimeline(MotionTimeline timeline, string? sourcePath = null);

        /// <summary>确保引擎持有 Timeline 引用 (用于 Seek 预览, 不启动播放)</summary>
        void EnsureTimelineLoaded(MotionTimeline timeline);

        /// <summary>开始播放</summary>
        void Play();

        /// <summary>暂停播放</summary>
        void Pause();

        /// <summary>恢复播放</summary>
        void Resume();

        /// <summary>停止播放</summary>
        void Stop();

        /// <summary>跳转到指定时间</summary>
        void Seek(double timeMs);

        /// <summary>强制停止 (释放资源)</summary>
        void ForceStop();

        /// <summary>Tick 驱动 (外部定时器调用)</summary>
        void Tick();

        /// <summary>
        /// 外部时钟 (影片/音频) 供时。非 null 时播放位置完全由它决定, 忽略内部计时与 speed。
        /// 影片编排必须由媒体当主时钟, 否则动作与画面会各自自由跑并逐渐漂移。
        /// 传 null 恢复内部计时驱动 (无媒体时的纯动作预览)。
        /// </summary>
        void SetExternalClockMs(double? mediaTimeMs);
    }
}
