using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Threading;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using ReactiveUI;

namespace IOStudio.ViewModels.Timeline
{
    public partial class TimelineEditorViewModel
    {
        // ---- 轨道分组 (PS 风格: 分组作为独立实体) ----

        /// <summary>分组模型列表 (持久化到 .motion 文件)</summary>
        private readonly List<TrackGroup> _groups = new();

        /// <summary>分组头 ViewModel 缓存</summary>
        private readonly Dictionary<string, GroupHeaderViewModel> _groupHeaders = new();

        /// <summary>
        /// 显示列表 — 分组头 + 轨道交替排列, 供 ItemsControl 绑定
        /// 类型为 object, 实际是 GroupHeaderViewModel 或 TrackViewModel
        /// </summary>
        public ObservableCollection<object> DisplayItems { get; } = new();

        public bool IsDisplayEmpty => DisplayItems.Count == 0;

        public bool HasNoTracks => Tracks.Count == 0;

        public string DisplayEmptyMessage =>
            HasNoTracks
                ? "尚未添加轨道"
                : DisplayFilter?.Mode == DisplayFilterMode.ActiveGroupOnly
                    ? "当前没有分组轨道"
                    : "没有符合当前筛选的轨道";

        // ---- UX-B2: 显示过滤 ----

        /// <summary>显示过滤模式</summary>
        public enum DisplayFilterMode
        {
            All,
            SelectedTrackOnly,
            ActiveGroupOnly,
            HasKeyframesInWorkArea,
        }

        /// <summary>过滤选项 (用于 ComboBox 绑定)</summary>
        public class DisplayFilterOption
        {
            public DisplayFilterMode Mode { get; set; }
            public string Label { get; set; } = "";

            public override string ToString() => Label;
        }

        public List<DisplayFilterOption> DisplayFilterOptions { get; } =
            new()
            {
                new DisplayFilterOption { Mode = DisplayFilterMode.All, Label = "全部轨道" },
                new DisplayFilterOption
                {
                    Mode = DisplayFilterMode.SelectedTrackOnly,
                    Label = "仅选中轨道"
                },
                new DisplayFilterOption
                {
                    Mode = DisplayFilterMode.ActiveGroupOnly,
                    Label = "仅当前分组"
                },
                new DisplayFilterOption
                {
                    Mode = DisplayFilterMode.HasKeyframesInWorkArea,
                    Label = "工作区内有关键帧",
                },
            };

        private DisplayFilterOption? _displayFilter;

        public DisplayFilterOption? DisplayFilter
        {
            get => _displayFilter ??= DisplayFilterOptions[0];
            set
            {
                this.RaiseAndSetIfChanged(ref _displayFilter, value);
                RefreshDisplayList();
            }
        }

        /// <summary>获取所有已知分组名 (去重, 有序)</summary>
        public List<string> GetGroupNames()
        {
            return _groups.Select(g => g.Name).ToList();
        }

        /// <summary>判断分组是否已折叠</summary>
        public bool IsGroupCollapsed(string groupName)
        {
            return _groups.FirstOrDefault(g => g.Name == groupName)?.Collapsed ?? false;
        }

        /// <summary>切换分组折叠状态</summary>
        public void ToggleGroupCollapse(string groupName)
        {
            SetGroupCollapsed(groupName, !IsGroupCollapsed(groupName));
        }

        /// <summary>设置分组折叠状态</summary>
        public void SetGroupCollapsed(string groupName, bool collapsed)
        {
            var group = _groups.FirstOrDefault(g => g.Name == groupName);
            if (group != null)
                group.Collapsed = collapsed;

            // 同步 GroupHeader ViewModel
            if (_groupHeaders.TryGetValue(groupName, out var header))
                header.IsCollapsed = collapsed;

            // 更新组内轨道可见性
            foreach (var t in Tracks.Where(t => t.Group == groupName))
                t.IsVisibleInTimeline = !collapsed;

            RefreshDisplayList();
        }

