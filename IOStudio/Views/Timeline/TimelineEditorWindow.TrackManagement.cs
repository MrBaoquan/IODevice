using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using IOStudio.Controls.Timeline;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using IOStudio.ViewModels.Timeline;

namespace IOStudio.Views.Timeline
{
    /// <summary>杞ㄩ亾绠＄悊 (宸ュ叿鏍?鍙抽敭鑿滃崟/鍒嗙粍/鎷栨嫿鎺掑簭/鏃堕暱缂栬緫) (浠?code-behind 鎻愬彇)</summary>
    public partial class TimelineEditorWindow
    {
        // ═══════ 工具栏按钮 ═══════

        private async void OnAddTrack(object? sender, RoutedEventArgs e)
        {
            if (ViewModel != null && ViewModel.Timeline == null)
                ViewModel.NewProjectCommand.Execute().Subscribe();

            var dialog = new AddTrackDialog();
            await dialog.ShowDialog(this);

            if (dialog.Result != null)
            {
                ViewModel?.AddTrack(
                    dialog.Result.DeviceName,
                    dialog.Result.OActionName,
                    dialog.Result.Label,
                    dialog.Result.Color,
                    dialog.Result.ValueType,
                    dialog.Result.OutputType,
                    dialog.Result.OAxisChannel
                );
            }
        }

        /// <summary>从分组头右键菜单添加轨道 — 自动加入该分组</summary>
        private async void OnAddTrackToGroup(object? sender, RoutedEventArgs e)
        {
            if (ViewModel == null)
                return;
            if (ViewModel.Timeline == null)
                ViewModel.NewProjectCommand.Execute().Subscribe();

            // 获取分组上下文
            GroupHeaderViewModel? groupVm = null;
            if (sender is MenuItem mi && mi.Tag is GroupHeaderViewModel g)
                groupVm = g;

            var dialog = new AddTrackDialog();
            await dialog.ShowDialog(this);

            if (dialog.Result != null)
            {
                ViewModel.AddTrack(
                    dialog.Result.DeviceName,
                    dialog.Result.OActionName,
                    dialog.Result.Label,
                    dialog.Result.Color,
                    dialog.Result.ValueType,
                    dialog.Result.OutputType,
                    dialog.Result.OAxisChannel
                );

                // 将新添加的轨道加入目标分组
                if (groupVm != null && ViewModel.Tracks.Count > 0)
                {
                    var newTrack = ViewModel.Tracks[^1]; // 最后添加的轨道
                    ViewModel.SetTrackGroup(newTrack, groupVm.Name);
                }
            }
        }

        /// <summary>
        /// 在播放头位置添加关键帧
        /// </summary>
        private void OnAddKeyframeAtPlayhead(object? sender, RoutedEventArgs e)
        {
            ViewModel?.AddKeyframeAtPlayhead();
            RefreshPropertyPanel();
            // 刷新所有 TrackClipControl
            foreach (var tcc in this.GetVisualDescendants().OfType<TrackClipControl>())
            {
                tcc.InvalidateVisual();
            }
        }

        /// <summary>删除轨道</summary>
        /// <summary>轨道启用/禁用切换</summary>
        private void OnToggleTrackEnabled(object? sender, RoutedEventArgs e)
        {
            if (
                sender is Avalonia.Controls.Primitives.ToggleButton tb
                && tb.DataContext is TrackViewModel trackVm
            )
            {
                // 刷新所有轨道外观 (Solo 联动)
                if (ViewModel != null)
                    foreach (var t in ViewModel.Tracks)
                        t.RaiseDimmingChanged();
                SyncCurveEditorData();
                RefreshAllTrackControls();
                ViewModel?.MarkDirty();
            }
        }

        /// <summary>轨道独奏切换</summary>
        private void OnToggleTrackSolo(object? sender, RoutedEventArgs e)
        {
            if (
                sender is Avalonia.Controls.Primitives.ToggleButton tb
                && tb.DataContext is TrackViewModel trackVm
            )
            {
                // Solo 变化影响所有轨道的外观
                if (ViewModel != null)
                    foreach (var t in ViewModel.Tracks)
                        t.RaiseDimmingChanged();
                SyncCurveEditorData();
                RefreshAllTrackControls();
                ViewModel?.MarkDirty();
            }
        }

        /// <summary>轨道静音切换</summary>
        private void OnToggleTrackMuted(object? sender, RoutedEventArgs e)
        {
            if (
                sender is Avalonia.Controls.Primitives.ToggleButton tb
                && tb.DataContext is TrackViewModel trackVm
            )
            {
                SyncCurveEditorData();
                RefreshAllTrackControls();
                ViewModel?.MarkDirty();
            }
        }

        private void OnDeleteTrack(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TrackViewModel trackVm)
            {
                ViewModel?.RemoveTrack(trackVm);
            }
        }

