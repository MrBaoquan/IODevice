using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using IOStudio.ViewModels.Timeline;

namespace IOStudio.Controls.Timeline.Behaviors
{
    /// <summary>
    /// 轨道拖拽排序行为 — 以附加属性方式附加到轨道列表控件,
    /// 替代原 TimelineEditorWindow.axaml.cs 中 ~180 行拖拽排序逻辑。
    /// </summary>
    /// <remarks>
    /// 使用方式 (AXAML):
    /// <code>
    /// &lt;ItemsControl behaviors:DragReorderBehavior.IsEnabled="True"
    ///              behaviors:DragReorderBehavior.ViewModel="{Binding}" /&gt;
    /// </code>
    /// </remarks>
    public static class DragReorderBehavior
    {
        // ── 附加属性 ──

        /// <summary>是否启用拖拽排序。</summary>
        public static readonly AttachedProperty<bool> IsEnabledProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>(
                "IsEnabled",
                typeof(DragReorderBehavior)
            );

        /// <summary>关联的 TimelineEditorViewModel。</summary>
        public static readonly AttachedProperty<TimelineEditorViewModel?> ViewModelProperty =
            AvaloniaProperty.RegisterAttached<Control, TimelineEditorViewModel?>(
                "ViewModel",
                typeof(DragReorderBehavior)
            );

        public static bool GetIsEnabled(Control c) => c.GetValue(IsEnabledProperty);

        public static void SetIsEnabled(Control c, bool v) => c.SetValue(IsEnabledProperty, v);

        public static TimelineEditorViewModel? GetViewModel(Control c) =>
            c.GetValue(ViewModelProperty);

        public static void SetViewModel(Control c, TimelineEditorViewModel? v) =>
            c.SetValue(ViewModelProperty, v);

        // ── 每控件状态 ──
        private sealed class DragState
        {
            public TrackViewModel? DragTrack;
            public bool IsDragging;
            public Point DragStart;
            public Border? CapturedBorder;
        }

        private static readonly AttachedProperty<DragState?> DragStateProperty =
            AvaloniaProperty.RegisterAttached<Control, DragState?>(
                "DragState",
                typeof(DragReorderBehavior)
            );

        private const double DragThreshold = 5;

        // ── 初始化 ──

        static DragReorderBehavior()
        {
            IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
        }

        private static void OnIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.NewValue is true)
            {
                control.AddHandler(
                    InputElement.PointerPressedEvent,
                    OnPointerPressed,
                    Avalonia.Interactivity.RoutingStrategies.Tunnel
                );
            }
            else
            {
                control.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
                control.SetValue(DragStateProperty, null);
            }
        }

        // ── 事件处理 ──

        private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control host || e.Source is not Visual visual)
                return;

            var vm = GetViewModel(host);
            if (vm is null)
                return;

            var border = FindTrackBorder(visual);
            if (border?.Tag is not TrackViewModel trackVm)
                return;

            var props = e.GetCurrentPoint(border).Properties;
            if (!props.IsLeftButtonPressed)
                return;

            // 选中此轨道
            vm.SelectedTrack = trackVm;
            foreach (var t in vm.Tracks)
                t.IsSelected = t == trackVm;

            var state = new DragState
            {
                DragTrack = trackVm,
                IsDragging = false,
                DragStart = e.GetPosition(host),
                CapturedBorder = border
            };
            host.SetValue(DragStateProperty, state);

            e.Pointer.Capture(border);
            border.PointerMoved += (s, ev) => OnPointerMoved(host, ev);
            border.PointerReleased += (s, ev) => OnPointerReleased(host, ev);
        }

        private static void OnPointerMoved(Control host, PointerEventArgs e)
        {
            var state = host.GetValue(DragStateProperty);
            var vm = GetViewModel(host);
            if (state?.DragTrack is null || vm is null)
                return;

            var pos = e.GetPosition(host);
            double dy = pos.Y - state.DragStart.Y;

            if (!state.IsDragging && Math.Abs(dy) > DragThreshold)
            {
                state.IsDragging = true;
                if (state.CapturedBorder is not null)
                {
                    state.CapturedBorder.Opacity = 0.5;
                    state.CapturedBorder.Cursor = new Cursor(StandardCursorType.DragMove);
                }
            }

            if (state.IsDragging)
            {
                double trackH = vm.TrackHeight;

                if (dy > trackH)
                {
                    vm.MoveTrackDown(state.DragTrack);
                    state.DragStart = pos;
                }
                else if (dy < -trackH)
                {
                    vm.MoveTrackUp(state.DragTrack);
                    state.DragStart = pos;
                }
            }
        }

        private static void OnPointerReleased(Control host, PointerReleasedEventArgs e)
        {
            var state = host.GetValue(DragStateProperty);
            var vm = GetViewModel(host);

            if (state?.CapturedBorder is not null)
            {
                state.CapturedBorder.Opacity = 1.0;
                state.CapturedBorder.Cursor = Cursor.Default;
                e.Pointer.Capture(null);
            }

            if (state is { IsDragging: true, DragTrack: not null } && vm is not null)
            {
                // 命中检测: 是否拖到了分组头上
                var pos = e.GetPosition(host);
                var hitTarget = host.InputHitTest(pos) as Visual;
                while (hitTarget is not null)
                {
                    if (hitTarget is Border b && b.Tag is GroupHeaderViewModel targetGroup)
                    {
                        vm.SetTrackGroup(state.DragTrack, targetGroup.Name);
                        break;
                    }
                    hitTarget = hitTarget.GetVisualParent();
                }
            }

            host.SetValue(DragStateProperty, null);
        }

        /// <summary>沿 Visual Tree 向上查找带有 TrackViewModel Tag 的 Border。</summary>
        private static Border? FindTrackBorder(Visual? visual)
        {
            while (visual is not null)
            {
                if (visual is Border border && border.Tag is TrackViewModel)
                    return border;
                visual = visual.GetVisualParent();
            }
            return null;
        }
    }
}
