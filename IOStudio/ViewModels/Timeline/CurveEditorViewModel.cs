using System;
using System.Collections.Generic;
using System.Linq;
using IOStudio.Controls.Timeline;
using IOStudio.Models.Motion;
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
                var clips = tvm.Clips?.Select(c => c.Clip).ToList() ?? new List<MotionClip>();
                data.Add(
                    new CurveTrackData
                    {
                        Label = tvm.Label,
                        Color = tvm.Color,
                        ValueType = tvm.ValueType,
                        IsMuted = tvm.IsMuted,
                        IsSolo = tvm.IsSolo,
                        IsEnabled = tvm.IsEnabled,
                        Clips = clips
                    }
                );
            }
            CurveTracks = data;
        }

        // ═══════ 曲线编辑操作 ═══════

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
    }
}
