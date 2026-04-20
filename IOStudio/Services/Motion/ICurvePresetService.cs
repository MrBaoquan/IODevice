using System.Collections.Generic;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 关键帧曲线预设服务接口。
    /// </summary>
    public interface ICurvePresetService
    {
        /// <summary>获取所有内置预设。</summary>
        IReadOnlyList<CurvePresetService.CurvePreset> GetBuiltInPresets();

        /// <summary>按类别分组获取预设。</summary>
        Dictionary<string, List<CurvePresetService.CurvePreset>> GetPresetsByCategory();

        /// <summary>根据名称查找预设。</summary>
        CurvePresetService.CurvePreset? FindPreset(string name);
    }

    /// <summary>
    /// 曲线预设服务实例化包装 — 委托给静态 <see cref="CurvePresetService"/>。
    /// </summary>
    public class CurvePresetServiceInstance : ICurvePresetService
    {
        /// <inheritdoc/>
        public IReadOnlyList<CurvePresetService.CurvePreset> GetBuiltInPresets() =>
            CurvePresetService.BuiltInPresets;

        /// <inheritdoc/>
        public Dictionary<string, List<CurvePresetService.CurvePreset>> GetPresetsByCategory() =>
            CurvePresetService.GetPresetsByCategory();

        /// <inheritdoc/>
        public CurvePresetService.CurvePreset? FindPreset(string name) =>
            CurvePresetService.FindPreset(name);
    }
}
