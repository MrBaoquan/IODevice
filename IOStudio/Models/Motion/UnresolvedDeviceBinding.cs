using System.Collections.Generic;

namespace IOStudio.Models.Motion
{
    /// <summary>
    /// 一条「设备名无人认领」的绑定问题 —— 按设备名聚合, 不是一个轨道一条。
    ///
    /// <para>一个未解析的设备名往往影响整组轨道 (如 <c>YDL-0</c> 影响 8 条), 逐轨道列出来
    /// 只是噪音; 而且修复动作本来就是按设备名一次改到位。</para>
    /// </summary>
    public class UnresolvedDeviceBinding
    {
        /// <summary>文件里写着、但设备表中没有的设备名。</summary>
        public string DeviceName { get; set; } = "";

        /// <summary>受影响的轨道数。</summary>
        public int TrackCount { get; set; }

        /// <summary>受影响轨道的 role (去重, 用于智能建议与显示)。</summary>
        public List<string> Roles { get; set; } = new();

        /// <summary>
        /// 受影响轨道的输出通道/动作名回显 —— 修复<b>不会</b>改这些, 列出来是让作者确认这一点。
        /// </summary>
        public List<string> Channels { get; set; } = new();

        /// <summary>输出类型回显 (oaxis / oaction 混合时给出汇总文字)。</summary>
        public string OutputTypeText { get; set; } = "";

        /// <summary>按 role 智能匹配出的建议目标设备名 (无匹配为 null)。</summary>
        public string? SuggestedDeviceName { get; set; }

        /// <summary>建议的可读说明, 如 "heave → YDL-0.Heave"。</summary>
        public string SuggestionText { get; set; } = "";

        /// <summary>受影响轨道的 id (应用修复时定位用)。</summary>
        public List<string> TrackIds { get; set; } = new();
    }
}