        /// <summary>
        /// 重建显示列表 — 按分组顺序排列: [分组头A, 轨道A1, 轨道A2, 分组头B, 轨道B1, ..., 未分组轨道]
        /// </summary>
        public void RefreshDisplayList()
        {
            DisplayItems.Clear();

            // UX-B2: 计算当前过滤模式下, 哪些轨道应可见
            var mode = DisplayFilter?.Mode ?? DisplayFilterMode.All;
            string? activeGroup =
                mode == DisplayFilterMode.ActiveGroupOnly ? SelectedTrack?.Group : null;
            bool TrackPassesFilter(TrackViewModel track) =>
                mode switch
                {
                    DisplayFilterMode.All => true,
                    DisplayFilterMode.SelectedTrackOnly
                        => SelectedTrack != null && track == SelectedTrack,
                    DisplayFilterMode.ActiveGroupOnly
                        => !string.IsNullOrEmpty(activeGroup) && track.Group == activeGroup,
                    DisplayFilterMode.HasKeyframesInWorkArea => TrackHasKeyframesInWorkArea(track),
                    _ => true,
                };

            // 已处理的轨道集合
            var processedTracks = new HashSet<string>();

            // 按分组顺序输出
            foreach (var group in _groups)
            {
                // 获取或创建 GroupHeader ViewModel
                if (!_groupHeaders.TryGetValue(group.Name, out var header))
                {
                    header = new GroupHeaderViewModel(group) { IsCollapsed = group.Collapsed };
                    header.SoloMuteChanged = ApplyGroupSoloMute; // UX-B4
                    _groupHeaders[group.Name] = header;
                }

                var groupTracks = Tracks.Where(t => t.Group == group.Name).ToList();
                header.TrackCount = groupTracks.Count;
                header.IsCollapsed = group.Collapsed;

                // UX-B2: 分组内无可见轨道时隐藏分组头 (All 模式保持原行为)
                var visibleGroupTracks =
                    mode == DisplayFilterMode.All
                        ? groupTracks
                        : groupTracks.Where(TrackPassesFilter).ToList();
                if (mode != DisplayFilterMode.All && visibleGroupTracks.Count == 0)
                {
                    foreach (var t in groupTracks)
                        processedTracks.Add(t.Id);
                    continue;
                }

                DisplayItems.Add(header);

                // 折叠时不显示子轨道
                if (!group.Collapsed)
                {
                    foreach (var track in visibleGroupTracks)
                    {
                        track.IsVisibleInTimeline = true;
                        DisplayItems.Add(track);
                    }
                    foreach (var track in groupTracks)
                        processedTracks.Add(track.Id);
                }
                else
                {
                    foreach (var track in groupTracks)
                    {
                        track.IsVisibleInTimeline = false;
                        processedTracks.Add(track.Id);
                    }
                }
            }

            // 未分组轨道
            foreach (var track in Tracks.Where(t => !processedTracks.Contains(t.Id)))
            {
                if (!TrackPassesFilter(track))
                    continue;
                track.IsVisibleInTimeline = true;
                DisplayItems.Add(track);
            }
            this.RaisePropertyChanged(nameof(IsDisplayEmpty));
            this.RaisePropertyChanged(nameof(HasNoTracks));
            this.RaisePropertyChanged(nameof(DisplayEmptyMessage));
        }

        /// <summary>UX-B2: 判断轨道在当前工作区域范围内是否有关键帧 (无工作区时等价于有关键帧)</summary>
        private bool TrackHasKeyframesInWorkArea(TrackViewModel track)
        {
            if (track.Clips.Count == 0)
                return false;
            if (!HasWorkArea)
                return track.Clips.Any(c => c.Keyframes.Count > 0);
            double inMs = WorkAreaInMs ?? 0;
            double outMs = WorkAreaOutMs ?? 0;
            foreach (var clip in track.Clips)
            {
                foreach (var kf in clip.Keyframes)
                {
                    double absTime = clip.StartMs + kf.TimeMs;
                    if (absTime >= inMs && absTime <= outMs)
                        return true;
                }
            }
            return false;
        }

