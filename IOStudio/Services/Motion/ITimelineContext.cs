using System;
using System.Collections.ObjectModel;
using IOStudio.Models.Motion;
using IOStudio.ViewModels.Timeline;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 时间轴上下文接口 — 提供跨服务/子模块的共享状态访问
    /// 所有子 ViewModel 和服务通过此接口访问共享状态,
    /// 避免直接依赖巨型 TimelineEditorViewModel
    /// </summary>
    public interface ITimelineContext
    {
        /// <summary>当前时间轴数据</summary>
        MotionTimeline? Timeline { get; }

        /// <summary>所有轨道 ViewModel</summary>
        ObservableCollection<TrackViewModel> Tracks { get; }

        /// <summary>当前播放头时间 (ms)</summary>
        double CurrentTimeMs { get; set; }

        /// <summary>时间轴总时长 (ms)</summary>
        double DurationMs { get; set; }

        /// <summary>当前选中的轨道</summary>
        TrackViewModel? SelectedTrack { get; set; }

        /// <summary>当前选中的关键帧</summary>
        KeyframeViewModel? SelectedKeyframe { get; set; }

        /// <summary>标记项目已修改 (脏标记)</summary>
        void MarkDirty();

        /// <summary>通知某轨道数据已变化, 刷新视图</summary>
        void NotifyTrackDataChanged(TrackViewModel track);

        /// <summary>重新计算时间轴总时长</summary>
        void RecalculateDuration();

        /// <summary>选中指定关键帧</summary>
        void SelectKeyframe(TrackViewModel track, int clipIdx, int kfIdx);

        /// <summary>清除关键帧选择</summary>
        void ClearKeyframeSelection();

        /// <summary>Undo/Redo 服务</summary>
        IUndoRedoService UndoRedo { get; }
    }
}
