using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace IOStudio.Models.Motion
{
    /// <summary>
    /// Idle 循环 (gap 填充) — 轨道"无 clip 覆盖"时的待机循环动作。
    /// <para>
    /// 语义与 C++ IODevice MotionPlayer::MotionIdleLoop 一致:
    /// 永不覆盖真实 clip (最低优先级), 只在空窗区按相位采样环形关键帧。
    /// </para>
    /// </summary>
    public class IdleLoop
    {
        /// <summary>是否启用 idle 循环 (false → 回退 neutralValue)</summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>一个循环周期时长 (毫秒)</summary>
        [JsonPropertyName("period_ms")]
        public double PeriodMs { get; set; } = 1000.0;

        /// <summary>与相邻 clip 的交叉淡化窗口 (毫秒, 0 = 硬切换)</summary>
        [JsonPropertyName("blend_ms")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double BlendMs { get; set; }

        /// <summary>
        /// 相位模式: "continuous" (用全局绝对时间, 跨片段空窗保持连贯) | "restart" (每个 gap 从头播)
        /// </summary>
        [JsonPropertyName("phase_mode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string PhaseMode { get; set; } = "continuous";

        /// <summary>相位偏移 (毫秒) — 同组多轨错相编排 (如四轴依次起伏)。continuous 下叠加到全局时间。</summary>
        [JsonPropertyName("phase_offset_ms")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double PhaseOffsetMs { get; set; }

        /// <summary>节奏组标识 — 同组多轨在 continuous 下共享全局时钟天然同步 (空字符串=独立轨)。</summary>
        [JsonPropertyName("group_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? GroupId { get; set; }

        /// <summary>单周期关键帧 (时间相对循环起始 0 ~ PeriodMs, 环形闭合)</summary>
        [JsonPropertyName("keyframes")]
        public List<MotionKeyframe> Keyframes { get; set; } = new();
    }
}
