namespace IOStudio.Models.Motion
{
    /// <summary>
    /// 时间轴视图模式
    /// </summary>
    public enum TimelineViewMode
    {
        /// <summary>关键帧视图 (Dopesheet) — 菱形标记在水平线上, 紧凑概览</summary>
        Dopesheet,

        /// <summary>曲线视图 (Curves) — 显示数值变化曲线, 精细调节</summary>
        Curves
    }
}
