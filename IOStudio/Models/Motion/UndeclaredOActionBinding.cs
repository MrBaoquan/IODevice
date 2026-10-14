using System.Collections.Generic;

namespace IOStudio.Models.Motion
{
    /// <summary>
    /// 一条「动作未在设备上声明」的问题 —— 按 (设备, 动作) 聚合。
    ///
    /// <para><b>为什么这个比"设备没声明"更难发现</b>: 设备名查不到时 native 侧
    /// <c>MotionPlayer</c> 至少会累加一个未解析计数; 而动作名查不到时
    /// <c>ExternalIO::SetDO</c> 直接 <c>return 0</c> —— 没有日志, 没有计数,
    /// 从任何角度看都是"播放正常、设备不动"。</para>
    /// </summary>
    public class UndeclaredOActionBinding
    {
        /// <summary>已声明、但缺这个动作的设备名。</summary>
        public string DeviceName { get; set; } = "";

        /// <summary>设备表里没有的动作名。</summary>
        public string OActionName { get; set; } = "";

        /// <summary>受影响的轨道数。</summary>
        public int TrackCount { get; set; }

        /// <summary>受影响轨道的 id。</summary>
        public List<string> TrackIds { get; set; } = new();

        /// <summary>受影响轨道的 role (去重, 用于显示与默认通道提示)。</summary>
        public List<string> Roles { get; set; } = new();

        /// <summary>
        /// 该设备<b>已经在用</b>的通道名 (取自既有 OAction 的 Key) —— 供用户挑选时参照。
        /// 刻意不列 <c>OAxis_00..31</c> 那套合成值: 真实内容会用 <c>OAxis_48</c> 这类越界通道,
        /// 拿合成集合当候选会把作者引向错的通道。
        /// </summary>
        public List<string> DeviceChannelVocabulary { get; set; } = new();

        /// <summary>建议的 Key 通道 (仅当动作名本身就是通道名时给出, 否则为空 = 必须由用户指定)。</summary>
        public string SuggestedChannel { get; set; } = "";
    }
}
