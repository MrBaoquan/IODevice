using Avalonia.Media;
using System.Collections.Generic;

namespace IOStudio.ViewModels;

/// <summary>
/// 示波器一帧数据：多通道采样窗口 + 可选目标预览曲线。
/// Channels[i] 为时间顺序数组（索引 0 最旧），有效区间 [ValidStart, ValidStart+ValidCount)。
/// </summary>
public class ScopeFrame
{
    public double[][] Channels = System.Array.Empty<double[]>();
    public int ValidStart = 0;
    public int ValidCount = 0;
    public double SampleIntervalMs = 10;
    public IReadOnlyList<IBrush>? Colors;
    public double[]? Preview;
    public bool[]? ChannelVisible;

    public bool HasValidData => ValidCount > 0 && Channels.Length > 0;

    public bool IsChannelVisible(int index)
    {
        return ChannelVisible == null || index >= ChannelVisible.Length || ChannelVisible[index];
    }
}
