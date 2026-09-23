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

            // 安全时机 (不在派发路径上): 读设备自述量程
            PrimeDeviceRanges(_currentTimeline);
        }

        public void Play()
        {
            if (_currentTimeline == null)
                return;

            // 安全时机 (不在派发路径上): 读设备自述量程
            PrimeDeviceRanges(_currentTimeline);

            // 新一次播放: 清空限速基线, 避免上次的值影响本次起步
            _rateLimiter.Reset();
            _lastDispatchMs = double.NaN;

            // native 派发模式: 让 native Slot 走外部时钟并开始播放, 由它执行换算+限速+写设备。
            // (非该模式下不调用 _slot.Play() —— 时间与输出完全由 C# 端驱动)
            if (UseNativeDispatch && _slot != null)
            {
                try
                {
                    _slot.ClockMode = IOToolkit.ClockMode.External;
                    _slot.Seek((float)_lastPositionMs);
                    _slot.Play();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NativeEngine] native 派发启动失败: {ex.Message}");
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

            // native 派发模式: 回中也交给 native (它自带平滑回中), C# 不再重复做一遍
            if (UseNativeDispatch && _slot != null)
            {
                try
                {
                    _slot.Stop();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NativeEngine] native 停止失败: {ex.Message}");
                }
                _lastPositionMs = 0;
                SetState(PlaybackState.Idle);
                return;
            }

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

            // native 派发模式: 把当前时间推给 native Slot, 并驱动它 Tick。
            // 外部时钟模式下 Tick 的 delta 不参与计时 (时间由外部给定), 而 SafetyGuard
            // 用内部时钟测间隔 —— 因此这里的 delta 取值不影响行为。
            if (UseNativeDispatch && _slot != null)
            {
                try
                {
                    _slot.SetExternalTime((float)currentMs);
                    IOToolkit.MotionPlayer.Tick(1f / 60f);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NativeEngine] native 派发失败: {ex.Message}");
                }
            }

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
        private readonly SafetyRateLimiter _rateLimiter = new();

        /// <summary>上次派发的时间 (ms)。NaN = 尚未派发过。</summary>
        private double _lastDispatchMs = double.NaN;

        private void SyncLiveValues(float timeMs)
        {
            if (_currentTimeline == null)
                return;

            var tracks = _currentTimeline.Tracks;
            if (tracks.Count == 0)
                return;

            // 速率限制的时间步长由实际派发时间差得出: Seek / 开始播放 / 长卡顿会自然
            // 形成"不连续", 由 SafetyRateLimiter 识别为重置而非限速。
            double deltaSec = double.IsNaN(_lastDispatchMs)
                ? 0
                : (timeMs - _lastDispatchMs) / 1000.0;
            _lastDispatchMs = timeMs;

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

                // 安全限速 (仅当设备声明速率): 限制单帧归一化变化量, 防止命令出物理上过猛的跳变。
                // 限速后的值同时用于 UI 批量值, 保证"看到的就是下发的"。
                // native 派发模式下限速与写设备都在 native (所以这里只更新 UI 值)。
                if (!UseNativeDispatch)
                {
                    float? maxRate = DeviceMaxRateProvider?.Invoke(track.DeviceName);
                    if (maxRate is { } rate && rate > 0f)
                        value = _rateLimiter.Limit(
                            track.DeviceName + "|" + channelName,
                            value,
                            deltaSec,
                            rate
                        );
                }

                _batchBuffer.Add((track.DeviceName, channelName, value));

                if (LiveOutputToDevice && !UseNativeDispatch)
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
                {
                    // 设备名写错会导致整条轨道静默失效，这里明确告警一次
                    Debug.WriteLine($"[NativeEngine] 设备不存在，轨道未输出: device='{track.DeviceName}' track='{track.Label}'");
                    return;
                }

                // 值域换算：内容层是归一化 0..1（中位 = neutral），通道 ABI 要工程量。
                // 放在这里（设备写入的唯一收口点），内容文件保持设备无关。
                float engineering = ToEngineering(track, value);

                if (track.OutputType == "oaxis" && !string.IsNullOrEmpty(track.OAxisChannel))
                {
                    IOToolkit.Key key = track.OAxisChannel;
                    device.SetDO(key, engineering);
                }
                else
                {
                    device.SetDO(track.OActionName, engineering);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[NativeEngine] WriteToDevice failed [{track.DeviceName}]: {ex.Message}"
                );
            }
        }

        /// <summary>
        /// 归一化值 → 工程量。未声明量程时直通，保证既有动作文件行为不变。
        /// <para>优先级：轨道 engineering_range（显式覆盖）→ （后续）设备 schema 声明量程 → 直通。</para>
        /// </summary>
        /// <summary>
        /// Device-declared engineering range provider. Injected by the host; the source
        /// should be the plugin capabilities / device schema. Returns (min,max) for the
        /// device+channel, or null when not declared.
        /// </summary>
        public static Func<string, string, (float Min, float Max)?>? DeviceRangeProvider { get; set; }

        /// <summary>
        /// 设备声明的最大变化速率 (归一化单位/秒)。由宿主注入; 返回 null = 未声明 → **不限速**。
        ///
        /// <para>只在设备自己声明速率时才启用限速, 因此对未声明的设备行为与引入前完全一致。
        /// 与 DeviceRangeProvider 一样是纯缓存查表, 可在派发路径安全调用。</para>
        /// </summary>
        public static Func<string, float?>? DeviceMaxRateProvider { get; set; }

        /// <summary>
        /// 是否由 native 承担**派发** (量程换算 + SafetyGuard 限速都在 native, 与放映端同一段代码)。
        ///
        /// <para>默认关闭: C# 侧驱动时间并直接写设备。打开后: 把解析后的量程推给 native、按帧驱动
        /// native Tick, C# 只负责驱动时间与 UI 显示 (不再直接写设备)。</para>
        ///
        /// <para>限速强度与驱动频率无关 —— native SafetyGuard 用内部时钟测实际间隔。</para>
        /// </summary>
        public bool UseNativeDispatch { get; set; }

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

        /// <summary>
        /// Normalized value -> engineering value. Pass-through when nothing is declared,
        /// so existing motion files keep their behaviour.
        /// Resolution order:
        ///   1) track engineering_range (explicit override; a reviewable deviation)
        ///   2) device-declared range via DeviceRangeProvider (single source of truth)
        ///   3) pass-through
        /// </summary>
        private static float ToEngineering(MotionTrack track, float normalized)
        {
            // 设备声明量程: 纯缓存查表, 不触碰 native, 故可安全地在派发路径调用。
            var declared = DeviceRangeProvider?.Invoke(track.DeviceName, ChannelKeyOf(track));

            return EngineeringValueConverter.ToEngineering(
                normalized,
                track.ValueType,
                track.NeutralValue,
                track.EngineeringRange,
                declared
            );
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
