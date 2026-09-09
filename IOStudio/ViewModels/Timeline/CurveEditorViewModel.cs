using System;
using System.Collections.Generic;
using System.Linq;
using IOStudio.Controls.Timeline;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using ReactiveUI;

namespace IOStudio.ViewModels.Timeline
{
    /// <summary>
    /// 曲线编辑器 ViewModel — 桥接 CurveEditorControl 与 TimelineEditorViewModel
    /// 管理曲线视图的轨道数据、选中状态和编辑操作
    /// </summary>
    public class CurveEditorViewModel : ViewModelBase
    {
        private readonly TimelineEditorViewModel _parent;

        public CurveEditorViewModel(TimelineEditorViewModel parent)
        {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        }

        // ═══════ 曲线轨道数据 ═══════

        private List<CurveTrackData>? _curveTracks;

        /// <summary>曲线轨道数据列表 (供 CurveEditorControl 绑定)</summary>
        public List<CurveTrackData>? CurveTracks
        {
            get => _curveTracks;
            set => this.RaiseAndSetIfChanged(ref _curveTracks, value);
        }

        // ═══════ 选中状态 ═══════

        private int _selectedTrackIdx = -1;

        /// <summary>当前选中的曲线轨道索引</summary>
        public int SelectedTrackIdx
        {
            get => _selectedTrackIdx;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedTrackIdx, value);
                this.RaisePropertyChanged(nameof(SelectedTrackLabel));
            }
        }

        /// <summary>选中轨道的显示名称</summary>
        public string SelectedTrackLabel =>
            _selectedTrackIdx >= 0 && CurveTracks != null && _selectedTrackIdx < CurveTracks.Count
                ? CurveTracks[_selectedTrackIdx].Label
                : "(无选中)";

        // ═══════ 数据同步 ═══════

        /// <summary>
        /// 从 TimelineEditorViewModel 同步轨道数据到曲线编辑器
        /// </summary>
        public void SyncFromTimeline()
        {
            var data = new List<CurveTrackData>();
            foreach (var tvm in _parent.Tracks)
            {
                // 关键帧必须升序, 否则 InterpolationEngine 的二分查找与曲线渲染都会产生"毛刺/锯齿"
                // (相邻帧间 te<=ts 被跳过, 插图把远点连成尖峰)。此处做防御性排序副本,
                // 不修改模型共享引用, 仅在曲线视图内保证有序渲染。
                var clips = (tvm.Clips?.Select(c => c.Clip) ?? Enumerable.Empty<MotionClip>())
                    .Select(c =>
                    {
                        if (c.Keyframes.Count == 0)
                            return c;
                        bool needSort = false;
                        for (int i = 1; i < c.Keyframes.Count; i++)
                        {
                            if (c.Keyframes[i].TimeMs < c.Keyframes[i - 1].TimeMs)
                            {
                                needSort = true;
                                break;
                            }
                        }
                        if (!needSort)
                            return c;
                        var copy = new MotionClip
                        {
                            Id = c.Id,
                            StartMs = c.StartMs,
                            EndMs = c.EndMs,
                            ActionInstanceId = c.ActionInstanceId,
                            SourceDefinitionId = c.SourceDefinitionId,
                            SourceRole = c.SourceRole,
                            SourceActionName = c.SourceActionName,
                            Keyframes = c.Keyframes.OrderBy(k => k.TimeMs).ToList()
                        };
                        return copy;
                    })
                    .ToList();
                data.Add(
                    new CurveTrackData
                    {
                        Label = tvm.Label,
                        Color = tvm.Color,
                        ValueType = tvm.ValueType,
                        NeutralValue = tvm.NeutralValue,
                        IdleLoop = tvm.IdleLoop,
                        OverrideKeyframes = tvm.OverrideKeyframes,
                        IsMuted = tvm.IsMuted,
                        IsSolo = tvm.IsSolo,
                        IsEnabled = tvm.IsEnabled,
                        IsLocked = tvm.IsLocked,
                        ShowInCurve = tvm.ShowInCurve,
                        Clips = clips
                    }
                );
            }
            CurveTracks = data;
        }

        // ═══════ 曲线编辑操作 ═══════

        /// <summary>
        /// 曲线关键帧拖拽提交 → 注册撤销/重做 (undo=起始快照, redo=拖后值)。
        /// </summary>
        public void CommitKeyframeEdit(
            IReadOnlyList<(MotionKeyframe Kf, double T0, float V0, double T1, float V1)> edits
        )
        {
            if (edits == null || edits.Count == 0)
                return;
            var list = edits.ToList();
            var editedKeyframes = list.Select(item => item.Kf).ToHashSet();
            var affectedTracks = _parent.Tracks
                .Where(track =>
                    track.Clips.Any(clip =>
                        clip.Clip.Keyframes.Any(keyframe => editedKeyframes.Contains(keyframe))
                    )
                )
                .ToList();
            if (!_parent.TryBeginTrackEdit(affectedTracks, "移动关键帧"))
            {
                ApplyKeyframeEdit(list, false);
                RefreshCurveAffected();
                return;
            }
            _parent.ExecuteCommand(
                new LambdaCommand(
                    "移动关键帧",
                    () =>
                    {
                        ApplyKeyframeEdit(list, true);
                        RefreshCurveAffected();
                    },
                    () =>
                    {
                        ApplyKeyframeEdit(list, false);
                        RefreshCurveAffected();
                    }
                )
            );
        }

        private static void ApplyKeyframeEdit(
            List<(MotionKeyframe Kf, double T0, float V0, double T1, float V1)> list,
            bool after
        )
        {
            foreach (var (kf, t0, v0, t1, v1) in list)
            {
                if (kf == null)
                    continue;
                kf.TimeMs = Math.Max(0, after ? t1 : t0);
                kf.Value = after ? v1 : v0;
            }
        }

        /// <summary>
        /// 贝塞尔切线编辑提交 → 注册撤销/重做。
        /// </summary>
        public void CommitTangentEdit(
            (
                int Ti,
                int Ci,
                int Ki,
                float Tin0,
                float Tout0,
                float Cp10,
                float Cp20,
                float Tin1,
                float Tout1,
                float Cp11,
                float Cp21
            ) e
        )
        {
            if (e.Ti < 0 || e.Ti >= _parent.Tracks.Count)
                return;
            var track = _parent.Tracks[e.Ti];
            if (!_parent.TryBeginTrackEdit(track, "调整切线"))
            {
                if (FindKeyframeByCurveIndex(e.Ti, e.Ci, e.Ki) is { } lockedKf)
                {
                    lockedKf.TangentIn = e.Tin0;
                    lockedKf.TangentOut = e.Tout0;
                    lockedKf.Cp1x = e.Cp10;
                    lockedKf.Cp2x = e.Cp20;
                    _parent.NotifyTrackDataChanged(track);
                }
                return;
            }
            _parent.ExecuteCommand(
                new LambdaCommand(
                    "调整切线",
                    () =>
                    {
                        if (FindKeyframeByCurveIndex(e.Ti, e.Ci, e.Ki) is { } kf)
                        {
                            kf.TangentIn = e.Tin1;
                            kf.TangentOut = e.Tout1;
                            kf.Cp1x = e.Cp11;
                            kf.Cp2x = e.Cp21;
                        }
                        RefreshCurveAffected();
                    },
                    () =>
                    {
                        if (FindKeyframeByCurveIndex(e.Ti, e.Ci, e.Ki) is { } kf)
                        {
                            kf.TangentIn = e.Tin0;
                            kf.TangentOut = e.Tout0;
                            kf.Cp1x = e.Cp10;
                            kf.Cp2x = e.Cp20;
                        }
                        RefreshCurveAffected();
                    }
                )
            );
        }

        private MotionKeyframe? FindKeyframeByCurveIndex(int ti, int ci, int ki)
        {
            if (ti < 0 || ti >= _parent.Tracks.Count)
                return null;
            var clips = _parent.Tracks[ti].Clips;
            if (ci < 0 || ci >= clips.Count)
                return null;
            var kfs = clips[ci].Clip.Keyframes;
            return ki >= 0 && ki < kfs.Count ? kfs[ki] : null;
        }

        /// <summary>撤销/重做后: 排序受影响的 clip 并通知 VM 刷新。</summary>
        private void RefreshCurveAffected()
        {
            try
            {
                foreach (var t in _parent.Tracks)
                {
                    foreach (var clipVm in t.Clips)
                    {
                        var kfs = clipVm.Clip.Keyframes;
                        bool needSort = false;
                        for (int i = 1; i < kfs.Count; i++)
                        {
                            if (kfs[i].TimeMs < kfs[i - 1].TimeMs)
                            {
                                needSort = true;
                                break;
                            }
                        }
                        if (needSort)
                            clipVm.Clip.Keyframes.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
                    }
                    _parent.NotifyTrackDataChanged(t);
                }
            }
            catch { }
        }

        /// <summary>
        /// 处理关键帧选中 (来自 CurveEditorControl.KeyframeSelected)
        /// </summary>
        public void OnKeyframeSelected(int trackIdx, int clipIdx, int kfIdx)
        {
            SelectedTrackIdx = trackIdx;

            if (trackIdx >= 0 && trackIdx < _parent.Tracks.Count)
            {
                var trackVm = _parent.Tracks[trackIdx];
                _parent.SelectKeyframe(trackVm, clipIdx, kfIdx);
            }
        }

        /// <summary>
        /// 处理关键帧移动完成 (来自 CurveEditorControl.KeyframeMoved)
        /// </summary>
        public void OnKeyframeMoved(
            int trackIdx,
            int clipIdx,
            int kfIdx,
            double absTimeMs,
            float value
        )
        {
            if (trackIdx < 0 || trackIdx >= _parent.Tracks.Count)
                return;

            var trackVm = _parent.Tracks[trackIdx];
            if (!_parent.TryBeginTrackEdit(trackVm, "移动关键帧"))
                return;
            if (clipIdx < 0 || clipIdx >= trackVm.Clips.Count)
                return;

            var clipVm = trackVm.Clips[clipIdx];
            double localMs = absTimeMs - clipVm.StartMs;
            if (localMs < 0)
                localMs = 0;

            // CurveEditorControl 拖拽期间直接排序了 Model 层的关键帧列表,
            // 导致 kfIdx 对应 Model 排序后的索引, 但 VM 的 ObservableCollection 未同步.
            // 通过 MotionKeyframe 引用找到正确的 VM 索引.
            int vmIdx = kfIdx;
            if (kfIdx < clipVm.Clip.Keyframes.Count)
            {
                var modelKf = clipVm.Clip.Keyframes[kfIdx];
                for (int i = 0; i < clipVm.Keyframes.Count; i++)
                {
                    if (clipVm.Keyframes[i].Keyframe == modelKf)
                    {
                        vmIdx = i;
                        break;
                    }
                }
            }

            // CurveEditorControl 拖拽期间已完成吸附, 此处不再重复吸附, 直接同步到 ViewModel
            _parent.MoveKeyframeDirect(trackVm, clipIdx, vmIdx, localMs, value);
        }

        /// <summary>
        /// 处理贝塞尔切线修改 (来自 CurveEditorControl.TangentChanged)
        /// </summary>
        public void OnTangentChanged(
            int trackIdx,
            int clipIdx,
            int kfIdx,
            float tangentIn,
            float tangentOut,
            float? cp1x,
            float? cp2x
        )
        {
            if (trackIdx < 0 || trackIdx >= _parent.Tracks.Count)
                return;

            var trackVm = _parent.Tracks[trackIdx];
            if (!_parent.TryBeginTrackEdit(trackVm, "调整切线"))
                return;
            if (clipIdx < 0 || clipIdx >= trackVm.Clips.Count)
                return;
            var clipVm = trackVm.Clips[clipIdx];

            // CurveEditorControl 的 kfIdx 是 Model 层索引, 需映射到 VM 层索引
            int vmIdx = kfIdx;
            if (kfIdx < clipVm.Clip.Keyframes.Count)
            {
                var modelKf = clipVm.Clip.Keyframes[kfIdx];
                for (int i = 0; i < clipVm.Keyframes.Count; i++)
                {
                    if (clipVm.Keyframes[i].Keyframe == modelKf)
                    {
                        vmIdx = i;
                        break;
                    }
                }
            }

            if (vmIdx < 0 || vmIdx >= clipVm.Keyframes.Count)
                return;

            clipVm.Keyframes[vmIdx].TangentIn = tangentIn;
            clipVm.Keyframes[vmIdx].TangentOut = tangentOut;
            clipVm.Keyframes[vmIdx].Cp1x = cp1x;
            clipVm.Keyframes[vmIdx].Cp2x = cp2x;
            trackVm.RaiseClipsChanged();
            _parent.MarkDirty();
        }

        /// <summary>
        /// 处理添加关键帧请求 (来自 CurveEditorControl.AddKeyframeRequested)
        /// </summary>
        public void OnAddKeyframeRequested(int trackIdx, double absTimeMs, float value)
        {
            if (trackIdx < 0 || trackIdx >= _parent.Tracks.Count)
                return;

            var trackVm = _parent.Tracks[trackIdx];
            _parent.AddKeyframeAtTime(trackVm, absTimeMs);
        }

        /// <summary>
        /// 空窗双击请求添加 Overlay 覆盖关键帧 (来自 CurveEditorControl.OverrideKeyframeAddRequested)。
        /// 绝对时间点, 优先级高于 idle, 带 Undo。
        /// </summary>
        public void OnOverrideKeyframeAddRequested(int trackIdx, double absTimeMs, float value)
        {
            if (trackIdx < 0 || trackIdx >= _parent.Tracks.Count)
                return;

            _parent.AddOverrideKeyframeAtTime(_parent.Tracks[trackIdx], absTimeMs, value);
        }

        /// <summary>
        /// 空窗双击请求添加 idle 待机循环关键帧 (来自 CurveEditorControl.IdleKeyframeAddRequested)。
        /// 相位 ∈ [0, period), 带 Undo。
        /// </summary>
        public void OnIdleKeyframeAddRequested(int trackIdx, double phaseMs, float value)
        {
            if (trackIdx < 0 || trackIdx >= _parent.Tracks.Count)
                return;

            _parent.AddIdleKeyframeAtTime(_parent.Tracks[trackIdx], phaseMs, value);
        }

        /// <summary>
        /// Overlay 覆盖关键帧拖拽提交 → 注册撤销/重做。
        /// kf 为模型共享引用 (曲线控件已改值), undo=起始快照, redo=拖后值。
        /// </summary>
        public void CommitOverrideKeyframeEdit(
            TrackViewModel track,
            MotionKeyframe kf,
            double t0,
            float v0,
            double t1,
            float v1
        )
        {
            if (track == null || kf == null)
                return;
            if (!_parent.TryBeginTrackEdit(track, "移动覆盖关键帧"))
            {
                kf.TimeMs = Math.Max(0, t0);
                kf.Value = Math.Clamp(v0, 0f, 1f);
                _parent.NotifyTrackDataChanged(track);
                return;
            }
            _parent.ExecuteCommand(
                new LambdaCommand(
                    "移动覆盖关键帧",
                    () =>
                    {
                        kf.TimeMs = Math.Max(0, t1);
                        kf.Value = Math.Clamp(v1, 0f, 1f);
                        _parent.NotifyTrackDataChanged(track);
                    },
                    () =>
                    {
                        kf.TimeMs = Math.Max(0, t0);
                        kf.Value = Math.Clamp(v0, 0f, 1f);
                        _parent.NotifyTrackDataChanged(track);
                    }
                )
            );
        }

        /// <summary>
        /// idle 幽灵曲线关键帧拖拽提交 → 注册撤销/重做。
        /// kf 为模型共享引用 (曲线控件已改相位/值), undo=起始快照, redo=拖后值。
        /// </summary>
        public void CommitIdleKeyframeEdit(
            TrackViewModel track,
            MotionKeyframe kf,
            double t0,
            float v0,
            double t1,
            float v1
        )
        {
            if (track == null || kf == null || track.IdleLoop == null)
                return;
            if (!_parent.TryBeginTrackEdit(track, "移动 idle 关键帧"))
            {
                double lockedPeriod =
                    track.IdleLoop.PeriodMs > 1.0 ? track.IdleLoop.PeriodMs : 1.0;
                kf.TimeMs = ClampPhase(t0, lockedPeriod);
                kf.Value = Math.Clamp(v0, 0f, 1f);
                _parent.NotifyTrackDataChanged(track);
                return;
            }
            double period = track.IdleLoop.PeriodMs > 1.0 ? track.IdleLoop.PeriodMs : 1.0;
            _parent.ExecuteCommand(
                new LambdaCommand(
                    "移动 idle 关键帧",
                    () =>
                    {
                        kf.TimeMs = ClampPhase(t1, period);
                        kf.Value = Math.Clamp(v1, 0f, 1f);
                        _parent.NotifyTrackDataChanged(track);
                    },
                    () =>
                    {
                        kf.TimeMs = ClampPhase(t0, period);
                        kf.Value = Math.Clamp(v0, 0f, 1f);
                        _parent.NotifyTrackDataChanged(track);
                    }
                )
            );
        }

        private static double ClampPhase(double t, double period)
        {
            double p = t % period;
            if (p < 0)
                p += period;
            return p;
        }

        /// <summary>
        /// idle 幽灵曲线关键帧删除 (带 Undo)。按相位定位删除, 撤销时按原相位/值恢复。
        /// </summary>
        public void OnIdleKeyframeDeleteRequested(TrackViewModel track, double phaseMs, float value)
        {
            if (track == null || track.IdleLoop == null || track.IdleLoop.Keyframes == null)
                return;
            if (!_parent.TryBeginTrackEdit(track, "删除 idle 关键帧"))
                return;
            var idleKfs = track.IdleLoop.Keyframes;
            var kf = idleKfs.FirstOrDefault(k => Math.Abs(k.TimeMs - phaseMs) < 0.5);
            if (kf == null)
                return;
            double t = kf.TimeMs;
            float v = kf.Value;

            _parent.ExecuteCommand(
                new LambdaCommand(
                    "删除 idle 关键帧",
                    () =>
                    {
                        track.IdleLoop!.Keyframes!.Remove(kf);
                        _parent.NotifyTrackDataChanged(track);
                    },
                    () =>
                    {
                        track.IdleLoop!.Keyframes!.Add(
                            new MotionKeyframe
                            {
                                TimeMs = t,
                                Value = v,
                                Interpolation = "linear"
                            }
                        );
                        track.IdleLoop.Keyframes.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
                        _parent.NotifyTrackDataChanged(track);
                    }
                )
            );
        }

        /// <summary>
        /// Overlay 覆盖关键帧删除 (带 Undo)。按时间定位删除, 撤销时按原值恢复。
        /// </summary>
        public void OnOverrideKeyframeDeleteRequested(
            TrackViewModel track,
            double absTimeMs,
            float value
        )
        {
            if (track == null)
                return;
            if (!_parent.TryBeginTrackEdit(track, "删除覆盖关键帧"))
                return;
            var ov = track.OverrideKeyframes;
            if (ov == null || ov.Count == 0)
                return;
            var kf = ov.FirstOrDefault(k => Math.Abs(k.TimeMs - absTimeMs) < 0.5);
            if (kf == null)
                return;
            double t = kf.TimeMs;
            float v = kf.Value;

            _parent.ExecuteCommand(
                new LambdaCommand(
                    "删除覆盖关键帧",
                    () =>
                    {
                        track.OverrideKeyframes!.Remove(kf);
                        _parent.NotifyTrackDataChanged(track);
                    },
                    () =>
                    {
                        track.OverrideKeyframes ??= new List<MotionKeyframe>();
                        track.OverrideKeyframes.Add(
                            new MotionKeyframe
                            {
                                TimeMs = t,
                                Value = v,
                                Interpolation = "linear"
                            }
                        );
                        track.OverrideKeyframes.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
                        _parent.NotifyTrackDataChanged(track);
                    }
                )
            );
        }

        /// <summary>
        /// 处理插值类型切换 (来自 CurveEditorControl.InterpolationChanged)
        /// </summary>
        public void OnInterpolationChanged(
            int trackIdx,
            int clipIdx,
            int kfIdx,
            string interpolation
        )
        {
            if (trackIdx < 0 || trackIdx >= _parent.Tracks.Count)
                return;

            var trackVm = _parent.Tracks[trackIdx];
            _parent.SetKeyframeInterpolation(trackVm, clipIdx, kfIdx, interpolation);
        }

        /// <summary>
        /// 曲线编辑器多选集合变化 → 同步到 VM 全局多选集 (Delete/批量插值/属性面板统一)。
        /// selections 为 (曲线trackIndex, clipIndex, kfIndex), 索引均为曲线视图 Model 层索引。
        /// </summary>
        public void OnMultiSelectionChanged(IReadOnlyList<(int Ti, int Ci, int Ki)> selections)
        {
            if (selections == null)
                return;

            _parent.ClearKeyframeSelection();
            foreach (var (ti, ci, ki) in selections)
            {
                if (ti < 0 || ti >= _parent.Tracks.Count)
                    continue;
                var trackVm = _parent.Tracks[ti];
                if (ci < 0 || ci >= trackVm.Clips.Count)
                    continue;
                // 曲线视图索引是 Model 层 (MotionClip.Keyframes), 需映射为 VM 层 KeyframeViewModel 索引
                var clipVm = trackVm.Clips[ci];
                int vmIdx = -1;
                var modelKf = clipVm.Clip.Keyframes.ElementAtOrDefault(ki);
                if (modelKf != null)
                {
                    for (int j = 0; j < clipVm.Keyframes.Count; j++)
                    {
                        if (clipVm.Keyframes[j].Keyframe == modelKf)
                        {
                            vmIdx = j;
                            break;
                        }
                    }
                }
                if (vmIdx < 0)
                    continue;
                _parent.AddToSelection(trackVm, ci, vmIdx);
            }
        }
    }
}
