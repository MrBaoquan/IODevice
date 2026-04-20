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
        // ---- 关键帧编辑 ----

        /// <summary>选中关键帧 (由 TrackClipControl.KeyframeSelected 触发)</summary>
        public void SelectKeyframe(TrackViewModel track, int clipIdx, int kfIdx)
        {
            SelectedTrack = track;
            SelectedClipIndex = clipIdx;
            SelectedKeyframeIndex = kfIdx;

            // 单选模式: 清空多选列表, 只保留当前
            _selectedKeyframes.Clear();

            if (clipIdx >= 0 && kfIdx >= 0 && clipIdx < track.Clips.Count)
            {
                var clipVm = track.Clips[clipIdx];
                if (kfIdx < clipVm.Keyframes.Count)
                {
                    SelectedKeyframe = clipVm.Keyframes[kfIdx];
                    // 强制通知 (即使选中同一个关键帧, 属性面板也需要刷新)
                    this.RaisePropertyChanged(nameof(SelectedKeyframe));
                    _selectedKeyframes.Add((track, clipIdx, kfIdx));
                    this.RaisePropertyChanged(nameof(IsMultiSelectMode));
                    this.RaisePropertyChanged(nameof(SelectedKeyframeCount));
                    return;
                }
            }

            SelectedKeyframe = null;
            this.RaisePropertyChanged(nameof(SelectedKeyframe));
            this.RaisePropertyChanged(nameof(IsMultiSelectMode));
            this.RaisePropertyChanged(nameof(SelectedKeyframeCount));
        }

        /// <summary>清除关键帧选中</summary>
        public void ClearKeyframeSelection()
        {
            SelectedClipIndex = -1;
            SelectedKeyframeIndex = -1;
            SelectedKeyframe = null;
            _selectedKeyframes.Clear();
            this.RaisePropertyChanged(nameof(IsMultiSelectMode));
            this.RaisePropertyChanged(nameof(SelectedKeyframeCount));
        }

        /// <summary>添加关键帧到多选列表 (Shift+Click)</summary>
        public void AddToSelection(TrackViewModel track, int clipIdx, int kfIdx)
        {
            // 避免重复添加
            if (
                _selectedKeyframes.Any(
                    s => s.Track == track && s.ClipIdx == clipIdx && s.KfIdx == kfIdx
                )
            )
                return;

            _selectedKeyframes.Add((track, clipIdx, kfIdx));

            // 更新主选中为最后添加的
            SelectedTrack = track;
            SelectedClipIndex = clipIdx;
            SelectedKeyframeIndex = kfIdx;
            if (clipIdx >= 0 && clipIdx < track.Clips.Count)
            {
                var clipVm = track.Clips[clipIdx];
                if (kfIdx >= 0 && kfIdx < clipVm.Keyframes.Count)
                    SelectedKeyframe = clipVm.Keyframes[kfIdx];
            }

            this.RaisePropertyChanged(nameof(IsMultiSelectMode));
            this.RaisePropertyChanged(nameof(SelectedKeyframeCount));
        }

        /// <summary>切换关键帧的多选状态 (Ctrl+Click)</summary>
        public void ToggleSelection(TrackViewModel track, int clipIdx, int kfIdx)
        {
            int idx = _selectedKeyframes.FindIndex(
                s => s.Track == track && s.ClipIdx == clipIdx && s.KfIdx == kfIdx
            );
            if (idx >= 0)
            {
                _selectedKeyframes.RemoveAt(idx);
                // 如果移除的是主选中, 切换到列表中最后一个
                if (_selectedKeyframes.Count > 0)
                {
                    var last = _selectedKeyframes[_selectedKeyframes.Count - 1];
                    SelectKeyframe(last.Track, last.ClipIdx, last.KfIdx);
                }
                else
                {
                    ClearKeyframeSelection();
                }
            }
            else
            {
                AddToSelection(track, clipIdx, kfIdx);
            }

            this.RaisePropertyChanged(nameof(IsMultiSelectMode));
            this.RaisePropertyChanged(nameof(SelectedKeyframeCount));
        }

        /// <summary>检查指定关键帧是否在多选列表中</summary>
        public bool IsKeyframeSelected(TrackViewModel track, int clipIdx, int kfIdx)
        {
            return _selectedKeyframes.Any(
                s => s.Track == track && s.ClipIdx == clipIdx && s.KfIdx == kfIdx
            );
        }

        /// <summary>获取指定轨道上的多选关键帧</summary>
        public IEnumerable<(int ClipIdx, int KfIdx)> GetMultiSelectedForTrack(TrackViewModel track)
        {
            return _selectedKeyframes
                .Where(s => s.Track == track)
                .Select(s => (s.ClipIdx, s.KfIdx));
        }

        /// <summary>获取多选关键帧的统计信息</summary>
        public (
            double MinTime,
            double MaxTime,
            float MinValue,
            float MaxValue,
            float AvgValue
        ) GetMultiSelectionStats()
        {
            if (_selectedKeyframes.Count == 0)
                return (0, 0, 0, 0, 0);

            double minTime = double.MaxValue,
                maxTime = double.MinValue;
            float minVal = float.MaxValue,
                maxVal = float.MinValue;
            double sumVal = 0;
            int count = 0;

            foreach (var (track, clipIdx, kfIdx) in _selectedKeyframes)
            {
                if (clipIdx < 0 || clipIdx >= track.Clips.Count)
                    continue;
                var clipVm = track.Clips[clipIdx];
                if (kfIdx < 0 || kfIdx >= clipVm.Keyframes.Count)
                    continue;

                var kf = clipVm.Keyframes[kfIdx];
                if (kf.TimeMs < minTime)
                    minTime = kf.TimeMs;
                if (kf.TimeMs > maxTime)
                    maxTime = kf.TimeMs;
                if (kf.Value < minVal)
                    minVal = kf.Value;
                if (kf.Value > maxVal)
                    maxVal = kf.Value;
                sumVal += kf.Value;
                count++;
            }

            if (count == 0)
                return (0, 0, 0, 0, 0);
            return (minTime, maxTime, minVal, maxVal, (float)(sumVal / count));
        }

        /// <summary>
        /// 在指定轨道的指定时间添加关键帧
        /// </summary>
        public void AddKeyframeAtTime(TrackViewModel track, double absoluteTimeMs)
        {
            if (track.Clips.Count == 0)
                return;

            // 找到包含该时间的 clip
            ClipViewModel? targetClip = null;
            int targetClipIdx = -1;
            for (int ci = 0; ci < track.Clips.Count; ci++)
            {
                var clipVm = track.Clips[ci];
                if (absoluteTimeMs >= clipVm.StartMs && absoluteTimeMs <= clipVm.EndMs)
                {
                    targetClip = clipVm;
                    targetClipIdx = ci;
                    break;
                }
            }

            // 如果没有找到包含该时间的 clip, 使用最后一个 clip 并扩展其 EndMs
            if (targetClip == null)
            {
                // 查找离 absoluteTimeMs 最近的 clip (通常是最后一个)
                int lastIdx = track.Clips.Count - 1;
                var lastClip = track.Clips[lastIdx];
                if (absoluteTimeMs > lastClip.EndMs)
                {
                    targetClip = lastClip;
                    targetClipIdx = lastIdx;
                }
                else if (absoluteTimeMs < track.Clips[0].StartMs)
                {
                    targetClip = track.Clips[0];
                    targetClipIdx = 0;
                }
            }

            if (targetClip == null || targetClipIdx < 0)
                return;

            double localMs = absoluteTimeMs - targetClip.StartMs;
            // 如果超出 clip 末端, 先扩展 EndMs
            if (absoluteTimeMs > targetClip.EndMs)
            {
                targetClip.EndMs = absoluteTimeMs + 500;
            }

            // 计算插值后的当前值作为新关键帧初始值
            float value = Services.Motion.InterpolationEngine.Evaluate(
                targetClip.Clip.Keyframes,
                localMs
            );

            // Bool 轨道: 值必须严格为 0 或 1, 且使用阶梯插值
            bool isBool = track.ValueType == "bool";
            if (isBool)
                value = value >= 0.5f ? 1f : 0f;
            string interpolation = isBool ? "step" : "bezier";

            int clipIdx = targetClipIdx;
            KeyframeViewModel? addedKfVm = null;

            _undoRedo.Execute(
                new LambdaCommand(
                    "添加关键帧",
                    () =>
                    {
                        addedKfVm = targetClip.AddKeyframe(localMs, value, interpolation);
                        int kfIdx = targetClip.Keyframes.IndexOf(addedKfVm);
                        SelectKeyframe(track, clipIdx, kfIdx);
                        NotifyTrackDataChanged(track);
                        RecalculateDuration();
                    },
                    () =>
                    {
                        if (addedKfVm != null)
                        {
                            targetClip.RemoveKeyframe(addedKfVm);
                            ClearKeyframeSelection();
                            NotifyTrackDataChanged(track);
                            RecalculateDuration();
                        }
                    }
                )
            );
            MarkDirty();
        }

        /// <summary>
        /// 在播放头位置添加关键帧 (快捷键 K, 工具栏按钮)
        /// </summary>
        public void AddKeyframeAtPlayhead()
        {
            // 优先使用选中的轨道, 否则使用第一条轨道
            var targetTrack = SelectedTrack ?? (Tracks.Count > 0 ? Tracks[0] : null);
            if (targetTrack == null)
                return;

            AddKeyframeAtTime(targetTrack, CurrentTimeMs);
        }

        /// <summary>
        /// 移动关键帧 (拖拽时调用)
        /// </summary>
        public void MoveKeyframe(
            TrackViewModel track,
            int clipIdx,
            int kfIdx,
            double newTimeMs,
            float newValue
        )
        {
            if (clipIdx < 0 || clipIdx >= track.Clips.Count)
                return;
            var clipVm = track.Clips[clipIdx];
            if (kfIdx < 0 || kfIdx >= clipVm.Keyframes.Count)
                return;

            // 对拖拽时间应用吸附 (转为绝对时间后吸附, 再转回本地时间)
            double absTime = clipVm.StartMs + newTimeMs;
            double snappedAbs = ApplySnap(absTime);
            newTimeMs = snappedAbs - clipVm.StartMs;
            if (newTimeMs < 0)
                newTimeMs = 0;

            // Bool 轨道: 值必须严格为 0 或 1
            if (track.ValueType == "bool")
                newValue = newValue >= 0.5f ? 1f : 0f;

            var kfVm = clipVm.Keyframes[kfIdx];
            kfVm.TimeMs = newTimeMs;
            kfVm.Value = newValue;

            // 关键帧超出 clip 末端时自动扩展 clip
            double absEnd = clipVm.StartMs + newTimeMs;
            if (absEnd > clipVm.EndMs)
                clipVm.EndMs = absEnd + 500;

            // 拖拽后按时间重新排序关键帧, 更新选中索引
            int newIdx = clipVm.SortKeyframesByTime(kfVm);
            if (newIdx >= 0 && newIdx != kfIdx)
            {
                // 更新选中索引以跟踪移动后的关键帧
                SelectedKeyframeIndex = newIdx;
                if (_selectedKeyframes.Count > 0)
                {
                    _selectedKeyframes.Clear();
                    _selectedKeyframes.Add((track, clipIdx, newIdx));
                }
            }

            NotifyTrackDataChanged(track);
            RecalculateDuration();
            MarkDirty();
        }

        /// <summary>
        /// 移动关键帧 — 不应用吸附 (CurveEditorControl 拖拽完成后调用, 拖拽期间已吸附)
        /// </summary>
        public void MoveKeyframeDirect(
            TrackViewModel track,
            int clipIdx,
            int kfIdx,
            double newTimeMs,
            float newValue
        )
        {
            if (clipIdx < 0 || clipIdx >= track.Clips.Count)
                return;
            var clipVm = track.Clips[clipIdx];
            if (kfIdx < 0 || kfIdx >= clipVm.Keyframes.Count)
                return;

            if (newTimeMs < 0)
                newTimeMs = 0;

            // Bool 轨道: 值必须严格为 0 或 1
            if (track.ValueType == "bool")
                newValue = newValue >= 0.5f ? 1f : 0f;

            var kfVm = clipVm.Keyframes[kfIdx];
            kfVm.TimeMs = newTimeMs;
            kfVm.Value = newValue;

            // 关键帧超出 clip 末端时自动扩展 clip
            double absEnd = clipVm.StartMs + newTimeMs;
            if (absEnd > clipVm.EndMs)
                clipVm.EndMs = absEnd + 500;

            // 拖拽后按时间重新排序关键帧, 更新选中索引
            int newIdx = clipVm.SortKeyframesByTime(kfVm);
            if (newIdx >= 0 && newIdx != kfIdx)
            {
                SelectedKeyframeIndex = newIdx;
                if (_selectedKeyframes.Count > 0)
                {
                    _selectedKeyframes.Clear();
                    _selectedKeyframes.Add((track, clipIdx, newIdx));
                }
            }

            NotifyTrackDataChanged(track);
            RecalculateDuration();
            MarkDirty();
        }

        /// <summary>
        /// 删除选中的关键帧
        /// </summary>
        public void DeleteSelectedKeyframe()
        {
            if (SelectedTrack == null || SelectedClipIndex < 0 || SelectedKeyframeIndex < 0)
                return;

            DeleteKeyframe(SelectedTrack, SelectedClipIndex, SelectedKeyframeIndex);
        }

        /// <summary>
        /// 删除指定关键帧
        /// </summary>
        public void DeleteKeyframe(TrackViewModel track, int clipIdx, int kfIdx)
        {
            if (clipIdx < 0 || clipIdx >= track.Clips.Count)
                return;
            var clipVm = track.Clips[clipIdx];
            if (kfIdx < 0 || kfIdx >= clipVm.Keyframes.Count)
                return;

            // 至少保留 1 个关键帧
            if (clipVm.Keyframes.Count <= 1)
                return;

            var kfVm = clipVm.Keyframes[kfIdx];
            double savedTimeMs = kfVm.TimeMs;
            float savedValue = kfVm.Value;
            string savedInterp = kfVm.Interpolation;
            float? savedTangentIn = kfVm.TangentIn;
            float? savedTangentOut = kfVm.TangentOut;
            string savedEventName = kfVm.EventName;
            string? savedEventData = kfVm.EventData;

            _undoRedo.Execute(
                new LambdaCommand(
                    "删除关键帧",
                    () =>
                    {
                        clipVm.RemoveKeyframe(kfVm);
                        ClearKeyframeSelection();
                        NotifyTrackDataChanged(track);
                        RecalculateDuration();
                    },
                    () =>
                    {
                        var restored = clipVm.AddKeyframe(savedTimeMs, savedValue);
                        restored.Interpolation = savedInterp;
                        restored.TangentIn = savedTangentIn;
                        restored.TangentOut = savedTangentOut;
                        restored.EventName = savedEventName;
                        restored.EventData = savedEventData;
                        int restoredIdx = clipVm.Keyframes.IndexOf(restored);
                        SelectKeyframe(track, clipIdx, restoredIdx);
                        NotifyTrackDataChanged(track);
                        RecalculateDuration();
                    }
                )
            );
            MarkDirty();
        }

        // ---- 批量操作 ----

        /// <summary>
        /// 批量删除所有选中的关键帧
        /// </summary>
        public int DeleteSelectedKeyframes()
        {
            if (_selectedKeyframes.Count == 0)
                return 0;

            // 从后往前删除, 避免索引偏移问题
            var sorted = _selectedKeyframes.OrderByDescending(s => s.KfIdx).ToList();

            // 收集可删除的关键帧快照 (用于 undo/redo)
            var deletable =
                new List<(
                    TrackViewModel Track,
                    int ClipIdx,
                    int KfIdx,
                    double TimeMs,
                    float Value,
                    string Interp,
                    float? TangentIn,
                    float? TangentOut,
                    string EventName,
                    string? EventData
                )>();

            foreach (var (track, clipIdx, kfIdx) in sorted)
            {
                if (clipIdx < 0 || clipIdx >= track.Clips.Count)
                    continue;
                var clipVm = track.Clips[clipIdx];
                if (kfIdx < 0 || kfIdx >= clipVm.Keyframes.Count)
                    continue;
                // 至少保留 1 个关键帧
                if (clipVm.Keyframes.Count <= 1)
                    continue;

                var kf = clipVm.Keyframes[kfIdx];
                deletable.Add(
                    (
                        track,
                        clipIdx,
                        kfIdx,
                        kf.TimeMs,
                        kf.Value,
                        kf.Interpolation,
                        kf.TangentIn,
                        kf.TangentOut,
                        kf.EventName,
                        kf.EventData
                    )
                );
            }

            if (deletable.Count == 0)
                return 0;

            _undoRedo.Execute(
                new LambdaCommand(
                    $"批量删除 {deletable.Count} 个关键帧",
                    () =>
                    {
                        var affectedTracks = new HashSet<TrackViewModel>();
                        foreach (var item in deletable)
                        {
                            var clipVm = item.Track.Clips[item.ClipIdx];
                            if (item.KfIdx < clipVm.Keyframes.Count)
                            {
                                clipVm.RemoveKeyframe(clipVm.Keyframes[item.KfIdx]);
                                affectedTracks.Add(item.Track);
                            }
                        }
                        ClearKeyframeSelection();
                        foreach (var track in affectedTracks)
                            NotifyTrackDataChanged(track);
                        RecalculateDuration();
                    },
                    () =>
                    {
                        var affectedTracks = new HashSet<TrackViewModel>();
                        // 按正序恢复
                        foreach (var item in deletable.AsEnumerable().Reverse())
                        {
                            var clipVm = item.Track.Clips[item.ClipIdx];
                            var restored = clipVm.AddKeyframe(item.TimeMs, item.Value);
                            restored.Interpolation = item.Interp;
                            restored.TangentIn = item.TangentIn;
                            restored.TangentOut = item.TangentOut;
                            restored.EventName = item.EventName;
                            restored.EventData = item.EventData;
                            affectedTracks.Add(item.Track);
                        }
                        foreach (var track in affectedTracks)
                            NotifyTrackDataChanged(track);
                        RecalculateDuration();
                    }
                )
            );
            MarkDirty();
            return deletable.Count;
        }

        /// <summary>
        /// 批量设置选中关键帧的插值类型 (支持 Undo/Redo)
        /// </summary>
        public void SetSelectedKeyframesInterpolation(string interpolation)
        {
            if (_selectedKeyframes.Count == 0)
                return;

            // 快照旧插值值
            var snapshot =
                new List<(TrackViewModel track, int clipIdx, int kfIdx, string oldInterp)>();
            foreach (var (track, clipIdx, kfIdx) in _selectedKeyframes)
            {
                if (clipIdx < 0 || clipIdx >= track.Clips.Count)
                    continue;
                var clipVm = track.Clips[clipIdx];
                if (kfIdx < 0 || kfIdx >= clipVm.Keyframes.Count)
                    continue;
                snapshot.Add((track, clipIdx, kfIdx, clipVm.Keyframes[kfIdx].Interpolation));
            }

            if (snapshot.Count == 0)
                return;

            _undoRedo.Execute(
                new LambdaCommand(
                    "设置插值类型",
                    () =>
                    {
                        var affected = new HashSet<TrackViewModel>();
                        foreach (var (track, clipIdx, kfIdx, _) in snapshot)
                        {
                            track.Clips[clipIdx].Keyframes[kfIdx].Interpolation = interpolation;
                            affected.Add(track);
                        }
                        foreach (var track in affected)
                            NotifyTrackDataChanged(track);
                    },
                    () =>
                    {
                        var affected = new HashSet<TrackViewModel>();
                        foreach (var (track, clipIdx, kfIdx, oldInterp) in snapshot)
                        {
                            track.Clips[clipIdx].Keyframes[kfIdx].Interpolation = oldInterp;
                            affected.Add(track);
                        }
                        foreach (var track in affected)
                            NotifyTrackDataChanged(track);
                    }
                )
            );
            MarkDirty();
        }

        /// <summary>
        /// 批量移动选中关键帧 (偏移量)
        /// </summary>
        public void MoveSelectedKeyframes(double deltaTimeMs)
        {
            if (_selectedKeyframes.Count == 0)
                return;

            // 快照旧时间值
            var snapshot =
                new List<(TrackViewModel track, int clipIdx, int kfIdx, double oldTime)>();
            foreach (var (track, clipIdx, kfIdx) in _selectedKeyframes)
            {
                if (clipIdx < 0 || clipIdx >= track.Clips.Count)
                    continue;
                var clipVm = track.Clips[clipIdx];
                if (kfIdx < 0 || kfIdx >= clipVm.Keyframes.Count)
                    continue;
                snapshot.Add((track, clipIdx, kfIdx, clipVm.Keyframes[kfIdx].TimeMs));
            }

            if (snapshot.Count == 0)
                return;

            _undoRedo.Execute(
                new LambdaCommand(
                    "移动关键帧",
                    () =>
                    {
                        var affected = new HashSet<TrackViewModel>();
                        foreach (var (track, clipIdx, kfIdx, oldTime) in snapshot)
                        {
                            double newTime = Math.Max(0, oldTime + deltaTimeMs);
                            track.Clips[clipIdx].Keyframes[kfIdx].TimeMs = newTime;
                            affected.Add(track);
                        }
                        foreach (var track in affected)
                            NotifyTrackDataChanged(track);
                        RecalculateDuration();
                    },
                    () =>
                    {
                        var affected = new HashSet<TrackViewModel>();
                        foreach (var (track, clipIdx, kfIdx, oldTime) in snapshot)
                        {
                            track.Clips[clipIdx].Keyframes[kfIdx].TimeMs = oldTime;
                            affected.Add(track);
                        }
                        foreach (var track in affected)
                            NotifyTrackDataChanged(track);
                        RecalculateDuration();
                    }
                )
            );
            MarkDirty();
        }

        /// <summary>
        /// 复制选中的关键帧到剪贴板
        /// 支持单选和多选模式
        /// </summary>
        public void CopySelectedKeyframes()
        {
            _clipboardKeyframes.Clear();

            // 收集所有要复制的关键帧 (使用绝对时间)
            var keyframesToCopy =
                new List<(
                    double AbsTimeMs,
                    float Value,
                    string Interpolation,
                    float? TangentIn,
                    float? TangentOut,
                    string TrackName
                )>();

            if (_selectedKeyframes.Count > 0)
            {
                // 多选模式
                foreach (var (track, clipIdx, kfIdx) in _selectedKeyframes)
                {
                    if (clipIdx < 0 || clipIdx >= track.Clips.Count)
                        continue;
                    var clipVm = track.Clips[clipIdx];
                    if (kfIdx < 0 || kfIdx >= clipVm.Keyframes.Count)
                        continue;

                    var kf = clipVm.Keyframes[kfIdx];
                    // 转换为绝对时间 = clip 起始 + 关键帧本地时间
                    double absTime = clipVm.StartMs + kf.TimeMs;
                    keyframesToCopy.Add(
                        (
                            absTime,
                            kf.Value,
                            kf.Interpolation,
                            kf.TangentIn,
                            kf.TangentOut,
                            track.Label
                        )
                    );
                }
            }
            else if (SelectedKeyframe != null && SelectedTrack != null)
            {
                // 单选模式
                var kf = SelectedKeyframe;
                double absTime = kf.TimeMs;
                // 加上当前选中 clip 的 StartMs
                if (SelectedClipIndex >= 0 && SelectedClipIndex < SelectedTrack.Clips.Count)
                    absTime = SelectedTrack.Clips[SelectedClipIndex].StartMs + kf.TimeMs;
                keyframesToCopy.Add(
                    (
                        absTime,
                        kf.Value,
                        kf.Interpolation,
                        kf.TangentIn,
                        kf.TangentOut,
                        SelectedTrack.Label
                    )
                );
            }

            if (keyframesToCopy.Count == 0)
                return;

            // 计算最早的时间作为基准
            double minTime = keyframesToCopy.Min(k => k.AbsTimeMs);

            // 存储相对时间偏移 (基于绝对时间)
            foreach (var (absTimeMs, value, interp, tangIn, tangOut, trackName) in keyframesToCopy)
            {
                _clipboardKeyframes.Add(
                    (absTimeMs - minTime, value, interp, tangIn, tangOut, trackName)
                );
            }

            this.RaisePropertyChanged(nameof(HasClipboardKeyframes));
        }

        /// <summary>
        /// 粘贴关键帧到当前播放头位置
        /// 优先粘贴到同名轨道，否则粘贴到选中轨道
        /// </summary>
        public void PasteKeyframes()
        {
            if (_clipboardKeyframes.Count == 0)
                return;
            if (SelectedTrack == null && Tracks.Count == 0)
                return;

            double pasteTimeMs = CurrentTimeMs;
            var affectedTracks = new HashSet<TrackViewModel>();
            var addedKeyframes =
                new List<(TrackViewModel Track, int ClipIdx, int KfIdx, double TimeMs)>();
            // 记录被覆盖的旧值 (用于撤销)
            var overwrittenValues =
                new List<(
                    TrackViewModel Track,
                    int ClipIdx,
                    double TimeMs,
                    float OldValue,
                    string OldInterp,
                    float? OldTangIn,
                    float? OldTangOut
                )>();

            foreach (
                var (relTime, value, interp, tangIn, tangOut, trackName) in _clipboardKeyframes
            )
            {
                double targetAbsTime = pasteTimeMs + relTime;

                // 尝试找到同名轨道
                TrackViewModel? targetTrack = Tracks.FirstOrDefault(t => t.Label == trackName);
                if (targetTrack == null)
                    targetTrack = SelectedTrack ?? Tracks.FirstOrDefault();
                if (targetTrack == null)
                    continue;

                // 确保轨道至少有一个 clip
                if (targetTrack.Clips.Count == 0)
                    continue;

                // 找到包含目标绝对时间的 clip (逻辑同 AddKeyframeAtTime)
                ClipViewModel? clipVm = null;
                int clipIdx = -1;
                for (int ci = 0; ci < targetTrack.Clips.Count; ci++)
                {
                    var c = targetTrack.Clips[ci];
                    if (targetAbsTime >= c.StartMs && targetAbsTime <= c.EndMs)
                    {
                        clipVm = c;
                        clipIdx = ci;
                        break;
                    }
                }
                if (clipVm == null)
                {
                    // 使用最后一个 clip 并扩展
                    clipIdx = targetTrack.Clips.Count - 1;
                    clipVm = targetTrack.Clips[clipIdx];
                    if (targetAbsTime > clipVm.EndMs)
                        clipVm.EndMs = targetAbsTime + 500;
                }

                // 绝对时间 → clip 本地时间
                double localTime = targetAbsTime - clipVm.StartMs;
                if (localTime < 0)
                    localTime = 0;

                // Bool 轨道: 值必须严格为 0 或 1
                float pasteValue = value;
                if (targetTrack.ValueType == "bool")
                    pasteValue = value >= 0.5f ? 1f : 0f;

                // 检查是否在该本地时间附近已有关键帧
                var existingKf = clipVm.Keyframes.FirstOrDefault(
                    k => Math.Abs(k.TimeMs - localTime) < 1.0
                );
                if (existingKf != null)
                {
                    // 已有关键帧 → 覆盖其值 (粘贴值模式)
                    overwrittenValues.Add(
                        (
                            targetTrack,
                            clipIdx,
                            existingKf.TimeMs,
                            existingKf.Value,
                            existingKf.Interpolation,
                            existingKf.TangentIn,
                            existingKf.TangentOut
                        )
                    );

                    existingKf.Value = pasteValue;
                    existingKf.Interpolation = interp;
                    existingKf.TangentIn = tangIn;
                    existingKf.TangentOut = tangOut;
                    affectedTracks.Add(targetTrack);
                    continue;
                }

                // 无关键帧 → 使用 ClipViewModel.AddKeyframe 同时更新模型和 VM
                var newKfVm = clipVm.AddKeyframe(localTime, pasteValue, interp);
                newKfVm.TangentIn = tangIn;
                newKfVm.TangentOut = tangOut;

                int newIdx = clipVm.Keyframes.IndexOf(newKfVm);
                addedKeyframes.Add((targetTrack, clipIdx, newIdx, localTime));
                affectedTracks.Add(targetTrack);
            }

            if (addedKeyframes.Count == 0 && overwrittenValues.Count == 0)
                return;

            // 注册撤销操作
            var undoSnapshot = addedKeyframes.Select(a => (a.Track, a.ClipIdx, a.TimeMs)).ToList();
            var overwriteSnapshot = overwrittenValues.ToList();
            _undoRedo.Execute(
                new LambdaCommand(
                    "粘贴关键帧",
                    () => { }, // 已经执行了粘贴
                    () =>
                    {
                        // 撤销：删除新增的关键帧
                        foreach (var (track, clipIdx, timeMs) in undoSnapshot)
                        {
                            if (clipIdx >= 0 && clipIdx < track.Clips.Count)
                            {
                                var clip = track.Clips[clipIdx];
                                var kfToRemove = clip.Keyframes.FirstOrDefault(
                                    k => Math.Abs(k.TimeMs - timeMs) < 0.5
                                );
                                if (kfToRemove != null)
                                    clip.Keyframes.Remove(kfToRemove);
                            }
                            NotifyTrackDataChanged(track);
                        }
                        // 撤销：恢复被覆盖的关键帧旧值
                        foreach (
                            var (
                                track,
                                clipIdx,
                                timeMs,
                                oldVal,
                                oldInterp,
                                oldTangIn,
                                oldTangOut
                            ) in overwriteSnapshot
                        )
                        {
                            if (clipIdx >= 0 && clipIdx < track.Clips.Count)
                            {
                                var clip = track.Clips[clipIdx];
                                var kf = clip.Keyframes.FirstOrDefault(
                                    k => Math.Abs(k.TimeMs - timeMs) < 0.5
                                );
                                if (kf != null)
                                {
                                    kf.Value = oldVal;
                                    kf.Interpolation = oldInterp;
                                    kf.TangentIn = oldTangIn;
                                    kf.TangentOut = oldTangOut;
                                }
                            }
                            NotifyTrackDataChanged(track);
                        }
                        RecalculateDuration();
                        MarkDirty();
                    }
                )
            );

            foreach (var track in affectedTracks)
                NotifyTrackDataChanged(track);
            RecalculateDuration();
            MarkDirty();
        }

        /// <summary>
        /// 设置关键帧插值类型
        /// </summary>
        public void SetKeyframeInterpolation(
            TrackViewModel track,
            int clipIdx,
            int kfIdx,
            string interpolation
        )
        {
            if (clipIdx < 0 || clipIdx >= track.Clips.Count)
                return;
            var clipVm = track.Clips[clipIdx];
            if (kfIdx < 0 || kfIdx >= clipVm.Keyframes.Count)
                return;

            clipVm.Keyframes[kfIdx].Interpolation = interpolation;

            // 切换到 bezier 时初始化默认切线值 (避免 null 导致平坦曲线与 linear 无区别)
            if (interpolation == "bezier")
            {
                var kf = clipVm.Keyframes[kfIdx];
                if (kf.TangentIn == null)
                    kf.TangentIn = 0f;
                if (kf.TangentOut == null)
                    kf.TangentOut = 0f;
            }

            NotifyTrackDataChanged(track);
        }

        /// <summary>
        /// 批量设置多选关键帧的插值类型 (支持 Undo/Redo)
        /// </summary>
        public void BatchSetInterpolation(string interpolation)
        {
            if (_selectedKeyframes.Count == 0)
                return;

            // 快照旧值
            var snapshots = new List<(KeyframeViewModel Kf, string OldInterp)>();
            var affectedTracks = new HashSet<TrackViewModel>();

            foreach (var (track, clipIdx, kfIdx) in _selectedKeyframes)
            {
                if (clipIdx < 0 || clipIdx >= track.Clips.Count)
                    continue;
                var clipVm = track.Clips[clipIdx];
                if (kfIdx < 0 || kfIdx >= clipVm.Keyframes.Count)
                    continue;
                var kf = clipVm.Keyframes[kfIdx];
                snapshots.Add((kf, kf.Interpolation));
                affectedTracks.Add(track);
            }

            if (snapshots.Count == 0)
                return;

            _undoRedo.Execute(
                new LambdaCommand(
                    $"批量设置插值 {interpolation}",
                    () =>
                    {
                        foreach (var (kf, _) in snapshots)
                            kf.Interpolation = interpolation;
                        foreach (var t in affectedTracks)
                            NotifyTrackDataChanged(t);
                    },
                    () =>
                    {
                        foreach (var (kf, oldInterp) in snapshots)
                            kf.Interpolation = oldInterp;
                        foreach (var t in affectedTracks)
                            NotifyTrackDataChanged(t);
                    }
                )
            );
            MarkDirty();
        }

        /// <summary>
        /// 批量应用曲线预设到多选关键帧 (委托给 CurvePresetService)
        /// </summary>
        public void BatchApplyPreset(string presetName)
        {
            if (_selectedKeyframes.Count == 0)
                return;

            var preset = CurvePresetService.FindPreset(presetName);
            if (preset == null)
                return;

            CurvePresetService.ApplyPresetBatch(this, _selectedKeyframes, preset);
        }

        /// <summary>
        /// 更新关键帧属性 (由属性面板调用, 支持 Undo/Redo)
        /// </summary>
        public void UpdateKeyframeProperties(
            double timeMs,
            float value,
            string interpolation,
            float? tangentIn,
            float? tangentOut,
            float? cp1x = null,
            float? cp2x = null
        )
        {
            if (SelectedKeyframe is null)
                return;

            // Bool 轨道: 值必须严格为 0 或 1
            if (SelectedTrack?.ValueType == "bool")
                value = value >= 0.5f ? 1f : 0f;

            // 快照旧属性
            var kf = SelectedKeyframe;
            var track = SelectedTrack;
            double oldTime = kf.TimeMs;
            float oldValue = kf.Value;
            string oldInterp = kf.Interpolation;
            float? oldTangentIn = kf.TangentIn;
            float? oldTangentOut = kf.TangentOut;
            float? oldCp1x = kf.Cp1x;
            float? oldCp2x = kf.Cp2x;

            _undoRedo.Execute(
                new LambdaCommand(
                    "修改关键帧属性",
                    () =>
                    {
                        kf.TimeMs = timeMs;
                        kf.Value = value;
                        kf.Interpolation = interpolation;
                        kf.TangentIn = tangentIn;
                        kf.TangentOut = tangentOut;
                        kf.Cp1x = cp1x;
                        kf.Cp2x = cp2x;
                        if (track is not null)
                            NotifyTrackDataChanged(track);
                    },
                    () =>
                    {
                        kf.TimeMs = oldTime;
                        kf.Value = oldValue;
                        kf.Interpolation = oldInterp;
                        kf.TangentIn = oldTangentIn;
                        kf.TangentOut = oldTangentOut;
                        kf.Cp1x = oldCp1x;
                        kf.Cp2x = oldCp2x;
                        if (track is not null)
                            NotifyTrackDataChanged(track);
                    }
                )
            );
            MarkDirty();
        }

        /// <summary>
        /// 更新选中关键帧的事件绑定 (支持 Undo/Redo)
        /// </summary>
        public void UpdateKeyframeEvent(string eventName, string? eventData)
        {
            if (SelectedKeyframe is null)
                return;

            var kf = SelectedKeyframe;
            string oldEventName = kf.EventName ?? "";
            string oldEventData = kf.EventData ?? "";
            string newEventData = eventData ?? "";

            _undoRedo.Execute(
                new LambdaCommand(
                    "修改关键帧事件",
                    () =>
                    {
                        kf.EventName = eventName;
                        kf.EventData = newEventData;
                    },
                    () =>
                    {
                        kf.EventName = oldEventName;
                        kf.EventData = oldEventData;
                    }
                )
            );
            MarkDirty();
        }

        /// <summary>
        /// 将指定插值类型应用到当前选中 Clip 的所有关键帧
        /// </summary>
        public void ApplyInterpolationToClip(string interpolation)
        {
            var track = SelectedTrack;
            if (track == null || SelectedClipIndex < 0 || SelectedClipIndex >= track.Clips.Count)
                return;

            var clipVm = track.Clips[SelectedClipIndex];
            if (clipVm.Keyframes.Count == 0)
                return;

            // 快照旧值
            var snapshots = clipVm.Keyframes
                .Select(kf => (Kf: kf, OldInterp: kf.Interpolation))
                .ToList();

            _undoRedo.Execute(
                new LambdaCommand(
                    $"应用插值到全部: {interpolation}",
                    () =>
                    {
                        foreach (var (kf, _) in snapshots)
                            kf.Interpolation = interpolation;
                        NotifyTrackDataChanged(track);
                    },
                    () =>
                    {
                        foreach (var (kf, oldInterp) in snapshots)
                            kf.Interpolation = oldInterp;
                        NotifyTrackDataChanged(track);
                    }
                )
            );
            MarkDirty();
        }
    }
}
