using System;
using System.Collections.Generic;
using System.Diagnostics;
using IOStudio.Models.Motion;
using IOToolkit;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 编辑器播放引擎 — C# 端驱动时间, 设备派发交给 native (唯一权威)。
    /// <para>
    /// 设计要点 (收敛后):
    /// - 时间驱动 → C# Stopwatch / 外部媒体时钟
    /// - UI 预览求值 → C# TrackValueEvaluator (仅用于显示, 不写设备)
    /// - 设备派发 → native MotionPlayer: 量程换算 + SafetyGuard 限速 + 写设备, 与放映端同源;
    ///   仅在 <see cref="LiveOutputToDevice"/> = true 时驱动 native
    /// - native Slot 走外部时钟: Tick 时 SetExternalTime + MotionPlayer.Tick
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

            // 安全时机 (不在派发路径上): 读设备自述量程
            PrimeDeviceRanges(_currentTimeline);
        }

        public void Play()
        {
            if (_currentTimeline == null)
                return;

            // 安全时机 (不在派发路径上): 读设备自述量程
            PrimeDeviceRanges(_currentTimeline);

            // 设备输出开启时, 由 native Slot 走外部时钟并开始播放, 由它执行换算+限速+写设备。
            // 关闭时纯预览: 不驱动 native、不写设备, 仅 C# 求值供 UI 显示。
            if (LiveOutputToDevice && _slot != null)
            {
                try
                {
                    _slot.ClockMode = IOToolkit.ClockMode.External;
                    _slot.Seek((float)_lastPositionMs);
                    _slot.Play();
                }
                catch (Exception ex)
                {
                    SetError("native 派发启动失败: " + ex.Message);
                }
            }

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

            // 回中交给 native (它自带平滑回中), C# 不再重复做一遍
            if (LiveOutputToDevice && _slot != null)
            {
                try
                {
                    _slot.Stop();
                }
                catch (Exception ex)
                {
                    SetError("native 停止失败: " + ex.Message);
                }
            }

            _lastPositionMs = 0;
            SetState(PlaybackState.Idle);
        }

        public void Seek(double timeMs)
        {
            _lastPositionMs = timeMs;

            if (_slot != null)
            {
                try
                {
                    _slot.Seek((float)timeMs);
                    // 设备输出开启时, 拖拽播放头也要把"该时刻的值"下发一次 —— 否则只有
                    // 播放(逐帧 Tick)才会输出设备, 拖动时间头时仿真侧静止不动。
                    if (LiveOutputToDevice)
                    {
                        _slot.SetExternalTime((float)timeMs);
                        IOToolkit.MotionPlayer.Tick(1f / 60f);
                    }
                }
                catch (Exception ex)
                {
                    SetError("native seek 失败: " + ex.Message);
                }
            }

            PositionChanged?.Invoke(timeMs);
            Notified?.Invoke(new PlaybackNotification.Position(timeMs));
            SyncLiveValues((float)timeMs);
        }

        public void ForceStop()
        {
            _playStopwatch.Stop();

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
            }
        }

        /// <summary>
        /// 外部时钟 (影片/音频) 供时。非 null 时播放位置<b>完全由它决定</b>, 忽略 Stopwatch 与 speed。
        ///
        /// <para>为什么必须由媒体当主时钟: 否则动作(Stopwatch)与画面(LibVLC)各自自由跑,
        /// LibVLC 速率不是精确 1x, 5 分钟以上必然漂移且无人察觉。</para>
        ///
        /// <para>传 null 恢复 Stopwatch 驱动 (无媒体时的纯动作预览)。</para>
        /// </summary>
        public void SetExternalClockMs(double? mediaTimeMs)
        {
            _externalClockMs = mediaTimeMs;
        }

        private double? _externalClockMs;

        private void TickPlaying()
        {
            if (_currentTimeline == null)
                return;

            double currentMs;            if (_externalClockMs is { } mediaMs && mediaMs >= 0)
            {
                // 媒体时钟为主: 位置由媒体决定, 不叠加 speed (媒体按 1x 播放)。
                // 同时把 Stopwatch 对齐到当前位置, 媒体时钟掉线时可无缝回退。
                currentMs = mediaMs;
                _playStartMs = currentMs;
                _playStopwatch.Restart();
            }
            else
            {
                double elapsed = _playStopwatch.Elapsed.TotalMilliseconds * _speed;
                currentMs = _playStartMs + elapsed;
            }

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

            // 设备输出开启时: 把当前时间推给 native Slot, 并驱动它 Tick。
            // 外部时钟模式下 Tick 的 delta 不参与计时 (时间由外部给定), 而 SafetyGuard
            // 用内部时钟测间隔 —— 因此这里的 delta 取值不影响行为。
            if (LiveOutputToDevice && _slot != null)
            {
                try
                {
                    _slot.SetExternalTime((float)currentMs);
                    IOToolkit.MotionPlayer.Tick(1f / 60f);
                }
                catch (Exception ex)
                {
                    SetError("native 派发失败: " + ex.Message);
                }
            }

            PositionChanged?.Invoke(currentMs);
            Notified?.Invoke(new PlaybackNotification.Position(currentMs));
            SyncLiveValues((float)currentMs);
        }

        // ---- LiveValue 同步 (纯 UI 预览) ----

        /// <summary>
        /// 用 C# 求值器算出各轨道当前值, 仅用于 UI 显示 (曲线/实时值)。
        /// 设备写入已完全交给 native (见 TickPlaying); 这里不再写设备、也不做限速。
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
        /// Device-declared engineering range provider. Injected by the host; the source
        /// should be the plugin capabilities / device schema. Returns (min,max) for the
        /// device+channel, or null when not declared.
        /// </summary>
        public static Func<string, string, (float Min, float Max)?>? DeviceRangeProvider { get; set; }

        /// <summary>最近一次内部错误 (native 派发/启动失败等); null = 无。</summary>
        public string? LastError { get; private set; }

        /// <summary>
        /// 记录一次 native 派发/启动失败。
        ///
        /// <para>此前这些失败只写 <c>Debug.WriteLine</c> —— Release 下等于没有, GUI 也从不展示,
        /// 于是"时间轴在走、设备却一动不动、且没有任何提示"这种最难排查的状态会静默出现。
        /// 现在: 写入应用日志(去重, 避免每帧刷屏) + 抛事件供界面提示。</para>
        /// </summary>
        private void SetError(string message)
        {
            LastError = message;
            if (_lastLoggedError == message)
                return; // 同一错误持续发生 (例如每帧 tick 失败) 不重复刷屏
            _lastLoggedError = message;

            try
            {
                Services.AppLog.Error("Engine", message);
            }
            catch
            {
                /* 日志失败不得影响播放 */
            }

            DispatchFailed?.Invoke(message);
        }

        /// <summary>native 派发/启动失败时触发 (已去重; 供界面提示, 不得抛异常)。</summary>
        public event Action<string>? DispatchFailed;

        private string? _lastLoggedError;

        /// <summary>
        /// Device-range warm-up hook. Invoked at safe points (timeline load / play), NEVER from the
        /// dispatch path: reading capabilities goes through a native RPC, and device dispatch itself
        /// runs inside the native tick.
        /// </summary>
        public static Action<IEnumerable<string>>? DeviceRangePrimer { get; set; }

        /// <summary>Warm up device ranges for every device the timeline touches (safe-point only).</summary>
        private static void PrimeDeviceRanges(MotionTimeline? timeline)
        {
            if (timeline == null)
                return;
            try
            {
                var names = new List<string>();
                foreach (var track in timeline.Tracks)
                    if (!string.IsNullOrEmpty(track.DeviceName))
                        names.Add(track.DeviceName);
                if (names.Count > 0)
                    DeviceRangePrimer?.Invoke(names);

                // 把三级优先级**解析后的绝对区间**推给 native (换算由 native 执行,
                // 与放映端共用同一段代码)。解析在宿主侧完成 —— 只有它知道轨道与设备声明。
                IOToolkit.MotionPlayer.ClearChannelRanges();
                foreach (var track in timeline.Tracks)
                {
                    string channel = ChannelKeyOf(track);
                    if (string.IsNullOrEmpty(track.DeviceName) || string.IsNullOrEmpty(channel))
                        continue;

                    var declared = DeviceRangeProvider?.Invoke(track.DeviceName, channel);
                    var resolved = EngineeringValueConverter.ResolveRange(
                        track.ValueType,
                        track.NeutralValue,
                        track.EngineeringRange,
                        declared
                    );
                    if (resolved is { } range)
                        IOToolkit.MotionPlayer.SetChannelRange(
                            track.DeviceName,
                            channel,
                            range.Min,
                            range.Max
                        );
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NativeEngine] PrimeDeviceRanges failed: {ex.Message}");
            }
        }

        /// <summary>Channel identity of a track (OAxis channel or OAction name).</summary>
        private static string ChannelKeyOf(MotionTrack track) =>
            track.OutputType == "oaxis" && !string.IsNullOrEmpty(track.OAxisChannel)
                ? track.OAxisChannel
                : track.OActionName;

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
