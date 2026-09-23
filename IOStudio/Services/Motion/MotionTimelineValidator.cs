using System;
using System.Collections.Generic;
using System.Linq;
using IOStudio.Models.Motion;

namespace IOStudio.Services.Motion
{
    /// <summary>校验问题级别。</summary>
    public enum MotionIssueSeverity
    {
        /// <summary>提示: 不影响正确性, 但值得知道。</summary>
        Info,

        /// <summary>警告: 多半不是你想要的 (会被静默忽略/遮蔽), 但能播。</summary>
        Warning,

        /// <summary>错误: 该内容不会按预期生效 (静默失效)。</summary>
        Error,
    }

    /// <summary>一条校验问题。<see cref="Code"/> 是稳定标识, 供自动化断言与忽略规则使用。</summary>
    public sealed class MotionValidationIssue
    {
        public MotionIssueSeverity Severity { get; init; }
        public string Code { get; init; } = "";
        public string Message { get; init; } = "";
        public string? TrackId { get; init; }
        public string? TrackLabel { get; init; }
        public double? TimeMs { get; init; }

        public override string ToString() =>
            $"[{Severity}] {Code}: {Message}"
            + (string.IsNullOrEmpty(TrackLabel) ? "" : $" (轨道: {TrackLabel})")
            + (TimeMs.HasValue ? $" @ {TimeMs.Value:F0}ms" : "");
    }

    /// <summary>校验可用的外部上下文 (可选注入; 不提供则跳过相应检查)。</summary>
    public sealed class MotionValidationOptions
    {
        /// <summary>判定设备当前是否已加载 (null = 跳过)。</summary>
        public Func<string, bool>? IsDeviceLoaded { get; init; }

        /// <summary>设备声明量程 (null = 跳过量程相关检查)。</summary>
        public Func<string, string, (float Min, float Max)?>? DeviceRange { get; init; }
    }

    /// <summary>
    /// 动作编排预检 — 在播放/交付前找出"会静默失效"的编排问题。
    ///
    /// <para>为什么必须工具化: 5 分钟以上的影片有几百个 clip, 人工不可能自查;
    /// 而这条链路上的失败几乎都是<b>静默</b>的 (遮蔽、钳位、量化、未绑定、设备未加载),
    /// 不报错、只是"值不对"。预检把这些变成明确的清单。</para>
    ///
    /// <para>纯函数, 无 IO/设备依赖 (设备与量程通过 <see cref="MotionValidationOptions"/> 注入),
    /// 因此可完整单测, 也可在 CLI/编辑器两处复用。</para>
    /// </summary>
    public static class MotionTimelineValidator
    {
        /// <summary>clip 与实例时长的容差 (毫秒)。</summary>
        private const double SpanToleranceMs = 1.0;

        public static IReadOnlyList<MotionValidationIssue> Validate(
            MotionTimeline? timeline,
            MotionValidationOptions? options = null
        )
        {
            var issues = new List<MotionValidationIssue>();
            if (timeline == null)
            {
                issues.Add(
                    new MotionValidationIssue
                    {
                        Severity = MotionIssueSeverity.Error,
                        Code = "timeline.null",
                        Message = "时间轴为空",
                    }
                );
                return issues;
            }

            options ??= new MotionValidationOptions();
            var tracks = timeline.Tracks ?? new List<MotionTrack>();

            ValidateTimelineScope(timeline, tracks, issues);
            foreach (var track in tracks)
                ValidateTrack(timeline, track, options, issues);
            ValidateInstances(timeline, tracks, issues);

            return issues;
        }

        /// <summary>是否含错误级问题 (可作为交付/播放门禁)。</summary>
        public static bool HasErrors(IReadOnlyList<MotionValidationIssue> issues) =>
            issues.Any(i => i.Severity == MotionIssueSeverity.Error);

        // ── 时间轴级 ──────────────────────────────────────

