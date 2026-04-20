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
        // ---- LiveValue 通道索引 (O(1) 查找替代 O(n) 线性扫描) ----

        private Dictionary<string, TrackViewModel>? _trackChannelIndex;

        /// <summary>
        /// 根据 (deviceName, channelName) 获取 TrackViewModel。
        /// 首次调用时构建索引，Tracks 变更后自动失效重建。
        /// </summary>
        private TrackViewModel? FindTrackByChannel(string deviceName, string channelName)
        {
            if (_trackChannelIndex == null)
                RebuildTrackChannelIndex();

            string key = string.Concat(deviceName, "\0", channelName);
            _trackChannelIndex!.TryGetValue(key, out var result);
            return result;
        }

        private void RebuildTrackChannelIndex()
        {
            _trackChannelIndex = new Dictionary<string, TrackViewModel>(
                Tracks.Count * 2,
                StringComparer.Ordinal
            );
            foreach (var track in Tracks)
            {
                // OAction 通道
                if (
                    !string.IsNullOrEmpty(track.DeviceName)
                    && !string.IsNullOrEmpty(track.OActionName)
                )
                {
                    string key = string.Concat(track.DeviceName, "\0", track.OActionName);
                    _trackChannelIndex.TryAdd(key, track);
                }
                // OAxis 通道
                if (
                    !string.IsNullOrEmpty(track.DeviceName)
                    && !string.IsNullOrEmpty(track.Track.OAxisChannel)
                )
                {
                    string key = string.Concat(track.DeviceName, "\0", track.Track.OAxisChannel);
                    _trackChannelIndex.TryAdd(key, track);
                }
            }
        }

        /// <summary>Tracks 集合变更时失效索引</summary>
        private void InvalidateTrackChannelIndex()
        {
            _trackChannelIndex = null;
        }

        // ---- 播放控制 ----

        private void Play()
        {
            if (_engine.State == PlaybackState.Paused)
            {
                _engine.Resume();
            }
            else
            {
                if (Timeline != null)
                {
                    _engine.LoadTimeline(Timeline);
                }
                _engine.Play();
            }
            StartTick();
        }

        private void Pause()
        {
            _engine.Pause();
            StopTick();
        }

        private void Stop()
        {
            _engine.Stop();
            // Tick 在回中完成后自动停止
        }

        private void ToggleLoop()
        {
            IsLoop = !IsLoop;
        }

        private void ToggleTheme()
        {
            IsDarkTheme = !IsDarkTheme;
            // 设置 Avalonia 全局主题
            if (Avalonia.Application.Current != null)
            {
                Avalonia.Application.Current.RequestedThemeVariant = IsDarkTheme
                    ? Avalonia.Styling.ThemeVariant.Dark
                    : Avalonia.Styling.ThemeVariant.Light;
            }
        }

        // ---- Tick 驱动 ----

        private void StartTick()
        {
            StopTick();
            // 60fps Tick 驱动
            _tickSubscription = Observable
                .Interval(TimeSpan.FromMilliseconds(16.67))
                .Subscribe(_ => _engine.Tick());
        }

        private void StopTick()
        {
            _tickSubscription?.Dispose();
            _tickSubscription = null;
        }

        // ---- 引擎事件处理 ----

        /// <summary>
        /// 统一引擎通知入口 — 按通知类型分派到各处理方法。
        /// 替代 5 个独立事件订阅, 简化生命周期管理。
        /// </summary>
        private void OnEngineNotified(PlaybackNotification notification)
        {
            switch (notification)
            {
                case PlaybackNotification.Position pos:
                    OnPositionChanged(pos.TimeMs);
                    break;
                case PlaybackNotification.StateChange sc:
                    OnStateChanged(sc.OldState, sc.NewState);
                    break;
                case PlaybackNotification.Completed:
                    OnPlaybackCompleted();
                    break;
                case PlaybackNotification.TimelineReady tr:
                    OnTimelineLoaded(tr.Timeline);
                    break;
                case PlaybackNotification.LiveValues lv:
                    OnLiveValuesBatchUpdated(lv.Channels);
                    break;
            }
        }

        private void OnPositionChanged(double timeMs)
        {
            Dispatcher.UIThread.Post(() =>
            {
                CurrentTimeMs = timeMs;
                this.RaisePropertyChanged(nameof(CurrentTimeDisplay));

                // ── 工作区域循环: 播放头超过出点时跳回入点 ──
                if (IsPlaying && IsLoop && HasWorkArea)
                {
                    if (timeMs >= _workAreaOutMs!.Value)
                    {
                        _engine.Seek(_workAreaInMs!.Value);
                        return;
                    }
                }

                // ── 播放时自动滚动: 播放头超过可视区右侧 85% 时平滑跟随 ──
                if (IsPlaying && PixelsPerMs > 0 && TimelineViewportWidth > 0)
                {
                    double playheadX = timeMs * PixelsPerMs - ScrollOffsetX;
                    double threshold = TimelineViewportWidth * 0.85;

                    if (playheadX > threshold)
                    {
                        // 将播放头置于可视区 20% 处
                        double targetOffset = timeMs * PixelsPerMs - TimelineViewportWidth * 0.2;
                        ScrollOffsetX = Math.Max(0, targetOffset);
                        AutoScrollTriggered?.Invoke(ScrollOffsetX);
                    }
                    else if (playheadX < 0)
                    {
                        // 播放头跑到左侧外 (倒放/循环跳回)
                        double targetOffset = timeMs * PixelsPerMs - TimelineViewportWidth * 0.1;
                        ScrollOffsetX = Math.Max(0, targetOffset);
                        AutoScrollTriggered?.Invoke(ScrollOffsetX);
                    }
                }
            });
        }

        /// <summary>播放自动滚动触发, View 层订阅以同步 TimeRuler 和 ScrollViewer</summary>
        public event Action<double>? AutoScrollTriggered;

        private void OnStateChanged(PlaybackState oldState, PlaybackState newState)
        {
            Dispatcher.UIThread.Post(() =>
            {
                PlaybackState = newState;
                IsPlaying = newState == PlaybackState.Playing;

                if (newState == PlaybackState.Idle)
                {
                    StopTick();
                }
            });
        }

        private void OnPlaybackCompleted()
        {
            Dispatcher.UIThread.Post(() =>
            {
                // 播放完成通知 (可扩展为 UI 提示)
                System.Diagnostics.Debug.WriteLine("[Timeline] Playback completed");
            });
        }

        private void OnTimelineLoaded(MotionTimeline timeline)
        {
            Dispatcher.UIThread.Post(() =>
            {
                DurationMs = timeline.DurationMs;
                this.RaisePropertyChanged(nameof(DurationDisplay));
            });
        }

        // ---- DO 值同步 (Phase 3.1.5) ----

        /// <summary>播放时设备值分发事件 — 更新轨道实时值 (单个事件, 兼容旧路径)</summary>
        private void OnValueDispatched(string deviceName, string oactionName, float value)
        {
            Dispatcher.UIThread.Post(() =>
            {
                // 找到匹配的轨道并更新实时值
                foreach (var track in Tracks)
                {
                    if (
                        string.Equals(track.DeviceName, deviceName, StringComparison.Ordinal)
                        && (
                            string.Equals(track.OActionName, oactionName, StringComparison.Ordinal)
                            || string.Equals(
                                track.Track.OAxisChannel,
                                oactionName,
                                StringComparison.Ordinal
                            )
                        )
                    )
                    {
                        track.LiveValue = value;
                        break;
                    }
                }
            });
        }

        /// <summary>批量 LiveValue 更新 — 单次 UIThread.Post 更新所有轨道 (消除逐轨道 Post 卡顿)</summary>
        private void OnLiveValuesBatchUpdated(
            IReadOnlyList<(string deviceName, string channelName, float value)> batch
        )
        {
            // 复制数据以便跨线程传递 (batch 缓冲可能被引擎复用)
            var snapshot = new (string deviceName, string channelName, float value)[batch.Count];
            for (int i = 0; i < batch.Count; i++)
                snapshot[i] = batch[i];

            Dispatcher.UIThread.Post(() =>
            {
                foreach (var (deviceName, channelName, value) in snapshot)
                {
                    var track = FindTrackByChannel(deviceName, channelName);
                    if (track != null)
                        track.LiveValue = value;
                }
            });
        }
    }
}
