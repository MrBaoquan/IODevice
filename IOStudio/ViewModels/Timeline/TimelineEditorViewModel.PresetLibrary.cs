using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;

namespace IOStudio.ViewModels.Timeline
{
    /// <summary>
    /// 效果预设库集成 — 应用预设到时间轴、从轨道保存预设。
    /// </summary>
    public partial class TimelineEditorViewModel
    {
        private EffectPresetLibrary? _presetLibrary;
        private PresetLibraryPanelViewModel? _presetPanelVm;

        /// <summary>预设库面板 ViewModel</summary>
        public PresetLibraryPanelViewModel PresetPanelVm
        {
            get
            {
                if (_presetPanelVm == null)
                {
                    if (_presetLibrary == null)
                        _presetLibrary = new EffectPresetLibrary();
                    _presetPanelVm = new PresetLibraryPanelViewModel(_presetLibrary);
                    _presetPanelVm.PresetApplyRequested += OnPresetApplyRequested;
                    _presetPanelVm.SavePresetRequested += OnSavePresetRequested;
                    _presetPanelVm.DeletePresetRequested += OnDeletePresetRequestedAsync;
                    _presetPanelVm.RenamePresetRequested += OnRenamePresetRequestedAsync;
                    _presetPanelVm.ChangeCategoryRequested += OnChangePresetCategoryRequestedAsync;
                    _presetPanelVm.SetTargetTrack(
                        SelectedTrack?.Id,
                        SelectedTrack?.Label,
                        SelectedTrack?.IsLocked == true
                    );
                }
                return _presetPanelVm;
            }
        }

        /// <summary>以一个可追踪、可整体撤销的动作实例插入预设。</summary>
        private async void OnPresetApplyRequested(EffectPreset preset, PresetApplyOptions options)
        {
            await PlacePresetAtTime(preset, options, CurrentTimeMs, PresetPanelVm.TargetTrackId);
        }

        /// <summary>使用默认参数把库中的预设拖放到指定时间。</summary>
        public async Task<bool> PlacePresetAtTime(
            string presetId,
            double startMs,
            string? targetTrackId = null
        )
        {
            var preset = PresetPanelVm.Library.FindPreset(presetId);
            if (preset == null)
            {
                PresetPanelVm.OperationStatus = "预设不存在或已被移除";
                return false;
            }

            return await PlacePresetAtTime(
                preset,
                new PresetApplyOptions
                {
                    Intensity = preset.DefaultIntensity,
                    Speed = 1.0,
                    TargetDurationMs = preset.DurationMs,
                },
                startMs,
                targetTrackId
            );
        }