        private static void ValidateTimelineScope(
            MotionTimeline timeline,
            List<MotionTrack> tracks,
            List<MotionValidationIssue> issues
        )
        {
            if (tracks.Count == 0)
            {
                issues.Add(
                    new MotionValidationIssue
                    {
                        Severity = MotionIssueSeverity.Error,
                        Code = "timeline.no_tracks",
                        Message = "时间轴没有任何轨道, 播放不会产生任何输出",
                    }
                );
                return;
            }

            if (timeline.DurationMs <= 0)
            {
                issues.Add(
                    new MotionValidationIssue
                    {
                        Severity = MotionIssueSeverity.Error,
                        Code = "timeline.zero_duration",
                        Message = "总时长为 0, 播放不会产生任何输出",
                    }
                );
                return;
            }

            // 内容超出总时长 → 超出部分永远播不到 (长片最容易踩)
            double maxEnd = 0;
            foreach (var track in tracks)
            {
                foreach (var clip in track.Clips ?? new List<MotionClip>())
                    maxEnd = Math.Max(maxEnd, clip.EndMs);
            }
            foreach (var inst in timeline.ActionInstances ?? new List<ActionInstance>())
                maxEnd = Math.Max(maxEnd, inst.StartMs + inst.DurationMs);

            if (maxEnd > timeline.DurationMs + SpanToleranceMs)
            {
                issues.Add(
                    new MotionValidationIssue
                    {
                        Severity = MotionIssueSeverity.Error,
                        Code = "timeline.content_beyond_duration",
                        Message =
                            $"内容最远到 {maxEnd:F0}ms, 但总时长只有 {timeline.DurationMs:F0}ms, "
                            + "超出部分不会被播放",
                        TimeMs = timeline.DurationMs,
                    }
                );
            }
        }

        // ── 轨道级 ────────────────────────────────────────

        private static void ValidateTrack(
            MotionTimeline timeline,
            MotionTrack track,
            MotionValidationOptions options,
            List<MotionValidationIssue> issues
        )
        {
            string label = string.IsNullOrEmpty(track.Label) ? track.Id : track.Label;

            // 输出通道未配置 → 静默无输出
            bool isOAxis = string.Equals(track.OutputType, "oaxis", StringComparison.OrdinalIgnoreCase);
            if (isOAxis && string.IsNullOrEmpty(track.OAxisChannel))
            {
                issues.Add(New(issues, MotionIssueSeverity.Error, "track.no_oaxis_channel",
                    "OAxis 轨道未指定通道, 输出会被静默丢弃", track, label));
            }
            else if (!isOAxis && string.IsNullOrEmpty(track.OActionName))
            {
                issues.Add(New(issues, MotionIssueSeverity.Error, "track.no_oaction",
                    "OAction 轨道未指定输出名, 输出会被静默丢弃", track, label));
            }

            if (string.IsNullOrEmpty(track.DeviceName))
            {
                issues.Add(New(issues, MotionIssueSeverity.Error, "track.no_device",
                    "轨道未指定设备, 输出会被静默丢弃", track, label));
            }
            else if (options.IsDeviceLoaded is { } isLoaded && !isLoaded(track.DeviceName))
            {
                issues.Add(New(issues, MotionIssueSeverity.Error, "track.device_not_loaded",
                    $"设备 '{track.DeviceName}' 当前未加载, 该轨道不会输出", track, label));
            }

            if (track.Muted)
            {
                issues.Add(New(issues, MotionIssueSeverity.Warning, "track.muted",
                    "轨道处于静音状态, 播放时不会输出", track, label));
            }
            if (!track.Enabled)
            {
                issues.Add(New(issues, MotionIssueSeverity.Warning, "track.disabled",
                    "轨道已被禁用, 播放时不会输出", track, label));
            }

            var clips = track.Clips ?? new List<MotionClip>();
            bool hasIdle =
                track.IdleLoop is { Enabled: true, Keyframes.Count: > 0 };

            if (clips.Count == 0 && !hasIdle)
            {
                issues.Add(New(issues, MotionIssueSeverity.Info, "track.always_neutral",
                    "轨道没有 clip 也没有待机循环, 全程输出中性值", track, label));
            }

            ValidateClips(track, label, clips, issues);
        }

