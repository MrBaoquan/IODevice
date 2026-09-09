using System;
using System.Collections.Generic;
using System.Diagnostics;
using IOStudio.Models.Motion;
using IOToolkit;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 编辑器播放引擎 — C# 端驱动时间 + C# 插值, 完全控制设备输出。
    /// <para>
    /// 设计要点:
    /// - 时间驱动 → C# Stopwatch (不依赖 C++ MotionPlayer.Update)
    /// - 插值求值 → C# InterpolationEngine (与曲线渲染完全一致)
    /// - 设备派发 → C# WriteToDevice (仅 LiveOutputToDevice=true 时输出)
    /// - C++ Slot 仅用于 JSON 加载 / Seek 同步, 不再 Play/Update
    /// </para>
    /// </summary>
    public class NativeMotionPlaybackEngine : IPlaybackEngine
    {
        private const string PreviewSlotId = "__editor_preview__";

        private readonly DeviceDispatcher _dispatcher;
        private MotionTimeline? _currentTimeline;
        private MotionSlot? _slot;

        // ---- C# 时间驱动 ----
        private PlaybackState _state = PlaybackState.Idle;
        private double _lastPositionMs;
        private bool _disposed;
        private readonly Stopwatch _playStopwatch = new();
        private double _playStartMs; // 播放/恢复时的起始位置
        private bool _loop;
        private double _speed = 1.0;

        // ---- 回中 (return-to-center) ----
        private Dictionary<int, float>? _returnStartValues;
        private readonly Stopwatch _returnStopwatch = new();
        private const double ReturnDurationMs = 1000;

        // ---- 批量 LiveValue 缓冲 ----
        private readonly List<(string deviceName, string channelName, float value)> _batchBuffer =
            new();

        public NativeMotionPlaybackEngine()
            : this(new DeviceDispatcher()) { }

        public NativeMotionPlaybackEngine(DeviceDispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        // ---- IPlaybackEngine 属性 ----

        public PlaybackState State => _state;

        public bool Loop
        {
            get => _loop;
            set => _loop = value;
        }

        public double Speed
        {
            get => _speed;
            set => _speed = Math.Max(0.01, value);
        }

        public DeviceDispatcher Dispatcher => _dispatcher;

        /// <summary>判断轨道在播放时是否应被跳过 (禁用/静音/非Solo)</summary>
        private bool IsTrackSilenced(MotionTrack track)
        {
            if (!track.Enabled)
                return true;
            if (track.Muted)
                return true;
            // Solo 逻辑: 任何轨道开启 Solo 时, 仅播放 Solo 轨道
            if (_currentTimeline != null)
            {
                bool anySolo = false;
                foreach (var t in _currentTimeline.Tracks)
                {
                    if (t.Enabled && t.Solo)
                    {
                        anySolo = true;
                        break;
                    }
                }
                if (anySolo && !track.Solo)
                    return true;
            }
            return false;
        }

        /// <summary>是否将预览值实时输出到物理设备</summary>
        public bool LiveOutputToDevice { get; set; }

        // ---- IPlaybackEngine 操作 ----

        /// <summary>
        /// 加载 Timeline — 保存 C# 引用, 并序列化为 JSON 给 C++ (用于 Seek 同步)。
        /// </summary>
        public bool LoadTimeline(MotionTimeline timeline, string? sourcePath = null)
        {
            if (timeline == null)
                return false;

            if (_state != PlaybackState.Idle)
                ForceStop();

            // 原子性：先尝试 C++ 加载，成功后才更新 C# 状态
            MotionSlot? newSlot = null;
            try
            {
                string json = MotionFileReader.ToJson(timeline);
                newSlot = IOToolkit.MotionPlayer.LoadSlotFromJson(PreviewSlotId, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NativeEngine] LoadSlotFromJson failed: {ex.Message}");
                return false;
            }

            _slot = newSlot;
            _currentTimeline = timeline;
            TimelineLoaded?.Invoke(timeline);
            Notified?.Invoke(new PlaybackNotification.TimelineReady(timeline));
            return true;
        }

        /// <summary>
        /// 确保引擎持有 Timeline 引用 (用于 Seek 预览, 无需完整 C++ 加载)。
        /// 当拖拽播放头但未按下 Play 时, ViewModel 调用此方法。
        /// </summary>
        public void EnsureTimelineLoaded(MotionTimeline timeline)
        {
            if (_currentTimeline == null && timeline != null)
                _currentTimeline = timeline;
        }

        public void Play()
        {
            if (_currentTimeline == null)
                return;

            // 不调用 _slot.Play() — 完全由 C# 端驱动时间, 控制设备输出
            _playStartMs = _lastPositionMs;
            _playStopwatch.Restart();
            SetState(PlaybackState.Playing);
        }

        public void Pause()
        {
            if (_state != PlaybackState.Playing)
                return;

            _playStopwatch.Stop();
            SetState(PlaybackState.Paused);
        }

        public void Resume()
        {
            if (_state != PlaybackState.Paused)
                return;

            _playStartMs = _lastPositionMs;
            _playStopwatch.Restart();
            SetState(PlaybackState.Playing);
        }

        public void Stop()
        {
            if (_state == PlaybackState.Idle)
                return;

            _playStopwatch.Stop();

            if (LiveOutputToDevice && _currentTimeline != null)
            {
                // 捕获当前各轨道值, 启动回中动画 → 平滑回到 0.5
                _returnStartValues = CaptureCurrentValues();
                _returnStopwatch.Restart();
                SetState(PlaybackState.Stopping);
            }
            else
            {
                // 无设备输出, 直接回到 Idle
                _lastPositionMs = 0;
                SetState(PlaybackState.Idle);
            }
        }

        public void Seek(double timeMs)
        {
            _slot?.Seek((float)timeMs);
            _lastPositionMs = timeMs;

            PositionChanged?.Invoke(timeMs);
            Notified?.Invoke(new PlaybackNotification.Position(timeMs));
            SyncLiveValues((float)timeMs);
        }

        public void ForceStop()
        {
            _playStopwatch.Stop();
            _returnStopwatch.Stop();
            _returnStartValues = null;

            if (_slot != null)
            {
                try
                {
                    _slot.Unload();
                }
                catch
                { /* 忽略已卸载 */
                }
                _slot = null;
            }
            _lastPositionMs = 0;
            SetState(PlaybackState.Idle);
        }

        // ---- Tick ----

        public void Tick()
        {
            switch (_state)
            {
                case PlaybackState.Playing:
                    TickPlaying();
                    break;

                case PlaybackState.Stopping:
                    TickStopping();
                    break;
            }
        }

        private void TickPlaying()
        {
            if (_currentTimeline == null)
                return;

            double elapsed = _playStopwatch.Elapsed.TotalMilliseconds * _speed;
            double currentMs = _playStartMs + elapsed;
            double duration = _currentTimeline.DurationMs;

            if (duration <= 0)
                duration = 1000;

            if (currentMs >= duration)
            {
                if (_loop)
                {
                    currentMs %= duration;
                    _playStartMs = currentMs;
                    _playStopwatch.Restart();
                }
                else
                {
                    _lastPositionMs = duration;
                    PositionChanged?.Invoke(duration);
                    Notified?.Invoke(new PlaybackNotification.Position(duration));
                    SyncLiveValues((float)duration);
                    _playStopwatch.Stop();
                    SetState(PlaybackState.Idle);
                    PlaybackCompleted?.Invoke();
                    Notified?.Invoke(new PlaybackNotification.Completed());
                    return;
                }
            }

            _lastPositionMs = currentMs;
            PositionChanged?.Invoke(currentMs);
            Notified?.Invoke(new PlaybackNotification.Position(currentMs));
            SyncLiveValues((float)currentMs);
        }

        private void TickStopping()
        {
            if (_returnStartValues == null || _currentTimeline == null)
            {
                _lastPositionMs = 0;
                SetState(PlaybackState.Idle);
                return;
            }

            double t = _returnStopwatch.Elapsed.TotalMilliseconds / ReturnDurationMs;

            if (t >= 1.0)
            {
                // 回中完成 → 写入中位值 (Float=0.5, Bool=0), 切换到 Idle
                if (LiveOutputToDevice)
                {
                    var tracks = _currentTimeline.Tracks;
                    for (int i = 0; i < tracks.Count; i++)
                    {
                        if (!IsTrackSilenced(tracks[i]))
                        {
                            float neutral = TrackValueEvaluator.ResolveNeutral(
                                tracks[i].ValueType,
                                tracks[i].NeutralValue
                            );
                            WriteToDevice(tracks[i], neutral);
                        }
                    }
                }

                _returnStartValues = null;
                _returnStopwatch.Stop();
                _lastPositionMs = 0;
                SetState(PlaybackState.Idle);
                return;
            }

            // Smoothstep 回中: 从当前值平滑过渡到中位值 (Float=0.5, Bool=0)
            float smooth = (float)(t * t * (3.0 - 2.0 * t));
            var allTracks = _currentTimeline.Tracks;
            _batchBuffer.Clear();

            for (int i = 0; i < allTracks.Count; i++)
            {
                if (IsTrackSilenced(allTracks[i]))
                    continue;

                _returnStartValues.TryGetValue(i, out float startVal);
                float targetVal = TrackValueEvaluator.ResolveNeutral(
                    allTracks[i].ValueType,
                    allTracks[i].NeutralValue
                );
                float value = startVal + (targetVal - startVal) * smooth;

                string channelName =
                    allTracks[i].OutputType == "oaxis"
                    && !string.IsNullOrEmpty(allTracks[i].OAxisChannel)
                        ? allTracks[i].OAxisChannel
                        : allTracks[i].OActionName;

                _batchBuffer.Add((allTracks[i].DeviceName, channelName, value));

                if (LiveOutputToDevice)
                    WriteToDevice(allTracks[i], value);
            }

            if (_batchBuffer.Count > 0)
            {
                LiveValuesBatchUpdated?.Invoke(_batchBuffer);
                Notified?.Invoke(new PlaybackNotification.LiveValues(_batchBuffer));
            }

            PositionChanged?.Invoke(0);
            Notified?.Invoke(new PlaybackNotification.Position(0));
        }

        /// <summary>捕获当前各轨道插值值 (用于回中起点)</summary>
        private Dictionary<int, float> CaptureCurrentValues()
        {
            var result = new Dictionary<int, float>();
            if (_currentTimeline == null)
                return result;

            var tracks = _currentTimeline.Tracks;
            for (int i = 0; i < tracks.Count; i++)
            {
                if (!IsTrackSilenced(tracks[i]))
                    result[i] = EvaluateTrackAtTime(tracks[i], (float)_lastPositionMs);
            }
            return result;
        }

        // ---- LiveValue 同步 ----

        /// <summary>
        /// C# InterpolationEngine 求值 → 批量 UI 更新 → 可选设备输出。
        /// </summary>
        private void SyncLiveValues(float timeMs)
        {
            if (_currentTimeline == null)
                return;

            var tracks = _currentTimeline.Tracks;
            if (tracks.Count == 0)
                return;

            _batchBuffer.Clear();

            for (int i = 0; i < tracks.Count; i++)
            {
                var track = tracks[i];
                if (IsTrackSilenced(track))
                    continue;

                float value = EvaluateTrackAtTime(track, timeMs);

                string channelName =
                    track.OutputType == "oaxis" && !string.IsNullOrEmpty(track.OAxisChannel)
                        ? track.OAxisChannel
                        : track.OActionName;

                _batchBuffer.Add((track.DeviceName, channelName, value));

                if (LiveOutputToDevice)
                    WriteToDevice(track, value);
            }

            if (_batchBuffer.Count > 0)
            {
                LiveValuesBatchUpdated?.Invoke(_batchBuffer);
                Notified?.Invoke(new PlaybackNotification.LiveValues(_batchBuffer));
            }
        }

        /// <summary>
        /// 轨道任一时刻求值 — 委托 TrackValueEvaluator (镜像 C++ MotionPlayer 语义: clip 覆盖→插值, 空窗→overlay/idle 循环/中性值)。
        /// </summary>
        private static float EvaluateTrackAtTime(MotionTrack track, float timeMs) =>
            TrackValueEvaluator.Evaluate(
                track.Clips,
                track.ValueType,
                track.NeutralValue,
                track.IdleLoop,
                track.OverrideKeyframes,
                timeMs
            );

        /// <summary>
        /// Bool 轨道专用阶梯求值 — 与 TrackClipControl.DrawBoolStepCurve 渲染逻辑完全一致。
        /// 忽略关键帧的 Interpolation 属性, 始终使用阶梯: 保持前一关键帧的值直到下一个关键帧。
        /// </summary>
        private static float EvaluateBoolStep(MotionClip clip, double absoluteTimeMs)
        {
            var kfs = clip.Keyframes;
            if (kfs.Count == 0)
                return 0f;

            double localMs = absoluteTimeMs - clip.StartMs;

            // 找到 localMs 所处位置的"最后一个已到达的关键帧"
            float val = kfs[0].Value;
            for (int k = 0; k < kfs.Count; k++)
            {
                if (kfs[k].TimeMs <= localMs)
                    val = kfs[k].Value;
                else
                    break;
            }
            return val >= 0.5f ? 1f : 0f;
        }

        /// <summary>
        /// 直接写入物理设备 (仅当 LiveOutputToDevice=true 时调用)
        /// </summary>
        private static void WriteToDevice(MotionTrack track, float value)
        {
            try
            {
                var device = IOToolkit.IODeviceController.GetIODevice(track.DeviceName);
                if (device == null)
                    return;

                if (track.OutputType == "oaxis" && !string.IsNullOrEmpty(track.OAxisChannel))
                {
                    IOToolkit.Key key = track.OAxisChannel;
                    device.SetDO(key, value);
                }
                else
                {
                    device.SetDO(track.OActionName, value);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[NativeEngine] WriteToDevice failed [{track.DeviceName}]: {ex.Message}"
                );
            }
        }

        // ---- 状态管理 ----

        private void SetState(PlaybackState newState)
        {
            if (_state == newState)
                return;
            var oldState = _state;
            _state = newState;
            StateChanged?.Invoke(oldState, newState);
            Notified?.Invoke(new PlaybackNotification.StateChange(oldState, newState));
        }

        // ---- 事件 ----

        public event Action<PlaybackNotification>? Notified;
        public event Action<double>? PositionChanged;
        public event Action<PlaybackState, PlaybackState>? StateChanged;
        public event Action? PlaybackCompleted;
        public event Action<MotionTimeline>? TimelineLoaded;
        public event Action<
            IReadOnlyList<(string deviceName, string channelName, float value)>
        >? LiveValuesBatchUpdated;

        // ---- Dispose ----

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            ForceStop();
        }
    }
}
