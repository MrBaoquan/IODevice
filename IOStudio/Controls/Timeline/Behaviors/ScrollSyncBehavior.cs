using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using IOStudio.Controls.Timeline;
using IOStudio.ViewModels.Timeline;

namespace IOStudio.Controls.Timeline.Behaviors
{
    /// <summary>
    /// 滚动/缩放同步行为 — 以附加属性方式同步 TrackHeaderScroller 与 TrackClipScroller 的垂直偏移,
    /// 以及 PixelsPerMs / ScrollOffsetX 到 TimeRuler 和所有 TrackClipControl。
    /// </summary>
    /// <remarks>
    /// 提取自 TimelineEditorWindow.axaml.cs 中:
    /// - SetupScrollSync() (~12 行)
    /// - SyncScrollToClips() (~15 行)
    /// - SyncZoomToRuler() (~10 行)
    /// 总计 ~37 行 → 本行为类统一管理。
    ///
    /// AXAML 使用:
    /// <code>
    /// &lt;Grid behaviors:ScrollSyncBehavior.IsEnabled="True"
    ///       behaviors:ScrollSyncBehavior.ViewModel="{Binding}" /&gt;
    /// </code>
    /// </remarks>
    public static class ScrollSyncBehavior
    {
        // ── 附加属性 ──

        public static readonly AttachedProperty<bool> IsEnabledProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>(
                "IsEnabled",
                typeof(ScrollSyncBehavior)
            );

        public static readonly AttachedProperty<TimelineEditorViewModel?> ViewModelProperty =
            AvaloniaProperty.RegisterAttached<Control, TimelineEditorViewModel?>(
                "ViewModel",
                typeof(ScrollSyncBehavior)
            );

        public static bool GetIsEnabled(Control c) => c.GetValue(IsEnabledProperty);

        public static void SetIsEnabled(Control c, bool v) => c.SetValue(IsEnabledProperty, v);

        public static TimelineEditorViewModel? GetViewModel(Control c) =>
            c.GetValue(ViewModelProperty);

        public static void SetViewModel(Control c, TimelineEditorViewModel? v) =>
            c.SetValue(ViewModelProperty, v);

        static ScrollSyncBehavior()
        {
            IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
        }

        private static void OnIsEnabledChanged(Control host, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.NewValue is true && host is Grid grid)
            {
                // 延迟到控件准备就绪后再绑定
                host.AttachedToVisualTree += (s, args) => AttachSync(grid);
            }
        }

        private static void AttachSync(Grid host)
        {
            // 查找同级 ScrollViewer
            var clipScroller =
                host.FindControl<ScrollViewer>("TrackClipScroller")
                ?? host.GetVisualDescendants()
                    .OfType<ScrollViewer>()
                    .FirstOrDefault(sv => sv.Name == "TrackClipScroller");

            var headerScroller =
                host.FindControl<ScrollViewer>("TrackHeaderScroller")
                ?? host.GetVisualDescendants()
                    .OfType<ScrollViewer>()
                    .FirstOrDefault(sv => sv.Name == "TrackHeaderScroller");

            if (clipScroller != null && headerScroller != null)
            {
                clipScroller.ScrollChanged += (s, e) =>
                {
                    headerScroller.Offset = new Vector(0, clipScroller.Offset.Y);
                };
            }
        }

        /// <summary>
        /// 同步 ScrollOffsetX + PixelsPerMs 到 TimeRuler (由 ViewModel 变更驱动调用)。
        /// </summary>
        public static void SyncZoomToRuler(Control host, TimelineEditorViewModel vm)
        {
            var ruler = host.GetVisualDescendants().OfType<TimeRulerControl>().FirstOrDefault();
            if (ruler != null)
            {
                ruler.PixelsPerMs = vm.PixelsPerMs;
                ruler.ScrollOffsetX = vm.ScrollOffsetX;
                ruler.InvalidateVisual();
            }
        }

        /// <summary>
        /// 同步 ScrollOffsetX + PixelsPerMs 到所有 TrackClipControl (播放自动滚动时调用)。
        /// </summary>
        public static void SyncScrollToClips(Control host, TimelineEditorViewModel vm)
        {
            double offset = vm.ScrollOffsetX;
            double ppm = vm.PixelsPerMs;
            foreach (var tcc in host.GetVisualDescendants().OfType<TrackClipControl>())
            {
                tcc.ScrollOffsetX = offset;
                tcc.PixelsPerMs = ppm;
                tcc.InvalidateVisual();
            }
        }
    }
}
