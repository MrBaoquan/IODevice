using System;
using System.Collections.Generic;
using System.Linq;
using IOStudio.Models.Motion;

namespace IOStudio.Services.Motion
{
    /// <summary>一条编排构建问题 (spec 级, 与 .motion 预检的 MotionValidationIssue 区分)。</summary>
    public sealed class ShowBuildIssue
    {
        public MotionIssueSeverity Severity { get; init; }
        public string Code { get; init; } = "";
        public string Message { get; init; } = "";

        public override string ToString() => $"[{Severity}] {Code}: {Message}";
    }

    /// <summary>构建结果。含 Error 时 <see cref="Timeline"/> 为 null (不产出半成品)。</summary>
    public sealed class ShowBuildResult
    {
        public MotionTimeline? Timeline { get; init; }
        public IReadOnlyList<ShowBuildIssue> Issues { get; init; } = Array.Empty<ShowBuildIssue>();
        public bool HasErrors => Issues.Any(i => i.Severity == MotionIssueSeverity.Error);
    }

    /// <summary>
    /// 演出规格 → .motion 构建器。
    ///
    /// <para>把 spec 的 "节拍表 + 预设 + 轨道" 展开成合法的 <see cref="MotionTimeline"/>:
    /// 物化 clip (按强度绕中性缩放)、生成 action_instance 并回填 source_*, 处理环境门控、
    /// 章节标记与事件, 并做同轨重叠/内容越界等**会静默失效**的检查。</para>
    ///
    /// <para>纯函数 (无 IO), 因此可完整单测; 文件读写由 CLI / 调用方负责。</para>
    /// </summary>
    public static class MotionShowBuilder
    {
        private static readonly string[] GroupPalette =
        {
            "#64748B", "#F97316", "#22C55E", "#38BDF8", "#C084FC", "#FB7185",
        };

