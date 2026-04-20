using IOStudio.Models.Motion;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// Motion 时间轴导出服务接口 — 支持 .motion / .csv / .json 格式导出。
    /// </summary>
    public interface IMotionExportService
    {
        /// <summary>导出时间轴到文件。</summary>
        /// <returns>导出结果。</returns>
        MotionExportService.ExportResult Export(
            MotionTimeline timeline,
            string filePath,
            MotionExportService.ExportOptions options
        );
    }

    /// <summary>
    /// 导出服务实例化包装 — 委托给静态 <see cref="MotionExportService"/>。
    /// </summary>
    public class MotionExportServiceInstance : IMotionExportService
    {
        /// <inheritdoc/>
        public MotionExportService.ExportResult Export(
            MotionTimeline timeline,
            string filePath,
            MotionExportService.ExportOptions options
        ) => MotionExportService.Export(timeline, filePath, options);
    }
}
