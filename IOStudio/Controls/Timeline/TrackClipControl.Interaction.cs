using System;
using System.Collections.Generic;
using System.Globalization;
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

        // ── 动作实例边界剪裁 (左右边缘拖拽) ──
        private bool _isPreparingActionResize;
        private bool _isResizingAction;
        private bool _resizeLeftEdge;
        private int _resizeClipIdx = -1;
        private string? _dragResizeInstanceId;
        private double _resizeClipStartMs;
        private double _resizeClipEndMs;
        private double _resizePreviewClipStartMs;
        private double _resizePreviewClipEndMs;
        private const double ClipEdgeHitPx = 8;

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var pos = e.GetPosition(this);
            var props = e.GetCurrentPoint(this).Properties;

            // 中键拖拽平移 (整个轨道区域可用)
            if (props.IsMiddleButtonPressed)
            {
                _isPanning = true;
                _panStartPos = pos;
                _panStartScrollOffset = ScrollOffsetX;
                e.Pointer.Capture(this);
                Cursor = new Cursor(StandardCursorType.SizeAll);
                e.Handled = true;
                return;
            }

            if (props.IsLeftButtonPressed)
            {
                // 边界剪裁优先: 命中动作实例 clip 的左/右边缘进入拉伸。
                var edgeHit = HitTestActionClipEdge(pos);
                if (edgeHit.HasValue && Clips != null)
                {
                    var edgeClip = Clips[edgeHit.Value.clipIdx];
                    _selectedClipIdx = edgeHit.Value.clipIdx;
                    _selectedKfIdx = -1;
                    _multiSelectedKeyframes.Clear();
                    ActionInstanceSelected?.Invoke(edgeClip.ActionInstanceId!);
                    Focus();
                    InvalidateVisual();
                    if (IsTrackLocked)
                    {
                        e.Handled = true;
                        return;
                    }
                    _resizeClipIdx = edgeHit.Value.clipIdx;
                    _resizeLeftEdge = edgeHit.Value.isLeft;
                    _dragResizeInstanceId = edgeClip.ActionInstanceId;
                    _resizeClipStartMs = edgeClip.StartMs;
                    _resizeClipEndMs = edgeClip.EndMs;
                    _resizePreviewClipStartMs = edgeClip.StartMs;
                    _resizePreviewClipEndMs = edgeClip.EndMs;
                    _isPreparingActionResize = true;
                    _isResizingAction = false;
                    _dragStartPos = pos;
                    e.Pointer.Capture(this);
                    Focus();
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                }

                // The action band has priority over keyframe hit testing. This
                // gives generated multi-track actions a stable selection target.
                var actionBandClipIndex = HitTestActionBand(pos);
                if (actionBandClipIndex.HasValue && Clips != null)
                {
                    var actionClip = Clips[actionBandClipIndex.Value];
                    _selectedClipIdx = actionBandClipIndex.Value;
                    _selectedKfIdx = -1;
                    _multiSelectedKeyframes.Clear();
                    ActionInstanceSelected?.Invoke(actionClip.ActionInstanceId!);
                    Focus();
                    InvalidateVisual();
                    if (IsTrackLocked)
                    {
                        e.Handled = true;
                        return;
                    }
                    _isPreparingActionDrag = true;
                    _isDraggingAction = false;
                    _dragStartPos = pos;
                    _dragActionInstanceId = actionClip.ActionInstanceId;
                    _dragActionStartMs = actionClip.StartMs;
                    _dragActionDurationMs = Math.Max(0, actionClip.EndMs - actionClip.StartMs);
                    _dragActionPreviewStartMs = _dragActionStartMs;
                    e.Pointer.Capture(this);
                    Focus();
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                }

                if (e.ClickCount == 2)
                {
                    if (!IsTrackLocked)
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
                        !IsTrackLocked
                        &&
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

                        if (!IsTrackLocked)
                        {
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
                    }
                    e.Pointer.Capture(this);
                    e.Handled = true;
                }
                else
                {
                    var clipIndex = HitTestClip(pos);
                    var clip = clipIndex.HasValue && Clips != null ? Clips[clipIndex.Value] : null;
                    if (clip?.IsGeneratedFromAction == true)
                    {
                        _selectedClipIdx = clipIndex!.Value;
                        _selectedKfIdx = -1;
                        _multiSelectedKeyframes.Clear();
                        ActionInstanceSelected?.Invoke(clip.ActionInstanceId!);
                        Focus();
                        InvalidateVisual();
                        e.Handled = true;
                        return;
                    }

                    // 未命中动作或关键帧: 清空选中并开始框选
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
                    var clipIndex = HitTestClip(pos);
                    var clip = clipIndex.HasValue && Clips != null ? Clips[clipIndex.Value] : null;
                    if (clip?.IsGeneratedFromAction == true)
                        ShowActionInstanceContextMenu(clip);
                    else
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
                return;
            }

            // 普通滚轮: 时间轴缩放 (与标尺一致), 保持鼠标下的时间位置不变。全时间轴区域可用。
            double factor = e.Delta.Y > 0 ? 1.2 : 1 / 1.2;
            double mouseX = e.GetPosition(this).X;
            double ppm = Math.Max(0.001, PixelsPerMs);
            double newPpm = Math.Clamp(ppm * factor, 0.001, 1.0);
            double timeAtMouse = (ScrollOffsetX + mouseX) / ppm;
            double newScroll = Math.Clamp(
                timeAtMouse * newPpm - mouseX,
                0,
                Math.Max(0, DurationMs * newPpm - Bounds.Width)
            );
            ZoomRequested?.Invoke(newPpm, newScroll);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var pos = e.GetPosition(this);

            if (_isPanning)
            {
                double maxScroll = Math.Max(0, DurationMs * PixelsPerMs - Bounds.Width);
                double newScroll = Math.Clamp(
                    _panStartScrollOffset - (pos.X - _panStartPos.X),
                    0,
                    maxScroll
                );
                PanRequested?.Invoke(newScroll);
                e.Handled = true;
                return;
            }

            if (_isPreparingActionResize && PixelsPerMs > 0)
            {
                double dx = pos.X - _dragStartPos.X;
                if (!_isResizingAction && Math.Abs(dx) >= 4)
                    _isResizingAction = true;

                if (_isResizingAction)
                {
                    double dMs = dx / PixelsPerMs;
                    if (_resizeLeftEdge)
                    {
                        // 拖左边界: 改 in 点, 右边界不动
                        _resizePreviewClipStartMs = SnapTime(Math.Max(0, _resizeClipStartMs + dMs));
                        _resizePreviewClipEndMs = _resizeClipEndMs;
                    }
                    else
                    {
                        // 拖右边界: 改时长, 左边界不动
                        _resizePreviewClipStartMs = _resizeClipStartMs;
                        _resizePreviewClipEndMs = SnapTime(
                            Math.Max(_resizeClipStartMs + 10, _resizeClipEndMs + dMs)
                        );
                    }
                    Cursor = new Cursor(StandardCursorType.SizeWestEast);
                    InvalidateVisual();
                }
                e.Handled = true;
            }
            else if (_isPreparingActionDrag && PixelsPerMs > 0)
            {
                double dx = pos.X - _dragStartPos.X;
                if (!_isDraggingAction && Math.Abs(dx) >= 4)
                    _isDraggingAction = true;

                if (_isDraggingAction)
                {
                    _dragActionPreviewStartMs = SnapTime(
                        Math.Max(0, _dragActionStartMs + dx / PixelsPerMs)
                    );
                    Cursor = new Cursor(StandardCursorType.SizeWestEast);
                    InvalidateVisual();
                    // 跨轨道同步预览: 广播给所有绑定同一实例的轨道
                    if (_dragActionInstanceId != null)
                        PublishSharedDragPreview(
                            _dragActionInstanceId,
                            _dragActionPreviewStartMs,
                            _dragActionDurationMs
                        );
                }
                e.Handled = true;
            }
            else if (_isBoxSelecting)
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
            else if (!IsTrackLocked && HitTestActionClipEdge(pos) != null)
            {
                // hover 到动作剪辑左/右边界: 显示可拉伸光标
                Cursor = new Cursor(StandardCursorType.SizeWestEast);
                e.Handled = true;
            }
            else if (Cursor != Cursor.Default)
            {
                Cursor = Cursor.Default;
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);

            if (_isPanning)
            {
                _isPanning = false;
                Cursor = Cursor.Default;
                e.Pointer.Capture(null);
                e.Handled = true;
                return;
            }

            if (_isPreparingActionResize)
            {
                bool changed =
                    Math.Abs(_resizePreviewClipStartMs - _resizeClipStartMs) > 0.1
                    || Math.Abs(_resizePreviewClipEndMs - _resizeClipEndMs) > 0.1;
                string? instanceId = _dragResizeInstanceId;
                double dStart = _resizePreviewClipStartMs - _resizeClipStartMs;
                double dEnd = _resizePreviewClipEndMs - _resizeClipEndMs;
                _isPreparingActionResize = false;
                _isResizingAction = false;
                _dragResizeInstanceId = null;
                Cursor = Cursor.Default;
                e.Pointer.Capture(null);
                InvalidateVisual();
                if (changed && !string.IsNullOrWhiteSpace(instanceId))
                    ActionInstanceResizeRequested?.Invoke(instanceId, dStart, dEnd);
                e.Handled = true;
                return;
            }

            if (_isPreparingActionDrag)
            {
                bool shouldMove =
                    _isDraggingAction
                    && !string.IsNullOrWhiteSpace(_dragActionInstanceId)
                    && Math.Abs(_dragActionPreviewStartMs - _dragActionStartMs) > 0.1;
                string? instanceId = _dragActionInstanceId;
                double targetMs = _dragActionPreviewStartMs;
                _isPreparingActionDrag = false;
                _isDraggingAction = false;
                _dragActionInstanceId = null;
                Cursor = Cursor.Default;
                e.Pointer.Capture(null);
                InvalidateVisual();
                ClearSharedDragPreview(); // 拖拽结束, 清除跨轨道共享预览
                if (shouldMove)
                    MoveActionInstanceToTimeRequested?.Invoke(instanceId!, targetMs);
                e.Handled = true;
            }
            else if (_isBoxSelecting)
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

        private void OnPresetDragOver(object? sender, DragEventArgs e)
        {
            if (IsTrackLocked || !e.DataTransfer.Contains(PresetDragFormats.PresetId))
            {
                e.DragEffects = DragDropEffects.None;
                return;
            }

            _isPresetDragOver = true;
            _presetDropPreviewTimeMs = SnapTime(Math.Max(0, XToTimeMs(e.GetPosition(this).X)));
            _presetDropPreviewDurationMs = 1000;
            if (
                e.DataTransfer.TryGetValue(PresetDragFormats.DurationMs) is string durationText
                && double.TryParse(
                    durationText,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var durationMs
                )
            )
                _presetDropPreviewDurationMs = Math.Max(1, durationMs);
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
            InvalidateVisual();
        }

        private void OnPresetDragLeave(object? sender, DragEventArgs e)
        {
            _isPresetDragOver = false;
            InvalidateVisual();
        }

        private void OnPresetDrop(object? sender, DragEventArgs e)
        {
            if (IsTrackLocked)
            {
                _isPresetDragOver = false;
                e.DragEffects = DragDropEffects.None;
                e.Handled = true;
                InvalidateVisual();
                return;
            }
            string? presetId = e.DataTransfer.TryGetValue(PresetDragFormats.PresetId);
            double targetMs = _presetDropPreviewTimeMs;
            _isPresetDragOver = false;
            InvalidateVisual();
            if (string.IsNullOrWhiteSpace(presetId))
            {
                e.DragEffects = DragDropEffects.None;
                return;
            }

            PresetDroppedAtTimeRequested?.Invoke(presetId, targetMs, TrackId);
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }

        /// <summary>命中动作实例 clip 的左/右边缘, 用于边界拉伸。返回 (clipIdx, 是否左边缘)。</summary>
        private (int clipIdx, bool isLeft)? HitTestActionClipEdge(Point pos)
        {
            if (Clips == null || PixelsPerMs <= 0)
                return null;
            for (int i = 0; i < Clips.Count; i++)
            {
                var clip = Clips[i];
                if (clip.ActionInstanceId is not { Length: > 0 })
                    continue;
                double x0 = clip.StartMs * PixelsPerMs - ScrollOffsetX;
                double x1 = clip.EndMs * PixelsPerMs - ScrollOffsetX;
                if (Math.Abs(pos.X - x0) <= ClipEdgeHitPx)
                    return (i, true);
                if (Math.Abs(pos.X - x1) <= ClipEdgeHitPx)
                    return (i, false);
            }
            return null;
        }

        private double SnapTime(double valueMs)
        {
            double threshold = SnapVisualThresholdPx / Math.Max(0.0001, PixelsPerMs);
            double closestDistance = double.MaxValue;
            double result = valueMs;

            // 全局吸附点 (标尺刻度/关键帧/事件等)
            var snapPoints = SnapPointsProvider != null ? SnapPointsProvider() : _snapPoints;
            if (snapPoints != null)
            {
                foreach (var point in snapPoints)
                {
                    double distance = Math.Abs(valueMs - point);
                    if (distance <= threshold && distance < closestDistance)
                    {
                        closestDistance = distance;
                        result = point;
                    }
                }
            }

            // 同轨道其它动作实例 clip 的边界作为吸附点 (无缝衔接)
            if (Clips != null)
            {
                string? excludeId = _dragResizeInstanceId ?? _dragActionInstanceId;
                foreach (var clip in Clips)
                {
                    if (
                        string.IsNullOrEmpty(clip.ActionInstanceId)
                        || clip.ActionInstanceId == excludeId
                    )
                        continue;
                    TrySnapBoundary(
                        valueMs,
                        clip.StartMs,
                        threshold,
                        ref closestDistance,
                        ref result
                    );
                    TrySnapBoundary(
                        valueMs,
                        clip.EndMs,
                        threshold,
                        ref closestDistance,
                        ref result
                    );
                }
            }
            return result;
        }

        private static void TrySnapBoundary(
            double valueMs,
            double boundary,
            double threshold,
            ref double closestDistance,
            ref double result
        )
        {
            double distance = Math.Abs(valueMs - boundary);
            if (distance <= threshold && distance < closestDistance)
            {
                closestDistance = distance;
                result = boundary;
            }
        }

        /// <summary>
        /// 检测范围 [startMs, endMs] 是否与同轨道其它内容重叠 (无缝衔接不算冲突)。
        /// </summary>
        private bool IsRangeConflicting(double startMs, double endMs, string? excludeInstanceId)
        {
            if (Clips == null || endMs <= startMs)
                return false;
            foreach (var clip in Clips)
            {
                if (clip.ActionInstanceId == excludeInstanceId)
                    continue;
                if (startMs < clip.EndMs && endMs > clip.StartMs)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 边界值跳变预警: 当预览边界与其它实例 clip 无缝衔接时,
        /// 比较衔接两侧的输出值, 差异超过阈值则返回提示文本。
        /// </summary>
        private string? GetBoundaryValueWarning(
            double previewStart,
            double previewEnd,
            int ownClipIndex,
            string? ownInstanceId
        )
        {
            if (Clips == null || ownClipIndex < 0 || ownClipIndex >= Clips.Count)
                return null;
            var ownClip = Clips[ownClipIndex];
            if (ownClip.Keyframes.Count == 0)
                return null;

            foreach (var clip in Clips)
            {
                if (clip.ActionInstanceId == ownInstanceId || clip.Keyframes.Count == 0)
                    continue;

                // 左边界与对方右边界衔接: 我方首帧 vs 对方尾帧
                if (Math.Abs(clip.EndMs - previewStart) < 0.1)
                {
                    float mine = ownClip.Keyframes[0].Value;
                    float other = clip.Keyframes[^1].Value;
                    if (Math.Abs(mine - other) > 0.05f)
                        return $"⚠ 边界值 {other:F2}→{mine:F2}";
                }
                // 右边界与对方左边界衔接: 我方尾帧 vs 对方首帧
                else if (Math.Abs(clip.StartMs - previewEnd) < 0.1)
                {
                    float mine = ownClip.Keyframes[^1].Value;
                    float other = clip.Keyframes[0].Value;
                    if (Math.Abs(mine - other) > 0.05f)
                        return $"⚠ 边界值 {mine:F2}→{other:F2}";
                }
            }
            return null;
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
                !IsTrackLocked
                &&
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
                IsEnabled = HasClipboardKeyframes && !IsTrackLocked
            };
            pasteItem.Click += (_, _) => PasteKeyframesRequested?.Invoke();

            var delItem = new MenuItem
            {
                Header = "删除关键帧 (Delete)",
                IsEnabled = !IsTrackLocked
            };
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
            string[] interpNames = { "线性 (Linear)", "贝塞尔 (Bezier)", "阶梯 (Step)", "缓入缓出 (Ease)" };

            if (isMulti)
            {
                // — 批量插值 —
                var batchHeader = new MenuItem
                {
                    Header = $"批量设置插值 ({_multiSelectedKeyframes.Count} 个关键帧)",
                    IsEnabled = false
                };
                menu.Items.Add(batchHeader);

                for (int i = 0; i < interpTypes.Length; i++)
                {
                    string t = interpTypes[i];
                    var mi = new MenuItem { Header = interpNames[i] };
                    mi.IsEnabled = !IsTrackLocked;
                    mi.Click += (_, _) => BatchSetInterpolationRequested?.Invoke(t);
                    menu.Items.Add(mi);
                }

                menu.Items.Add(new Separator());

                // — 批量预设 —
                var presetMenu = new MenuItem
                {
                    Header = "批量应用预设",
                    IsEnabled = !IsTrackLocked
                };
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
                        Header = $"{(active ? "✓ " : "")}{interpNames[i]}",
                        FontWeight = active ? FontWeight.Bold : FontWeight.Normal,
                        IsEnabled = !IsTrackLocked
                    };
                    mi.Click += (_, _) => SetInterpolation(t);
                    menu.Items.Add(mi);
                }

                menu.Items.Add(new Separator());

                // — 单选: 预设 —
                var presetTitle = new MenuItem { Header = "曲线预设", IsEnabled = false };
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
                            Header = $"{(match ? "✓ " : "")}{preset.DisplayName}",
                            FontWeight = match ? FontWeight.Bold : FontWeight.Normal,
                            IsEnabled = !IsTrackLocked
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
                Header = $"在此添加关键帧 ({TimeSpan.FromMilliseconds(clickTimeMs):mm\\:ss\\.f})",
                IsEnabled = !IsTrackLocked
            };
            addKfItem.Click += (_, _) => AddKeyframeAtTimeRequested?.Invoke(clickTimeMs);

            var addEvtItem = new MenuItem
            {
                Header = $"在此添加事件 ({TimeSpan.FromMilliseconds(clickTimeMs):mm\\:ss\\.f})"
            };
            addEvtItem.Click += (_, _) => AddEventAtTimeRequested?.Invoke(clickTimeMs);

            var pasteItem = new MenuItem
            {
                Header = "粘贴关键帧 (Ctrl+V)",
                IsEnabled = HasClipboardKeyframes && !IsTrackLocked
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

        private void ShowActionInstanceContextMenu(MotionClip clip)
        {
            var instanceId = clip.ActionInstanceId;
            if (string.IsNullOrEmpty(instanceId))
                return;

            var menu = new ContextMenu();
            menu.Items.Add(
                new MenuItem
                {
                    Header = clip.SourceActionName ?? "动作实例",
                    IsEnabled = false,
                    FontWeight = FontWeight.SemiBold,
                }
            );
            menu.Items.Add(new Separator());

            var duplicate = new MenuItem
            {
                Header = "复制到播放头",
                IsEnabled = !IsTrackLocked
            };
            duplicate.Click += (_, _) => DuplicateActionInstanceRequested?.Invoke(instanceId);
            menu.Items.Add(duplicate);

            var move = new MenuItem
            {
                Header = "移动到播放头",
                IsEnabled = !IsTrackLocked
            };
            move.Click += (_, _) => MoveActionInstanceRequested?.Invoke(instanceId);
            menu.Items.Add(move);

            var save = new MenuItem { Header = "另存为效果预设..." };
            save.Click += (_, _) => SaveActionInstanceAsPresetRequested?.Invoke(instanceId);
            menu.Items.Add(save);
            menu.Items.Add(new Separator());

            var delete = new MenuItem
            {
                Header = "删除整组动作",
                Foreground = new SolidColorBrush(Color.Parse("#dc2626")),
                IsEnabled = !IsTrackLocked,
            };
            delete.Click += (_, _) => DeleteActionInstanceRequested?.Invoke(instanceId);
            menu.Items.Add(delete);

            _activeContextMenu?.Close();
            _activeContextMenu = menu;
            menu.Open(this);
        }
    }
}