        public static ShowBuildResult Build(ShowSpec spec)
        {
            var issues = new List<ShowBuildIssue>();

            if (spec is null)
                return Fail(issues, "spec.null", "spec 为空");

            if (string.IsNullOrWhiteSpace(spec.Device))
                issues.Add(Err("spec.no_device", "spec 未指定 device"));
            if (spec.DurationMs <= 0)
                issues.Add(Err("spec.zero_duration", "spec duration_ms 必须 > 0"));

            // ── 轨道 ──
            var tracks = new List<MotionTrack>();
            var roleToTrack = new Dictionary<string, MotionTrack>(StringComparer.Ordinal);
            foreach (var ts in spec.Tracks)
            {
                string id = string.IsNullOrWhiteSpace(ts.Id) ? $"trk_{ts.Role}" : ts.Id!;
                bool isOAxis = string.Equals(ts.OutputType, "oaxis", StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrWhiteSpace(ts.Role))
                    issues.Add(Err("track.no_role", $"轨道 '{id}' 未指定 role"));
                else if (roleToTrack.ContainsKey(ts.Role))
                    issues.Add(Err("track.duplicate_role", $"role '{ts.Role}' 被多条轨道使用"));
                if (isOAxis && string.IsNullOrWhiteSpace(ts.Channel))
                    issues.Add(Err("track.no_channel", $"轨道 '{id}' (oaxis) 未指定 channel"));
                if (!isOAxis && string.IsNullOrWhiteSpace(ts.OAction))
                    issues.Add(Err("track.no_oaction", $"轨道 '{id}' (oaction) 未指定 oaction"));

                var track = new MotionTrack
                {
                    Id = id,
                    DeviceName = spec.Device,
                    OutputType = ts.OutputType,
                    OAxisChannel = ts.Channel,
                    OActionName = ts.OAction,
                    Label = string.IsNullOrWhiteSpace(ts.Label) ? ts.Role : ts.Label,
                    Color = ts.Color,
                    ValueType = ts.ValueType,
                    Group = ts.Group,
                    Role = ts.Role,
                    NeutralValue = ts.Neutral,
                    EngineeringRange = ts.EngineeringRange,
                };
                if (ts.Idle is { } idleSpec && idleSpec.Keyframes.Count > 0)
                {
                    track.IdleLoop = new IdleLoop
                    {
                        Enabled = true,
                        PeriodMs = idleSpec.PeriodMs,
                        BlendMs = idleSpec.BlendMs,
                        PhaseMode = idleSpec.PhaseMode,
                        PhaseOffsetMs = idleSpec.PhaseOffsetMs,
                        GroupId = idleSpec.GroupId,
                        Keyframes = idleSpec.Keyframes,
                    };
                }
                tracks.Add(track);
                if (!string.IsNullOrWhiteSpace(ts.Role))
                    roleToTrack[ts.Role] = track;
            }

            // ── 预设 ──
            var presets = new List<EffectPreset>();
            var presetById = new Dictionary<string, ShowPresetSpec>(StringComparer.Ordinal);
            foreach (var ps in spec.Presets)
            {
                if (string.IsNullOrWhiteSpace(ps.Id))
                {
                    issues.Add(Err("preset.no_id", "存在未指定 id 的预设"));
                    continue;
                }
                if (presetById.ContainsKey(ps.Id))
                {
                    issues.Add(Err("preset.duplicate_id", $"预设 id '{ps.Id}' 重复"));
                    continue;
                }
                if (ps.DurationMs <= 0)
                    issues.Add(Err("preset.zero_duration", $"预设 '{ps.Id}' duration_ms 必须 > 0"));
                if (ps.Channels.Count == 0)
                    issues.Add(Err("preset.no_channels", $"预设 '{ps.Id}' 没有 channels"));
                presetById[ps.Id] = ps;
                presets.Add(new EffectPreset
                {
                    Id = ps.Id,
                    Revision = 1,
                    Name = ps.Name,
                    Category = ps.Category,
                    Description = ps.Description,
                    DurationMs = ps.DurationMs,
                    DefaultIntensity = ps.DefaultIntensity,
                    MinIntensity = ps.MinIntensity,
                    MaxIntensity = ps.MaxIntensity,
                    Channels = ps.Channels
                        .Select(c => new PresetChannel
                        {
                            Role = c.Role,
                            Scale = c.Scale,
                            PhaseOffsetMs = c.PhaseOffsetMs,
                            Keyframes = c.Keyframes,
                        })
                        .ToList(),
                });
            }

            // ── 节拍表 → 实例 + clip ──
            var clipBuckets = new Dictionary<string, List<(double Start, double End, MotionClip Clip)>>(
                StringComparer.Ordinal
            );
            foreach (var t in tracks)
                clipBuckets[t.Id] = new List<(double, double, MotionClip)>();

            var instances = new List<ActionInstance>();
            int cueIndex = 0;
            foreach (var cue in spec.Cues.OrderBy(c => c.TimeMs))
            {
                cueIndex++;
                if (!presetById.TryGetValue(cue.Preset, out var preset))
                {
                    issues.Add(Err("cue.unknown_preset", $"节拍 @{cue.TimeMs:F0}ms 引用未知预设 '{cue.Preset}'"));
                    continue;
                }

                double start = cue.TimeMs;
                double end = start + preset.DurationMs;
                string actId = $"act_{cueIndex:D2}_{preset.Id}";
                var roleTrackIds = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var ch in preset.Channels)
                {
                    if (!roleToTrack.TryGetValue(ch.Role, out var track))
                    {
                        issues.Add(
                            Err(
                                "cue.role_unmapped",
                                $"节拍 '{preset.Id}' 的 role '{ch.Role}' 没有对应轨道"
                            )
                        );
                        continue;
                    }
                    roleTrackIds[ch.Role] = track.Id;

                    float neutral = TrackValueEvaluator.ResolveNeutral(track.ValueType, track.NeutralValue);
                    double effScale = cue.Intensity * ch.Scale;
                    var kfs = ch.Keyframes
                        .Select(k => ScaleKeyframe(k, neutral, effScale))
                        .ToList();

                    var clip = new MotionClip
                    {
                        Id = $"clip_{cueIndex:D2}_{ch.Role}",
                        StartMs = start,
                        EndMs = end,
                        Keyframes = kfs,
                        ActionInstanceId = actId,
                        SourceDefinitionId = preset.Id,
                        SourceRole = ch.Role,
                        SourceActionName = preset.Name,
                    };
                    clipBuckets[track.Id].Add((start, end, clip));
                }

                instances.Add(new ActionInstance
                {
                    Id = actId,
                    DefinitionId = preset.Id,
                    DefinitionRevision = 1,
                    Name = preset.Name,
                    StartMs = start,
                    DurationMs = preset.DurationMs,
                    Intensity = cue.Intensity,
                    PlaybackRate = 1.0,
                    LoopCount = 1,
                    Enabled = true,
                    RoleTrackIds = roleTrackIds,
                });
            }

            // ── 环境门控 ──
            int gateIndex = 0;
            foreach (var gate in spec.Gates)
            {
                gateIndex++;
                if (!roleToTrack.TryGetValue(gate.Role, out var track))
                {
                    issues.Add(Err("gate.role_unmapped", $"门控 role '{gate.Role}' 没有对应轨道"));
                    continue;
                }
                if (gate.EndMs <= gate.StartMs)
                {
                    issues.Add(Err("gate.invalid_range", $"门控 '{gate.Role}' 区间非法 ({gate.StartMs:F0}→{gate.EndMs:F0})"));
                    continue;
                }
                double fade = Math.Max(0, gate.FadeMs);
                double span = gate.EndMs - gate.StartMs;
                if (fade * 2 > span)
                    fade = span / 2;

                var kfs = new List<MotionKeyframe>();
                if (fade > 0)
                {
                    kfs.Add(Kf(0, 0, "step"));
                    kfs.Add(Kf(fade, gate.Level, "ease_in_out"));
                    kfs.Add(Kf(span - fade, gate.Level, "ease_in_out"));
                    kfs.Add(Kf(span, 0, "ease_in_out"));
                }
                else
                {
                    kfs.Add(Kf(0, gate.Level, "step"));
                    kfs.Add(Kf(span, gate.Level, "step"));
                }
                var clip = new MotionClip
                {
                    Id = $"clip_gate_{gateIndex:D2}_{gate.Role}",
                    StartMs = gate.StartMs,
                    EndMs = gate.EndMs,
                    Keyframes = kfs,
                };
                clipBuckets[track.Id].Add((gate.StartMs, gate.EndMs, clip));
            }

