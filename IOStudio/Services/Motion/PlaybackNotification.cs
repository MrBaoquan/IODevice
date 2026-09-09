using System.Collections.Generic;
using IOStudio.Models.Motion;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 播放引擎统一通知 — 判别联合 (Discriminated Union)。
    /// 替代 5 个独立事件, 简化订阅与扩展。
    /// </summary>
    public abstract record PlaybackNotification
    {
        /// <summary>播放位置变化 (每 Tick 触发)</summary>
        public sealed record Position(double TimeMs) : PlaybackNotification;

        /// <summary>播放状态变化</summary>
        public sealed record StateChange(PlaybackState OldState, PlaybackState NewState)
            : PlaybackNotification;

        /// <summary>播放完成 (非循环模式到达末尾)</summary>
        public sealed record Completed : PlaybackNotification;

        /// <summary>时间轴加载完成</summary>
        public sealed record TimelineReady(MotionTimeline Timeline) : PlaybackNotification;

        /// <summary>批量 LiveValue 更新</summary>
        public sealed record LiveValues(
            IReadOnlyList<(string DeviceName, string ChannelName, float Value)> Channels
        ) : PlaybackNotification;
    }
}
