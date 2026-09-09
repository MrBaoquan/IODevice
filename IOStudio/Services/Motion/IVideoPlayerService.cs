using System;
using System.Threading.Tasks;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 视频播放器服务接口 — 抽象视频播放/暂停/同步操作,
    /// 使 ViewModel 不直接依赖 LibVLC 或 UI 控件。
    /// </summary>
    public interface IVideoPlayerService : IDisposable
    {
        /// <summary>当前视频文件路径。</summary>
        string? CurrentFilePath { get; }

        /// <summary>视频时长 (毫秒)。</summary>
        double DurationMs { get; }

        /// <summary>是否已加载视频。</summary>
        bool IsLoaded { get; }

        /// <summary>是否正在播放。</summary>
        bool IsPlaying { get; }

        /// <summary>加载视频文件。</summary>
        Task<bool> LoadAsync(string filePath);

        /// <summary>播放。</summary>
        void Play();

        /// <summary>暂停。</summary>
        void Pause();

        /// <summary>停止。</summary>
        void Stop();

        /// <summary>同步到指定时间 (毫秒)。</summary>
        void SyncTime(double timeMs);

        /// <summary>
        /// 精确帧预览 — 使用 Play→短暂解码→Pause 方式 seek 到精确帧,
        /// 比直接设置 Time 属性更准确 (默认仅 seek 到最近 I 帧)。
        /// </summary>
        void SeekAccurate(double timeMs);

        /// <summary>获取当前播放位置 (毫秒)。</summary>
        double CurrentTimeMs { get; }

        /// <summary>绑定渲染窗口句柄 (Windows HWND)。</summary>
        void SetHwnd(IntPtr hwnd);

        /// <summary>设置缩放比例。0 = 自适应窗口, >0 = 指定倍数。</summary>
        void SetScale(float scale);

        /// <summary>设置静音状态。</summary>
        void SetMute(bool mute);

        /// <summary>视频加载完成事件。</summary>
        event Action<string>? VideoLoaded;

        /// <summary>视频时长检测完成事件。</summary>
        event Action<double>? DurationDetected;

        /// <summary>播放状态变化事件: Playing。</summary>
        event Action? MediaPlaying;

        /// <summary>播放状态变化事件: Paused。</summary>
        event Action? MediaPaused;

        /// <summary>播放状态变化事件: Stopped。</summary>
        event Action? MediaStopped;
    }
}
