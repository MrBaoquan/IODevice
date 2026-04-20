using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;

namespace IOStudio.Controls.Timeline
{
    public partial class TrackClipControl
    {
        // ═══════ 交互 ═══════

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var pos = e.GetPosition(this);
            var props = e.GetCurrentPoint(this).Properties;

            if (props.IsLeftButtonPressed)
            {
                if (e.ClickCount == 2)
                {
                    AddKeyframeRequested?.Invoke(XToTimeMs(pos.X));
                    e.Handled = true;
                    return;
                }

                var hit = HitTestKeyframe(pos);
                if (hit.HasValue)
                {
                    var (ci, ki) = hit.Value;
                    bool isShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                    bool isCtrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

                    if (isShift)
                    {
                        KeyframeAddToSelection?.Invoke(ci, ki);
                    }
                    else if (isCtrl)
                    {
                        KeyframeToggleSelection?.Invoke(ci, ki);
                    }
                    else if (
                        _multiSelectedKeyframes.Count > 1
                        && _multiSelectedKeyframes.Contains((ci, ki))
                    )
                    {
                        // 多选拖拽
                        _isDraggingMultiKf = true;
                        _dragStartPos = pos;
                        _multiDragStartAbsTimeMs = (pos.X + ScrollOffsetX) / PixelsPerMs;
                    }
                    else
                    {
                        _selectedClipIdx = ci;
                        _selectedKfIdx = ki;
                        KeyframeSelected?.Invoke(_selectedClipIdx, _selectedKfIdx);

                        _isDraggingKf = true;
                        _dragStartPos = pos;
                        var kf = Clips![_selectedClipIdx].Keyframes[_selectedKfIdx];
                        _draggedKfRef = kf;
                        _dragStartTimeMs = kf.TimeMs;
                        _dragStartValue = kf.Value;
                        KeyframeMoved?.Invoke(
                            _selectedClipIdx,
                            _selectedKfIdx,
                            kf.TimeMs,
                            kf.Value
                        );
                    }
                    e.Pointer.Capture(this);
                    e.Handled = true;
                }
                else
                {
                    // 未命中 — 清空选中, 开始框选
                    _selectedClipIdx = -1;
                    _selectedKfIdx = -1;
                    _multiSelectedKeyframes.Clear();
                    KeyframeSelected?.Invoke(-1, -1);

                    _isBoxSelecting = true;
                    _boxSelectStart = pos;
                    _boxSelectCurrent = pos;
                    e.Pointer.Capture(this);
                }
                Focus();
                InvalidateVisual();
            }
            else if (props.IsRightButtonPressed)
            {
                var hit = HitTestKeyframe(pos);
                if (hit.HasValue)
                {
                    _selectedClipIdx = hit.Value.Item1;
                    _selectedKfIdx = hit.Value.Item2;
                    KeyframeSelected?.Invoke(_selectedClipIdx, _selectedKfIdx);
                    InvalidateVisual();
                    ShowKeyframeContextMenu(pos);
                    e.Handled = true;
                }
                else
                {
                    ShowTimelineAreaContextMenu(pos);
                    e.Handled = true;
                }
            }
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                // Shift+滚轮: 调整轨道高度 (达芬奇风格)
                double delta = e.Delta.Y > 0 ? 8 : -8;
                TrackHeightChangeRequested?.Invoke(delta);
                e.Handled = true;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var pos = e.GetPosition(this);

