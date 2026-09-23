using System;
using System.Linq;
using IOStudio.Services.Motion;

namespace IOStudio.ViewModels.Timeline
{
    /// <summary>
    /// 编排预检的编辑器侧接入 —— 播放前把"会静默失效"的问题摆到状态栏。
    ///
    /// <para>不做阻断: 作者可能正在离线编排 (设备未加载), 硬拦会挡住正常流程。
    /// 预检的职责是<b>让人知道</b>, 门禁交给 CLI 的 <c>--validate</c>。</para>
    /// </summary>
    public partial class TimelineEditorViewModel
    {
        /// <summary>播放前跑一次预检并把摘要推到状态栏 (不阻断播放)。</summary>
        private void ReportValidationSummary()
        {
            try
            {
                var issues = MotionTimelineValidator.Validate(
                    Timeline,
                    new MotionValidationOptions
                    {
                        // 设备存在性: 取不到设备信息时保守返回 true, 避免误报
                        IsDeviceLoaded = name =>
                        {
                            try
                            {
                                return IOToolkit.IODeviceController.GetIODevice(name) != null;
                            }
                            catch
                            {
                                return true;
                            }
                        },
                        DeviceRange = DeviceRangeCatalog.Resolve,
                    }
                );

                int errors = issues.Count(i => i.Severity == MotionIssueSeverity.Error);
                int warnings = issues.Count(i => i.Severity == MotionIssueSeverity.Warning);
                if (errors == 0 && warnings == 0)
                    return;

                var first = issues.FirstOrDefault(i => i.Severity == MotionIssueSeverity.Error)
                    ?? issues.First(i => i.Severity == MotionIssueSeverity.Warning);

                string suffix = errors > 0 ? $" (首个: {first.Message})" : "";
                PushStatus($"编排预检: {errors} 个错误, {warnings} 个警告{suffix}");
            }
            catch
            {
                // 预检失败不应影响播放
            }
        }
    }
}
