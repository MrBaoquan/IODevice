using System.Text.Json.Serialization;

namespace IOStudio.Models.Motion
{
    /// <summary>
    /// 影片媒体绑定 — 让"影片 + 动作"成为一个可复现的项目整体。
    ///
    /// <para>没有它, 每次打开 .motion 都要重新选片, 而且无法判断当前动作是否与正确的影片对齐
    /// (5 分钟以上的影片尤其致命)。</para>
    /// </summary>
    public class MotionMedia
    {
        /// <summary>
        /// 视频文件路径。优先存<b>相对于 .motion 所在目录</b>的路径 (可移植), 否则存绝对路径。
        /// 加载时若解析不到, 只提示"媒体缺失", 不阻断动作编辑。
        /// </summary>
        [JsonPropertyName("video_path")]
        public string? VideoPath { get; set; }

        /// <summary>视频时长 (毫秒)。用于媒体尚未加载时也能给出正确的时间轴范围。</summary>
        [JsonPropertyName("duration_ms")]
        public double DurationMs { get; set; }
    }
}
