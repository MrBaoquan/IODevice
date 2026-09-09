using System;
using System.Collections.Generic;
using System.Diagnostics;
using IOStudio.Models.Motion;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// [已废弃] 纯 C# 播放引擎 — 核心状态机 + Tick 驱动。
    /// 已被 <see cref="NativeMotionPlaybackEngine"/> 替代 (Phase 3.2)。
    /// 保留仅用于无 C++ DLL 环境的回退测试。
    /// </summary>
    [Obsolete("使用 NativeMotionPlaybackEngine 替代，播放逻辑已迁移至 C++ IODevice.dll")]
    public class MotionPlaybackEngine : IPlaybackEngine
    {
        private readonly DeviceDispatcher _dispatcher;
        private readonly SafetyGuard _safetyGuard;
        private readonly Stopwatch _stopwatch = new();

        // ---- 时间 ----
        private double _accumulatedMs;
        private double _lastTickMs;

        // ---- 回中 ----
        private Dictionary<string, float>? _returnStartValues;
        private double _returnElapsedMs;
        private const double ReturnDurationMs = 1000; // 回中时间 1 秒

        public MotionPlaybackEngine()
            : this(new DeviceDispatcher(), new SafetyGuard()) { }

        public MotionPlaybackEngine(DeviceDispatcher dispatcher, SafetyGuard safetyGuard)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _safetyGuard = safetyGuard ?? throw new ArgumentNullException(nameof(safetyGuard));
        }

        // ---- 状态属性 ----

        /// <summary>当前播放状态</summary>
        public PlaybackState State { get; private set; } = PlaybackState.Idle;

        /// <summary>当前加载的时间轴</summary>
        public MotionTimeline? CurrentTimeline { get; private set; }

        /// <summary>当前播放位置 (毫秒)</summary>
        public double CurrentTimeMs
        {
            get => _accumulatedMs;
            private set => _accumulatedMs = value;
        }

        /// <summary>总时长 (毫秒)</summary>
        public double DurationMs => CurrentTimeline?.DurationMs ?? 0;

        /// <summary>播放速度 (1.0 = 正常)</summary>
        public double Speed { get; set; } = 1.0;

        /// <summary>是否循环播放</summary>
        public bool Loop { get; set; }

        /// <summary>当前加载的文件路径</summary>
        public string? CurrentFile { get; private set; }

        /// <summary>设备分发器 (供外部监听事件)</summary>
        public DeviceDispatcher Dispatcher => _dispatcher;

        /// <summary>安全保护 (供外部配置参数)</summary>
        public SafetyGuard Safety => _safetyGuard;

        // ---- 操作 ----

        /// <summary>
        /// 加载 .motion 文件
        /// </summary>
        public bool Load(string motionFilePath)
        {
            var timeline = MotionFileReader.Read(motionFilePath);
            if (timeline == null)
                return false;

            return LoadTimeline(timeline, motionFilePath);
        }

        /// <summary>
        /// 直接加载 MotionTimeline 对象 (编辑器内预览用)
        /// </summary>
        public bool LoadTimeline(MotionTimeline timeline, string? sourcePath = null)
        {
            if (timeline == null)
                return false;

            // 如果正在播放, 先停止
            if (State != PlaybackState.Idle)
                ForceStop();

            CurrentTimeline = timeline;
            CurrentFile = sourcePath;
            CurrentTimeMs = 0;
            _safetyGuard.Reset();

            TimelineLoaded?.Invoke(timeline);
            return true;
        }

        /// <summary>确保引擎持有 Timeline 引用 (废弃引擎直接调用 LoadTimeline)</summary>
        public void EnsureTimelineLoaded(MotionTimeline timeline)
        {
            if (CurrentTimeline == null && timeline != null)
                LoadTimeline(timeline);
        }

        /// <summary>从头播放</summary>
        public void Play()
        {
            if (CurrentTimeline == null)
                return;

            CurrentTimeMs = 0;
            SetState(PlaybackState.Playing);
            _stopwatch.Restart();
            _lastTickMs = 0;
        }

        /// <summary>从指定时间点播放</summary>
        public void PlayFrom(double startMs)
        {
            if (CurrentTimeline == null)
                return;

            CurrentTimeMs = Math.Clamp(startMs, 0, DurationMs);
            SetState(PlaybackState.Playing);
            _stopwatch.Restart();
            _lastTickMs = 0;
        }

        /// <summary>暂停</summary>
        public void Pause()
        {
            if (State != PlaybackState.Playing)
                return;

            _stopwatch.Stop();
            SetState(PlaybackState.Paused);
        }

        /// <summary>恢复播放</summary>
        public void Resume()
        {
            if (State != PlaybackState.Paused)
                return;

            _stopwatch.Start();
            SetState(PlaybackState.Playing);
        }

        /// <summary>停止 (平滑回中)</summary>
        public void Stop()
        {
            if (State == PlaybackState.Idle)
                return;

            if (State == PlaybackState.Playing || State == PlaybackState.Paused)
            {
                // 记录各通道当前值, 准备回中
                _returnStartValues = new Dictionary<string, float>();
                if (CurrentTimeline != null)
                {
                    foreach (var track in CurrentTimeline.Tracks)
                    {
                        if (track.Muted)
                            continue;
                        string key =
                            track.OutputType == "oaxis" && !string.IsNullOrEmpty(track.OAxisChannel)
                                ? $"{track.DeviceName}.oaxis.{track.OAxisChannel}"
                                : $"{track.DeviceName}.{track.OActionName}";
                        // 采样当前时间点的值作为回中起始
                        float currentVal = EvaluateTrack(track, CurrentTimeMs);
                        _returnStartValues[key] = currentVal;
                    }
                }

                _returnElapsedMs = 0;
                _stopwatch.Restart();
                _lastTickMs = 0;
                SetState(PlaybackState.Stopping);
            }
        }

        /// <summary>强制停止 (立即归 Idle, 不回中)</summary>
        public void ForceStop()
        {
            _stopwatch.Stop();
            CurrentTimeMs = 0;
            _returnStartValues = null;
            _safetyGuard.Reset();
            SetState(PlaybackState.Idle);
        }

        /// <summary>跳转到指定时间</summary>
        public void Seek(double timeMs)
        {
            if (CurrentTimeline == null)
                return;

            CurrentTimeMs = Math.Clamp(timeMs, 0, DurationMs);

            // 立即评估并输出 (无论是否在播放)
            if (State == PlaybackState.Playing || State == PlaybackState.Paused)
            {
                EvaluateAndDispatch(0.016); // 假设 16ms 间隔
            }

            PositionChanged?.Invoke(CurrentTimeMs);
        }

        /// <summary>
        /// 核心 Tick — 由外部定时器调用 (推荐 16.67ms 间隔)
        /// </summary>
        public void Tick()
        {
            double currentMs = _stopwatch.Elapsed.TotalMilliseconds;
            double deltaMs = currentMs - _lastTickMs;
            _lastTickMs = currentMs;

            // 安全 clamp: 防止挂起恢复后的巨大 delta
            deltaMs = Math.Min(deltaMs, 100);

            switch (State)
            {
                case PlaybackState.Playing:
                    TickPlaying(deltaMs);
                    break;

                case PlaybackState.Stopping:
                    TickStopping(deltaMs);
                    break;
            }
        }

        // ---- 内部 Tick 逻辑 ----

        private void TickPlaying(double deltaMs)
        {
            CurrentTimeMs += deltaMs * Speed;

            if (CurrentTimeMs >= DurationMs)
            {
                if (Loop)
                {
                    CurrentTimeMs %= DurationMs;
                }
                else
                {
                    CurrentTimeMs = DurationMs;
                    EvaluateAndDispatch(deltaMs / 1000.0);
                    Stop();
                    PlaybackCompleted?.Invoke();
                    return;
                }
            }

            EvaluateAndDispatch(deltaMs / 1000.0);
            PositionChanged?.Invoke(CurrentTimeMs);
        }

        private void TickStopping(double deltaMs)
        {
            _returnElapsedMs += deltaMs;
            float t = (float)Math.Min(_returnElapsedMs / ReturnDurationMs, 1.0);
            float eased = t * t * (3f - 2f * t); // Smoothstep

            if (_returnStartValues != null && CurrentTimeline != null)
            {
                foreach (var track in CurrentTimeline.Tracks)
                {
                    if (track.Muted)
                        continue;
                    string key =
                        track.OutputType == "oaxis" && !string.IsNullOrEmpty(track.OAxisChannel)
                            ? $"{track.DeviceName}.oaxis.{track.OAxisChannel}"
                            : $"{track.DeviceName}.{track.OActionName}";
                    if (_returnStartValues.TryGetValue(key, out float startVal))
                    {
                        float val = startVal + (0.5f - startVal) * eased;
                        _dispatcher.DispatchTrack(
                            track.DeviceName,
                            track.OActionName,
                            track.OutputType,
                            track.OAxisChannel,
                            val
                        );
                    }
                }
            }

            if (t >= 1.0f)
            {
                _stopwatch.Stop();
                _returnStartValues = null;
                _safetyGuard.Reset();
                SetState(PlaybackState.Idle);
            }
        }

        // ---- 评估与分发 ----

        private void EvaluateAndDispatch(double deltaTimeSec)
        {
            if (CurrentTimeline == null)
                return;

            foreach (var track in CurrentTimeline.Tracks)
            {
                if (track.Muted)
                    continue;

                float rawValue = EvaluateTrack(track, CurrentTimeMs);
                string channelKey =
                    track.OutputType == "oaxis" && !string.IsNullOrEmpty(track.OAxisChannel)
                        ? $"{track.DeviceName}.oaxis.{track.OAxisChannel}"
                        : $"{track.DeviceName}.{track.OActionName}";
                float safeValue = _safetyGuard.Check(channelKey, rawValue, deltaTimeSec);
                _dispatcher.DispatchTrack(
                    track.DeviceName,
                    track.OActionName,
                    track.OutputType,
                    track.OAxisChannel,
                    safeValue
                );
            }
        }

        /// <summary>
        /// 评估单个轨道在指定时间的值
        /// </summary>
        private static float EvaluateTrack(MotionTrack track, double absoluteTimeMs)
        {
            foreach (var clip in track.Clips)
            {
                if (absoluteTimeMs >= clip.StartMs && absoluteTimeMs <= clip.EndMs)
                {
                    return InterpolationEngine.EvaluateClip(clip, absoluteTimeMs);
                }
            }
            return 0.5f; // 无 Clip 覆盖时返回中位
        }

        // ---- 状态变更 ----

        private void SetState(PlaybackState newState)
        {
            if (State == newState)
                return;
            var oldState = State;
            State = newState;
            StateChanged?.Invoke(oldState, newState);
        }

        // ---- 事件 ----

        /// <summary>统一通知事件 (废弃引擎仅声明, 不使用)</summary>
        public event Action<PlaybackNotification>? Notified;

        /// <summary>播放完成 (非循环模式到达末尾)</summary>
        public event Action? PlaybackCompleted;

        /// <summary>播放位置变化</summary>
        public event Action<double>? PositionChanged;

        /// <summary>状态变化 (oldState, newState)</summary>
        public event Action<PlaybackState, PlaybackState>? StateChanged;

        /// <summary>时间轴加载完成</summary>
        public event Action<MotionTimeline>? TimelineLoaded;

        /// <summary>批量 LiveValue 更新 (废弃引擎不使用)</summary>
        public event Action<
            IReadOnlyList<(string deviceName, string channelName, float value)>
        >? LiveValuesBatchUpdated;

        /// <summary>是否将预览值实时输出到物理设备 (废弃引擎不使用)</summary>
        public bool LiveOutputToDevice { get; set; }

        /// <summary>释放资源</summary>
        public void Dispose()
        {
            ForceStop();
        }
    }
}