            // ── 同轨重叠 (会被求值器静默遮蔽) ──
            foreach (var t in tracks)
            {
                var ordered = clipBuckets[t.Id].OrderBy(c => c.Start).ToList();
                for (int i = 0; i + 1 < ordered.Count; i++)
                {
                    if (ordered[i + 1].Start < ordered[i].End - 1.0)
                        issues.Add(
                            Err(
                                "clip.overlap",
                                $"轨道 '{t.Role}' 上 clip 重叠 ({ordered[i].Start:F0}..{ordered[i].End:F0} 与 "
                                    + $"{ordered[i + 1].Start:F0}..{ordered[i + 1].End:F0})"
                            )
                        );
                }
                t.Clips = ordered.Select(c => c.Clip).ToList();
            }

            // ── 内容越界 ──
            double maxEnd = tracks.SelectMany(t => t.Clips).Select(c => c.EndMs).DefaultIfEmpty(0).Max();
            if (maxEnd > spec.DurationMs + 1.0)
                issues.Add(
                    Err(
                        "spec.content_beyond_duration",
                        $"内容最远到 {maxEnd:F0}ms, 但 duration_ms={spec.DurationMs:F0}ms, 超出部分播不到"
                    )
                );

            if (issues.Any(i => i.Severity == MotionIssueSeverity.Error))
                return new ShowBuildResult { Timeline = null, Issues = issues };

            // ── 章节 / 事件 / 分组 / 绑定 ──
            var markers = spec.Chapters
                .Select(c => new TimelineMarker
                {
                    TimeMs = c.TimeMs,
                    Name = c.Name,
                    Note = c.Note,
                    Color = "#94A3B8",
                    LaneId = "chapters",
                })
                .ToList();

            var events = spec.Events
                .Select(e => new TimelineEvent
                {
                    TimeMs = e.TimeMs,
                    EventName = e.Name,
                    EventData = e.Data,
                    Color = e.Color,
                    DataType = e.DataType,
                    LaneId = string.IsNullOrWhiteSpace(e.Lane) ? "default" : e.Lane,
                })
                .ToList();

            var groups = new List<TrackGroup>();
            int gi = 0;
            foreach (var name in tracks.Select(t => t.Group).Where(g => !string.IsNullOrWhiteSpace(g)).Distinct())
            {
                groups.Add(new TrackGroup
                {
                    Name = name,
                    Color = GroupPalette[gi % GroupPalette.Length],
                    Collapsed = false,
                });
                gi++;
            }

            var eventLanes = events
                .Select(e => e.LaneId)
                .Distinct()
                .Select(id => new EventLane { Id = id, Name = id, Color = "#38BDF8" })
                .ToList();
            var markerLanes = markers
                .Select(m => m.LaneId)
                .Distinct()
                .Select(id => new MarkerLane { Id = id, Name = "章节", Color = "#94A3B8" })
                .ToList();

            var timeline = new MotionTimeline
            {
                Version = "3.0",
                Name = spec.Name,
                DurationMs = spec.DurationMs,
                Fps = spec.Fps,
                Tracks = tracks,
                ActionInstances = instances,
                RoleTrackBindings = roleToTrack.ToDictionary(kv => kv.Key, kv => kv.Value.Id, StringComparer.Ordinal),
                EmbeddedPresets = presets,
                Groups = groups,
                Events = events,
                Markers = markers,
                EventLanes = eventLanes,
                MarkerLanes = markerLanes,
            };
            return new ShowBuildResult { Timeline = timeline, Issues = issues };
        }

        // ── helpers ──

        private static MotionKeyframe ScaleKeyframe(MotionKeyframe src, float neutral, double scale)
        {
            float v = (float)Math.Clamp(neutral + (src.Value - neutral) * scale, 0.0, 1.0);
            return new MotionKeyframe
            {
                TimeMs = src.TimeMs,
                Value = (float)Math.Round(v, 4),
                Interpolation = src.Interpolation,
                TangentIn = src.TangentIn,
                TangentOut = src.TangentOut,
                Cp1x = src.Cp1x,
                Cp2x = src.Cp2x,
                Event = src.Event,
            };
        }

        private static MotionKeyframe Kf(double t, double v, string interp) =>
            new() { TimeMs = t, Value = (float)Math.Round(v, 4), Interpolation = interp };

        private static ShowBuildIssue Err(string code, string message) =>
            new() { Severity = MotionIssueSeverity.Error, Code = code, Message = message };

        private static ShowBuildResult Fail(List<ShowBuildIssue> issues, string code, string message)
        {
            issues.Add(Err(code, message));
            return new ShowBuildResult { Timeline = null, Issues = issues };
        }
    }
}
