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
    /// <summary>瀵艰埅/鍚屾/鍒锋柊杈呭姪 (浠?code-behind 鎻愬彇)</summary>
    public partial class TimelineEditorWindow
    {
        /// <summary>刷新所有 TrackClipControl</summary>
        private void RefreshAllTrackControls()
        {
            foreach (var tcc in this.GetVisualDescendants().OfType<TrackClipControl>())
                tcc.InvalidateVisual();

            // 曲线视图模式下同步数据并刷新
            if (ViewModel?.ViewMode == TimelineViewMode.Curves)
                SyncCurveEditorData();

            // 更新独立高度轨道的 UI
            RefreshTrackHeights();
        }

        /// <summary>应用独立轨道高度到轨道头和片段控件</summary>
        private void RefreshTrackHeights()
        {
            if (ViewModel == null)
                return;

            // 查找轨道头 Border 并应用独立高度
            foreach (var border in this.GetVisualDescendants().OfType<Border>())
            {
                if (border.Tag is TrackViewModel trackVm && trackVm.HasIndividualHeight)
                {
                    border.Height = trackVm.IndividualTrackHeight;
                }
            }

            // 查找 TrackClipControl 并应用独立高度
            foreach (var tcc in this.GetVisualDescendants().OfType<TrackClipControl>())
            {
                if (tcc.DataContext is TrackViewModel trackVm && trackVm.HasIndividualHeight)
                {
                    tcc.Height = trackVm.IndividualTrackHeight;
                }
            }
        }

        /// <summary>同步剪贴板状态到所有 TrackClipControl (控制粘贴菜单项启用)</summary>
        private void SyncClipboardState()
        {
            bool has = ViewModel?.HasClipboardKeyframes ?? false;
            foreach (var tcc in this.GetVisualDescendants().OfType<TrackClipControl>())
                tcc.HasClipboardKeyframes = has;
        }

        /// <summary>跳转到上一个关键帧 (对标 AE/Premiere 的关键帧导航, 二分查找优化)</summary>
        private void NavigateToPrevKeyframe()
        {
            if (ViewModel == null)
                return;

            // 在所有激活轨道中导航 (跳过禁用/静音轨道, 尊重 Solo)
            var activeTracks = GetActiveTracksForNavigation();

            double bestTime = -1;
            double playhead = ViewModel.CurrentTimeMs;

            foreach (var t in activeTracks)
            {
                foreach (var clipVm in t.Clips)
                {
                    var kfs = clipVm.Clip.Keyframes;
                    if (kfs.Count == 0)
                        continue;
                    double startMs = clipVm.StartMs;

                    // 二分查找: 最后一个 absTime < playhead - 0.5
                    double target = playhead - 0.5 - startMs;
                    int lo = 0,
                        hi = kfs.Count;
                    while (lo < hi)
                    {
                        int mid = (lo + hi) / 2;
                        if (kfs[mid].TimeMs < target)
                            lo = mid + 1;
                        else
                            hi = mid;
                    }
                    // lo 是第一个 >= target 的索引, 所以 lo-1 是最后一个 < target
                    if (lo > 0)
                    {
                        double absTime = startMs + kfs[lo - 1].TimeMs;
                        if (absTime > bestTime)
                            bestTime = absTime;
                    }
                }
            }

            if (bestTime >= 0)
                ViewModel.SeekTo(bestTime);
        }

        /// <summary>工具栏: 上一个关键帧</summary>
        private void OnPrevKeyframe(object? sender, RoutedEventArgs e) => NavigateToPrevKeyframe();

        /// <summary>工具栏: 下一个关键帧</summary>
        private void OnNextKeyframe(object? sender, RoutedEventArgs e) => NavigateToNextKeyframe();

        /// <summary>跳转到下一个关键帧 (对标 AE/Premiere 的关键帧导航, 二分查找优化)</summary>
        private void NavigateToNextKeyframe()
        {
            if (ViewModel == null)
                return;

            var activeTracks = GetActiveTracksForNavigation();

            double bestTime = double.MaxValue;
            double playhead = ViewModel.CurrentTimeMs;

            foreach (var t in activeTracks)
            {
                foreach (var clipVm in t.Clips)
                {
                    var kfs = clipVm.Clip.Keyframes;
                    if (kfs.Count == 0)
                        continue;
                    double startMs = clipVm.StartMs;

                    // 二分查找: 第一个 absTime > playhead + 0.5
                    double target = playhead + 0.5 - startMs;
                    int lo = 0,
                        hi = kfs.Count;
                    while (lo < hi)
                    {
                        int mid = (lo + hi) / 2;
                        if (kfs[mid].TimeMs <= target)
                            lo = mid + 1;
                        else
                            hi = mid;
                    }
                    if (lo < kfs.Count)
                    {
                        double absTime = startMs + kfs[lo].TimeMs;
                        if (absTime < bestTime)
                            bestTime = absTime;
                    }
                }
            }

            if (bestTime < double.MaxValue)
                ViewModel.SeekTo(bestTime);
        }

        /// <summary>获取所有激活的轨道用于关键帧导航 (过滤禁用/静音, 尊重 Solo)</summary>
        private TrackViewModel[] GetActiveTracksForNavigation()
        {
            if (ViewModel == null)
                return Array.Empty<TrackViewModel>();

            var allTracks = ViewModel.Tracks;
            bool anySolo = allTracks.Any(t => t.IsSolo);

            return allTracks
                .Where(t => t.IsEnabled && !t.IsMuted && (!anySolo || t.IsSolo))
                .ToArray();
        }

        /// <summary>选择相邻轨道 (↑/↓ 键)</summary>
        private void SelectAdjacentTrack(int direction)
        {
            if (ViewModel == null || ViewModel.Tracks.Count == 0)
                return;

            int currentIdx = -1;
            if (ViewModel.SelectedTrack != null)
                currentIdx = ViewModel.Tracks.IndexOf(ViewModel.SelectedTrack);

            int nextIdx = currentIdx + direction;
            if (nextIdx < 0)
                nextIdx = ViewModel.Tracks.Count - 1;
            else if (nextIdx >= ViewModel.Tracks.Count)
                nextIdx = 0;

            ViewModel.SelectedTrack = ViewModel.Tracks[nextIdx];
        }

        /// <summary>同步缩放级别到 TimeRulerControl</summary>
        private void SyncZoomToRuler()
        {
            var ruler = this.FindControl<TimeRulerControl>("TimeRuler");
            if (ruler != null && ViewModel != null)
            {
                ruler.PixelsPerMs = ViewModel.PixelsPerMs;
                ruler.ScrollOffsetX = ViewModel.ScrollOffsetX;
                ruler.InvalidateVisual();
            }
        }

        /// <summary>同步 ScrollOffsetX 到所有 TrackClipControl (播放自动滚动时调用)</summary>
        private void SyncScrollToClips()
        {
            if (ViewModel == null)
                return;
            double offset = ViewModel.ScrollOffsetX;
            double ppm = ViewModel.PixelsPerMs;
            foreach (var tcc in this.GetVisualDescendants().OfType<TrackClipControl>())
            {
                tcc.ScrollOffsetX = offset;
                tcc.PixelsPerMs = ppm;
                tcc.InvalidateVisual();
            }
        }

        /// <summary>同步工作区域标记到时间标尺与轨道主体叠加层</summary>
        private void SyncWorkAreaToRuler()
        {
            var ruler = this.FindControl<TimeRulerControl>("TimeRuler");
            if (ruler != null && ViewModel != null)
            {
                ruler.WorkAreaInMs = ViewModel.WorkAreaInMs ?? -1;
                ruler.WorkAreaOutMs = ViewModel.WorkAreaOutMs ?? -1;
                ruler.InvalidateVisual();
            }
            var overlay = this.FindControl<WorkAreaOverlay>("WorkAreaOverlay");
            if (overlay != null && ViewModel != null)
            {
                overlay.WorkAreaInMs = ViewModel.WorkAreaInMs ?? -1;
                overlay.WorkAreaOutMs = ViewModel.WorkAreaOutMs ?? -1;
                overlay.InvalidateVisual();
            }
        }
    }
}