        /// <summary>轨道上移</summary>
        private void OnMoveTrackUp(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TrackViewModel trackVm)
            {
                ViewModel?.MoveTrackUp(trackVm);
            }
        }

        /// <summary>轨道下移</summary>
        private void OnMoveTrackDown(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TrackViewModel trackVm)
            {
                ViewModel?.MoveTrackDown(trackVm);
            }
        }

        /// <summary>删除选中轨道 (菜单操作)</summary>
        private void OnDeleteSelectedTrack(object? sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedTrack != null)
            {
                ViewModel.RemoveTrack(ViewModel.SelectedTrack);
            }
        }

        // ═══════ 轨道右键上下文菜单 ═══════
        // 注: MoveTrackUp/Down, ToggleLock, DeleteTrack, RemoveFromGroup
        //     已迁移到 TimelineEditorViewModel.ContextMenuCommands.cs，
        //     通过 AXAML Command 绑定调用。

        /// <summary>设置轨道独立高度 (右键菜单)</summary>
        private void OnCtxSetTrackHeight(object? sender, RoutedEventArgs e)
        {
            if (
                sender is MenuItem mi
                && mi.Tag is TrackViewModel trackVm
                && mi.CommandParameter is string heightStr
                && ViewModel != null
            )
            {
                if (double.TryParse(heightStr, out double height))
                {
                    ViewModel.SetTrackIndividualHeight(trackVm, height);
                    RefreshAllTrackControls();
                }
            }
        }

        /// <summary>重置轨道为全局高度 (右键菜单)</summary>
        private void OnCtxResetTrackHeight(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is TrackViewModel trackVm && ViewModel != null)
            {
                ViewModel.ResetTrackHeight(trackVm);
                RefreshAllTrackControls();
            }
        }

        /// <summary>设置轨道颜色 (右键菜单)</summary>
        private void OnCtxSetColor(object? sender, RoutedEventArgs e)
        {
            if (
                sender is MenuItem mi
                && mi.Tag is TrackViewModel trackVm
                && mi.CommandParameter is string color
            )
            {
                trackVm.Color = color;
                // 刷新轨道绘制
                foreach (var tcc in this.GetVisualDescendants().OfType<TrackClipControl>())
                {
                    if (tcc.DataContext == trackVm)
                        tcc.InvalidateVisual();
                }
                // 同步颜色到曲线编辑器
                SyncCurveEditorData();
                ViewModel?.MarkDirty();
            }
        }

        // ═══════ 轨道分组 ═══════

        /// <summary>树形分组展开/折叠按钮点击</summary>
        private void OnToggleGroupExpand(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TrackViewModel trackVm && ViewModel != null)
            {
                if (!string.IsNullOrEmpty(trackVm.Group))
                    ViewModel.ToggleGroupCollapse(trackVm.Group);
            }
            else if (
                sender is Button btn2
                && btn2.Tag is GroupHeaderViewModel groupVm
                && ViewModel != null
            )
            {
                ViewModel.ToggleGroupCollapse(groupVm.Name);
            }
        }

        // ═══════ 分组头右键菜单 ═══════

        /// <summary>分组头右键: 重命名分组</summary>
        private async void OnCtxRenameGroup(object? sender, RoutedEventArgs e)
        {
            if (ViewModel is null)
                return;

            GroupHeaderViewModel? groupVm = null;
            if (sender is MenuItem mi && mi.Tag is GroupHeaderViewModel g)
                groupVm = g;
            if (groupVm is null)
                return;

            var vm = new GroupDialogViewModel(GroupDialogMode.Rename, groupVm.Name);
            var dialog = new GroupDialog(vm);
            await dialog.ShowDialog(this);

            if (
                vm.IsConfirmed
                && !string.IsNullOrWhiteSpace(vm.GroupName)
                && vm.GroupName.Trim() != groupVm.Name
            )
            {
                ViewModel.RenameGroup(groupVm.Name, vm.GroupName.Trim());
            }
        }

        // 注: DeleteGroup 已迁移到 ViewModel.DeleteGroupCommand (AXAML Command 绑定)

        // ═══════ 轨道面板级别的分组管理 (右键面板空白区) ═══════

        /// <summary>面板右键: 新建分组 (弹出对话框输入名称并选择轨道)</summary>
        private async void OnPanelNewGroup(object? sender, RoutedEventArgs e)
        {
            if (ViewModel is null)
                return;

            var vm = new GroupDialogViewModel(GroupDialogMode.NewWithTracks);
            vm.PopulateTracks(ViewModel.Tracks, ViewModel.SelectedTrack);
            var dialog = new GroupDialog(vm);
            await dialog.ShowDialog(this);

            if (vm.IsConfirmed && !string.IsNullOrWhiteSpace(vm.GroupName))
            {
                var groupName = vm.GroupName.Trim();
                ViewModel.CreateGroup(groupName);

                var selectedTracks = vm.GetSelectedTracks();
                if (selectedTracks.Length > 0)
                {
                    foreach (var track in selectedTracks)
                        ViewModel.SetTrackGroup(track, groupName);
                }
                else if (ViewModel.SelectedTrack is not null)
                {
                    ViewModel.SetTrackGroup(ViewModel.SelectedTrack, groupName);
                }
            }
        }

        // 注: CollapseAllGroups / ExpandAllGroups 已迁移到 ViewModel Commands (AXAML 绑定)

        /// <summary>新建分组并将当前轨道放入</summary>
        private async void OnCtxNewGroup(object? sender, RoutedEventArgs e)
        {
            if (
                sender is not MenuItem mi
                || mi.Tag is not TrackViewModel trackVm
                || ViewModel is null
            )
                return;

            var vm = new GroupDialogViewModel(GroupDialogMode.NewSimple);
            var dialog = new GroupDialog(vm);
            await dialog.ShowDialog(this);

            if (vm.IsConfirmed && !string.IsNullOrWhiteSpace(vm.GroupName))
            {
                ViewModel.SetTrackGroup(trackVm, vm.GroupName.Trim());
            }
        }

        /// <summary>移入已有分组</summary>
        private void OnCtxMoveToGroup(object? sender, RoutedEventArgs e)
        {
            if (
                sender is not MenuItem mi
                || mi.Tag is not TrackViewModel trackVm
                || ViewModel == null
            )
                return;

            var groups = ViewModel.GetGroupNames();
            if (groups.Count == 0)
            {
                // 没有分组, 引导新建
                OnCtxNewGroup(sender, e);
                return;
            }

            // 动态构建子菜单
            var menu = new ContextMenu();
            foreach (var gn in groups)
            {
                var item = new MenuItem { Header = gn };
                var groupName = gn;
                item.Click += (_, _) =>
                {
                    ViewModel.SetTrackGroup(trackVm, groupName);
                };
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            var newGroupItem = new MenuItem { Header = "+ 新建分组..." };
            newGroupItem.Click += (_, _) => OnCtxNewGroup(sender, e);
            menu.Items.Add(newGroupItem);
            _activeContextMenu?.Close();
            _activeContextMenu = menu;
            menu.Open(this);
        }

        // ═══════ 轨道高度循环 ═══════

        /// <summary>循环切换轨道高度</summary>
        private void OnCycleTrackHeight(object? sender, RoutedEventArgs e)
        {
            if (ViewModel == null)
                return;
            ViewModel.CycleTrackHeight();
            RefreshAllTrackControls();
        }

        /// <summary>重置所有轨道为全局默认高度</summary>
        private void OnResetAllTrackHeights(object? sender, RoutedEventArgs e)
        {
            if (ViewModel == null)
                return;
            ViewModel.ResetAllTrackHeights();
            RefreshAllTrackControls();
        }

        /// <summary>切换波形轨道显示/隐藏。</summary>
        private void OnToggleWaveform(object? sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
                ViewModel.IsWaveformVisible = !ViewModel.IsWaveformVisible;
        }

        /// <summary>切换节拍检测启用/禁用。启用时如果波形已存在则立即执行检测。</summary>
        private void OnToggleBeatDetection(object? sender, RoutedEventArgs e)
        {
            if (ViewModel is null)
                return;

            ViewModel.IsBeatDetectionEnabled = !ViewModel.IsBeatDetectionEnabled;

            // 如果刚启用且波形数据已存在, 立即执行节拍检测
            if (
                ViewModel.IsBeatDetectionEnabled
                && ViewModel.WaveformData is not null
                && ViewModel.BeatMarkers is null
            )
            {
                var beats = BeatDetector.DetectBeats(ViewModel.WaveformData);
                ViewModel.BeatMarkers = beats;
                var waveformControl = this.FindControl<WaveformControl>("WaveformTrack");
                waveformControl?.SetBeatMarkers(beats);
            }
            // 如果禁用, 清除节拍标记
            else if (!ViewModel.IsBeatDetectionEnabled)
            {
                ViewModel.BeatMarkers = null;
                var waveformControl = this.FindControl<WaveformControl>("WaveformTrack");
                waveformControl?.SetBeatMarkers(null);
            }
        }

        // ═══════ 总时长编辑 ═══════

        private void OnDurationDoubleClick(object? sender, TappedEventArgs e)
        {
            var displayText = this.FindControl<TextBlock>("DurationDisplayText");
            var editBox = this.FindControl<TextBox>("DurationEditBox");
            if (displayText == null || editBox == null || ViewModel == null)
                return;

            // 切换到编辑模式
            displayText.IsVisible = false;
            editBox.IsVisible = true;
            // 显示当前秒数 (0 = 自动)
            double currentSec =
                ViewModel.ManualDurationMs > 0
                    ? ViewModel.ManualDurationMs / 1000.0
                    : ViewModel.DurationMs / 1000.0;
            editBox.Text = currentSec.ToString("F1");
            editBox.Focus();
            editBox.SelectAll();
        }

        private void OnDurationEditKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitDurationEdit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelDurationEdit();
                e.Handled = true;
            }
        }

        private void OnDurationEditLostFocus(object? sender, RoutedEventArgs e)
        {
            CommitDurationEdit();
        }

        private void CommitDurationEdit()
        {
            var displayText = this.FindControl<TextBlock>("DurationDisplayText");
            var editBox = this.FindControl<TextBox>("DurationEditBox");
            if (displayText == null || editBox == null || ViewModel == null)
                return;

            if (double.TryParse(editBox.Text, out double seconds) && seconds >= 0)
            {
                double ms = seconds * 1000.0;
                ViewModel.SetManualDuration(ms);
            }

            displayText.IsVisible = true;
            editBox.IsVisible = false;
        }

        private void CancelDurationEdit()
        {
            var displayText = this.FindControl<TextBlock>("DurationDisplayText");
            var editBox = this.FindControl<TextBox>("DurationEditBox");
            if (displayText != null)
                displayText.IsVisible = true;
            if (editBox != null)
                editBox.IsVisible = false;
        }

        // ═══════ 当前时间编辑 (双击输入精确时间) ═══════

        private void OnCurrentTimeDoubleClick(object? sender, TappedEventArgs e)
        {
            var displayText = this.FindControl<TextBlock>("CurrentTimeDisplayText");
            var editBox = this.FindControl<TextBox>("CurrentTimeEditBox");
            if (displayText == null || editBox == null || ViewModel == null)
                return;

            displayText.IsVisible = false;
            editBox.IsVisible = true;
            // 显示当前秒数
            double currentSec = ViewModel.CurrentTimeMs / 1000.0;
            editBox.Text = currentSec.ToString("F3");
            editBox.Focus();
            editBox.SelectAll();
        }

        private void OnCurrentTimeEditKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitCurrentTimeEdit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelCurrentTimeEdit();
                e.Handled = true;
            }
        }

        private void OnCurrentTimeEditLostFocus(object? sender, RoutedEventArgs e)
        {
            CommitCurrentTimeEdit();
        }

        private void CommitCurrentTimeEdit()
        {
            var displayText = this.FindControl<TextBlock>("CurrentTimeDisplayText");
            var editBox = this.FindControl<TextBox>("CurrentTimeEditBox");
            if (displayText == null || editBox == null || ViewModel == null)
                return;

            string input = editBox.Text?.Trim() ?? "";
            double? timeMs = ParseTimeInput(input);
            if (timeMs.HasValue)
            {
                double clamped = Math.Max(0, Math.Min(timeMs.Value, ViewModel.DurationMs));
                ViewModel.SeekTo(clamped);
                // 直接同步视频
                var videoPreview = this.FindControl<VideoPreviewControl>("VideoPreview");
                videoPreview?.SyncTime(clamped);
                var ruler = this.FindControl<TimeRulerControl>("TimeRuler");
                ruler?.InvalidateVisual();
            }

            displayText.IsVisible = true;
            editBox.IsVisible = false;
        }

        private void CancelCurrentTimeEdit()
        {
            var displayText = this.FindControl<TextBlock>("CurrentTimeDisplayText");
            var editBox = this.FindControl<TextBox>("CurrentTimeEditBox");
            if (displayText != null)
                displayText.IsVisible = true;
            if (editBox != null)
                editBox.IsVisible = false;
        }

        /// <summary>
        /// 解析时间输入: 支持纯秒数 (如 "3.5") 或 mm:ss.fff 格式
        /// </summary>
        private static double? ParseTimeInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            // 尝试 mm:ss.fff 格式
            if (input.Contains(':'))
            {
                var parts = input.Split(':');
                if (
                    parts.Length == 2
                    && double.TryParse(parts[0], out double mins)
                    && double.TryParse(parts[1], out double secs)
                )
                {
                    return (mins * 60 + secs) * 1000.0;
                }
                return null;
            }

            // 尝试纯秒数
            if (double.TryParse(input, out double seconds) && seconds >= 0)
            {
                return seconds * 1000.0;
            }

            // 尝试纯毫秒数 (大数字自动识别为 ms)
            if (double.TryParse(input, out double ms) && ms > 100)
            {
                return ms;
            }

            return null;
        }

        // ═══════ 轨道拖拽排序 ═══════

        private TrackViewModel? _dragTrack;
        private bool _isTrackDragging;
        private Point _trackDragStart;
        private const double DragThreshold = 5;

        /// <summary>
        /// 轨道头按下 — 准备拖拽排序, 同时选中轨道
        /// </summary>
        private void OnTrackHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && border.Tag is TrackViewModel trackVm)
            {
                // 选中此轨道 (更新 ViewModel.IsSelected + TrackClipControl.IsTrackSelected)
                if (ViewModel != null)
                {
                    ViewModel.SelectedTrack = trackVm;
                    foreach (var t in ViewModel.Tracks)
                        t.IsSelected = (t == trackVm);
                    SyncSelectedTrackToCurveEditor(trackVm);
                }

                // 标记右侧轨道片段高亮
                foreach (var tcc in this.GetVisualDescendants().OfType<TrackClipControl>())
                {
                    bool match = tcc.DataContext == trackVm;
                    tcc.IsTrackSelected = match;
                }

                var props = e.GetCurrentPoint(border).Properties;
                if (props.IsLeftButtonPressed)
                {
                    _dragTrack = trackVm;
                    _isTrackDragging = false;
                    _trackDragStart = e.GetPosition(this);
                    e.Pointer.Capture(border);
                    border.PointerMoved += OnTrackHeaderPointerMoved;
                    border.PointerReleased += OnTrackHeaderPointerReleased;
                }
            }
        }

        private void OnTrackHeaderPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_dragTrack == null || ViewModel == null)
                return;
            var pos = e.GetPosition(this);
            double dy = pos.Y - _trackDragStart.Y;

            if (!_isTrackDragging && Math.Abs(dy) > DragThreshold)
            {
                _isTrackDragging = true;
                // 拖拽开始: 降低被拖拽轨道头的透明度 + 更改光标
                if (sender is Border b)
                {
                    b.Opacity = 0.5;
                    b.Cursor = new Cursor(StandardCursorType.DragMove);
                }
            }

            if (_isTrackDragging)
            {
                double trackH = ViewModel.TrackHeight;

                // 更新拖拽目标指示 (高亮目标轨道)
                UpdateDragDropIndicator(pos);

                if (dy > trackH)
                {
                    ViewModel.MoveTrackDown(_dragTrack);
                    _trackDragStart = pos;
                }
                else if (dy < -trackH)
                {
                    ViewModel.MoveTrackUp(_dragTrack);
                    _trackDragStart = pos;
                }
            }
        }

        private void OnTrackHeaderPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            var draggedTrack = _dragTrack;

            if (sender is Border border)
            {
                // 恢复透明度和光标
                border.Opacity = 1.0;
                border.Cursor = Cursor.Default;
                border.PointerMoved -= OnTrackHeaderPointerMoved;
                border.PointerReleased -= OnTrackHeaderPointerReleased;
                e.Pointer.Capture(null);
            }

            // 清除拖拽指示
            ClearDragDropIndicator();

            // 拖拽到分组: 检测释放位置是否在某个分组头或轨道上
            if (_isTrackDragging && draggedTrack != null && ViewModel != null)
            {
                var pos = e.GetPosition(this);
                var hitTarget = this.InputHitTest(pos) as Visual;
                bool handled = false;
                // 向上查找 Border, 检查 Tag 是否为 GroupHeaderViewModel 或 TrackViewModel
                while (hitTarget != null)
                {
                    if (hitTarget is Border b)
                    {
                        if (b.Tag is GroupHeaderViewModel targetGroupVm)
                        {
                            // 拖放到分组头 → 加入该分组
                            ViewModel.SetTrackGroup(draggedTrack, targetGroupVm.Name);
                            handled = true;
                            break;
                        }
                        if (b.Tag is TrackViewModel targetTrackVm)
                        {
                            if (targetTrackVm != draggedTrack)
                            {
                                if (targetTrackVm.HasGroup)
                                {
                                    // 拖放到有分组的轨道 → 加入同分组
                                    ViewModel.SetTrackGroup(draggedTrack, targetTrackVm.Group);
                                }
                                else
                                {
                                    // 拖放到无分组的轨道 → 移出当前分组
                                    if (draggedTrack.HasGroup)
                                        ViewModel.RemoveTrackFromGroup(draggedTrack);
                                }
                            }
                            handled = true;
                            break;
                        }
                    }
                    hitTarget = hitTarget.GetVisualParent() as Visual;
                }
                // 拖放到空白区域 → 移出当前分组
                if (!handled && draggedTrack.HasGroup)
                {
                    ViewModel.RemoveTrackFromGroup(draggedTrack);
                }
            }

            _dragTrack = null;
            _isTrackDragging = false;
        }

        private Border? _dragDropHighlightTarget;

        /// <summary>更新拖拽放置指示: 高亮目标轨道/分组</summary>
        private void UpdateDragDropIndicator(Point pos)
        {
            ClearDragDropIndicator();

            var hitTarget = this.InputHitTest(pos) as Visual;
            while (hitTarget != null)
            {
                if (hitTarget is Border b)
                {
                    if (b.Tag is GroupHeaderViewModel || b.Tag is TrackViewModel targetTvm)
                    {
                        // 不高亮自身
                        if (b.Tag is TrackViewModel tv && tv == _dragTrack)
                        {
                            hitTarget = hitTarget.GetVisualParent() as Visual;
                            continue;
                        }
                        _dragDropHighlightTarget = b;
                        b.BorderBrush = new SolidColorBrush(Avalonia.Media.Color.Parse("#60a5fa"));
                        b.BorderThickness = new Thickness(0, 2, 0, 0);
                        break;
                    }
                }
                hitTarget = hitTarget.GetVisualParent() as Visual;
            }
        }

        /// <summary>清除拖拽放置指示</summary>
        private void ClearDragDropIndicator()
        {
            if (_dragDropHighlightTarget != null)
            {
                _dragDropHighlightTarget.BorderBrush = null;
                _dragDropHighlightTarget.BorderThickness = new Thickness(0);
                _dragDropHighlightTarget = null;
            }
        }

        /// <summary>在播放头位置添加独立事件</summary>
        private void OnAddEventAtPlayhead(object? sender, RoutedEventArgs e)
        {
            ViewModel?.AddEventAtPlayhead();
            SyncEventsToRuler();
        }

        private void OnSetWorkAreaIn(object? sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.SetWorkAreaIn();
                SyncWorkAreaToRuler();
            }
        }

        private void OnSetWorkAreaOut(object? sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.SetWorkAreaOut();
                SyncWorkAreaToRuler();
            }
        }

        private void OnClearWorkArea(object? sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.ClearWorkArea();
                SyncWorkAreaToRuler();
            }
        }

        private void OnDopesheetClick(object? sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
                ViewModel.ViewMode = TimelineViewMode.Dopesheet;
        }

        private void OnCurvesClick(object? sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.ViewMode = TimelineViewMode.Curves;
                SyncCurveEditorData();
            }
        }

        /// <summary>同步轨道数据到曲线编辑器控件</summary>
        private void SyncCurveEditorData()
        {
            var curveEditor = this.FindControl<CurveEditorControl>("CurveEditor");
            if (curveEditor == null || ViewModel == null)
                return;

            // 通过 CurveEditorViewModel 同步数据
            ViewModel.CurveEditorVm.SyncFromTimeline();
            curveEditor.CurveTracks = ViewModel.CurveEditorVm.CurveTracks;

            // 传入吸附信息
            curveEditor.SetSnapInfo(ViewModel.IsSnapEnabled, ViewModel.GetSnapPoints());

            // 绑定事件 (仅首次)
            if (curveEditor.Tag is not string s || s != "curve-wired")
            {
                curveEditor.Tag = "curve-wired";

                curveEditor.KeyframeSelected += (ti, ci, ki) =>
                {
                    ViewModel.CurveEditorVm.OnKeyframeSelected(ti, ci, ki);
                    // 同步轨道选中状态到 TrackClipControl (保持一致性)
                    if (ti >= 0 && ti < ViewModel.Tracks.Count)
                    {
                        var trackVm = ViewModel.Tracks[ti];
                        foreach (var t in ViewModel.Tracks)
                            t.IsSelected = (t == trackVm);
                        foreach (var tcc in this.GetVisualDescendants().OfType<TrackClipControl>())
                            tcc.IsTrackSelected = (tcc.DataContext == trackVm);
                    }
                    RefreshPropertyPanel();
                };

                curveEditor.KeyframeMoved += (ti, ci, ki, absTimeMs, value) =>
                {
                    // 更新吸附信息
                    curveEditor.SetSnapInfo(ViewModel.IsSnapEnabled, ViewModel.GetSnapPoints());
                    ViewModel.CurveEditorVm.OnKeyframeMoved(ti, ci, ki, absTimeMs, value);
                    // 排序后索引可能变化, 刷新曲线数据并同步选中索引
                    SyncCurveEditorData();
                    if (ViewModel.SelectedKeyframeIndex >= 0)
                        curveEditor.SetSelectedKeyframeIndex(
                            ti,
                            ci,
                            ViewModel.SelectedKeyframeIndex
                        );
                    RefreshAllTrackControls();
                    RefreshPropertyPanel();
                };

                curveEditor.TangentChanged += (ti, ci, ki, tangentIn, tangentOut, cp1x, cp2x) =>
                {
                    ViewModel.CurveEditorVm.OnTangentChanged(
                        ti,
                        ci,
                        ki,
                        tangentIn,
                        tangentOut,
                        cp1x,
                        cp2x
                    );
                    RefreshPropertyPanel();
                };

                curveEditor.AddKeyframeRequested += (ti, absTimeMs, value) =>
                {
                    ViewModel.CurveEditorVm.OnAddKeyframeRequested(ti, absTimeMs, value);
                    SyncCurveEditorData(); // 重新同步数据
                    RefreshPropertyPanel();
                };

                curveEditor.InterpolationChanged += (ti, ci, ki, interp) =>
                {
                    ViewModel.CurveEditorVm.OnInterpolationChanged(ti, ci, ki, interp);
                    RefreshPropertyPanel();
                };

                curveEditor.PresetApplyRequested += (ti, ci, ki, presetName) =>
                {
                    // 同步预设参数到 ViewModel (切线等已在控件中设置到 MotionKeyframe)
                    if (ti >= 0 && ti < ViewModel.Tracks.Count)
                    {
                        var trackVm = ViewModel.Tracks[ti];
                        if (ci >= 0 && ci < trackVm.Clips.Count)
                        {
                            var clipVm = trackVm.Clips[ci];
                            if (ki >= 0 && ki < clipVm.Keyframes.Count)
                            {
                                var kfVm = clipVm.Keyframes[ki];
                                var preset = Services.Motion.CurvePresetService.FindPreset(
                                    presetName
                                );
                                if (preset != null)
                                {
                                    kfVm.Interpolation = preset.Interpolation;
                                    kfVm.TangentIn = preset.TangentIn;
                                    kfVm.TangentOut = preset.TangentOut;
                                }
                            }
                        }
                        ViewModel.NotifyTrackDataChanged(trackVm);
                    }
                    ViewModel.MarkDirty();
                    RefreshAllTrackControls();
                    RefreshPropertyPanel();
                };

                curveEditor.DeleteMultiSelectedRequested += (selections) =>
                {
                    if (ViewModel == null || selections.Count == 0)
                        return;
                    // 将曲线编辑器的 trackIndex 转换为 TrackViewModel, 同步到 ViewModel
                    ViewModel.ClearKeyframeSelection();
                    foreach (var (ti, ci, ki) in selections)
                    {
                        if (ti >= 0 && ti < ViewModel.Tracks.Count)
                            ViewModel.AddToSelection(ViewModel.Tracks[ti], ci, ki);
                    }
                    ViewModel.DeleteSelectedKeyframes();
                    SyncCurveEditorData();
                    RefreshAllTrackControls();
                    RefreshPropertyPanel();
                };

                // Shift+滚轮: 曲线编辑器面板上也支持调整轨道高度
                curveEditor.TrackHeightChangeRequested += (delta) =>
                {
                    if (ViewModel == null)
                        return;
                    foreach (var t in ViewModel.Tracks)
                    {
                        double currentHeight = t.HasIndividualHeight
                            ? t.IndividualTrackHeight
                            : ViewModel.TrackHeight;
                        double newHeight = Math.Clamp(currentHeight + delta, 36, 200);
                        t.SetIndividualHeight(newHeight);
                        t.UpdateCompactDisplay(ViewModel.TrackHeight);
                    }
                    RefreshTrackHeights();
                };

                // 多选关键帧拖拽完成
                curveEditor.MultiKeyframeMoved += (movedList) =>
                {
                    if (ViewModel == null || movedList.Count == 0)
                        return;
                    // 将曲线编辑器的 trackIndex 转换为 TrackViewModel, 同步到 ViewModel
                    foreach (var (ti, ci, ki, absTimeMs, value) in movedList)
                    {
                        if (ti >= 0 && ti < ViewModel.Tracks.Count)
                        {
                            var trackVm = ViewModel.Tracks[ti];
                            if (ci >= 0 && ci < trackVm.Clips.Count)
                            {
                                var clipVm = trackVm.Clips[ci];
                                double localMs = absTimeMs - clipVm.StartMs;
                                if (localMs < 0)
                                    localMs = 0;

                                // ki 是 Model 排序后的索引, 需通过引用找到 VM 正确索引
                                int vmIdx = ki;
                                if (ki < clipVm.Clip.Keyframes.Count)
                                {
                                    var modelKf = clipVm.Clip.Keyframes[ki];
                                    for (int j = 0; j < clipVm.Keyframes.Count; j++)
                                    {
                                        if (clipVm.Keyframes[j].Keyframe == modelKf)
                                        {
                                            vmIdx = j;
                                            break;
                                        }
                                    }
                                }
                                ViewModel.MoveKeyframeDirect(trackVm, ci, vmIdx, localMs, value);
                            }
                        }
                    }
                    SyncCurveEditorData();
                    RefreshAllTrackControls();
                    RefreshPropertyPanel();
                };
            }
        }

        /// <summary>同步选中轨道到曲线编辑器控件 (双击添加关键帧时使用正确轨道)</summary>
        private void SyncSelectedTrackToCurveEditor(TrackViewModel trackVm)
        {
            if (ViewModel == null)
                return;
            var curveEditor = this.FindControl<CurveEditorControl>("CurveEditor");
            if (curveEditor == null)
                return;
            int idx = ViewModel.Tracks.IndexOf(trackVm);
            curveEditor.SetSelectedTrack(idx);
        }

        /// <summary>
        /// 跳转到开始按钮
        /// </summary>
        private void OnJumpToStart(object? sender, RoutedEventArgs e)
        {
            ViewModel?.SeekTo(0);
            var videoPreview = this.FindControl<VideoPreviewControl>("VideoPreview");
            if (videoPreview != null && ViewModel != null)
                videoPreview.SyncTime(0);
            var ruler = this.FindControl<TimeRulerControl>("TimeRuler");
            ruler?.InvalidateVisual();
        }

        /// <summary>
        /// 跳转到结尾按钮
        /// </summary>
        private void OnJumpToEnd(object? sender, RoutedEventArgs e)
        {
            if (ViewModel == null)
                return;
            ViewModel.SeekTo(ViewModel.DurationMs);
            var videoPreview = this.FindControl<VideoPreviewControl>("VideoPreview");
            if (videoPreview != null)
                videoPreview.SyncTime(ViewModel.DurationMs);
            var ruler = this.FindControl<TimeRulerControl>("TimeRuler");
            ruler?.InvalidateVisual();
        }

        /// <summary>
        /// 缩放适配窗口按钮
        /// </summary>
        private void OnZoomToFit(object? sender, RoutedEventArgs e)
        {
            SyncTimelineViewportWidth();
            ViewModel?.ZoomToFit();
            // 同步到 TimeRulerControl
            var ruler = this.FindControl<TimeRulerControl>("TimeRuler");
            if (ruler != null && ViewModel != null)
            {
                ruler.PixelsPerMs = ViewModel.PixelsPerMs;
                ruler.ScrollOffsetX = ViewModel.ScrollOffsetX;
                ruler.InvalidateVisual();
            }
            UpdateZoomSlider();
        }

        // ─── 缩放滑块 ───
        private bool _isZoomSliderSyncing;

        private void OnZoomSliderChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_isZoomSliderSyncing || e.Property.Name != "Value")
                return;
            var slider = this.FindControl<Slider>("ZoomSlider");
            if (slider == null || ViewModel == null)
                return;

            // 将 0-100 映射到对数尺度: 0.001 ~ 1.0
            double t = slider.Value / 100.0;
            double ppm = 0.001 * Math.Pow(1000, t); // log scale
            ViewModel.PixelsPerMs = Math.Clamp(ppm, 0.001, 1.0);
            SyncZoomToRuler();
            UpdateZoomPercentText();
        }

        private void OnZoomIn(object? sender, RoutedEventArgs e)
        {
            if (ViewModel == null)
                return;
            ViewModel.PixelsPerMs = Math.Min(1.0, ViewModel.PixelsPerMs * 1.25);
            SyncZoomToRuler();
            UpdateZoomSlider();
        }

        private void OnZoomOut(object? sender, RoutedEventArgs e)
        {
            if (ViewModel == null)
                return;
            ViewModel.PixelsPerMs = Math.Max(0.001, ViewModel.PixelsPerMs / 1.25);
            SyncZoomToRuler();
            UpdateZoomSlider();
        }

        private void UpdateZoomSlider()
        {
            _isZoomSliderSyncing = true;
            try
            {
                var slider = this.FindControl<Slider>("ZoomSlider");
                if (slider != null && ViewModel != null)
                {
                    // 对数反算: ppm = 0.001 * 1000^t  =>  t = log(ppm/0.001) / log(1000)
                    double ppm = Math.Clamp(ViewModel.PixelsPerMs, 0.001, 1.0);
                    double t = Math.Log(ppm / 0.001) / Math.Log(1000);
                    slider.Value = Math.Clamp(t * 100, 0, 100);
                }
                UpdateZoomPercentText();
            }
            finally
            {
                _isZoomSliderSyncing = false;
            }
        }

        private void UpdateZoomPercentText()
        {
            var txt = this.FindControl<TextBlock>("ZoomPercentText");
            if (txt != null && ViewModel != null)
            {
                // 以 0.1 ppm 为 100% 基准
                double pct = ViewModel.PixelsPerMs / 0.1 * 100;
                txt.Text = pct >= 100 ? $"{pct:F0}%" : $"{pct:F1}%";
            }
        }

        /// <summary>
        /// 同步时间轴视口宽度到 ViewModel
        /// </summary>
        private void SyncTimelineViewportWidth()
        {
            if (ViewModel == null)
                return;
            var scroller = this.FindControl<ScrollViewer>("TrackClipScroller");
            if (scroller != null)
            {
                ViewModel.TimelineViewportWidth = scroller.Bounds.Width;
            }
        }
    }
}