        /// <summary>新建分组 (支持 Undo/Redo)</summary>
        public GroupHeaderViewModel CreateGroup(string name, string color = "#7c3aed")
        {
            // 避免重名
            if (_groups.Any(g => g.Name == name))
                return _groupHeaders[name];

            var group = new TrackGroup { Name = name, Color = color };
            GroupHeaderViewModel? header = null;

            _undoRedo.Execute(
                new LambdaCommand(
                    "新建分组",
                    () =>
                    {
                        if (!_groups.Contains(group))
                            _groups.Add(group);
                        SyncGroupsToTimeline();
                        header = new GroupHeaderViewModel(group)
                        {
                            SoloMuteChanged = ApplyGroupSoloMute
                        };
                        _groupHeaders[name] = header;
                        RefreshDisplayList();
                    },
                    () =>
                    {
                        _groups.Remove(group);
                        _groupHeaders.Remove(name);
                        SyncGroupsToTimeline();
                        RefreshDisplayList();
                    }
                )
            );
            MarkDirty();
            return header!;
        }

        /// <summary>删除分组 (子轨道变为未分组, 不删除轨道)</summary>
        public void DeleteGroup(string groupName)
        {
            var group = _groups.FirstOrDefault(g => g.Name == groupName);
            if (group == null)
                return;

            var affectedTracks = Tracks.Where(t => t.Group == groupName).ToList();
            GroupHeaderViewModel? header = _groupHeaders.TryGetValue(groupName, out var h)
                ? h
                : null;

            _undoRedo.Execute(
                new LambdaCommand(
                    "删除分组",
                    () =>
                    {
                        _groups.RemoveAll(g => g.Name == groupName);
                        _groupHeaders.Remove(groupName);
                        foreach (var t in affectedTracks)
                        {
                            t.Group = "";
                            t.IsVisibleInTimeline = true;
                        }
                        SyncGroupsToTimeline();
                        RefreshDisplayList();
                    },
                    () =>
                    {
                        _groups.Add(group);
                        if (header != null)
                            _groupHeaders[groupName] = header;
                        foreach (var t in affectedTracks)
                            t.Group = groupName;
                        SyncGroupsToTimeline();
                        RefreshDisplayList();
                    }
                )
            );
            MarkDirty();
        }

        /// <summary>重命名分组 (支持 Undo/Redo)</summary>
        public void RenameGroup(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName) || oldName == newName)
                return;
            if (_groups.Any(g => g.Name == newName))
                return; // 新名已存在

            var group = _groups.FirstOrDefault(g => g.Name == oldName);
            if (group is null)
                return;

            void ApplyRename(string from, string to)
            {
                group.Name = to;
                if (_groupHeaders.TryGetValue(from, out var h))
                {
                    _groupHeaders.Remove(from);
                    h.Name = to;
                    _groupHeaders[to] = h;
                }
                foreach (var t in Tracks.Where(t => t.Group == from))
                    t.Group = to;
                SyncGroupsToTimeline();
                RefreshDisplayList();
            }

