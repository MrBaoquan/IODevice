using System.Collections.Generic;
using System.Text.Json.Serialization;
using IOStudio.Models.Motion;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 动作编排「演出规格」(show spec) — 面向脚本/流水线的紧凑编排输入格式。
    ///
    /// <para>为什么需要: .motion 是**产物**格式, 手工/脚本直接拼 JSON 既易错又不可复核
    /// (clip 跨度、实例↔预设一致性、同轨重叠、内容越界全靠人肉保证)。本 spec 把
    /// "轨道(role→通道) / 预设 / 节拍表(cue) / 环境门控 / 章节 / 事件" 分开表达,
    /// 由 <see cref="MotionShowBuilder"/> 负责展开成合法的 <see cref="MotionTimeline"/>。</para>
    ///
    /// <para>用 CLI 生成: <c>IOStudio.exe --motion-build &lt;spec.json&gt; [--out &lt;file.motion&gt;]</c></para>
    /// </summary>
    public sealed class ShowSpec
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("duration_ms")]
        public double DurationMs { get; set; }

        [JsonPropertyName("fps")]
        public double Fps { get; set; } = 60;

        /// <summary>目标设备名 (IODevice.xml 中的设备名), 应用到所有未单独覆盖的轨道。</summary>
        [JsonPropertyName("device")]
        public string Device { get; set; } = "";

        [JsonPropertyName("tracks")]
        public List<ShowTrackSpec> Tracks { get; set; } = new();

        [JsonPropertyName("presets")]
        public List<ShowPresetSpec> Presets { get; set; } = new();

        /// <summary>节拍表 — 在指定时间放置一个预设实例 (按 role 展开到各轨道)。</summary>
        [JsonPropertyName("cues")]
        public List<ShowCueSpec> Cues { get; set; } = new();

        /// <summary>环境门控 — 特效通道的 on/off 窗口。</summary>
        [JsonPropertyName("gates")]
        public List<ShowGateSpec> Gates { get; set; } = new();

        [JsonPropertyName("chapters")]
        public List<ShowChapterSpec> Chapters { get; set; } = new();

        [JsonPropertyName("events")]
        public List<ShowEventSpec> Events { get; set; } = new();
    }

    public sealed class ShowTrackSpec
    {
        /// <summary>轨道 ID (缺省 = "trk_" + role)。</summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        /// <summary>逻辑角色标签 — cue/gate 通过它定位轨道。</summary>
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        /// <summary>OAxis 通道名 (output_type=oaxis 时必填)。</summary>
        [JsonPropertyName("channel")]
        public string Channel { get; set; } = "";

        [JsonPropertyName("output_type")]
        public string OutputType { get; set; } = "oaxis";

        [JsonPropertyName("oaction")]
        public string OAction { get; set; } = "";

        [JsonPropertyName("label")]
        public string Label { get; set; } = "";

        [JsonPropertyName("color")]
        public string Color { get; set; } = "#4FC3F7";

        [JsonPropertyName("group")]
        public string Group { get; set; } = "";

        [JsonPropertyName("value_type")]
        public string ValueType { get; set; } = "float";

        /// <summary>中性/空闲值 (缺省: float=0.5, bool=0)。</summary>
        [JsonPropertyName("neutral")]
        public float? Neutral { get; set; }

        /// <summary>工程量程覆盖。</summary>
        [JsonPropertyName("engineering_range")]
        public float? EngineeringRange { get; set; }

        [JsonPropertyName("idle")]
        public ShowIdleSpec? Idle { get; set; }
    }

    public sealed class ShowIdleSpec
    {
        [JsonPropertyName("period_ms")]
        public double PeriodMs { get; set; } = 1000;

        [JsonPropertyName("blend_ms")]
        public double BlendMs { get; set; }

        [JsonPropertyName("phase_mode")]
        public string PhaseMode { get; set; } = "continuous";

        [JsonPropertyName("phase_offset_ms")]
        public double PhaseOffsetMs { get; set; }

        [JsonPropertyName("group_id")]
        public string? GroupId { get; set; }

        [JsonPropertyName("keyframes")]
        public List<MotionKeyframe> Keyframes { get; set; } = new();
    }

    public sealed class ShowPresetSpec
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("category")]
        public string Category { get; set; } = "";

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("duration_ms")]
        public double DurationMs { get; set; }

        [JsonPropertyName("default_intensity")]
        public float DefaultIntensity { get; set; } = 0.8f;

        [JsonPropertyName("min_intensity")]
        public float MinIntensity { get; set; } = 0.2f;

        [JsonPropertyName("max_intensity")]
        public float MaxIntensity { get; set; } = 1.2f;

        [JsonPropertyName("channels")]
        public List<ShowPresetChannelSpec> Channels { get; set; } = new();
    }

    public sealed class ShowPresetChannelSpec
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("scale")]
        public float Scale { get; set; } = 1.0f;

        [JsonPropertyName("phase_offset_ms")]
        public double PhaseOffsetMs { get; set; }

        [JsonPropertyName("keyframes")]
        public List<MotionKeyframe> Keyframes { get; set; } = new();
    }

    public sealed class ShowCueSpec
    {
        [JsonPropertyName("time_ms")]
        public double TimeMs { get; set; }

        [JsonPropertyName("preset")]
        public string Preset { get; set; } = "";

        [JsonPropertyName("intensity")]
        public float Intensity { get; set; } = 1.0f;
    }

    public sealed class ShowGateSpec
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("start_ms")]
        public double StartMs { get; set; }

        [JsonPropertyName("end_ms")]
        public double EndMs { get; set; }

        /// <summary>开关淡入淡出时长 (毫秒, 0 = 硬切)。</summary>
        [JsonPropertyName("fade_ms")]
        public double FadeMs { get; set; } = 300;

        /// <summary>开启电平 (缺省 1)。</summary>
        [JsonPropertyName("level")]
        public float Level { get; set; } = 1.0f;
    }

    public sealed class ShowChapterSpec
    {
        [JsonPropertyName("time_ms")]
        public double TimeMs { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("note")]
        public string? Note { get; set; }
    }

    public sealed class ShowEventSpec
    {
        [JsonPropertyName("time_ms")]
        public double TimeMs { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("data")]
        public string? Data { get; set; }

        [JsonPropertyName("lane")]
        public string Lane { get; set; } = "default";

        [JsonPropertyName("color")]
        public string Color { get; set; } = "#f59e0b";

        [JsonPropertyName("data_type")]
        public string DataType { get; set; } = "json";
    }
}