            if (_isBoxSelecting)
            {
                _boxSelectCurrent = pos;
                InvalidateVisual();
                e.Handled = true;
            }
            else if (_isDraggingMultiKf)
            {
                double ppm = PixelsPerMs;
                if (ppm > 0)
                {
                    double absMs = (pos.X + ScrollOffsetX) / ppm;
                    MultiKeyframeDragDelta?.Invoke(absMs - _multiDragStartAbsTimeMs);
                }
                e.Handled = true;
            }
            else if (_isDraggingKf && Clips != null)
            {
                double h = Bounds.Height;
                double ppm = PixelsPerMs;
                if (ppm <= 0)
                    return;

                double dx = pos.X - _dragStartPos.X;
                double dtMs = dx / ppm;
                double newTimeMs = Math.Max(0, _dragStartTimeMs + dtMs);
                float newValue =
                    ViewMode == TimelineViewMode.Dopesheet ? _dragStartValue : YToValue(pos.Y, h);

                if (string.Equals(ValueType, "bool", StringComparison.OrdinalIgnoreCase))
                    newValue = newValue >= 0.5f ? 1f : 0f;

                var clip = Clips[_selectedClipIdx];
                newTimeMs = Math.Max(0, newTimeMs);

                // 吸附
                _snapTargetTimeMs = null;
                if (_snapPoints != null)
                {
                    double absMs = clip.StartMs + newTimeMs;
                    double threshold = SnapVisualThresholdPx / ppm;
                    double closest = double.MaxValue;
                    foreach (var sp in _snapPoints)
                    {
                        double d = Math.Abs(absMs - sp);
                        if (d < closest && d <= threshold)
                        {
                            closest = d;
                            _snapTargetTimeMs = sp;
                        }
                    }
                }

                KeyframeMoved?.Invoke(_selectedClipIdx, _selectedKfIdx, newTimeMs, newValue);

                // 排序后索引可能变化
                if (_draggedKfRef != null)
                {
                    int newIdx = clip.Keyframes.IndexOf(_draggedKfRef);
                    if (newIdx >= 0 && newIdx != _selectedKfIdx)
                    {
                        _selectedKfIdx = newIdx;
                        _dragStartTimeMs = _draggedKfRef.TimeMs;
                        _dragStartPos = pos;
                    }
                }
                InvalidateVisual();
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);

            if (_isBoxSelecting)
            {
                _isBoxSelecting = false;
                e.Pointer.Capture(null);
                var found = GetKeyframesInRect(
                    Math.Min(_boxSelectStart.X, _boxSelectCurrent.X),
                    Math.Min(_boxSelectStart.Y, _boxSelectCurrent.Y),
                    Math.Abs(_boxSelectCurrent.X - _boxSelectStart.X),
                    Math.Abs(_boxSelectCurrent.Y - _boxSelectStart.Y)
                );

                if (found.Count > 0)
                {
                    _multiSelectedKeyframes.Clear();
                    foreach (var kf in found)
                        _multiSelectedKeyframes.Add(kf);
                    (_selectedClipIdx, _selectedKfIdx) = found[0];
                    KeyframeSelected?.Invoke(found[0].clipIdx, found[0].kfIdx);
                    BoxSelectCompleted?.Invoke(found);
                }
                InvalidateVisual();
            }
            else if (_isDraggingMultiKf)
            {
                _isDraggingMultiKf = false;
                e.Pointer.Capture(null);
                MultiKeyframeDragCompleted?.Invoke();
                InvalidateVisual();
            }
            else if (_isDraggingKf)
            {
                _isDraggingKf = false;
                _draggedKfRef = null;
                _snapTargetTimeMs = null;
                e.Pointer.Capture(null);
                InvalidateVisual();
            }
        }

        private List<(int clipIdx, int kfIdx)> GetKeyframesInRect(
            double rx,
            double ry,
            double rw,
            double rh
        )
        {
            var result = new List<(int, int)>();
            if (Clips == null || PixelsPerMs <= 0)
                return result;

            double ppm = PixelsPerMs;
            double h = Bounds.Height;

            for (int ci = 0; ci < Clips.Count; ci++)
            {
                var clip = Clips[ci];
                for (int ki = 0; ki < clip.Keyframes.Count; ki++)
                {
                    var kf = clip.Keyframes[ki];
                    double absMs = clip.StartMs + kf.TimeMs;
                    double kx = absMs * ppm - ScrollOffsetX;

                    if (ViewMode == TimelineViewMode.Dopesheet)
                    {
                        if (kx >= rx && kx <= rx + rw)
                            result.Add((ci, ki));
                    }
                    else
                    {
                        double ky = ValueToY(kf.Value, h);
                        if (kx >= rx && kx <= rx + rw && ky >= ry && ky <= ry + rh)
                            result.Add((ci, ki));
                    }
                }
            }
            return result;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (
                (e.Key == Key.Delete || e.Key == Key.Back)
                && _multiSelectedKeyframes.Count <= 1
                && _selectedClipIdx >= 0
                && _selectedKfIdx >= 0
            )
            {
                DeleteKeyframeRequested?.Invoke(_selectedClipIdx, _selectedKfIdx);
                _selectedClipIdx = -1;
                _selectedKfIdx = -1;
                _multiSelectedKeyframes.Clear();
                InvalidateVisual();
                e.Handled = true;
            }
        }