        private async Task<bool> PlacePresetAtTime(
            EffectPreset preset,
            PresetApplyOptions options,
            double startMs,
            string? targetTrackId = null
        )
        {
            if (Timeline == null)
                return false;

            double insertTimeMs = Math.Max(0, startMs);

            // 数值/单通道预设放任意轨都合理: 无目标轨时自动用上下文轨道 (选中轨→播放头所在轨→首轨),
            // 避免无谓的匹配确认打扰。
            if (string.IsNullOrWhiteSpace(targetTrackId))
            {
                targetTrackId =
                    SelectedTrack?.Id
                    ?? Tracks
                        .FirstOrDefault(
                            t =>
                                t.Clips.Any(
                                    c => CurrentTimeMs >= c.StartMs && CurrentTimeMs <= c.EndMs
                                )
                        )
                        ?.Id
                    ?? Tracks.FirstOrDefault()?.Id;
            }

            // 1) 落轨规划: 存在"需新建/未匹配"通道时, 弹出匹配确认, 让用户自行决定放置轨道。
            Dictionary<string, string?>? overrideMap = null;
            var plan = ActionInstanceService.PlanPlacement(
                Timeline,
                preset,
                options,
                targetTrackId
            );
            if (plan.NeedsConfirmation && _dialogService != null)
            {
                var dialog = new IOStudio.Views.Timeline.TrackMatchDialog(Timeline, plan);
                await _dialogService.ShowDialogAsync(dialog);
                if (!dialog.Confirmed)
                    return false;
                overrideMap = dialog.GetMapping();
            }

            // 2) 执行 (快照撤销)
            string beforeJson = MotionFileReader.ToJson(Timeline);
            var candidate = MotionFileReader.ReadFromJson(beforeJson);
            if (candidate == null)
            {
                PresetPanelVm.OperationStatus = "无法创建动作编辑快照";
                return false;
            }
            var placement = ActionInstanceService.PlacePreset(
                candidate,
                preset,
                options,
                insertTimeMs,
                targetTrackId,
                overrideMap
            );
            if (!placement.Success)
            {
                PresetPanelVm.OperationStatus = placement.Error ?? "动作插入失败";
                PushStatus(PresetPanelVm.OperationStatus);
                return false;
            }

            string afterJson = MotionFileReader.ToJson(candidate);

            _undoRedo.Execute(
                new LambdaCommand(
                    "插入动作实例",
                    () =>
                    {
                        var redoTimeline = MotionFileReader.ReadFromJson(afterJson);
                        if (redoTimeline != null)
                        {
                            LoadTimeline(redoTimeline);
                            CurrentTimeMs = insertTimeMs;
                        }
                    },
                    () =>
                    {
                        var undoTimeline = MotionFileReader.ReadFromJson(beforeJson);
                        if (undoTimeline != null)
                        {
                            LoadTimeline(undoTimeline);
                            CurrentTimeMs = insertTimeMs;
                        }
                    }
                )
            );

            PresetPanelVm.OperationStatus =
                $"已插入“{preset.Name}” · {placement.Instance?.DurationMs / 1000.0:F2}s";
            MarkDirty();
            PushStatus(PresetPanelVm.OperationStatus);
            return true;
        }

        /// <summary>保存预设请求 — 弹出名称输入, 收集轨道 Clip, 创建并保存预设</summary>
        private async void OnSavePresetRequested(object? sender, EventArgs e)
        {
            if (Timeline == null)
                return;

            var instanceId = GetFocusedActionInstanceId();
            if (instanceId == null)
            {
                PresetPanelVm.OperationStatus = "请先选中一个动作片段，或将播放头停在动作范围内";
                return;
            }

            // 弹出名称输入对话框
            string? name = null;
            if (_dialogService != null)
                name = await _dialogService.InputAsync(
                    "保存为效果预设",
                    "请输入预设名称。单轨动作保存为与通道无关的数值关键帧预设。",
                    "新预设"
                );

            if (string.IsNullOrWhiteSpace(name))
                return;

            var preset = ActionInstanceService.CreatePresetFromInstance(
                Timeline,
                instanceId,
                name.Trim()
            );
            if (preset == null)
            {
                PresetPanelVm.OperationStatus = "当前动作没有可保存的关键帧";
                return;
            }

            // 保存到库
            if (_presetLibrary == null)
            {
                _presetLibrary = new EffectPresetLibrary();
                _presetLibrary.Initialize();
            }

            _presetLibrary.Upsert(preset);
            _presetLibrary.SaveAll();
            PresetPanelVm.RefreshPresets();
            PresetPanelVm.RefreshCategories();
            PresetPanelVm.OperationStatus = $"已保存预设“{preset.Name}” · {preset.KeyframeCount} 关键帧";
        }

        /// <summary>删除预设请求处理 — 确认后删除并刷新。</summary>
        private async void OnDeletePresetRequestedAsync(EffectPreset preset)
        {
            if (preset == null || _presetLibrary == null)
                return;
            bool ok = true;
            if (_dialogService != null)
            {
                ok = await _dialogService.ConfirmAsync("删除预设", $"确定删除预设“{preset.Name}”？此操作不可撤销。");
            }
            if (!ok)
                return;

            if (_presetLibrary.Delete(preset.Id))
            {
                _presetLibrary.SaveAll();
                PresetPanelVm.RefreshPresets();
                PresetPanelVm.RefreshCategories();
                PresetPanelVm.OperationStatus = $"已删除预设“{preset.Name}”";
            }
            else
            {
                PresetPanelVm.OperationStatus = $"无法删除“{preset.Name}”(内置预设)";
            }
        }

