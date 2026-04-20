using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using LibVLCSharp.Shared;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// LibVLC 视频播放器服务实现 — 封装 LibVLC 生命周期和播放控制。
    /// 延迟初始化: 仅在首次 <see cref="LoadAsync"/> 时创建 LibVLC 实例。
    /// </summary>
    public class LibVlcVideoPlayerService : IVideoPlayerService
    {
        private LibVLC? _libVlc;
        private MediaPlayer? _mediaPlayer;
        private Media? _currentMedia;
        private bool _vlcInitialized;
        private bool _isSeeking;
        private IntPtr _pendingHwnd; // 缓存 HWND, 在 MediaPlayer 创建后自动应用

        /// <inheritdoc/>
        public string? CurrentFilePath { get; private set; }

        /// <inheritdoc/>
        public double DurationMs { get; private set; }

        /// <inheritdoc/>
        public bool IsLoaded => _currentMedia is not null;

        /// <inheritdoc/>
        public bool IsPlaying => _mediaPlayer?.IsPlaying ?? false;

        /// <inheritdoc/>
        public event Action<string>? VideoLoaded;

        /// <inheritdoc/>
        public event Action<double>? DurationDetected;

        /// <inheritdoc/>
        public event Action? MediaPlaying;

        /// <inheritdoc/>
        public event Action? MediaPaused;

        /// <inheritdoc/>
        public event Action? MediaStopped;

        private bool EnsureInitialized()
        {
            if (_vlcInitialized)
                return _libVlc is not null && _mediaPlayer is not null;

            _vlcInitialized = true;
            try
            {
                Core.Initialize();
                _libVlc = new LibVLC("--no-audio", "--no-osd", "--no-snapshot-preview");
                _mediaPlayer = new MediaPlayer(_libVlc);

                _mediaPlayer.LengthChanged += (_, args) =>
                {
                    DurationMs = args.Length;
                    Dispatcher.UIThread.Post(() => DurationDetected?.Invoke(args.Length));
                };
                _mediaPlayer.Playing += (_, _) =>
                    Dispatcher.UIThread.Post(() => MediaPlaying?.Invoke());
                _mediaPlayer.Paused += (_, _) =>
                    Dispatcher.UIThread.Post(() => MediaPaused?.Invoke());
                _mediaPlayer.Stopped += (_, _) =>
                    Dispatcher.UIThread.Post(() => MediaStopped?.Invoke());

                // 如果在 MediaPlayer 创建前已收到 HWND, 立即应用
                if (_pendingHwnd != IntPtr.Zero)
                    _mediaPlayer.Hwnd = _pendingHwnd;

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[LibVlcVideoPlayerService] Init failed: {ex.Message}"
                );
                return false;
            }
        }

        /// <inheritdoc/>
        public Task<bool> LoadAsync(string filePath)
        {
            if (!EnsureInitialized() || _libVlc is null || _mediaPlayer is null)
                return Task.FromResult(false);

            try
            {
                _currentMedia?.Dispose();
                _currentMedia = new Media(_libVlc, filePath, FromType.FromPath);
                _mediaPlayer.Media = _currentMedia;
                CurrentFilePath = filePath;

                // 确保 HWND 已绑定 (防止弹出独立窗口)
                if (_pendingHwnd != IntPtr.Zero)
                    _mediaPlayer.Hwnd = _pendingHwnd;

                // Play→Pause→Seek(0) for initialization
                _mediaPlayer.Play();
                _mediaPlayer.SetPause(true);
                _mediaPlayer.Time = 0;

                VideoLoaded?.Invoke(filePath);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[LibVlcVideoPlayerService] Load failed: {ex.Message}"
                );
                return Task.FromResult(false);
            }
        }

        /// <inheritdoc/>
        public void Play()
        {
            _mediaPlayer?.Play();
        }

        /// <inheritdoc/>
        public void Pause()
        {
            _mediaPlayer?.SetPause(true);
        }

        /// <inheritdoc/>
        public void Stop()
        {
            _mediaPlayer?.Stop();
        }

        /// <inheritdoc/>
        public double CurrentTimeMs => _mediaPlayer?.Time ?? 0;

        /// <inheritdoc/>
        public void SyncTime(double timeMs)
        {
            if (_mediaPlayer is null || _isSeeking)
                return;

            _isSeeking = true;
            try
            {
                _mediaPlayer.Time = (long)timeMs;
            }
            finally
            {
                _isSeeking = false;
            }
        }

        /// <inheritdoc/>
        public void SeekAccurate(double timeMs)
        {
            if (_mediaPlayer is null || _isSeeking)
                return;

            _isSeeking = true;
            try
            {
                // 先粗略 seek 到目标附近 (I 帧)
                _mediaPlayer.Time = (long)timeMs;

                // 使用 NextFrame() 逐帧推进到精确位置
                // LibVLC NextFrame 会解码下一帧并暂停
                if (!_mediaPlayer.IsPlaying)
                {
                    _mediaPlayer.NextFrame();
                }
            }
            finally
            {
                _isSeeking = false;
            }
        }

        /// <inheritdoc/>
        public void SetHwnd(IntPtr hwnd)
        {
            _pendingHwnd = hwnd;
            if (_mediaPlayer is not null)
                _mediaPlayer.Hwnd = hwnd;
        }

        /// <inheritdoc/>
        public void SetScale(float scale)
        {
            if (_mediaPlayer is not null)
                _mediaPlayer.Scale = scale;
        }

        /// <inheritdoc/>
        public void SetMute(bool mute)
        {
            if (_mediaPlayer is not null)
                _mediaPlayer.Mute = mute;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _mediaPlayer?.Stop();
            _mediaPlayer?.Dispose();
            _currentMedia?.Dispose();
            _libVlc?.Dispose();
            _mediaPlayer = null;
            _currentMedia = null;
            _libVlc = null;
        }
    }
}