            _undoRedo.Execute(
                new LambdaCommand(
                    "重命名分组",
                    () => ApplyRename(oldName, newName),
                    () => ApplyRename(newName, oldName)
                )
            );
            MarkDirty();
        }

        /// <summary>将选中轨道设置到指定分组</summary>
        public void SetTrackGroup(TrackViewModel track, string groupName)
        {
            var oldGroup = track.Group ?? "";

            _undoRedo.Execute(
                new LambdaCommand(
                    "设置轨道分组",
                    () =>
                    {
                        track.Group = groupName;
                        if (!_groups.Any(g => g.Name == groupName))
                            _groups.Add(new TrackGroup { Name = groupName });
                        SyncGroupsToTimeline();
                        RefreshDisplayList();
                    },
                    () =>
                    {
                        track.Group = oldGroup;
                        SyncGroupsToTimeline();
                        RefreshDisplayList();
                    }
                )
            );
            MarkDirty();
        }

        /// <summary>从分组中移除轨道</summary>
        public void RemoveTrackFromGroup(TrackViewModel track)
        {
            var oldGroup = track.Group ?? "";
            var oldVisible = track.IsVisibleInTimeline;

            _undoRedo.Execute(
                new LambdaCommand(
                    "移出分组",
                    () =>
                    {
                        track.Group = "";
                        track.IsVisibleInTimeline = true;
                        RefreshDisplayList();
                    },
                    () =>
                    {
                        track.Group = oldGroup;
                        track.IsVisibleInTimeline = oldVisible;
                        RefreshDisplayList();
                    }
                )
            );
            MarkDirty();
        }

        /// <summary>批量静音/取消静音分组</summary>
        public void ToggleGroupMute(string groupName)
        {
            var groupTracks = Tracks.Where(t => t.Group == groupName).ToList();
            bool anyUnmuted = groupTracks.Any(t => !t.IsMuted);
            foreach (var t in groupTracks)
                t.IsMuted = anyUnmuted;
        }

        /// <summary>批量锁定/解锁分组</summary>
        public void ToggleGroupLock(string groupName)
        {
            var groupTracks = Tracks.Where(t => t.Group == groupName).ToList();
            if (groupTracks.Count == 0)
                return;
            bool anyUnlocked = groupTracks.Any(t => !t.IsLocked);
            foreach (var t in groupTracks)
                t.IsLocked = anyUnlocked;
            MarkDirty();
            PushStatus(anyUnlocked ? $"已锁定分组“{groupName}”" : $"已解锁分组“{groupName}”");
        }

        /// <summary>
        /// UX-B4: 将 GroupHeader 的 Solo/Mute 状态级联到组内所有轨道
        /// </summary>
        private void ApplyGroupSoloMute(GroupHeaderViewModel header)
        {
            var groupTracks = Tracks.Where(t => t.Group == header.Name).ToList();
            foreach (var t in groupTracks)
            {
                t.IsMuted = header.IsMuted;
                t.IsSolo = header.IsSolo;
            }

            // Solo 互斥: 若当前分组为 Solo, 其它分组自动取消 Solo
            if (header.IsSolo)
            {
                foreach (var other in _groupHeaders.Values)
                {
                    if (other != header && other.IsSolo)
                        other.IsSolo = false;
                }
            }

            MarkDirty();
        }

        /// <summary>同步分组模型到 Timeline</summary>
        private void SyncGroupsToTimeline()
        {
            if (Timeline == null)
                return;
            Timeline.Groups = _groups.Count > 0 ? new List<TrackGroup>(_groups) : null;
        }

        /// <summary>从 Timeline 模型加载分组</summary>
        private void LoadGroupsFromTimeline()
        {
            _groups.Clear();
            _groupHeaders.Clear();

            if (Timeline?.Groups != null)
            {
                foreach (var g in Timeline.Groups)
                    _groups.Add(g);
            }

            // 扫描轨道中引用但不在 Groups 列表中的分组 (兼容旧文件)
            foreach (var t in Tracks)
            {
                if (!string.IsNullOrEmpty(t.Group) && !_groups.Any(g => g.Name == t.Group))
                    _groups.Add(new TrackGroup { Name = t.Group });
            }
        }

        // ---- 轨道管理 ----

        private void LoadTimeline(MotionTimeline timeline)
        {
            // 快照替换会重建全部 TrackViewModel，先清理旧对象选择，避免属性面板
            // 或快捷键继续持有已脱离当前时间轴的引用。
            SelectedTrack = null;
            ClearMultiSelection();
            ClearKeyframeSelection();
            SelectedActionInstanceId = null;
            SelectedEvent = null;

            // v2 文件没有动作编排元数据，加载时补齐为空集合并保持向后兼容。
            timeline.ActionInstances ??= new List<ActionInstance>();
            timeline.RoleTrackBindings ??= new Dictionary<string, string>();
            timeline.EmbeddedPresets ??= new List<EffectPreset>();
            Timeline = timeline;
            DurationMs = timeline.DurationMs;
            CurrentTimeMs = 0;

            Tracks.Clear();
            foreach (var track in timeline.Tracks)
            {
                Tracks.Add(new TrackViewModel(track));
            }

            // 初始化各轨道紧凑显示状态
            RefreshAllTrackCompactDisplay();

            // 加载独立事件
            Events.Clear();
            if (timeline.Events != null)
            {
                foreach (var evt in timeline.Events)
                    Events.Add(evt);
            }

            // 根据实际关键帧数据重新计算时长
            RecalculateDuration();

            // 加载标记
            _markerService.LoadFromTimeline(timeline);

            // 加载分组并重建显示列表
            LoadGroupsFromTimeline();
            RefreshDisplayList();

            this.RaisePropertyChanged(nameof(DurationDisplay));
            this.RaisePropertyChanged(nameof(CurrentTimeDisplay));
        }

        /// <summary>
        /// 添加轨道 (绑定到设备的 OAction 输出动作)
        /// </summary>
        public void AddTrack(
            string deviceName,
            string oactionName,
            string label,
            string color = "#4FC3F7",
            string valueType = "float",
            string outputType = "oaction",
            string oaxisChannel = ""
        )
        {
            if (Timeline == null)
                return;

            // Bool 类型默认值为 0 (关), Float 类型默认值为 0.5 (中位)
            float defaultValue = valueType == "bool" ? 0f : 0.5f;

            var track = new MotionTrack
            {
                DeviceName = deviceName,
                OActionName = oactionName,
                Label = label,
                Color = color,
                ValueType = valueType,
                OutputType = outputType,
                OAxisChannel = oaxisChannel,
                Clips = new()
                {
                    new MotionClip
                    {
                        StartMs = 0,
                        EndMs = DurationMs,
                        Keyframes = new()
                        {
                            new MotionKeyframe
                            {
                                TimeMs = 0,
                                Value = defaultValue,
                                Interpolation = valueType == "bool" ? "step" : null
                            },
                            new MotionKeyframe
                            {
                                TimeMs = DurationMs,
                                Value = defaultValue,
                                Interpolation = valueType == "bool" ? "step" : null
                            },
                        }
                    }
                }
            };

            var trackVm = new TrackViewModel(track);

            _undoRedo.Execute(
                new LambdaCommand(
                    $"添加轨道 {label}",
                    () =>
                    {
                        Timeline!.Tracks.Add(track);
                        Tracks.Add(trackVm);
                        trackVm.UpdateCompactDisplay(TrackHeight);
                        RecalculateDuration();
                        RefreshDisplayList();
                    },
                    () =>
                    {
                        Timeline!.Tracks.Remove(track);
                        Tracks.Remove(trackVm);
                        if (SelectedTrack == trackVm)
                        {
                            SelectedTrack = null;
                            ClearKeyframeSelection();
                        }
                        RecalculateDuration();
                        RefreshDisplayList();
                    }
                )
            );
            MarkDirty();
        }

        /// <summary>
        /// 移除轨道
        /// </summary>
        public void RemoveTrack(TrackViewModel trackVm)
        {
            if (Timeline == null)
                return;
            if (!TryBeginTrackEdit(trackVm, "删除轨道"))
                return;
            if (!Timeline.Tracks.Contains(trackVm.Track))
                return;

            double playhead = CurrentTimeMs;
            string beforeJson = MotionFileReader.ToJson(Timeline);
            var candidate = MotionFileReader.ReadFromJson(beforeJson);
            if (candidate == null)
                return;

            candidate.ActionInstances ??= new List<ActionInstance>();
            candidate.RoleTrackBindings ??= new Dictionary<string, string>();

            var affectedActionIds = candidate.Tracks
                .Where(track => track.Id == trackVm.Id)
                .SelectMany(track => track.Clips)
                .Select(clip => clip.ActionInstanceId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .Concat(
                    candidate.ActionInstances
                        .Where(instance => instance.RoleTrackIds.Values.Contains(trackVm.Id))
                        .Select(instance => instance.Id)
                )
                .ToHashSet(StringComparer.Ordinal);

            foreach (var track in candidate.Tracks)
            {
                track.Clips.RemoveAll(
                    clip =>
                        clip.ActionInstanceId != null
                        && affectedActionIds.Contains(clip.ActionInstanceId)
                );
            }
            candidate.ActionInstances.RemoveAll(instance => affectedActionIds.Contains(instance.Id));
            candidate.Tracks.RemoveAll(track => track.Id == trackVm.Id);

            foreach (
                var role in candidate.RoleTrackBindings
                    .Where(binding => binding.Value == trackVm.Id)
                    .Select(binding => binding.Key)
                    .ToList()
            )
            {
                candidate.RoleTrackBindings.Remove(role);
            }

            string afterJson = MotionFileReader.ToJson(candidate);
            _undoRedo.Execute(
                new LambdaCommand(
                    $"删除轨道 {trackVm.Label}",
                    () => RestoreTimelineSnapshot(afterJson, playhead),
                    () => RestoreTimelineSnapshot(beforeJson, playhead)
                )
            );
            MarkDirty();
            PushStatus(
                affectedActionIds.Count > 0
                    ? $"已删除轨道“{trackVm.Label}”，并移除 {affectedActionIds.Count} 个关联动作"
                    : $"已删除轨道“{trackVm.Label}”"
            );
        }

        /// <summary>
        /// 将轨道上移一位
        /// </summary>
        public void MoveTrackUp(TrackViewModel trackVm)
        {
            if (Timeline == null)
                return;
            int idx = Tracks.IndexOf(trackVm);
            if (idx <= 0)
                return;

            var modelTrack = trackVm.Track;
            int modelIdx = Timeline.Tracks.IndexOf(modelTrack);

            _undoRedo.Execute(
                new LambdaCommand(
                    "上移轨道",
                    () =>
                    {
                        int i = Tracks.IndexOf(trackVm);
                        if (i > 0)
                            Tracks.Move(i, i - 1);
                        int mi = Timeline.Tracks.IndexOf(modelTrack);
                        if (mi > 0)
                        {
                            Timeline.Tracks.RemoveAt(mi);
                            Timeline.Tracks.Insert(mi - 1, modelTrack);
                        }
                        RefreshDisplayList();
                    },
                    () =>
                    {
                        int i = Tracks.IndexOf(trackVm);
                        if (i >= 0 && i < Tracks.Count - 1)
                            Tracks.Move(i, i + 1);
                        int mi = Timeline.Tracks.IndexOf(modelTrack);
                        if (mi >= 0 && mi < Timeline.Tracks.Count - 1)
                        {
                            Timeline.Tracks.RemoveAt(mi);
                            Timeline.Tracks.Insert(mi + 1, modelTrack);
                        }
                        RefreshDisplayList();
                    }
                )
            );
            MarkDirty();
        }

        /// <summary>
        /// 将轨道下移一位
        /// </summary>
        public void MoveTrackDown(TrackViewModel trackVm)
        {
            if (Timeline == null)
                return;
            int idx = Tracks.IndexOf(trackVm);
            if (idx < 0 || idx >= Tracks.Count - 1)
                return;

            var modelTrack = trackVm.Track;
            int modelIdx = Timeline.Tracks.IndexOf(modelTrack);

            _undoRedo.Execute(
                new LambdaCommand(
                    "下移轨道",
                    () =>
                    {
                        int i = Tracks.IndexOf(trackVm);
                        if (i >= 0 && i < Tracks.Count - 1)
                            Tracks.Move(i, i + 1);
                        int mi = Timeline.Tracks.IndexOf(modelTrack);
                        if (mi >= 0 && mi < Timeline.Tracks.Count - 1)
                        {
                            Timeline.Tracks.RemoveAt(mi);
                            Timeline.Tracks.Insert(mi + 1, modelTrack);
                        }
                        RefreshDisplayList();
                    },
                    () =>
                    {
                        int i = Tracks.IndexOf(trackVm);
                        if (i > 0)
                            Tracks.Move(i, i - 1);
                        int mi = Timeline.Tracks.IndexOf(modelTrack);
                        if (mi > 0)
                        {
                            Timeline.Tracks.RemoveAt(mi);
                            Timeline.Tracks.Insert(mi - 1, modelTrack);
                        }
                        RefreshDisplayList();
                    }
                )
            );
            MarkDirty();
        }

        /// <summary>
        /// 跳转到指定时间 (由 TimeRulerControl 调用)
        /// 启用吸附时, 播放头拖拽自动吸附到最近的关键帧/事件
        /// 如果正在播放, 先暂停播放以避免 Tick 驱动与用户拖拽冲突
        /// </summary>
        public void SeekTo(double timeMs)
        {
            // 播放中人为调整时间头时, 自动暂停以避免冲突
            if (IsPlaying)
                Pause();

            double snapped = ApplySnap(timeMs);
            CurrentTimeMs = ClampTime(snapped);
            // 确保引擎持有 Timeline 引用 (拖拽播放头时可能未按过 Play)
            if (Timeline != null)
                _engine.EnsureTimelineLoaded(Timeline);
            _engine.Seek(CurrentTimeMs);
            this.RaisePropertyChanged(nameof(CurrentTimeDisplay));
        }

        /// <summary>原子更新轨道属性并纳入撤销栈。</summary>
        public void UpdateTrackProperties(TrackViewModel trackVm, AddTrackResult result)
        {
            if (trackVm == null || result == null)
                return;

            var old = new AddTrackResult
            {
                DeviceName = trackVm.DeviceName,
                OActionName = trackVm.OActionName,
                OutputType = trackVm.Track.OutputType,
                OAxisChannel = trackVm.Track.OAxisChannel,
                Label = trackVm.Label,
                Color = trackVm.Color,
                ValueType = trackVm.ValueType
            };

            void Apply(AddTrackResult value)
            {
                trackVm.Label = value.Label;
                trackVm.Color = value.Color;
                trackVm.DeviceName = value.DeviceName;
                trackVm.OActionName = value.OActionName;
                trackVm.Track.OutputType = value.OutputType;
                trackVm.Track.OAxisChannel = value.OAxisChannel;
                if (trackVm.ValueType != value.ValueType)
                {
                    trackVm.ValueType = value.ValueType;
                    if (value.ValueType == "bool")
                    {
                        foreach (var clip in trackVm.Track.Clips)
                            foreach (var kf in clip.Keyframes)
                            {
                                kf.Value = kf.Value >= 0.5f ? 1f : 0f;
                                kf.Interpolation = "step";
                            }
                    }
                }
                // 注意: 不写 IdleLoop — 待机循环已由时间轴专项编辑器管理,
                // 属性对话框不再触碰 idle, 避免保存时误清空已有待机配置。
                trackVm.RaiseAllBindingsChanged();
                trackVm.RaiseClipsChanged();
                NotifyTrackDataChanged(trackVm);
                InvalidateTrackChannelIndex();
            }

            _undoRedo.Execute(new LambdaCommand("修改轨道属性", () => Apply(result), () => Apply(old)));
            MarkDirty();
        }

        /// <summary>
        /// 根据所有轨道关键帧自动计算时间轴总时长
        /// Clip 的 EndMs 精确跟踪最后一个关键帧位置 + 少量余量，不会无限增长
        /// 当用户手动设置了时长时, 以手动值为准
        /// </summary>
        public void RecalculateDuration()
        {
            if (Timeline == null)
                return;

            double maxContentMs = 1000; // 最小 1 秒

            // 计算每个 clip 的实际内容结束时间，并精确收缩 clip.EndMs
            foreach (var track in Timeline.Tracks)
            {
                foreach (var clip in track.Clips)
                {
                    double clipMaxKfTime = 0;
                    foreach (var kf in clip.Keyframes)
                    {
                        if (kf.TimeMs > clipMaxKfTime)
                            clipMaxKfTime = kf.TimeMs;
                    }

                    // Clip 结束时间 = 最后关键帧时间 + 500ms 余量，向上取整到 500ms
                    double clipEnd =
                        Math.Ceiling((clip.StartMs + clipMaxKfTime + 500) / 500.0) * 500;
                    clip.EndMs = clipEnd;

                    if (clipEnd > maxContentMs)
                        maxContentMs = clipEnd;
                }
            }

            // 考虑视频时长
            if (VideoDurationMs > maxContentMs)
                maxContentMs = VideoDurationMs;

            // 时间轴总时长 = 最大内容时间 + 1 秒余量，向上取整到 1000ms
            double newDuration = Math.Ceiling((maxContentMs + 1000) / 1000.0) * 1000;

            // 不允许缩减到比当前播放头位置还小
            if (CurrentTimeMs > newDuration)
                newDuration = Math.Ceiling((CurrentTimeMs + 1000) / 1000.0) * 1000;

            // 如果用户手动设置了时长, 以手动值为准 (但不能小于内容时长)
            if (ManualDurationMs > 0)
            {
                newDuration = Math.Max(newDuration, ManualDurationMs);
            }
            else
            {
                // 自动模式: 只增不减, 防止编辑操作导致时间轴意外缩短
                if (newDuration < DurationMs)
                    newDuration = DurationMs;
            }

            DurationMs = newDuration;
            Timeline.DurationMs = newDuration;
            this.RaisePropertyChanged(nameof(DurationDisplay));

            // 通知所有轨道重绘
            foreach (var trackVm in Tracks)
            {
                trackVm.RaiseClipsChanged();
            }
        }
    }
}