        /// <summary>重命名预设请求处理 — 输入新名称后更新并保存。</summary>
        private async void OnRenamePresetRequestedAsync(EffectPreset preset)
        {
            if (preset == null || _presetLibrary == null)
                return;
            string? newName = null;
            if (_dialogService != null)
                newName = await _dialogService.InputAsync("重命名预设", "请输入新的预设名称：", preset.Name);
            if (string.IsNullOrWhiteSpace(newName))
                return;

            preset.Name = newName.Trim();
            _presetLibrary.Upsert(preset);
            _presetLibrary.SaveAll();
            PresetPanelVm.RefreshPresets();
            PresetPanelVm.OperationStatus = $"已重命名为“{preset.Name}”";
        }

        /// <summary>修改预设分类请求处理 — 输入新分类后更新并保存。</summary>
        private async void OnChangePresetCategoryRequestedAsync(EffectPreset preset)
        {
            if (preset == null || _presetLibrary == null)
                return;
            string? newCategory = null;
            if (_dialogService != null)
                newCategory = await _dialogService.InputAsync(
                    "修改分类",
                    "请输入预设的分类名称 (如: 失重效果 / 过山车 / 特效)：",
                    string.IsNullOrEmpty(preset.Category) ? "特效" : preset.Category
                );
            if (string.IsNullOrWhiteSpace(newCategory))
                return;

            preset.Category = newCategory.Trim();
            _presetLibrary.Upsert(preset);
            _presetLibrary.SaveAll();
            PresetPanelVm.RefreshPresets();
            PresetPanelVm.RefreshCategories();
            PresetPanelVm.OperationStatus = $"已移动“{preset.Name}”到分类“{preset.Category}”";
        }

        /// <summary>
        /// 从当前选中的轨道 Clip 组创建效果预设 (供"另存为预设"菜单项调用)
        /// </summary>
        public void SaveSelectedClipsAsPreset(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || Timeline == null)
                return;

            var instanceId = GetFocusedActionInstanceId();
            if (instanceId == null)
                return;

            var preset = ActionInstanceService.CreatePresetFromInstance(
                Timeline,
                instanceId,
                name.Trim()
            );
            if (preset == null)
                return;

            // 保存到库
            if (_presetLibrary == null)
            {
                _presetLibrary = new EffectPresetLibrary();
                _presetLibrary.Initialize();
            }

            _presetLibrary.Upsert(preset);
            _presetLibrary.SaveAll();
            PresetPanelVm.RefreshPresets();
            PresetPanelVm.RefreshCategories();
        }

        /// <summary>复制动作实例，并将副本起点放到当前播放头。</summary>
        public void DuplicateActionInstanceAtPlayhead(string instanceId)
        {
            ExecuteActionMutation(
                "复制动作实例",
                candidate =>
                    ActionInstanceService.DuplicateInstance(candidate, instanceId, CurrentTimeMs),
                result => $"已复制“{result.Instance?.Name}”到 {CurrentTimeMs / 1000.0:F2}s"
            );
        }

        /// <summary>将动作实例及其全部通道整体移动到当前播放头。</summary>
        public void MoveActionInstanceToPlayhead(string instanceId)
        {
            MoveActionInstanceToTime(instanceId, CurrentTimeMs);
        }

        /// <summary>将动作实例及其全部通道整体移动到指定时间。</summary>
        public void MoveActionInstanceToTime(string instanceId, double startMs)
        {
            ExecuteActionMutation(
                "移动动作实例",
                candidate => ActionInstanceService.MoveInstance(candidate, instanceId, startMs),
                result => $"已移动“{result.Instance?.Name}”到 {Math.Max(0, startMs) / 1000.0:F2}s"
            );
        }

        /// <summary>调整动作实例边界（拖左边界=改 in 点, 拖右边界=改时长），可撤销。</summary>
        public void ResizeActionInstance(string instanceId, double deltaStartMs, double deltaEndMs)
        {
            if (Math.Abs(deltaStartMs) < 0.1 && Math.Abs(deltaEndMs) < 0.1)
                return;
            ExecuteActionMutation(
                "调整动作边界",
                candidate =>
                    ActionInstanceService.ResizeInstance(
                        candidate,
                        instanceId,
                        deltaStartMs,
                        deltaEndMs
                    ),
                result => result.Success ? "已调整动作边界" : (result.Error ?? "边界调整失败")
            );
        }