        private static void ValidateClips(
            MotionTrack track,
            string label,
            List<MotionClip> clips,
            List<MotionValidationIssue> issues
        )
        {
            // 区间非法
            foreach (var clip in clips)
            {
                if (clip.EndMs <= clip.StartMs)
                {
                    issues.Add(New(issues, MotionIssueSeverity.Error, "clip.invalid_range",
                        $"clip 区间非法 ({clip.StartMs:F0} → {clip.EndMs:F0}ms), 不会生效",
                        track, label, clip.StartMs));
                }

                if ((clip.Keyframes?.Count ?? 0) == 0)
                {
                    issues.Add(New(issues, MotionIssueSeverity.Warning, "clip.empty_keyframes",
                        $"clip ({clip.StartMs:F0}ms 起) 没有关键帧, 期间输出中性值",
                        track, label, clip.StartMs));
                }

                ValidateKeyframes(track, label, clip, issues);
            }

            // 重叠: 求值器取第一个命中的 clip, 后面的会被静默遮蔽
            var ordered = clips.OrderBy(c => c.StartMs).ToList();
            for (int i = 0; i + 1 < ordered.Count; i++)
            {
                var a = ordered[i];
                var b = ordered[i + 1];
                if (b.StartMs < a.EndMs - SpanToleranceMs)
                {
                    issues.Add(New(issues, MotionIssueSeverity.Warning, "clip.overlap",
                        $"clip 重叠 ({b.StartMs:F0}ms 起的 clip 会被 {a.StartMs:F0}ms 起的遮蔽)",
                        track, label, b.StartMs));
                }
            }
        }

        private static void ValidateKeyframes(
            MotionTrack track,
            string label,
            MotionClip clip,
            List<MotionValidationIssue> issues
        )
        {
            bool isBool = string.Equals(track.ValueType, "bool", StringComparison.OrdinalIgnoreCase);
            double clipSpan = clip.EndMs - clip.StartMs;
            double lastKfMs = double.NegativeInfinity;

            foreach (var kf in clip.Keyframes ?? new List<MotionKeyframe>())
            {
                // 乱序关键帧会被求值器退化为线性扫描 (结果不确定), 且曾触发就地排序改写文档
                if (kf.TimeMs < lastKfMs - SpanToleranceMs)
                {
                    issues.Add(New(issues, MotionIssueSeverity.Warning, "keyframe.unsorted",
                        $"关键帧时间未升序 ({lastKfMs:F0}ms 之后出现 {kf.TimeMs:F0}ms), 求值结果不可靠",
                        track, label, clip.StartMs + kf.TimeMs));
                }
                lastKfMs = Math.Max(lastKfMs, kf.TimeMs);

                // 关键帧落在 clip 区间外 → 不参与该 clip 的求值
                if (kf.TimeMs < -SpanToleranceMs || kf.TimeMs > clipSpan + SpanToleranceMs)
                {
                    issues.Add(New(issues, MotionIssueSeverity.Warning, "keyframe.out_of_clip",
                        $"关键帧 @{kf.TimeMs:F0}ms 超出 clip 区间 (0..{clipSpan:F0}ms), 不会被求值",
                        track, label, clip.StartMs + kf.TimeMs));
                }

                if (isBool)
                {
                    // bool 轨在 0.5 处量化, 非 0/1 的值含义不明确
                    if (Math.Abs(kf.Value) > 1e-4 && Math.Abs(kf.Value - 1f) > 1e-4)
                    {
                        issues.Add(New(issues, MotionIssueSeverity.Warning, "keyframe.bool_not_binary",
                            $"bool 轨道出现非 0/1 值 {kf.Value:F3}, 会被按 0.5 阈值量化",
                            track, label, clip.StartMs + kf.TimeMs));
                    }
                }
                else if (kf.Value < -1e-4f || kf.Value > 1f + 1e-4f)
                {
                    // 归一化值越界 → 求值器会静默钳位到 [0,1]
                    issues.Add(New(issues, MotionIssueSeverity.Error, "keyframe.out_of_normalized",
                        $"关键帧值 {kf.Value:F3} 超出归一化范围 0..1, 会被静默钳位",
                        track, label, clip.StartMs + kf.TimeMs));
                }
            }
        }

        // ── 动作实例级 ────────────────────────────────────

