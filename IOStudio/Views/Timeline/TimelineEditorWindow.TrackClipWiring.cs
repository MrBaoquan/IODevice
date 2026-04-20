using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using IOStudio.Controls.Timeline;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using IOStudio.ViewModels.Timeline;

namespace IOStudio.Views.Timeline
{
    /// <summary>杞ㄩ亾浜嬩欢缁戝畾 + 灞炴€ч潰鏉垮埛鏂?(浠?code-behind 鎻愬彇)</summary>
    public partial class TimelineEditorWindow
    {
        // ═══════ 轨道事件绑定 ═══════

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            if (ViewModel != null)
            {
                ViewModel.Tracks.CollectionChanged += (_, _) =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(
                        WireTrackClipControls,
                        Avalonia.Threading.DispatcherPriority.Loaded
                    );
                };

                // DisplayItems 重建后也需要重新绑定事件 (RefreshDisplayList 会清空并重建)
                ViewModel.DisplayItems.CollectionChanged += (_, _) =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(
                        WireTrackClipControls,
                        Avalonia.Threading.DispatcherPriority.Loaded
                    );
                };
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(
                WireTrackClipControls,
                Avalonia.Threading.DispatcherPriority.Loaded
            );
        }

        /// <summary>
        /// 扫描 Visual Tree, 为每个 TrackClipControl 绑定事件
        /// </summary>
        private void WireTrackClipControls()
        {
            var controls = this.GetVisualDescendants().OfType<TrackClipControl>().ToList();
            foreach (var tcc in controls)
            {
                if (tcc.Tag is string s && s == "wired")
                    continue;
                tcc.Tag = "wired";

                tcc.AddKeyframeRequested += ms =>
                {
                    var trackVm = tcc.DataContext as TrackViewModel;
                    if (trackVm != null && ViewModel != null)
                    {
                        ViewModel.AddKeyframeAtTime(trackVm, ms);
                        RefreshPropertyPanel();
                        tcc.InvalidateVisual();
                    }
                };

                tcc.KeyframeSelected += (clipIdx, kfIdx) =>
                {
                    var trackVm = tcc.DataContext as TrackViewModel;
                    if (trackVm != null && ViewModel != null)
                    {
                        // 清除所有其他 TrackClipControl 的选中状态
                        foreach (
                            var otherTcc in this.GetVisualDescendants().OfType<TrackClipControl>()
                        )
                        {
                            if (otherTcc != tcc)
                                otherTcc.ClearSelection();
                        }

                        // 更新轨道选中高亮
                        ViewModel.SelectedTrack = trackVm;
                        foreach (var t in ViewModel.Tracks)
                            t.IsSelected = (t == trackVm);
                        foreach (
                            var otherTcc in this.GetVisualDescendants().OfType<TrackClipControl>()
                        )
                            otherTcc.IsTrackSelected = (otherTcc.DataContext == trackVm);

                        // 同步选中轨道到曲线编辑器 (双击添加关键帧时使用)
                        SyncSelectedTrackToCurveEditor(trackVm);

                        if (clipIdx >= 0 && kfIdx >= 0)
                            ViewModel.SelectKeyframe(trackVm, clipIdx, kfIdx);
                        else
                            ViewModel.ClearKeyframeSelection();
                        // 不刷新检查器: 检查器只跟随播放头, 不跟随关键帧选中
                    }
                };

                // Shift+Click 追加多选
                tcc.KeyframeAddToSelection += (clipIdx, kfIdx) =>
                {
                    var trackVm = tcc.DataContext as TrackViewModel;
                    if (trackVm != null && ViewModel != null)
                    {
                        ViewModel.AddToSelection(trackVm, clipIdx, kfIdx);
                        SyncMultiSelectionToControls();
                        RefreshPropertyPanel();
                    }
                };

                // Ctrl+Click 切换多选
                tcc.KeyframeToggleSelection += (clipIdx, kfIdx) =>
                {
                    var trackVm = tcc.DataContext as TrackViewModel;
                    if (trackVm != null && ViewModel != null)
                    {
                        ViewModel.ToggleSelection(trackVm, clipIdx, kfIdx);
                        SyncMultiSelectionToControls();
                        RefreshPropertyPanel();
                    }
                };

                tcc.KeyframeMoved += (clipIdx, kfIdx, newTimeMs, newValue) =>
                {
                    var trackVm = tcc.DataContext as TrackViewModel;
                    if (trackVm != null && ViewModel != null)
                    {
                        // 传入吸附点以显示视觉参考线
                        if (ViewModel.IsSnapEnabled)
                            tcc.SetSnapPoints(ViewModel.GetSnapPoints());
                        else
                            tcc.SetSnapPoints(null);

                        ViewModel.MoveKeyframe(trackVm, clipIdx, kfIdx, newTimeMs, newValue);
                        RefreshPropertyPanel();
                    }
                };

                tcc.DeleteKeyframeRequested += (clipIdx, kfIdx) =>
                {
                    var trackVm = tcc.DataContext as TrackViewModel;
                    if (trackVm != null && ViewModel != null)
                    {
                        ViewModel.DeleteKeyframe(trackVm, clipIdx, kfIdx);
                        RefreshPropertyPanel();
                    }
                };

                tcc.SetInterpolationRequested += (clipIdx, kfIdx, interp) =>
                {
                    var trackVm = tcc.DataContext as TrackViewModel;
                    if (trackVm != null && ViewModel != null)
                    {
                        ViewModel.SetKeyframeInterpolation(trackVm, clipIdx, kfIdx, interp);
                        RefreshPropertyPanel();
                    }
                };

                // 关键帧复制/粘贴 (来自右键菜单)
                tcc.CopyKeyframesRequested += () =>
                {
                    ViewModel?.CopySelectedKeyframes();
                    // 刷新所有 TCC 的剪贴板状态
                    SyncClipboardState();
                };

                tcc.PasteKeyframesRequested += () =>
                {
                    ViewModel?.PasteKeyframes();
                    RefreshAllTrackControls();
                    SyncCurveEditorData();
                    RefreshPropertyPanel();
                };

                // 在指定时间添加关键帧 (来自时间轴空白区右键菜单)
                tcc.AddKeyframeAtTimeRequested += (timeMs) =>
                {
                    var trackVm = tcc.DataContext as TrackViewModel;
                    if (trackVm != null && ViewModel != null)
                    {
                        ViewModel.AddKeyframeAtTime(trackVm, timeMs);
                        tcc.InvalidateVisual();
                        RefreshPropertyPanel();
                    }
                };

                // 在指定时间添加事件 (来自时间轴空白区右键菜单)
                tcc.AddEventAtTimeRequested += (timeMs) =>
                {
                    ViewModel?.AddEventAtTime(timeMs);
                    SyncEventsToRuler();
                };

                // 框选关键帧完成: 同步到 ViewModel 的多选列表
                tcc.BoxSelectCompleted += (selectedList) =>
                {
                    var trackVm = tcc.DataContext as TrackViewModel;
                    if (trackVm != null && ViewModel != null)
                    {
                        // 清除所有其他 TrackClipControl 的选中状态
                        foreach (
                            var otherTcc in this.GetVisualDescendants().OfType<TrackClipControl>()
                        )
                        {
                            if (otherTcc != tcc)
                                otherTcc.ClearSelection();
                        }

                        // 同步框选结果到 ViewModel 的多选列表
                        ViewModel.ClearKeyframeSelection();
                        foreach (var (clipIdx, kfIdx) in selectedList)
                        {
                            ViewModel.AddToSelection(trackVm, clipIdx, kfIdx);
                        }
                        SyncMultiSelectionToControls();
                        RefreshPropertyPanel();
                    }
                };

                // 应用曲线预设 (来自右键菜单)
                tcc.ApplyPresetRequested += (clipIdx, kfIdx, presetName) =>
                {
                    var trackVm = tcc.DataContext as TrackViewModel;
                    if (trackVm != null && ViewModel != null)
                    {
                        var preset = CurvePresetService.FindPreset(presetName);
                        if (preset != null)
                        {
                            CurvePresetService.ApplyPreset(
                                ViewModel,
                                trackVm,
                                clipIdx,
                                kfIdx,
                                preset
                            );
                            tcc.InvalidateVisual();
                            RefreshPropertyPanel();
                        }
                    }
                };

                // 批量设置插值类型 (多选关键帧)
                tcc.BatchSetInterpolationRequested += (interpolation) =>
                {
                    if (ViewModel != null)
                    {
                        ViewModel.BatchSetInterpolation(interpolation);
                        RefreshAllTrackControls();
                        RefreshPropertyPanel();
                    }
                };

                // 批量应用曲线预设 (多选关键帧)
                tcc.BatchApplyPresetRequested += (presetName) =>
                {
                    if (ViewModel != null)
                    {
                        ViewModel.BatchApplyPreset(presetName);
                        RefreshAllTrackControls();
                        RefreshPropertyPanel();
                    }
                };

                // Shift+滚轮缩放全部轨道高度 (达芬奇风格)
                tcc.TrackHeightChangeRequested += (delta) =>
                {
                    if (ViewModel == null)
                        return;
                    foreach (var t in ViewModel.Tracks)
                    {
                        double currentHeight = t.HasIndividualHeight
                            ? t.IndividualTrackHeight
                            : ViewModel.TrackHeight;
                        double newHeight = Math.Clamp(currentHeight + delta, 36, 200);
                        t.SetIndividualHeight(newHeight);
                        t.UpdateCompactDisplay(ViewModel.TrackHeight);
                    }
                    RefreshTrackHeights();
                };

                // 多选关键帧拖拽 (Dope Sheet 模式)
                double multiDragAccumDelta = 0;
                List<(
                    TrackViewModel track,
                    int clipIdx,
                    int kfIdx,
                    double origTimeMs
                )>? multiDragSnapshot = null;

                tcc.MultiKeyframeDragDelta += (deltaTimeMs) =>
                {
                    if (ViewModel == null)
                        return;
                    // 首次拖拽时保存快照
                    if (multiDragSnapshot == null)
                    {
                        multiDragSnapshot = new();
                        foreach (var (track, clipIdx, kfIdx) in ViewModel.SelectedKeyframes)
                        {
                            if (clipIdx >= 0 && clipIdx < track.Clips.Count)
                            {
                                var clipVm = track.Clips[clipIdx];
                                if (kfIdx >= 0 && kfIdx < clipVm.Keyframes.Count)
                                    multiDragSnapshot.Add(
                                        (track, clipIdx, kfIdx, clipVm.Keyframes[kfIdx].TimeMs)
                                    );
                            }
                        }
                    }
                    // 应用偏移到所有选中关键帧
                    foreach (var (track, clipIdx, kfIdx, origTime) in multiDragSnapshot)
                    {
                        if (clipIdx >= 0 && clipIdx < track.Clips.Count)
                        {
                            var clipVm = track.Clips[clipIdx];
                            if (kfIdx >= 0 && kfIdx < clipVm.Keyframes.Count)
                                clipVm.Keyframes[kfIdx].TimeMs = Math.Max(
                                    0,
                                    origTime + deltaTimeMs
                                );
                        }
                    }
                    multiDragAccumDelta = deltaTimeMs;
                    RefreshAllTrackControls();
                };

                tcc.MultiKeyframeDragCompleted += () =>
                {
                    if (ViewModel == null || multiDragSnapshot == null)
                        return;
                    // 恢复原始位置, 然后通过 ViewModel 正式移动 (支持撤销)
                    foreach (var (track, clipIdx, kfIdx, origTime) in multiDragSnapshot)
                    {
                        if (clipIdx >= 0 && clipIdx < track.Clips.Count)
                        {
                            var clipVm = track.Clips[clipIdx];
                            if (kfIdx >= 0 && kfIdx < clipVm.Keyframes.Count)
                                clipVm.Keyframes[kfIdx].TimeMs = origTime;
                        }
                    }
                    // 通过 ViewModel 正式执行多选移动 (记录撤销)
                    if (Math.Abs(multiDragAccumDelta) > 0.1)
                        ViewModel.MoveSelectedKeyframes(multiDragAccumDelta);
                    multiDragSnapshot = null;
                    multiDragAccumDelta = 0;
                    RefreshAllTrackControls();
                    RefreshPropertyPanel();
                };
            }
        }

        // ═══════ 属性面板 ═══════

        /// <summary>
        /// 采样所有轨道在当前播放头时刻的插值, 更新 TrackViewModel.LiveValue
        /// 使轨道头预览值跟随播放头 (播放状态下由 OnLiveValuesBatchUpdated 覆盖)
        /// </summary>
        private void RefreshAllTrackLiveValues()
        {
            if (ViewModel == null)
                return;
            double playheadMs = ViewModel.CurrentTimeMs;
            foreach (var track in ViewModel.Tracks)
            {
                if (track.Clips.Count == 0)
                {
                    track.LiveValue = 0f;
                    continue;
                }
                float v = 0f;
                bool hit = false;
                for (int ci = 0; ci < track.Clips.Count; ci++)
                {
                    var clipVm = track.Clips[ci];
                    if (playheadMs >= clipVm.StartMs && playheadMs <= clipVm.EndMs)
                    {
                        double localMs = playheadMs - clipVm.StartMs;
                        v = Services.Motion.InterpolationEngine.Evaluate(
                            clipVm.Clip.Keyframes,
                            localMs
                        );
                        hit = true;
                        break;
                    }
                }
                if (!hit)
                    v = 0f;
                track.LiveValue = v;
            }
        }

        private void RefreshPropertyPanel()
        {
            if (ViewModel == null)
                return;

            var kfVm = ViewModel.KeyframePropertyVm;

            // 多选模式: 显示多选摘要 + 统计信息
            if (ViewModel.IsMultiSelectMode)
            {
                var stats = ViewModel.GetMultiSelectionStats();
                kfVm.ShowMultiSelection(
                    ViewModel.SelectedKeyframeCount,
                    stats.MinTime,
                    stats.MaxTime,
                    stats.MinValue,
                    stats.MaxValue,
                    stats.AvgValue
                );
                return;
            }

            // 检查器始终跟随播放头 + 选中轨道
            var track = ViewModel.SelectedTrack;
            if (track == null || track.Clips.Count == 0)
            {
                kfVm.Clear();
                return;
            }

            double playheadMs = ViewModel.CurrentTimeMs;
            string valueType = track.ValueType ?? "float";
            string trackName = track.Label ?? "";
            string trackColor = track.Color ?? "#4FC3F7";
            float interpolatedValue = 0.5f;
            bool hasKfAtPlayhead = false;
            string interpolation = "linear";
            float? tangentIn = null;
            float? tangentOut = null;
            float? cp1x = null;
            float? cp2x = null;
            int clipIdx = -1;
            int kfIdx = -1;
            int totalKfCount = 0;
            string intervalInterp = "";

            // 统计关键帧总数
            for (int ci = 0; ci < track.Clips.Count; ci++)
                totalKfCount += track.Clips[ci].Clip.Keyframes.Count;

            for (int ci = 0; ci < track.Clips.Count; ci++)
            {
                var clipVm = track.Clips[ci];
                if (playheadMs >= clipVm.StartMs && playheadMs <= clipVm.EndMs)
                {
                    double localMs = playheadMs - clipVm.StartMs;
                    interpolatedValue = Services.Motion.InterpolationEngine.Evaluate(
                        clipVm.Clip.Keyframes,
                        localMs
                    );
                    // 检查播放头是否恰在关键帧上 (±5ms 容差)
                    for (int ki = 0; ki < clipVm.Clip.Keyframes.Count; ki++)
                    {
                        var kfModel = clipVm.Clip.Keyframes[ki];
                        if (Math.Abs(kfModel.TimeMs - localMs) < 5)
                        {
                            hasKfAtPlayhead = true;
                            interpolation = kfModel.Interpolation ?? "linear";
                            tangentIn = kfModel.TangentIn;
                            tangentOut = kfModel.TangentOut;
                            cp1x = kfModel.Cp1x;
                            cp2x = kfModel.Cp2x;
                            clipIdx = ci;
                            kfIdx = ki;
                            // 同步选中状态以便编辑
                            ViewModel.SelectKeyframe(track, ci, ki);
                            break;
                        }
                    }
                    // 确定区间插值类型 (前一个关键帧的插值)
                    if (!hasKfAtPlayhead)
                    {
                        for (int ki = clipVm.Clip.Keyframes.Count - 1; ki >= 0; ki--)
                        {
                            if (clipVm.Clip.Keyframes[ki].TimeMs <= localMs)
                            {
                                intervalInterp =
                                    clipVm.Clip.Keyframes[ki].Interpolation ?? "linear";
                                break;
                            }
                        }
                    }
                    break;
                }
            }

            // 检测播放头位置的事件 (±5ms 容差)
            Models.Motion.TimelineEvent? playheadEvent = null;
            foreach (var evt in ViewModel.Events)
            {
                if (Math.Abs(evt.TimeMs - playheadMs) < 5)
                {
                    playheadEvent = evt;
                    break;
                }
            }

            kfVm.ShowTrackState(
                playheadMs,
                interpolatedValue,
                valueType,
                trackName,
                hasKfAtPlayhead,
                interpolation,
                tangentIn,
                tangentOut,
                cp1x,
                cp2x,
                clipIdx,
                kfIdx,
                trackColor,
                totalKfCount,
                intervalInterp
            );
            kfVm.SetPlayheadEvent(playheadEvent);
        }

        /// <summary>
        /// 同步 ViewModel 多选状态到所有 TrackClipControl
        /// </summary>
        private void SyncMultiSelectionToControls()
        {
            if (ViewModel == null)
                return;

            var controls = this.GetVisualDescendants().OfType<TrackClipControl>().ToList();
            foreach (var tcc in controls)
            {
                var trackVm = tcc.DataContext as TrackViewModel;
                if (trackVm == null)
                    continue;

                // 筛选属于这条轨道的多选关键帧
                var trackSelections = ViewModel
                    .GetMultiSelectedForTrack(trackVm)
                    .Select(s => (s.ClipIdx, s.KfIdx));
                tcc.SetMultiSelectedKeyframes(trackSelections);
            }
        }
    }
}