        /// <summary>删除动作实例及其所有通道片段。</summary>
        public void DeleteActionInstance(string instanceId)
        {
            ExecuteActionMutation(
                "删除动作实例",
                candidate => ActionInstanceService.DeleteInstance(candidate, instanceId),
                result => $"已删除动作“{result.Instance?.Name}”"
            );
        }

        /// <summary>从指定动作实例另存为独立预设。</summary>
        public async void SaveActionInstanceAsPreset(string instanceId)
        {
            if (Timeline == null)
                return;

            var instance = Timeline.ActionInstances?.FirstOrDefault(i => i.Id == instanceId);
            if (instance == null)
                return;

            string? name = instance.Name;
            if (_dialogService != null)
            {
                name = await _dialogService.InputAsync(
                    "保存为效果预设",
                    "请输入预设名称。单轨动作保存为与通道无关的数值关键帧预设。",
                    instance.Name
                );
            }
            if (string.IsNullOrWhiteSpace(name))
                return;

            var preset = ActionInstanceService.CreatePresetFromInstance(
                Timeline,
                instanceId,
                name.Trim()
            );
            if (preset == null)
            {
                PresetPanelVm.OperationStatus = "当前动作没有可保存的关键帧";
                return;
            }

            _presetLibrary ??= new EffectPresetLibrary();
            if (_presetLibrary.AllPresets.Count == 0)
                _presetLibrary.Initialize();
            _presetLibrary.Upsert(preset);
            _presetLibrary.SaveAll();
            PresetPanelVm.RefreshPresets();
            PresetPanelVm.RefreshCategories();
            PresetPanelVm.OperationStatus = $"已保存预设“{preset.Name}” · {preset.KeyframeCount} 关键帧";
        }

        public string? GetFocusedActionInstanceId()
        {
            if (
                SelectedTrack != null
                && SelectedClipIndex >= 0
                && SelectedClipIndex < SelectedTrack.Clips.Count
            )
            {
                var selectedId = SelectedTrack.Clips[SelectedClipIndex].Clip.ActionInstanceId;
                if (!string.IsNullOrEmpty(selectedId))
                    return selectedId;
            }

            var atPlayhead = Timeline
                ?.ActionInstances?.Where(
                    i =>
                        i.Enabled
                        && CurrentTimeMs >= i.StartMs
                        && CurrentTimeMs <= i.StartMs + i.DurationMs
                )
                .Select(i => i.Id)
                .Distinct()
                .Take(2)
                .ToList();
            return atPlayhead?.Count == 1 ? atPlayhead[0] : null;
        }