        private static void ValidateInstances(
            MotionTimeline timeline,
            List<MotionTrack> tracks,
            List<MotionValidationIssue> issues
        )
        {
            var instances = timeline.ActionInstances ?? new List<ActionInstance>();
            var presets = timeline.EmbeddedPresets ?? new List<EffectPreset>();
            var trackIds = new HashSet<string>(
                tracks.Select(t => t.Id).Where(id => !string.IsNullOrEmpty(id)),
                StringComparer.Ordinal
            );
            var allClips = tracks.SelectMany(t => t.Clips ?? new List<MotionClip>()).ToList();

            foreach (var inst in instances)
            {
                string label = string.IsNullOrEmpty(inst.Name) ? inst.Id : inst.Name;

                // 预设定义缺失 → 无法重建/追溯
                if (
                    !string.IsNullOrEmpty(inst.DefinitionId)
                    && !presets.Any(p =>
                        string.Equals(p.Id, inst.DefinitionId, StringComparison.Ordinal)
                    )
                )
                {
                    issues.Add(
                        new MotionValidationIssue
                        {
                            Severity = MotionIssueSeverity.Error,
                            Code = "instance.definition_missing",
                            Message =
                                $"动作实例 '{label}' 引用的预设 '{inst.DefinitionId}' "
                                + "不在项目内嵌快照里, 无法重建",
                            TimeMs = inst.StartMs,
                        }
                    );
                }

                // role 未绑定到存在的轨道 → 该角色静默不输出
                foreach (var (role, trackId) in inst.RoleTrackIds ?? new Dictionary<string, string>())
                {
                    if (!trackIds.Contains(trackId))
                    {
                        issues.Add(
                            new MotionValidationIssue
                            {
                                Severity = MotionIssueSeverity.Error,
                                Code = "instance.orphan_role",
                                Message =
                                    $"动作实例 '{label}' 的角色 '{role}' 绑定到不存在的轨道 "
                                    + $"'{trackId}', 该角色不会输出",
                                TimeMs = inst.StartMs,
                            }
                        );
                    }
                }

                // 实例时长与已物化 clip 跨度不一致 → 提示漂移
                var own = allClips.Where(c =>
                    string.Equals(c.ActionInstanceId, inst.Id, StringComparison.Ordinal)
                ).ToList();
                if (own.Count > 0)
                {
                    double minStart = own.Min(c => c.StartMs);
                    double maxEnd = own.Max(c => c.EndMs);
                    bool startMismatch = Math.Abs(minStart - inst.StartMs) > SpanToleranceMs;
                    bool spanMismatch =
                        Math.Abs((maxEnd - minStart) - inst.DurationMs) > SpanToleranceMs;
                    if (startMismatch || spanMismatch)
                    {
                        issues.Add(
                            new MotionValidationIssue
                            {
                                Severity = MotionIssueSeverity.Warning,
                                Code = "instance.clip_span_mismatch",
                                Message =
                                    $"动作实例 '{label}' 的时长 ({inst.StartMs:F0}+{inst.DurationMs:F0}ms) "
                                    + $"与已物化 clip ({minStart:F0}..{maxEnd:F0}ms) 不一致",
                                TimeMs = inst.StartMs,
                            }
                        );
                    }
                }
            }

            // clip 引用了不存在的实例 → 无法追溯来源
            var instanceIds = new HashSet<string>(
                instances.Select(i => i.Id),
                StringComparer.Ordinal
            );
            foreach (var track in tracks)
            {
                string label = string.IsNullOrEmpty(track.Label) ? track.Id : track.Label;
                foreach (var clip in track.Clips ?? new List<MotionClip>())
                {
                    if (
                        clip.IsGeneratedFromAction
                        && !instanceIds.Contains(clip.ActionInstanceId!)
                    )
                    {
                        issues.Add(New(issues, MotionIssueSeverity.Warning, "clip.instance_missing",
                            $"clip ({clip.StartMs:F0}ms 起) 引用的动作实例已不存在",
                            track, label, clip.StartMs));
                    }
                }
            }
        }

        private static MotionValidationIssue New(
            List<MotionValidationIssue> _,
            MotionIssueSeverity severity,
            string code,
            string message,
            MotionTrack track,
            string label,
            double? timeMs = null
        ) =>
            new()
            {
                Severity = severity,
                Code = code,
                Message = message,
                TrackId = track.Id,
                TrackLabel = label,
                TimeMs = timeMs,
            };
    }
}
