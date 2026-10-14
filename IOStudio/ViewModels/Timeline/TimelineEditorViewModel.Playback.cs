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
            // 播放前预检: 把静默失效问题摆到状态栏 (不阻断播放)
            ReportValidationSummary();

            // 设备绑定丢失是"时间轴在走、设备却不动"的头号原因, 且完全静默。
            // 自动提示一次 (同一文件 + 同一组未解析设备, 本会话只提示一次, 不反复打断)。
            _ = CheckDeviceBindingsAsync(autoPrompt: true);

            // 设备输出关闭时, 播放不会驱动任何设备 —— 这是"时间轴在走但设备不动"的常见原因,
            // 且工具栏只有一个很小的状态点, 容易被忽略。这里明确说一次。
            if (!IsLiveOutputEnabled)
                PushStatus("设备输出未开启：仅本地预览，不会驱动设备 (点击走带「设备输出」开启)");

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
            // 清掉外部时钟, 避免过期的媒体时间泄漏到后续 Play/Seek
            _engine.SetExternalClockMs(null);
            StopTick();
        }

        private void Stop()
        {
            _engine.Stop();
            _engine.SetExternalClockMs(null);
            // Tick 在回中完成后自动停止
            ReportUnresolvedChannels();
            ReportRateLimitedChannels();
        }

        /// <summary>
        /// 停播时汇报"本次播放有多少输出没送出去"。
        ///
        /// <para>native 派发时查不到设备的通道会被<b>静默</b>丢弃 (无日志、无 UI, 状态仍是 playing),
        /// 现场只表现为"动作在走、设备不动"。native 侧只按场次累加、不逐帧打日志, 这里读一次汇总
        /// 并写进状态栏, 让丢失可见。</para>
        ///
        /// <para>与打开文件时的静态设备绑定预检<b>互补而非重复</b>: 静态预检按
        /// <c>Config/IODevice.xml</c> 判断名字有没有被声明 (管作者写错); 这里报告运行期
        /// "这一刻真的没送出去", 也覆盖"名字在表里但设备没建出来"这种预检会放过的情形。</para>
        ///
        /// <para>设备输出未开启时 native 根本不派发, 计数必为空 —— 那种情况下"设备不动"是
        /// 设置问题, 已由开播时的提示说明, 这里不再插话。</para>
        /// </summary>
        private void ReportUnresolvedChannels()
        {
            IReadOnlyDictionary<string, long> unresolved;
            try
            {
                unresolved = _engine.UnresolvedChannels;
            }
            catch
            {
                return; // 统计不是关键路径, 取不到就静默
            }

            if (unresolved.Count == 0)
                return;

            long total = unresolved.Values.Sum();
            // 通道多了状态栏放不下: 按丢弃次数取前几条, 其余归入"等 N 个"
            const int shown = 3;
            var top = unresolved
                .OrderByDescending(kv => kv.Value)
                .Take(shown)
                .Select(kv => kv.Key)
                .ToList();
            string list = string.Join(", ", top);
            if (unresolved.Count > top.Count)
                list += $" 等 {unresolved.Count} 个通道";

            PushStatus(
                $"⚠ 本次播放有 {unresolved.Count} 个通道未送达设备 (共 {total} 次丢弃): {list}"
                    + "。工具栏「设备绑定检查」可一键映射。"
            );
        }

        /// <summary>
        /// 报告被速率上限削过的通道。
        ///
        /// <para>与"未送达"互补: 那些是**根本没发出去**, 这些是**发出去了但被削慢**。
        /// 被限速的输出看起来完全正常 (只是变慢), 所以不说的话, 作者会把"电缸跟不上内容"
        /// 误当成设备故障 —— 而实际原因是他写的曲线太快。</para>
        /// </summary>
        private void ReportRateLimitedChannels()
        {
            IReadOnlyDictionary<string, long> limited;
            try
            {
                limited = _engine.RateLimitedChannels;
            }
            catch
            {
                return; // 统计不是关键路径, 取不到就静默
            }

            if (limited.Count == 0)
                return;

            const int shown = 3;
            var top = limited
                .OrderByDescending(kv => kv.Value)
                .Take(shown)
                .Select(kv => kv.Key)
                .ToList();
            string list = string.Join(", ", top);
            if (limited.Count > top.Count)
                list += $" 等 {limited.Count} 个通道";

            PushStatus(
                $"本次播放有 {limited.Count} 个通道被速率上限削过: {list}"
                    + "。内容要求的跳变超过了硬件允许的速度，已在设备表用 Key 的 SlewRate 属性限速。"
            );
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
            TickStartCount++;
            StopTick();
            // 60fps Tick 驱动。每次 Tick 先把媒体时钟推给引擎 ——
            // 影片编排由媒体当主时钟, 否则动作与画面会各自自由跑并逐渐漂移。
            //
            // 关键: tick 里抛异常会让 Rx 订阅**终止**, 表现为"播放不动了"却毫无提示。
            // 因此这里兜住并记录, 让它可见而不是静默失效。
            _tickSubscription = Observable
                .Interval(TimeSpan.FromMilliseconds(16.67))
                .Subscribe(_ =>
                {
                    try
                    {
                        TickCount++;
                        Services.Motion.PollDiagnostics.Sample();

                        var swMedia = System.Diagnostics.Stopwatch.StartNew();
                        PushMediaClock();
                        long mediaMs = swMedia.ElapsedMilliseconds;
                        if (mediaMs > TickMaxMediaMs)
                            TickMaxMediaMs = mediaMs;

                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        _engine.Tick();
                        long elapsed = sw.ElapsedMilliseconds;
                        if (elapsed > TickMaxEngineMs)
                            TickMaxEngineMs = elapsed;
                    }
                    catch (Exception ex)
                    {
                        TickError = ex.GetType().Name + ": " + ex.Message;
                        PushStatus("播放 tick 出错: " + TickError);
                    }
                });
        }

        private string? _tickError;

        /// <summary>播放 tick 已执行次数 (诊断: 区分"tick 终止"与"tick 在跑但别处没生效")。</summary>
        public long TickCount { get; private set; }

        /// <summary>引擎 Tick 的最长耗时 (毫秒)。用于定位"tick 被拖慢"这类静默问题。</summary>
        public long TickMaxEngineMs { get; private set; }

        /// <summary>媒体时钟推送 (PushMediaClock) 的最长耗时 (毫秒)。</summary>
        public long TickMaxMediaMs { get; private set; }

        /// <summary>StartTick 被调用次数。用于定位"interval 被反复重启"这类调度问题。</summary>
        public long TickStartCount { get; private set; }

        /// <summary>StopTick 被调用次数。</summary>
        public long TickStopCount { get; private set; }

        /// <summary>播放 tick 内最近一次异常 (null = 无)。tick 异常会终止整条播放驱动, 必须可见。</summary>
        public string? TickError
        {
            get => _tickError;
            private set => this.RaiseAndSetIfChanged(ref _tickError, value);
        }

        private void StopTick()
        {
            TickStopCount++;
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
                // 自然播完也要汇报未送达通道 —— 否则"播完就静默结束"时丢失无人知晓
                ReportUnresolvedChannels();
                ReportRateLimitedChannels();
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