        /// <summary>
        /// 查找时间轴中来自指定预设的任意动作实例 (优先播放头所在, 其次选中, 再任意第一个)。
        /// 用于"编辑曲线"优先编辑实例, 让编辑结果立即反映到时间轴。
        /// </summary>
        public string? FindInstanceByPreset(string presetId)
        {
            if (
                string.IsNullOrEmpty(presetId)
                || Timeline?.ActionInstances == null
                || Timeline.ActionInstances.Count == 0
            )
                return null;

            var candidates = Timeline.ActionInstances
                .Where(
                    i => string.Equals(i.DefinitionId, presetId, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            if (candidates.Count == 0)
                return null;

            var atPlayhead = candidates.FirstOrDefault(
                i => CurrentTimeMs >= i.StartMs && CurrentTimeMs <= i.StartMs + i.DurationMs
            );
            if (atPlayhead != null)
                return atPlayhead.Id;

            var focusedId = GetFocusedActionInstanceId();
            if (focusedId != null)
            {
                var focused = candidates.FirstOrDefault(i => i.Id == focusedId);
                if (focused != null)
                    return focused.Id;
            }

            return candidates[0].Id;
        }

        /// <summary>
        /// 构造动作实例的曲线编辑数据：每个剪辑一条通道, 关键帧时间轴相对剪辑起点。
        /// 编辑的是时间轴中实例的关键帧数据, 保存后立即写回并可在时间轴预览。
        /// </summary>
        public EffectPreset? BuildInstanceCurvePreset(string instanceId)
        {
            if (Timeline == null || string.IsNullOrEmpty(instanceId))
                return null;
            var inst = Timeline.ActionInstances?.FirstOrDefault(i => i.Id == instanceId);
            if (inst == null)
                return null;
            var clips = ActionInstanceService.GetInstanceClips(Timeline, instanceId);
            if (clips.Count == 0)
                return null;

            var preset = new EffectPreset
            {
                Id = inst.DefinitionId,
                Name = inst.Name,
                DurationMs = Math.Max(
                    inst.DurationMs,
                    clips.Max(c => c.Clip.EndMs - c.Clip.StartMs)
                ),
            };
            foreach (var (role, clip) in clips)
            {
                preset.Channels.Add(
                    new PresetChannel
                    {
                        Role = role,
                        Keyframes = clip.Keyframes
                            ?.Select(
                                k =>
                                    new MotionKeyframe
                                    {
                                        TimeMs = k.TimeMs,
                                        Value = k.Value,
                                        Interpolation = k.Interpolation,
                                        TangentIn = k.TangentIn,
                                        TangentOut = k.TangentOut,
                                        Cp1x = k.Cp1x,
                                        Cp2x = k.Cp2x,
                                        Event = k.Event,
                                    }
                            )
                            .ToList(),
                    }
                );
            }
            return preset;
        }

        /// <summary>把曲线编辑器保存的结果写回动作实例（快照式撤销, 立即刷新生效）。</summary>
        public void UpdateInstanceCurve(string instanceId, EffectPreset edited)
        {
            if (edited == null)
                return;
            ExecuteActionMutation(
                "编辑动作曲线",
                candidate =>
                    ActionInstanceService.ReplaceInstanceKeyframes(
                        candidate,
                        instanceId,
                        edited.Channels
                    ),
                result => result.Success ? "已更新动作曲线" : (result.Error ?? "更新失败")
            );
        }

        private void ExecuteActionMutation(
            string description,
            Func<MotionTimeline, ActionPlacementResult> mutate,
            Func<ActionPlacementResult, string> successMessage
        )
        {
            if (Timeline == null)
                return;

            double playhead = CurrentTimeMs;
            string beforeJson = MotionFileReader.ToJson(Timeline);
            var candidate = MotionFileReader.ReadFromJson(beforeJson);
            if (candidate == null)
                return;

            var result = mutate(candidate);
            if (!result.Success)
            {
                PresetPanelVm.OperationStatus = result.Error ?? "动作操作失败";
                PushStatus(result.Error ?? "动作操作失败");
                return;
            }

            string afterJson = MotionFileReader.ToJson(candidate);
            _undoRedo.Execute(
                new LambdaCommand(
                    description,
                    () => RestoreTimelineSnapshot(afterJson, playhead),
                    () => RestoreTimelineSnapshot(beforeJson, playhead)
                )
            );
            PresetPanelVm.OperationStatus = successMessage(result);
            MarkDirty();
        }

        private void RestoreTimelineSnapshot(string json, double playhead)
        {
            var restored = MotionFileReader.ReadFromJson(json);
            if (restored == null)
                return;
            LoadTimeline(restored);
            CurrentTimeMs = playhead;
        }

        /// <summary>
        /// 添加轨道 (带 role 参数的重载)
        /// </summary>
        private void AddTrack(
            string deviceName,
            string oactionName,
            string label,
            string color = "#4FC3F7",
            string valueType = "float",
            string outputType = "oaction",
            string oaxisChannel = "",
            string role = ""
        )
        {
            if (Timeline == null)
                return;

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
                Role = role,
                Clips = new()
                {
                    new MotionClip
                    {
                        StartMs = 0,
                        EndMs = DurationMs,
                        Keyframes = new()
                        {
                            new MotionKeyframe { TimeMs = 0, Value = defaultValue },
                            new MotionKeyframe { TimeMs = DurationMs, Value = defaultValue },
                        }
                    }
                }
            };

            var trackVm = new TrackViewModel(track);
            Tracks.Add(trackVm);
            Timeline.Tracks.Add(track);

            MarkDirty();
        }
    }
}
