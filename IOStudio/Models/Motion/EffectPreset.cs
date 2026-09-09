using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;

namespace IOStudio.Models.Motion
{
    /// <summary>
    /// 效果预设 — 多通道联动的动作模板 (用户可直接拖拽到时间轴的积木块)。
    /// <para>
    /// 区别于 CurvePresetService (仅作用于单个关键帧的插值类型)，
    /// EffectPreset 是包含多个逻辑通道关键帧序列的完整效果模板。
    /// </para>
    /// </summary>
    public class EffectPreset
    {
        /// <summary>预设唯一标识 (如 "fx_weightless_rise")</summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        /// <summary>预设内容修订号。实例固定到修订号，避免库更新后静默改变影片。</summary>
        [JsonPropertyName("revision")]
        public int Revision { get; set; } = 1;

        /// <summary>显示名称 (如 "失重上升")</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary>分类 (如 "失重效果" / "过山车" / "冲击震动" / "特效")</summary>
        [JsonPropertyName("category")]
        public string Category { get; set; } = "";

        /// <summary>描述 / 用途说明</summary>
        [JsonPropertyName("description")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Description { get; set; }

        /// <summary>
        /// 基准时长 (毫秒)。参数化重采样时, 目标时长相对于它做时间缩放。
        /// </summary>
        [JsonPropertyName("duration_ms")]
        public double DurationMs { get; set; } = 3000;

        /// <summary>
        /// 与设备通道无关的单曲线关键帧数据。存在时优先于旧版 Channels，
        /// 放置阶段由用户选择的目标轨道承载这组数值。
        /// </summary>
        [JsonPropertyName("keyframes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<MotionKeyframe>? Keyframes { get; set; }

        [JsonIgnore]
        public int KeyframeCount => Keyframes?.Count ?? Channels.Sum(c => c.Keyframes?.Count ?? 0);

        /// <summary>面向编辑器显示的基准时长，避免把毫秒误读成秒。</summary>
        [JsonIgnore]
        public string DurationDisplay =>
            $"{(DurationMs / 1000.0).ToString("F1", CultureInfo.InvariantCulture)}s";

        /// <summary>默认强度 (0~1 缩放系数基准)</summary>
        [JsonPropertyName("default_intensity")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public float DefaultIntensity { get; set; } = 0.8f;

        /// <summary>强度下限 (参数化校验)</summary>
        [JsonPropertyName("min_intensity")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public float MinIntensity { get; set; } = 0.2f;

        /// <summary>强度上限 (参数化校验)</summary>
        [JsonPropertyName("max_intensity")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public float MaxIntensity { get; set; } = 1.2f;

        /// <summary>各逻辑通道的联动曲线</summary>
        [JsonPropertyName("channels")]
        public List<PresetChannel> Channels { get; set; } = new();

        /// <summary>
        /// 继承的父预设 Id (可选)。子预设仅覆盖部分通道, 未覆盖的通道从父预设继承。
        /// </summary>
        [JsonPropertyName("base_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? BaseId { get; set; }

        /// <summary>
        /// 收藏标记 (运行时 UI 状态, 不随 .mfx 序列化, 由面板独立持久化)。
        /// </summary>
        [JsonIgnore]
        public bool IsFavorite { get; set; }

        /// <summary>是否来自内置预设库 (运行时 UI 状态, 内置项不可重命名/删除)。</summary>
        [JsonIgnore]
        public bool IsBuiltIn { get; set; }
    }
}
