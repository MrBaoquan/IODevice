using System;
using System.Collections.Generic;
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
            var issues = RunValidation();
            if (issues is null)
                return;

            int errors = issues.Count(i => i.Severity == MotionIssueSeverity.Error);
            int warnings = issues.Count(i => i.Severity == MotionIssueSeverity.Warning);
            if (errors == 0 && warnings == 0)
                return;

            var first = issues.FirstOrDefault(i => i.Severity == MotionIssueSeverity.Error)
                ?? issues.First(i => i.Severity == MotionIssueSeverity.Warning);

            string suffix = errors > 0 ? $" (首个: {first.Message})" : "";
            PushStatus($"编排预检: {errors} 个错误, {warnings} 个警告{suffix}");
        }

        /// <summary>
        /// 跑一次预检。失败返回 null —— 预检本身出错不应影响播放或编排。
        /// </summary>
        private IReadOnlyList<MotionValidationIssue>? RunValidation()
        {
            try
            {
                return MotionTimelineValidator.Validate(Timeline, BuildValidationOptions());
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 预检的设备侧判据。
        ///
        /// <para><b>两个判据必须分开</b>: "名字没被设备表认领"是必然失败 (要修),
        /// "已声明但设备没装载"是离线编排的正常态 (不能报警)。此前二者共用一个
        /// <c>GetIODevice(name) != null</c> —— 而 <c>IODeviceController</c> 对任何名字都会
        /// 现造一个包装器, 于是判断恒真, <c>track.device_not_loaded</c> 从未触发过。</para>
        /// </summary>
        private static MotionValidationOptions BuildValidationOptions() =>
            new()
            {
                IsDeviceDeclared = MotionDeviceCatalog.IsDeclared,
                IsDeviceLoaded = name =>
                {
                    // 设备表整体没加载时, 任何名字的 IsValid() 都是 false —— 此时不判定,
                    // 否则离线编排会被"设备未加载"淹没。
                    if (!IOToolkit.IODeviceController.IsLoaded)
                        return true;

                    try
                    {
                        return IOToolkit.IODeviceController.GetIODevice(name).IsValid();
                    }
                    catch
                    {
                        return true;
                    }
                },
                DeviceRange = DeviceRangeCatalog.Resolve,
                // 设备已声明但动作没声明 → native 侧 ExternalIO::SetDO 查 OActionMappings 查不到,
                // 直接 return 0 (无日志无计数)。判据与设备名同源, 复用同一份缓存目录。
                IsOActionDeclared = MotionDeviceCatalog.IsOActionDeclared,
            };
    }
}