        // ═══════ 右键菜单 ═══════

        private void ShowKeyframeContextMenu(Point pos)
        {
            var menu = new ContextMenu();
            bool isMulti = _multiSelectedKeyframes.Count > 1;

            // — 复制 / 粘贴 / 删除 —
            var copyItem = new MenuItem { Header = "复制关键帧 (Ctrl+C)" };
            copyItem.Click += (_, _) => CopyKeyframesRequested?.Invoke();

            var pasteItem = new MenuItem
            {
                Header = "粘贴关键帧 (Ctrl+V)",
                IsEnabled = HasClipboardKeyframes
            };
            pasteItem.Click += (_, _) => PasteKeyframesRequested?.Invoke();

            var delItem = new MenuItem { Header = "删除关键帧 (Delete)" };
            delItem.Click += (_, _) =>
            {
                if (_selectedClipIdx >= 0 && _selectedKfIdx >= 0)
                {
                    DeleteKeyframeRequested?.Invoke(_selectedClipIdx, _selectedKfIdx);
                    _selectedClipIdx = -1;
                    _selectedKfIdx = -1;
                    InvalidateVisual();
                }
            };

            menu.Items.Add(copyItem);
            menu.Items.Add(pasteItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(delItem);
            menu.Items.Add(new Separator());

            // 当前插值类型
            string currentInterp = "";
            if (_selectedClipIdx >= 0 && _selectedKfIdx >= 0)
            {
                var clips = Clips;
                if (clips != null && _selectedClipIdx < clips.Count)
                {
                    var kfs = clips[_selectedClipIdx].Keyframes;
                    if (_selectedKfIdx < kfs.Count)
                        currentInterp = kfs[_selectedKfIdx].Interpolation ?? "";
                }
            }

            string[] interpTypes = { "linear", "bezier", "step", "ease_in_out" };
            string[] interpIcons = { "📏", "〰️", "⬛", "🔄" };
            string[] interpNames = { "线性 (Linear)", "贝塞尔 (Bezier)", "阶梯 (Step)", "缓入缓出 (Ease)" };

            if (isMulti)
            {
                // — 批量插值 —
                var batchHeader = new MenuItem
                {
                    Header = $"🎯 批量设置插值 ({_multiSelectedKeyframes.Count} 个关键帧)",
                    IsEnabled = false
                };
                menu.Items.Add(batchHeader);

                for (int i = 0; i < interpTypes.Length; i++)
                {
                    string t = interpTypes[i];
                    var mi = new MenuItem { Header = $"  {interpIcons[i]} {interpNames[i]}" };
                    mi.Click += (_, _) => BatchSetInterpolationRequested?.Invoke(t);
                    menu.Items.Add(mi);
                }

                menu.Items.Add(new Separator());

                // — 批量预设 —
                var presetMenu = new MenuItem { Header = "🎨 批量应用预设" };
                foreach (var cat in CurvePresetService.GetPresetsByCategory())
                {
                    var catMenu = new MenuItem { Header = cat.Key };
                    foreach (var p in cat.Value)
                    {
                        var preset = p;
                        var pi = new MenuItem { Header = preset.DisplayName };
                        pi.Click += (_, _) => BatchApplyPresetRequested?.Invoke(preset.Name);
                        catMenu.Items.Add(pi);
                    }
                    presetMenu.Items.Add(catMenu);
                }
                menu.Items.Add(presetMenu);
            }
            else
            {
                // — 单选: 插值类型 —
                for (int i = 0; i < interpTypes.Length; i++)
                {
                    string t = interpTypes[i];
                    bool active = string.Equals(
                        currentInterp,
                        t,
                        StringComparison.OrdinalIgnoreCase
                    );
                    var mi = new MenuItem
                    {
                        Header = $"  {interpIcons[i]} {interpNames[i]}{(active ? " ✓" : "")}",
                        FontWeight = active ? FontWeight.Bold : FontWeight.Normal
                    };
                    mi.Click += (_, _) => SetInterpolation(t);
                    menu.Items.Add(mi);
                }

                menu.Items.Add(new Separator());

                // — 单选: 预设 —
                var presetTitle = new MenuItem { Header = "🎨 曲线预设", IsEnabled = false };
                menu.Items.Add(presetTitle);

                foreach (var cat in CurvePresetService.GetPresetsByCategory())
                {
                    foreach (var p in cat.Value)
                    {
                        var preset = p;
                        bool match = false;
                        if (_selectedClipIdx >= 0 && _selectedKfIdx >= 0)
                        {
                            var clips = Clips;
                            if (clips != null && _selectedClipIdx < clips.Count)
                            {
                                var kfs = clips[_selectedClipIdx].Keyframes;
                                if (_selectedKfIdx < kfs.Count)
                                {
                                    var kf = kfs[_selectedKfIdx];
                                    match =
                                        string.Equals(
                                            kf.Interpolation,
                                            preset.Interpolation,
                                            StringComparison.OrdinalIgnoreCase
                                        )
                                        && kf.TangentIn == preset.TangentIn
                                        && kf.TangentOut == preset.TangentOut;
                                }
                            }
                        }
                        var pi = new MenuItem
                        {
                            Header = $"  {preset.DisplayName}{(match ? " ✓" : "")}",
                            FontWeight = match ? FontWeight.Bold : FontWeight.Normal
                        };
                        pi.Click += (_, _) =>
                        {
                            if (_selectedClipIdx >= 0 && _selectedKfIdx >= 0)
                            {
                                ApplyPresetRequested?.Invoke(
                                    _selectedClipIdx,
                                    _selectedKfIdx,
                                    preset.Name
                                );
                                InvalidateVisual();
                            }
                        };
                        menu.Items.Add(pi);
                    }
                }
            }

            _activeContextMenu?.Close();
            _activeContextMenu = menu;
            menu.Open(this);
        }

        private void ShowTimelineAreaContextMenu(Point pos)
        {
            double clickTimeMs = (pos.X + ScrollOffsetX) / Math.Max(0.001, PixelsPerMs);
            if (clickTimeMs < 0)
                return;

            // 检查附近是否有关键帧
            bool hasNearbyKf = false;
            if (Clips != null)
            {
                double threshold = 20 / Math.Max(0.001, PixelsPerMs);
                foreach (var clip in Clips)
                {
                    foreach (var kf in clip.Keyframes)
                    {
                        if (Math.Abs(clip.StartMs + kf.TimeMs - clickTimeMs) < threshold)
                        {
                            hasNearbyKf = true;
                            break;
                        }
                    }
                    if (hasNearbyKf)
                        break;
                }
            }

            var menu = new ContextMenu();

            var addKfItem = new MenuItem
            {
                Header = $"➕ 在此添加关键帧 ({TimeSpan.FromMilliseconds(clickTimeMs):mm\\:ss\\.f})"
            };
            addKfItem.Click += (_, _) => AddKeyframeAtTimeRequested?.Invoke(clickTimeMs);

            var addEvtItem = new MenuItem
            {
                Header = $"🔔 在此添加事件 ({TimeSpan.FromMilliseconds(clickTimeMs):mm\\:ss\\.f})"
            };
            addEvtItem.Click += (_, _) => AddEventAtTimeRequested?.Invoke(clickTimeMs);

            var pasteItem = new MenuItem
            {
                Header = "粘贴关键帧 (Ctrl+V)",
                IsEnabled = HasClipboardKeyframes
            };
            pasteItem.Click += (_, _) => PasteKeyframesRequested?.Invoke();

            menu.Items.Add(addKfItem);
            menu.Items.Add(addEvtItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(pasteItem);

            _activeContextMenu?.Close();
            _activeContextMenu = menu;
            menu.Open(this);
        }
    }
}
