namespace IOStudio.Models.Motion
{
    /// <summary>
    /// 播放状态枚举
    /// </summary>
    public enum PlaybackState
    {
        /// <summary>未加载或已停止</summary>
        Idle,

        /// <summary>播放中</summary>
        Playing,

        /// <summary>已暂停</summary>
        Paused,

        /// <summary>平滑回中中 (Stop 后渐变到中位)</summary>
        Stopping
    }
}
