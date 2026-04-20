using IOStudio.Models.Motion;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// .motion 文件读写服务接口。
    /// </summary>
    public interface IMotionFileReader
    {
        /// <summary>从 .motion 文件加载时间轴。</summary>
        MotionTimeline? Read(string filePath);

        /// <summary>从 JSON 字符串解析时间轴。</summary>
        MotionTimeline? ReadFromJson(string json);

        /// <summary>将时间轴写入 .motion 文件。</summary>
        /// <returns>是否写入成功。</returns>
        bool Write(MotionTimeline timeline, string filePath);
    }

    /// <summary>
    /// 文件读写服务实例化包装 — 委托给静态 <see cref="MotionFileReader"/>。
    /// </summary>
    public class MotionFileReaderInstance : IMotionFileReader
    {
        /// <inheritdoc/>
        public MotionTimeline? Read(string filePath) => MotionFileReader.Read(filePath);

        /// <inheritdoc/>
        public MotionTimeline? ReadFromJson(string json) => MotionFileReader.ReadFromJson(json);

        /// <inheritdoc/>
        public bool Write(MotionTimeline timeline, string filePath) =>
            MotionFileReader.Write(timeline, filePath);
    }
}
