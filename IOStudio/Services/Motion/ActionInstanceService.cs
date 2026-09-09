using System;
using System.Collections.Generic;
using System.Linq;
using IOStudio.Models.Motion;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 影片动作实例服务。负责把多通道预设作为一个原子动作放入时间轴，
    /// 并生成现有播放器可直接消费的 MotionClip。
    /// </summary>
    public static class ActionInstanceService
    {
        /// <summary>动作实例边界调整时允许的最小时长 (毫秒), 防止拖到零宽/负宽。</summary>
        private const double MinActionDurationMs = 10;

        public static ActionPlacementResult PlacePreset(
            MotionTimeline timeline,
            EffectPreset preset,
            PresetApplyOptions options,
            double startMs,
            string? targetTrackId = null,
            IReadOnlyDictionary<string, string?>? roleToTrackOverride = null
        )
        {
            if (timeline == null)
                return ActionPlacementResult.Fail("时间轴不可用");
            if (
                preset == null
                || (
                    preset.Keyframes is not { Count: > 0 }
                    && (preset.Channels == null || preset.Channels.Count == 0)
                )
            )
                return ActionPlacementResult.Fail("预设不包含可用关键帧");

            timeline.ActionInstances ??= new List<ActionInstance>();
            timeline.Tracks ??= new List<MotionTrack>();
            timeline.RoleTrackBindings ??= new Dictionary<string, string>();
            timeline.EmbeddedPresets ??= new List<EffectPreset>();

            var generated = PresetParameterApplier.Apply(preset, options);
            if (generated.Clips.Count == 0)
                return ActionPlacementResult.Fail("预设没有生成任何动作片段");

            var instance = new ActionInstance
            {
                DefinitionId = preset.Id,
                DefinitionRevision = Math.Max(1, preset.Revision),
                Name = preset.Name,
                StartMs = Math.Max(0, startMs),
                DurationMs = generated.DurationMs,
                Intensity = options.Intensity,
                PlaybackRate = options.Speed > 0 ? options.Speed : 1.0,
            };

            var targets = new List<(GeneratedClip generated, MotionTrack track, bool isNew)>();
            var newTracks = new List<MotionTrack>();
            foreach (var generatedClip in generated.Clips)
            {
                var role = generatedClip.Role.Trim();
                MotionTrack? match = null;

                // 用户显式映射 (来自匹配确认对话框) 优先
                if (
                    roleToTrackOverride != null
                    && roleToTrackOverride.TryGetValue(role, out var overrideTrackId)
                )
                {
                    if (!string.IsNullOrWhiteSpace(overrideTrackId))
                    {
                        match = timeline.Tracks.FirstOrDefault(t => t.Id == overrideTrackId);
                        if (match == null)
                        {
                            return ActionPlacementResult.Fail($"映射的目标轨道不存在: {overrideTrackId}");
                        }
                    }
                    else
                    {
                        // null/空 = 新建轨道
                        match = CreateUnboundRoleTrack(role);
                        newTracks.Add(match);
                    }
                }
                else if (
                    string.IsNullOrWhiteSpace(role) && !string.IsNullOrWhiteSpace(targetTrackId)
                )
                    match = timeline.Tracks.FirstOrDefault(t => t.Id == targetTrackId);

                if (match == null && string.IsNullOrWhiteSpace(role))
                    return ActionPlacementResult.Fail("请将无通道预设拖到一条目标轨道");

                match ??=
                    ResolveTrack(timeline, role)
                    ?? newTracks.FirstOrDefault(
                        t => string.Equals(t.Role, role, StringComparison.OrdinalIgnoreCase)
                    );
                if (match == null)
                {
                    match = CreateUnboundRoleTrack(role);
                    newTracks.Add(match);
                }
                targets.Add((generatedClip, match, newTracks.Contains(match)));
                if (!string.IsNullOrWhiteSpace(role))
                    instance.RoleTrackIds[role] = match.Id;
            }

            if (targets.Count == 0)
                return ActionPlacementResult.Fail("预设没有可绑定的逻辑通道");

            var lockedTarget = targets.Select(target => target.track).FirstOrDefault(IsLocked);
            if (lockedTarget != null)
                return LockedTrackFailure(lockedTarget, "插入动作");

            // 所有冲突必须在修改模型前发现，避免半应用状态。
            foreach (var target in targets)
            {
                double endMs = instance.StartMs + target.generated.Clip.EndMs;
                var conflicts = target.track.Clips
                    .Where(c => RangesOverlap(instance.StartMs, endMs, c.StartMs, c.EndMs))
                    .Where(c => !IsNeutralPlaceholder(c, target.track.ValueType))
                    .ToList();
                if (conflicts.Count > 0)
                {
                    return ActionPlacementResult.Fail(
                        $"轨道“{target.track.Label}”在目标时间已有内容，请先移动动作或清理重叠片段"
                    );
                }
            }

            foreach (var track in newTracks)
                timeline.Tracks.Add(track);

            var addedClips = new List<MotionClip>();
            foreach (var target in targets)
            {
                target.track.Clips.RemoveAll(
                    c =>
                        RangesOverlap(
                            instance.StartMs,
                            instance.StartMs + target.generated.Clip.EndMs,
                            c.StartMs,
                            c.EndMs
                        ) && IsNeutralPlaceholder(c, target.track.ValueType)
                );

                var clip = target.generated.Clip;
                clip.StartMs += instance.StartMs;
                clip.EndMs += instance.StartMs;
                clip.ActionInstanceId = instance.Id;
                clip.SourceDefinitionId = preset.Id;
                clip.SourceRole = target.generated.Role;
                clip.SourceActionName = preset.Name;
                // 值类型一致性: bool 轨落轨前把关键帧二值化并强制 step 插值
                NormalizeClipToTrackValueType(target.track, clip);
                target.track.Clips.Add(clip);
                target.track.Clips.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
                addedClips.Add(clip);

                if (!string.IsNullOrWhiteSpace(target.generated.Role))
                    timeline.RoleTrackBindings[target.generated.Role] = target.track.Id;
            }

            timeline.ActionInstances.Add(instance);
            UpsertEmbeddedPreset(timeline, preset);
            timeline.Version = "3.0";
            timeline.DurationMs = Math.Max(
                timeline.DurationMs,
                instance.StartMs + instance.DurationMs
            );

            return new ActionPlacementResult
            {
                Success = true,
                Instance = instance,
                AddedTracks = newTracks,
                AddedClips = addedClips,
            };
        }

        /// <summary>依据项目中固定的预设快照重建一个实例的全部 Clip。</summary>
        public static ActionPlacementResult RebuildInstance(
            MotionTimeline timeline,
            string instanceId
        )
        {
            var instances = timeline.ActionInstances ??= new List<ActionInstance>();
            timeline.Tracks ??= new List<MotionTrack>();
            var instance = instances.FirstOrDefault(i => i.Id == instanceId);
            if (instance == null)
                return ActionPlacementResult.Fail("动作实例不存在");
            var preset = timeline.EmbeddedPresets?.FirstOrDefault(
                p => p.Id == instance.DefinitionId && p.Revision == instance.DefinitionRevision
            );
            if (preset == null)
                return ActionPlacementResult.Fail("项目中缺少动作定义快照");

            var originalClips = timeline.Tracks
                .Select(
                    t =>
                        (
                            Track: t,
                            Clips: t.Clips.Where(c => c.ActionInstanceId == instanceId).ToList()
                        )
                )
                .Where(x => x.Clips.Count > 0)
                .ToList();
            var lockedTrack = originalClips.Select(item => item.Track).FirstOrDefault(IsLocked);
            if (lockedTrack != null)
                return LockedTrackFailure(lockedTrack, "重建动作");
            int originalIndex = instances.IndexOf(instance);

            foreach (var item in originalClips)
                item.Track.Clips.RemoveAll(c => c.ActionInstanceId == instanceId);
            instances.Remove(instance);

            var rebuilt = PlacePreset(
                timeline,
                preset,
                new PresetApplyOptions
                {
                    Intensity = instance.Intensity,
                    // DurationMs 已是最终时长，重建时不再重复应用播放速率。
                    Speed = 1.0,
                    TargetDurationMs = instance.DurationMs,
                },
                instance.StartMs,
                ResolveGenericTargetTrackId(preset, originalClips.Select(x => x.Track))
            );
            if (!rebuilt.Success || rebuilt.Instance == null)
            {
                foreach (var item in originalClips)
                {
                    item.Track.Clips.AddRange(item.Clips);
                    item.Track.Clips.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
                }
                instances.Insert(Math.Max(0, originalIndex), instance);
                return rebuilt;
            }

            if (rebuilt.Instance != null)
            {
                string generatedId = rebuilt.Instance.Id;
                rebuilt.Instance.Id = instanceId;
                rebuilt.Instance.Name = instance.Name;
                rebuilt.Instance.DefinitionRevision = instance.DefinitionRevision;
                rebuilt.Instance.PlaybackRate = instance.PlaybackRate;
                rebuilt.Instance.LoopCount = instance.LoopCount;
                rebuilt.Instance.Enabled = instance.Enabled;
                foreach (
                    var clip in rebuilt.AddedClips.Where(c => c.ActionInstanceId == generatedId)
                )
                    clip.ActionInstanceId = instanceId;
            }
            return rebuilt;
        }

        /// <summary>在新时间位置创建同一动作定义的独立实例。</summary>
        public static ActionPlacementResult DuplicateInstance(
            MotionTimeline timeline,
            string instanceId,
            double newStartMs
        )
        {
            var source = timeline.ActionInstances?.FirstOrDefault(i => i.Id == instanceId);
            if (source == null)
                return ActionPlacementResult.Fail("动作实例不存在");
            var preset = timeline.EmbeddedPresets?.FirstOrDefault(
                p => p.Id == source.DefinitionId && p.Revision == source.DefinitionRevision
            );
            if (preset == null)
                return ActionPlacementResult.Fail("项目中缺少动作定义快照");

            var duplicated = PlacePreset(
                timeline,
                preset,
                new PresetApplyOptions
                {
                    Intensity = source.Intensity,
                    Speed = 1.0,
                    TargetDurationMs = source.DurationMs,
                },
                newStartMs,
                ResolveGenericTargetTrackId(
                    preset,
                    timeline.Tracks.Where(t => t.Clips.Any(c => c.ActionInstanceId == instanceId))
                )
            );
            if (duplicated.Success && duplicated.Instance != null)
            {
                duplicated.Instance.Name = source.Name;
                duplicated.Instance.PlaybackRate = source.PlaybackRate;
                duplicated.Instance.LoopCount = source.LoopCount;
                duplicated.Instance.Enabled = source.Enabled;
            }
            return duplicated;
        }

        /// <summary>将动作实例及其所有通道 Clip 整体移动到新时间。</summary>
        public static ActionPlacementResult MoveInstance(
            MotionTimeline timeline,
            string instanceId,
            double newStartMs
        )
        {
            var instance = timeline.ActionInstances?.FirstOrDefault(i => i.Id == instanceId);
            if (instance == null)
                return ActionPlacementResult.Fail("动作实例不存在");

            var linked = timeline.Tracks
                .SelectMany(
                    t =>
                        t.Clips
                            .Where(c => c.ActionInstanceId == instanceId)
                            .Select(c => (Track: t, Clip: c))
                )
                .ToList();
            if (linked.Count == 0)
                return ActionPlacementResult.Fail("动作实例没有关联片段");
            var lockedTrack = linked.Select(item => item.Track).FirstOrDefault(IsLocked);
            if (lockedTrack != null)
                return LockedTrackFailure(lockedTrack, "移动动作");

            double targetStart = Math.Max(0, newStartMs);
            double delta = targetStart - instance.StartMs;
            foreach (var item in linked)
            {
                double targetClipStart = item.Clip.StartMs + delta;
                double targetClipEnd = item.Clip.EndMs + delta;
                bool conflict = item.Track.Clips
                    .Where(c => c.ActionInstanceId != instanceId)
                    .Where(c => !IsNeutralPlaceholder(c, item.Track.ValueType))
                    .Any(c => RangesOverlap(targetClipStart, targetClipEnd, c.StartMs, c.EndMs));
                if (conflict)
                {
                    return ActionPlacementResult.Fail($"轨道“{item.Track.Label}”在目标时间已有内容，动作未移动");
                }
            }

            foreach (var item in linked)
            {
                double targetClipStart = item.Clip.StartMs + delta;
                double targetClipEnd = item.Clip.EndMs + delta;
                item.Track.Clips.RemoveAll(
                    c =>
                        c.ActionInstanceId != instanceId
                        && IsNeutralPlaceholder(c, item.Track.ValueType)
                        && RangesOverlap(targetClipStart, targetClipEnd, c.StartMs, c.EndMs)
                );
                item.Clip.StartMs = targetClipStart;
                item.Clip.EndMs = targetClipEnd;
                item.Track.Clips.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
            }

            instance.StartMs = targetStart;
            timeline.DurationMs = Math.Max(timeline.DurationMs, targetStart + instance.DurationMs);
            return new ActionPlacementResult
            {
                Success = true,
                Instance = instance,
                AddedClips = linked.Select(x => x.Clip).ToList(),
            };
        }

        /// <summary>
        /// 调整动作实例边界（剪裁）：拖左边界=移动 in 点（右边界不动）, 拖右边界=改时长（左边界不动）。
        /// 关键帧保持时间轴绝对位置——拖左边界时相对剪辑起点重算（左侧被裁/左侧留白, 视觉上是区域的拉伸而非整体平移）,
        /// 拖右边界时相对时间不变（缩短时裁掉尾部）。带冲突检测。
        /// </summary>
        public static ActionPlacementResult ResizeInstance(
            MotionTimeline timeline,
            string instanceId,
            double deltaStartMs,
            double deltaEndMs
        )
        {
            var instance = timeline.ActionInstances?.FirstOrDefault(i => i.Id == instanceId);
            if (instance == null)
                return ActionPlacementResult.Fail("动作实例不存在");

            var linked = timeline.Tracks
                .SelectMany(
                    t =>
                        t.Clips
                            .Where(c => c.ActionInstanceId == instanceId)
                            .Select(c => (Track: t, Clip: c))
                )
                .ToList();
            if (linked.Count == 0)
                return ActionPlacementResult.Fail("动作实例没有关联片段");
            var lockedTrack = linked.Select(item => item.Track).FirstOrDefault(IsLocked);
            if (lockedTrack != null)
                return LockedTrackFailure(lockedTrack, "调整动作边界");

            double oldStart = instance.StartMs;
            double oldEnd = instance.StartMs + instance.DurationMs;
            double newStart = Math.Max(0, oldStart + deltaStartMs);
            double newEnd = oldEnd + deltaEndMs;
            if (newEnd < newStart + MinActionDurationMs)
                return ActionPlacementResult.Fail("压缩后动作过短");

            double dStart = newStart - oldStart;
            double dEnd = newEnd - oldEnd;

            // 冲突检查: 调整后的范围不得压住其它实例/普通 Clip。
            foreach (var item in linked)
            {
                double newClipStart = item.Clip.StartMs + dStart;
                double newClipEnd = item.Clip.EndMs + dEnd;
                bool conflict = item.Track.Clips
                    .Where(c => c.ActionInstanceId != instanceId)
                    .Where(c => !IsNeutralPlaceholder(c, item.Track.ValueType))
                    .Any(c => RangesOverlap(newClipStart, newClipEnd, c.StartMs, c.EndMs));
                if (conflict)
                {
                    return ActionPlacementResult.Fail($"轨道“{item.Track.Label}”在目标范围已有内容，边界未调整");
                }
            }

            foreach (var item in linked)
            {
                double oldDur = item.Clip.EndMs - item.Clip.StartMs;
                double newClipStart = item.Clip.StartMs + dStart;
                double newClipEnd = item.Clip.EndMs + dEnd;
                double newDur = newClipEnd - newClipStart;

                item.Clip.StartMs = newClipStart;
                item.Clip.EndMs = newClipEnd;
                // 关键帧按比例同步缩放（默认行为），不删除任何关键帧。
                if (oldDur > 0 && newDur > 0 && Math.Abs(newDur - oldDur) > 0.01)
                {
                    double scale = newDur / oldDur;
                    foreach (var kf in item.Clip.Keyframes)
                        kf.TimeMs *= scale;
                }
                item.Track.Clips.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
            }

            instance.StartMs = newStart;
            instance.DurationMs = newEnd - newStart;
            timeline.DurationMs = Math.Max(timeline.DurationMs, newEnd);
            return new ActionPlacementResult
            {
                Success = true,
                Instance = instance,
                AddedClips = linked.Select(x => x.Clip).ToList(),
            };
        }

        /// <summary>删除动作实例及其全部烘焙 Clip。</summary>
        public static ActionPlacementResult DeleteInstance(
            MotionTimeline timeline,
            string instanceId
        )
        {
            var instances = timeline.ActionInstances ??= new List<ActionInstance>();
            timeline.Tracks ??= new List<MotionTrack>();
            var instance = instances.FirstOrDefault(i => i.Id == instanceId);
            if (instance == null)
                return ActionPlacementResult.Fail("动作实例不存在");

            var lockedTrack = timeline.Tracks.FirstOrDefault(
                track => IsLocked(track) && track.Clips.Any(c => c.ActionInstanceId == instanceId)
            );
            if (lockedTrack != null)
                return LockedTrackFailure(lockedTrack, "删除动作");

            var removed = new List<MotionClip>();
            foreach (var track in timeline.Tracks)
            {
                removed.AddRange(track.Clips.Where(c => c.ActionInstanceId == instanceId));
                track.Clips.RemoveAll(c => c.ActionInstanceId == instanceId);
            }
            instances.Remove(instance);
            return new ActionPlacementResult
            {
                Success = true,
                Instance = instance,
                AddedClips = removed,
            };
        }

        /// <summary>从单个动作实例创建局部时间归一化的独立预设。</summary>
        public static EffectPreset? CreatePresetFromInstance(
            MotionTimeline timeline,
            string instanceId,
            string name,
            string category = "用户自定义"
        )
        {
            var instance = timeline.ActionInstances?.FirstOrDefault(i => i.Id == instanceId);
            if (instance == null || string.IsNullOrWhiteSpace(name))
                return null;

            var linked = timeline.Tracks
                .SelectMany(
                    t =>
                        t.Clips
                            .Where(c => c.ActionInstanceId == instanceId)
                            .Select(c => (Track: t, Clip: c))
                )
                .ToList();
            if (linked.Count == 0)
                return null;

            double minStart = linked.Min(x => x.Clip.StartMs);
            double maxEnd = linked.Max(x => x.Clip.EndMs);
            var preset = new EffectPreset
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Revision = 1,
                Name = name.Trim(),
                Category = string.IsNullOrWhiteSpace(category) ? "用户自定义" : category,
                DurationMs = Math.Max(0, maxEnd - minStart),
                DefaultIntensity = 1f,
                MinIntensity = 0.2f,
                MaxIntensity = 1.2f,
            };

            if (linked.Count == 1)
            {
                var item = linked[0];
                preset.Keyframes = item.Clip.Keyframes
                    .Select(k =>
                    {
                        var clone = CloneKeyframe(k);
                        clone.TimeMs += item.Clip.StartMs - minStart;
                        return clone;
                    })
                    .ToList();
                return preset;
            }

            foreach (var item in linked)
            {
                string role = item.Clip.SourceRole ?? item.Track.Role;
                if (string.IsNullOrWhiteSpace(role))
                    continue;
                preset.Channels.Add(
                    new PresetChannel
                    {
                        Role = role,
                        Scale = 1f,
                        Keyframes = item.Clip.Keyframes
                            .Select(k =>
                            {
                                var clone = CloneKeyframe(k);
                                clone.TimeMs += item.Clip.StartMs - minStart;
                                return clone;
                            })
                            .ToList(),
                    }
                );
            }

            return preset.Channels.Count > 0 ? preset : null;
        }

        private static string? ResolveGenericTargetTrackId(
            EffectPreset preset,
            IEnumerable<MotionTrack> linkedTracks
        )
        {
            if (preset.Keyframes is not { Count: > 0 })
                return null;
            return linkedTracks.Select(t => t.Id).FirstOrDefault();
        }

        private static MotionTrack? ResolveTrack(MotionTimeline timeline, string role)
        {
            var binding = timeline.RoleTrackBindings.FirstOrDefault(
                pair => string.Equals(pair.Key, role, StringComparison.OrdinalIgnoreCase)
            );
            if (!string.IsNullOrEmpty(binding.Key))
            {
                var bound = timeline.Tracks.FirstOrDefault(t => t.Id == binding.Value);
                if (bound != null)
                    return bound;
            }

            var matches = timeline.Tracks
                .Where(t => string.Equals(t.Role, role, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        private static MotionTrack CreateUnboundRoleTrack(string role)
        {
            // 多通道预设落轨: 尝试把"逻辑角色"自动绑定到已加载设备的 OAction/OAxis 通道,
            // 避免生成空壳轨道 (此前需用户手动配置设备/通道)。
            var matched = MatchDeviceChannelForRole(role);
            if (matched != null)
            {
                return new MotionTrack
                {
                    DeviceName = matched.Value.DeviceName,
                    OActionName = matched.Value.OActionName ?? "",
                    OutputType = matched.Value.IsOAxis ? "oaxis" : "oaction",
                    OAxisChannel = matched.Value.OAxisChannel ?? "",
                    Label = role,
                    Role = role,
                    Color = "#64748b",
                    ValueType = "float",
                    Clips = new List<MotionClip>(),
                };
            }

            return new MotionTrack
            {
                DeviceName = "",
                OActionName = "",
                OutputType = "oaxis",
                OAxisChannel = "",
                Label = role,
                Role = role,
                Color = "#64748b",
                ValueType = "float",
                Clips = new List<MotionClip>(),
            };
        }

        /// <summary>
        /// 用角色名启发式匹配已加载设备的 OAction / OAxis 通道, 实现"角色→物理通道"自动绑定。
        /// 匹配顺序: OAction 名 → OAction 标签 → OAxis 通道名 (大小写不敏感)。
        /// </summary>
        private static (
            string DeviceName,
            string? OActionName,
            string? OAxisChannel,
            bool IsOAxis
        )? MatchDeviceChannelForRole(string role)
        {
            if (string.IsNullOrWhiteSpace(role))
                return null;
            try
            {
                foreach (var dev in new DeviceSchemaService().LoadDevices())
                {
                    foreach (var oa in dev.OActions)
                    {
                        if (
                            string.Equals(oa.OActionName, role, StringComparison.OrdinalIgnoreCase)
                            || (
                                !string.IsNullOrEmpty(oa.Label)
                                && string.Equals(oa.Label, role, StringComparison.OrdinalIgnoreCase)
                            )
                        )
                        {
                            return (dev.DeviceName, oa.OActionName, null, false);
                        }
                    }
                    foreach (var ch in dev.OAxisChannels)
                    {
                        if (string.Equals(ch.ChannelName, role, StringComparison.OrdinalIgnoreCase))
                        {
                            return (dev.DeviceName, null, ch.ChannelName, true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ActionInstanceService] 设备通道匹配失败: {ex.Message}"
                );
            }
            return null;
        }

        private static bool RangesOverlap(double aStart, double aEnd, double bStart, double bEnd) =>
            aStart < bEnd && bStart < aEnd;

        private static bool IsNeutralPlaceholder(MotionClip clip, string valueType)
        {
            if (clip.IsGeneratedFromAction || clip.Keyframes.Count == 0 || clip.Keyframes.Count > 2)
                return false;
            float neutral = string.Equals(valueType, "bool", StringComparison.OrdinalIgnoreCase)
                ? 0f
                : 0.5f;
            return clip.StartMs <= 0.001
                && clip.Keyframes.All(k => Math.Abs(k.Value - neutral) < 0.0001f);
        }

        /// <summary>
        /// 值类型一致性: 目标轨道为 bool 时, 把关键帧二值化 (>=0.5→1) 并强制 step 插值,
        /// 避免浮点曲线落入开关量轨道造成抖动。
        /// </summary>
        private static void NormalizeClipToTrackValueType(MotionTrack track, MotionClip clip)
        {
            if (
                track == null
                || clip == null
                || !string.Equals(track.ValueType, "bool", StringComparison.OrdinalIgnoreCase)
            )
                return;
            foreach (var kf in clip.Keyframes)
            {
                float v = kf.Value >= 0.5f ? 1f : 0f;
                if (Math.Abs(kf.Value - v) > 0.0001f)
                    kf.Value = v;
                kf.Interpolation = "step";
            }
        }

        private static void UpsertEmbeddedPreset(MotionTimeline timeline, EffectPreset preset)
        {
            int index = timeline.EmbeddedPresets.FindIndex(
                p => p.Id == preset.Id && p.Revision == preset.Revision
            );
            var snapshot = ClonePreset(preset);
            if (index >= 0)
                timeline.EmbeddedPresets[index] = snapshot;
            else
                timeline.EmbeddedPresets.Add(snapshot);
        }

        private static EffectPreset ClonePreset(EffectPreset source) =>
            new()
            {
                Id = source.Id,
                Revision = source.Revision,
                Name = source.Name,
                Category = source.Category,
                Description = source.Description,
                DurationMs = source.DurationMs,
                DefaultIntensity = source.DefaultIntensity,
                MinIntensity = source.MinIntensity,
                MaxIntensity = source.MaxIntensity,
                BaseId = source.BaseId,
                Channels = source.Channels.Select(CloneChannel).ToList(),
                Keyframes = source.Keyframes?.Select(CloneKeyframe).ToList(),
            };

        /// <summary>
        /// 读取动作实例跨轨的剪辑（按轨道顺序），供曲线编辑器载入。
        /// 每个剪辑一条曲线，关键帧时间轴相对剪辑起点（与 .motion 存储一致）。
        /// </summary>
        public static List<(string Role, MotionClip Clip)> GetInstanceClips(
            MotionTimeline timeline,
            string instanceId
        )
        {
            if (timeline?.Tracks == null || string.IsNullOrEmpty(instanceId))
                return new();
            return timeline.Tracks
                .SelectMany(t => t.Clips)
                .Where(c => string.Equals(c.ActionInstanceId, instanceId, StringComparison.Ordinal))
                .Select(c => (Role: c.SourceRole ?? "", Clip: c))
                .ToList();
        }

        /// <summary>
        /// 用编辑后的通道曲线覆盖动作实例各剪辑的关键帧（剪辑起点保持不变, 时长随最后关键帧收缩/扩展）。
        /// 调用方负责快照撤销。
        /// </summary>
        public static ActionPlacementResult ReplaceInstanceKeyframes(
            MotionTimeline timeline,
            string instanceId,
            IReadOnlyList<PresetChannel> channels
        )
        {
            var clips = GetInstanceClips(timeline, instanceId);
            if (clips.Count == 0)
                return ActionPlacementResult.Fail("未找到该动作实例对应的剪辑");
            if (channels == null || channels.Count == 0)
                return ActionPlacementResult.Fail("编辑结果不包含曲线数据");
            var lockedTrack = timeline.Tracks.FirstOrDefault(
                track => IsLocked(track) && track.Clips.Any(c => c.ActionInstanceId == instanceId)
            );
            if (lockedTrack != null)
                return LockedTrackFailure(lockedTrack, "编辑动作曲线");

            for (var i = 0; i < clips.Count && i < channels.Count; i++)
            {
                var clip = clips[i].Clip;
                clip.Keyframes =
                    channels[i].Keyframes?.Select(CloneKeyframe).ToList()
                    ?? new List<MotionKeyframe>();

                if (clip.Keyframes.Count > 0)
                    clip.EndMs = clip.StartMs + clip.Keyframes.Max(k => k.TimeMs);
            }
            return new ActionPlacementResult { Success = true };
        }

        private static PresetChannel CloneChannel(PresetChannel source) =>
            new()
            {
                Role = source.Role,
                Scale = source.Scale,
                PhaseOffsetMs = source.PhaseOffsetMs,
                TemplateRef = source.TemplateRef,
                Keyframes = source.Keyframes?.Select(CloneKeyframe).ToList(),
            };

        private static MotionKeyframe CloneKeyframe(MotionKeyframe source) =>
            new()
            {
                TimeMs = source.TimeMs,
                Value = source.Value,
                Interpolation = source.Interpolation,
                TangentIn = source.TangentIn,
                TangentOut = source.TangentOut,
                Cp1x = source.Cp1x,
                Cp2x = source.Cp2x,
                Event = source.Event,
            };

        private static bool IsLocked(MotionTrack track) => track.Locked;

        private static ActionPlacementResult LockedTrackFailure(
            MotionTrack track,
            string operation
        ) => ActionPlacementResult.Fail($"轨道“{track.Label}”已锁定，无法{operation}");

        /// <summary>
        /// 生成落轨规划 (不修改模型): 对预设每个通道, 按"拖放目标轨 → Role 匹配 → 设备/OAction 启发式匹配"
        /// 给出建议轨道; 无法明确匹配的通道需要用户在确认对话框决定。
        /// </summary>
        public static PresetPlacementPlan PlanPlacement(
            MotionTimeline timeline,
            EffectPreset preset,
            PresetApplyOptions options,
            string? targetTrackId = null
        )
        {
            var plan = new PresetPlacementPlan();
            if (timeline == null || preset == null)
                return plan;

            var generated = PresetParameterApplier.Apply(preset, options);
            foreach (var gc in generated.Clips)
            {
                var role = gc.Role.Trim();
                var item = new PlacementPlanItem { Role = role };
                var candidates = new List<string>();
                string? suggested = null;

                // 1) 用户拖放目标轨 (意图优先)
                if (!string.IsNullOrWhiteSpace(targetTrackId))
                {
                    var target = timeline.Tracks.FirstOrDefault(t => t.Id == targetTrackId);
                    if (target != null && !candidates.Contains(target.Id))
                    {
                        candidates.Add(target.Id);
                        suggested = target.Id;
                    }
                }

                // 2) Role 绑定/同名唯一匹配
                if (!string.IsNullOrWhiteSpace(role))
                {
                    var roleTrack = ResolveTrack(timeline, role);
                    if (roleTrack != null && !candidates.Contains(roleTrack.Id))
                    {
                        candidates.Add(roleTrack.Id);
                        suggested ??= roleTrack.Id;
                    }

                    // 3) 设备/OAction 启发式匹配到的项目轨道
                    var devMatch = MatchDeviceChannelForRole(role);
                    if (devMatch != null)
                    {
                        var devTrack = timeline.Tracks.FirstOrDefault(
                            t =>
                                string.Equals(
                                    t.DeviceName,
                                    devMatch.Value.DeviceName,
                                    StringComparison.OrdinalIgnoreCase
                                )
                                && (
                                    devMatch.Value.IsOAxis
                                        ? string.Equals(
                                            t.OAxisChannel,
                                            devMatch.Value.OAxisChannel,
                                            StringComparison.OrdinalIgnoreCase
                                        )
                                        : string.Equals(
                                            t.OActionName,
                                            devMatch.Value.OActionName,
                                            StringComparison.OrdinalIgnoreCase
                                        )
                                )
                        );
                        if (devTrack != null && !candidates.Contains(devTrack.Id))
                            candidates.Add(devTrack.Id);
                        suggested ??= devTrack?.Id;
                    }
                }

                item.CandidateTrackIds = candidates;
                item.SuggestedTrackId = suggested;
                plan.Items.Add(item);
            }

            return plan;
        }
    }

    public class ActionPlacementResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public ActionInstance? Instance { get; set; }
        public List<MotionTrack> AddedTracks { get; set; } = new();
        public List<MotionClip> AddedClips { get; set; } = new();

        public static ActionPlacementResult Fail(string error) => new() { Error = error };
    }

    /// <summary>落轨规划项 — 一个预设通道应放置到哪条轨道。</summary>
    public class PlacementPlanItem
    {
        /// <summary>通道逻辑角色 (Role)。</summary>
        public string Role { get; set; } = "";

        /// <summary>候选轨道 ID (按意图优先级: 拖放目标轨 → Role 匹配 → 设备/OAction 匹配)。</summary>
        public List<string> CandidateTrackIds { get; set; } = new();

        /// <summary>建议轨道 ID (null = 需要新建)。</summary>
        public string? SuggestedTrackId { get; set; }

        /// <summary>是否无建议轨道 (需用户决定新建或选轨)。</summary>
        public bool ShouldCreateNew => SuggestedTrackId == null;
    }

    /// <summary>落轨规划 — 由 PlanPlacement 生成, 供匹配确认对话框使用。</summary>
    public class PresetPlacementPlan
    {
        public List<PlacementPlanItem> Items { get; set; } = new();

        /// <summary>是否需要匹配确认 (存在需新建/未匹配通道)。</summary>
        public bool NeedsConfirmation => Items.Any(i => i.ShouldCreateNew);
    }
}
