using System;
using System.Collections.Generic;
using System.Linq;
using IOStudio.Models.Motion;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 预设参数化重采样器 — 将效果预设 + 参数(强度/速度/时长) 生成可落轨的 MotionClip 序列。
    /// <para>
    /// 参数化只发生在"生成时刻"，落轨后的 Clip 是普通关键帧序列，用户可继续用曲线编辑器精调。
    /// </para>
    /// </summary>
    public static class PresetParameterApplier
    {
        /// <summary>
        /// 应用参数到效果预设，生成通道 → Clip 映射列表。
        /// </summary>
        /// <param name="preset">效果预设</param>
        /// <param name="options">参数化选项</param>
        /// <returns>每个逻辑通道对应的 GeneratedClip。调用方负责按 role 匹配到实际轨道.</returns>
        public static PresetApplicationResult Apply(EffectPreset preset, PresetApplyOptions options)
        {
            var result = new PresetApplicationResult();

            if (preset == null || (preset.Keyframes == null && (preset.Channels == null || preset.Channels.Count == 0)))
                return result;

            double baseDuration = preset.DurationMs > 0 ? preset.DurationMs : 3000;
            double requestedDuration =
                options.TargetDurationMs > 0 ? options.TargetDurationMs : baseDuration;
            double speedFactor = options.Speed > 0 ? options.Speed : 1.0;
            // 速度倍率遵循主流编辑器心智: 2x = 用时减半, 0.5x = 用时加倍。
            double targetDuration = requestedDuration / speedFactor;
            double timeScale = targetDuration / baseDuration;
            float intensity = Math.Clamp(
                options.Intensity,
                preset.MinIntensity,
                preset.MaxIntensity
            );

            if (preset.Keyframes is { Count: > 0 })
            {
                var generated = preset.Keyframes
                    .Select(kf => CloneAndParameterize(kf, new PresetChannel(), intensity, timeScale))
                    .OrderBy(kf => kf.TimeMs)
                    .ToList();
                result.Clips.Add(new GeneratedClip { Role = "", Clip = new MotionClip
                {
                    Id = Guid.NewGuid().ToString("N")[..8],
                    StartMs = 0,
                    EndMs = Math.Max(targetDuration, generated[^1].TimeMs),
                    Keyframes = generated,
                }});
                result.DurationMs = result.Clips[0].Clip.EndMs;
                return result;
            }

            foreach (var channel in preset.Channels)
            {
                if (string.IsNullOrEmpty(channel.Role))
                    continue;

                // 获取关键帧源: 内联优先, 无内联时通过 TemplateRef 查找
                var sourceKeyframes = channel.Keyframes;
                if (
                    (sourceKeyframes == null || sourceKeyframes.Count == 0)
                    && !string.IsNullOrEmpty(channel.TemplateRef)
                )
                {
                    sourceKeyframes = CurveTemplateLibrary.FindTemplate(channel.TemplateRef);
                }

                if (sourceKeyframes == null || sourceKeyframes.Count == 0)
                    continue;

                // 深拷贝关键帧 (不修改原预设)
                var generated = sourceKeyframes
                    .Select(
                        kf => CloneAndParameterize(kf, channel, intensity, timeScale)
                    )
                    .ToList();

                // 排序 (相位偏移可能改变顺序)
                generated.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));

                double startMs = 0;
                double endMs = targetDuration;

                // 如果关键帧有实际时间跨度, 以最后一个关键帧时间为准
                if (generated.Count > 0)
                {
                    endMs = Math.Max(endMs, generated[^1].TimeMs);
                }

                var clip = new MotionClip
                {
                    Id = Guid.NewGuid().ToString("N")[..8],
                    StartMs = startMs,
                    EndMs = endMs,
                    Keyframes = generated
                };

                result.Clips.Add(new GeneratedClip { Role = channel.Role, Clip = clip });
            }

            result.DurationMs = result.Clips
                .Select(c => c.Clip.EndMs)
                .DefaultIfEmpty(0)
                .Max();

            return result;
        }

        /// <summary>深拷贝 + 参数化单个关键帧</summary>
        private static MotionKeyframe CloneAndParameterize(
            MotionKeyframe source,
            PresetChannel channel,
            float intensity,
            double timeScale
        )
        {
            // 时间缩放
            double newTime = source.TimeMs * timeScale;

            // 相位偏移
            newTime += channel.PhaseOffsetMs;

            // 幅度缩放: value' = 0.5 + (value - 0.5) × intensity × channelScale
            float newValue = 0.5f + (source.Value - 0.5f) * intensity * channel.Scale;
            newValue = Math.Clamp(newValue, 0f, 1f);

            return new MotionKeyframe
            {
                TimeMs = Math.Max(0, newTime),
                Value = newValue,
                Interpolation = source.Interpolation,
                TangentIn = source.TangentIn,
                TangentOut = source.TangentOut,
                Cp1x = source.Cp1x,
                Cp2x = source.Cp2x,
                Event = source.Event
            };
        }
    }

    /// <summary>预设参数化选项</summary>
    public class PresetApplyOptions
    {
        /// <summary>强度 (0.2~1.2, 默认 1.0 使用预设默认强度)</summary>
        public float Intensity { get; set; } = 1.0f;

        /// <summary>速度倍率 (0.5x~2x, 默认 1.0)</summary>
        public double Speed { get; set; } = 1.0;

        /// <summary>
        /// 目标时长 (毫秒, 0 或负值表示使用预设基准时长)。
        /// 与 Speed 同时作用: effectiveDuration = targetDuration / speed。
        /// </summary>
        public double TargetDurationMs { get; set; }
    }

    /// <summary>参数化应用结果 — 包含所有生成的通道 Clip</summary>
    public class PresetApplicationResult
    {
        /// <summary>全部通道生成后的实际最大时长。</summary>
        public double DurationMs { get; set; }

        /// <summary>生成的 Clip 列表 (按角色标记)</summary>
        public List<GeneratedClip> Clips { get; set; } = new();
    }

    /// <summary>参数化生成的单个通道 Clip</summary>
    public class GeneratedClip
    {
        /// <summary>逻辑角色标签 (与 PresetChannel.Role 一致)</summary>
        public string Role { get; set; } = "";

        /// <summary>生成的可落轨 MotionClip</summary>
        public MotionClip Clip { get; set; } = new();
    }

    /// <summary>
    /// 曲线模板仓库 — 按名称查找内置曲线模板。
    /// 模板为预设作者定义的"常用曲线形状", 可被多个效果预设引用。
    /// 非效果预设, 仅为单通道关键帧序列。
    /// </summary>
    internal static class CurveTemplateLibrary
    {
        private static readonly Dictionary<string, List<MotionKeyframe>> _templates = new();

        /// <summary>注册或覆盖一个曲线模板</summary>
        internal static void Register(string name, List<MotionKeyframe> keyframes)
        {
            _templates[name] = keyframes;
        }

        /// <summary>按名称查找曲线模板, 返回深拷贝</summary>
        internal static List<MotionKeyframe>? FindTemplate(string name)
        {
            if (string.IsNullOrEmpty(name) || !_templates.TryGetValue(name, out var source))
                return null;

            // 返回深拷贝
            return source
                .Select(
                    kf =>
                        new MotionKeyframe
                        {
                            TimeMs = kf.TimeMs,
                            Value = kf.Value,
                            Interpolation = kf.Interpolation,
                            TangentIn = kf.TangentIn,
                            TangentOut = kf.TangentOut,
                            Cp1x = kf.Cp1x,
                            Cp2x = kf.Cp2x,
                            Event = kf.Event
                        }
                )
                .ToList();
        }
    }
}
