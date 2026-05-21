using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace IOStudio.Models.Motion
{
    /// <summary>
    /// 动作时间轴根模型 — .motion 文件的顶层结构
    /// </summary>
    public class MotionTimeline
    {
        /// <summary>格式版本号 (v2.0 起支持 EventLanes/MarkerLanes + 轨道 ShowInCurve)</summary>
        [JsonPropertyName("version")]
        public string Version { get; set; } = "2.0";

        /// <summary>项目名称</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary>总时长 (毫秒)</summary>
        [JsonPropertyName("duration_ms")]
        public double DurationMs { get; set; }

        /// <summary>帧率 (仅用于编辑器 UI 对齐)</summary>
        [JsonPropertyName("fps")]
        public double Fps { get; set; } = 60;

        /// <summary>轨道列表</summary>
        [JsonPropertyName("tracks")]
        public List<MotionTrack> Tracks { get; set; } = new();

        /// <summary>轨道分组列表 (持久化分组实体, 即使无子轨道也保留)</summary>
        [JsonPropertyName("groups")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<TrackGroup>? Groups { get; set; }

        /// <summary>独立事件列表 (不绑定到关键帧, 在指定时间点触发)</summary>
        [JsonPropertyName("events")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<TimelineEvent> Events { get; set; } = new();

        /// <summary>标记/书签列表 (用于标记时间轴重要位置, 快速导航)</summary>
        [JsonPropertyName("markers")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<TimelineMarker>? Markers { get; set; }

        /// <summary>事件轨道列表 (v2.0) — 空或缺省时按单条默认轨处理</summary>
        [JsonPropertyName("event_lanes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<EventLane>? EventLanes { get; set; }

        /// <summary>标记轨道列表 (v2.0) — 空或缺省时按单条默认轨处理</summary>
        [JsonPropertyName("marker_lanes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<MarkerLane>? MarkerLanes { get; set; }
    }
}
