using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;

namespace IOStudio.Controls.Timeline
{
    // Interaction methods extracted from CurveEditorControl
    public partial class CurveEditorControl
    {
        private bool IsCurveTrackLocked(int trackIdx) =>
            CurveTracks != null
            && trackIdx >= 0
            && trackIdx < CurveTracks.Count
            && CurveTracks[trackIdx].IsLocked;

        private bool HasLockedMultiSelection() =>
            _multiSelectedKfs.Any(item => IsCurveTrackLocked(item.trackIdx));

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            Focus(); // 获得键盘焦点, 使 Delete/方向键等快捷键事件到达本控件
            var pos = e.GetPosition(this);
            var props = e.GetCurrentPoint(this).Properties;
            double viewH = Bounds.Height;

            // 中键拖拽平移
            if (props.IsMiddleButtonPressed)
            {
                _isPanning = true;
                _panStart = pos;
                _panStartOffsetX = ScrollOffsetX;
                _panStartValueOffset = ValueOffset;
                _dragMode = DragMode.Pan;
                e.Handled = true;
                return;
            }

            if (!props.IsLeftButtonPressed)
            {
                // 右键: 关键帧插值类型上下文菜单 / 空白区域菜单
                if (props.IsRightButtonPressed)
                {
                    var kfHitR = HitTestKeyframe(pos, viewH);
                    if (kfHitR != null)
                    {
                        var (rti, rci, rki) = kfHitR.Value;
                        _selectedTrackIdx = rti;
                        _selectedClipIdx = rci;
                        _selectedKfIdx = rki;
                        bool inMulti = _multiSelectedKfs.Contains((rti, rci, rki));
                        if (_multiSelectedKfs.Count > 1 && inMulti)
                        {
                            // 右键命中的关键帧已在多选中 → 保持多选, 不清空 VM 选择 (批量插值/预设可用)
                            _trackPanelSelectedIdx = rti;
                        }
                        else
                        {
                            // 否则: 单帧右键 → 收敛为单选
                            if (_multiSelectedKfs.Count > 0)
                            {
                                _multiSelectedKfs.Clear();
                                NotifyMultiSelectionChanged();
                            }
                            KeyframeSelected?.Invoke(rti, rci, rki);
                        }
                        InvalidateVisual();
                        ShowInterpolationContextMenu(pos, rti, rci, rki);
                        e.Handled = true;
                    }
                    else
                    {
                        // 空白区域右键: 显示添加关键帧菜单
                        ShowEmptyAreaContextMenu(pos, viewH);
                        e.Handled = true;
                    }
                }
                return;
            }

            bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

            // 检测 Overlay 覆盖关键帧命中 (空窗自由数值点, 圆形标记)。优先于 clip 关键帧。
            var overrideHit = HitTestOverrideKeyframe(pos, viewH);
            if (overrideHit != null)
            {
                var (oti, oki) = overrideHit.Value;
                _selectedTrackIdx = oti;
                _trackPanelSelectedIdx = oti;
                _selectedClipIdx = -1;
                _selectedKfIdx = -1;
                _selectedOverrideKfIdx = oki;
                _dragMode = IsCurveTrackLocked(oti)
                    ? DragMode.None
                    : DragMode.OverrideKeyframe;
                _hasDragStarted = false;
                _dragStart = pos;
                var ovList = CurveTracks![oti].OverrideKeyframes;
                _dragStartTimeMs = ovList![oki].TimeMs;
                _dragStartValue = ovList[oki].Value;
                e.Handled = true;
                InvalidateVisual();
                return;
            }

            // 检测关键帧命中 (优先级高于切线手柄, 避免选择关键帧时误触手柄)
            var kfHit = HitTestKeyframe(pos, viewH);
            if (kfHit != null)
            {
                var (ti, ci, ki) = kfHit.Value;

                if (ctrl)
                {
                    // Ctrl+Click: 切换多选
                    if (_multiSelectedKfs.Contains((ti, ci, ki)))
                        _multiSelectedKfs.Remove((ti, ci, ki));
                    else
                        _multiSelectedKfs.Add((ti, ci, ki));
                    _trackPanelSelectedIdx = ti;
                    NotifyMultiSelectionChanged();
                }
                else if (shift)
                {
                    _multiSelectedKfs.Add((ti, ci, ki));
                    _trackPanelSelectedIdx = ti;
                    NotifyMultiSelectionChanged();
                }
                else
                {
                    // 如果点击的关键帧已在多选中, 进入多选拖拽模式
                    if (
                        _multiSelectedKfs.Count > 1
                        && _multiSelectedKfs.Contains((ti, ci, ki))
                        && !HasLockedMultiSelection()
                    )
                    {
                        _dragMode = DragMode.MultiKeyframe;
                        _hasDragStarted = false;
                        _dragStart = pos;
                        _dragStartTimeMs = XToTimeMs(pos.X);
                        _dragStartValue = YToValue(pos.Y, viewH);

                        // 快照所有多选关键帧的初始位置 (引用关键帧对象, 排序安全)
                        _multiDragSnapshot = new();
                        foreach (var (sti, sci, ski) in _multiSelectedKfs)
                        {
                            if (sti < CurveTracks!.Count)
                            {
                                var sclip = CurveTracks[sti].Clips[sci];
                                var skf = sclip.Keyframes[ski];
                                _multiDragSnapshot.Add((sti, sci, skf, skf.TimeMs, skf.Value));
                            }
                        }
                    }
                    else
                    {
                        if (_multiSelectedKfs.Count > 0)
                        {
                            _multiSelectedKfs.Clear();
                            NotifyMultiSelectionChanged();
                        }
                        _selectedTrackIdx = ti;
                        _trackPanelSelectedIdx = ti; // 记住最近交互的轨道, 双击添加关键帧时使用
                        _selectedClipIdx = ci;
                        _selectedKfIdx = ki;
                        _selectedOverrideKfIdx = -1;
                        _selectedIdleKfIdx = -1;
                        _dragMode = IsCurveTrackLocked(ti) ? DragMode.None : DragMode.Keyframe;
                        _hasDragStarted = false;
                        _dragStart = pos;

                        var clip = CurveTracks![ti].Clips[ci];
                        var kf = clip.Keyframes[ki];
                        _dragStartTimeMs = clip.StartMs + kf.TimeMs;
                        _dragStartValue = kf.Value;

                        KeyframeSelected?.Invoke(ti, ci, ki);
                    }
                }
                e.Handled = true;
                InvalidateVisual();
                return;
            }

            // 检测切线手柄命中 (仅在未命中关键帧时)
            var tangentHit = HitTestTangentHandle(pos, viewH);
            if (tangentHit != null)
            {
                _selectedTrackIdx = tangentHit.Value.trackIdx;
                _selectedClipIdx = tangentHit.Value.clipIdx;
                _selectedKfIdx = tangentHit.Value.kfIdx;
                _dragMode = IsCurveTrackLocked(_selectedTrackIdx)
                    ? DragMode.None
                    : (tangentHit.Value.isIn ? DragMode.TangentIn : DragMode.TangentOut);
                _hasDragStarted = false;
                _dragStart = pos;
                var kf = CurveTracks![_selectedTrackIdx].Clips[_selectedClipIdx].Keyframes[
                    _selectedKfIdx
                ];
                _dragStartTangent = tangentHit.Value.isIn
                    ? (kf.TangentIn ?? 0f)
                    : (kf.TangentOut ?? 0f);
                _dragStartCpx = tangentHit.Value.isIn ? (kf.Cp1x ?? 1f) : (kf.Cp2x ?? 1f);
                e.Handled = true;
                InvalidateVisual();
                return;
            }

            // 双击空白: 添加关键帧
            if (e.ClickCount == 2)
            {
                // 防止拖拽结束后误触双击 (400ms 保护期)
                if (Environment.TickCount64 - _lastDragEndTick < 400)
                    return;

                // 优先使用轨道面板选中, 其次使用曲线编辑器内选中, 最后回退到第一个可见轨道
                int trackIdx =
                    _trackPanelSelectedIdx >= 0 ? _trackPanelSelectedIdx : _selectedTrackIdx;
                if (trackIdx < 0 && CurveTracks != null)
                {
                    for (int ti = 0; ti < CurveTracks.Count; ti++)
                    {
                        if (!IsTrackHidden(ti))
                        {
                            trackIdx = ti;
                            _selectedTrackIdx = ti;
                            break;
                        }
                    }
                }
                if (trackIdx >= 0 && CurveTracks != null && trackIdx < CurveTracks.Count)
                {
                    if (IsCurveTrackLocked(trackIdx))
                    {
                        e.Handled = true;
                        return;
                    }
                    double timeMs = XToTimeMs(pos.X);
                    float value = YToValue(pos.Y, viewH);

                    // 判断该时刻是否落在任何 clip 内 (空窗 → 添加 Overlay 覆盖关键帧)
                    bool inAnyClip = false;
                    foreach (var c in CurveTracks[trackIdx].Clips)
                    {
                        if (timeMs >= c.StartMs && timeMs <= c.EndMs)
                        {
                            inAnyClip = true;
                            break;
                        }
                    }
                    if (!inAnyClip)
                    {
                        // 空窗: 动画曲线视图不编辑 idle (独立待机编辑器接管), 双击空窗 → 添加 Overlay 覆盖关键帧
                        OverrideKeyframeAddRequested?.Invoke(trackIdx, timeMs, value);
                    }

                    // 检查目标轨道上是否已有很近的关键帧 (防止重叠)
                    double thresholdMs = Math.Max(KfHitRadius * 2 / PixelsPerMs, 20);
                    bool tooClose = false;
                    foreach (var clip in CurveTracks[trackIdx].Clips)
                    {
                        foreach (var kf in clip.Keyframes)
                        {
                            if (Math.Abs((clip.StartMs + kf.TimeMs) - timeMs) < thresholdMs)
                            {
                                tooClose = true;
                                break;
                            }
                        }
                        if (tooClose)
                            break;
                    }

                    if (!tooClose)
                    {
                        AddKeyframeRequested?.Invoke(trackIdx, timeMs, value);
                        e.Handled = true;
                        return;
                    }
                }
            }

            // 空白区域: 开始框选
            if (!shift && !ctrl)
            {
                _selectedTrackIdx = -1;
                _selectedClipIdx = -1;
                _selectedKfIdx = -1;
                _selectedOverrideKfIdx = -1;
                _selectedIdleKfIdx = -1;
                _multiSelectedKfs.Clear();
                _dragMode = DragMode.BoxSelect;
                _boxSelectStart = pos;
                _boxSelectRect = new Rect(pos, new Size(0, 0));
                InvalidateVisual();
            }

            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var pos = e.GetPosition(this);
            double viewH = Bounds.Height;

            switch (_dragMode)
            {
                case DragMode.Pan:
                    double dx = pos.X - _panStart.X;
                    double dy = pos.Y - _panStart.Y;
                    ScrollOffsetX = Math.Max(0, _panStartOffsetX - dx);
                    PanRequested?.Invoke(ScrollOffsetX);
                    double usableH = viewH - GridPaddingTop - GridPaddingBottom;
                    if (usableH > 0)
                        ValueOffset = _panStartValueOffset + dy / usableH / ValueZoom;
                    InvalidateVisual();
                    e.Handled = true;
                    break;

                case DragMode.OverrideKeyframe:
                    if (CurveTracks != null && _selectedTrackIdx >= 0)
                    {
                        // 拖拽阈值检测: 防止单击时微小鼠标抖动修改关键帧值
                        if (!_hasDragStarted)
                        {
                            double dist = Math.Sqrt(
                                (pos.X - _dragStart.X) * (pos.X - _dragStart.X)
                                    + (pos.Y - _dragStart.Y) * (pos.Y - _dragStart.Y)
                            );
                            if (dist < DragThreshold)
                                break;
                            _hasDragStarted = true;
                        }

                        var ovList = CurveTracks[_selectedTrackIdx].OverrideKeyframes;
                        if (ovList == null || ovList.Count == 0)
                            break;
                        var okf = ovList[_selectedOverrideKfIdx];
                        double newTimeMs = Math.Max(XToTimeMs(pos.X), 0);
                        float newValue = YToValue(pos.Y, viewH);
                        if (
                            string.Equals(
                                CurveTracks[_selectedTrackIdx].ValueType,
                                "bool",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                            newValue = newValue >= 0.5f ? 1f : 0f;
                        okf.TimeMs = newTimeMs;
                        okf.Value = Math.Clamp(newValue, 0f, 1f);
                        // 拖动后保持排序 (与 clip 关键帧一致)
                        var sorted = ovList.OrderBy(o => o.TimeMs).ToList();
                        for (int i = 0; i < sorted.Count; i++)
                            ovList[i] = sorted[i];
                        _selectedOverrideKfIdx = ovList.IndexOf(okf);
                        InvalidateVisual();
                    }
                    e.Handled = true;
                    break;

                case DragMode.Keyframe:
                    if (CurveTracks != null && _selectedTrackIdx >= 0)
                    {
                        // 拖拽阈值检测: 防止单击时微小鼠标抖动修改关键帧值
                        if (!_hasDragStarted)
                        {
                            double dist = Math.Sqrt(
                                (pos.X - _dragStart.X) * (pos.X - _dragStart.X)
                                    + (pos.Y - _dragStart.Y) * (pos.Y - _dragStart.Y)
                            );
                            if (dist < DragThreshold)
                                break;
                            _hasDragStarted = true;
                        }

                        double newTimeMs = XToTimeMs(pos.X);
                        float newValue = YToValue(pos.Y, viewH);
                        var clip = CurveTracks[_selectedTrackIdx].Clips[_selectedClipIdx];
                        var kf = clip.Keyframes[_selectedKfIdx];

                        // 吸附逻辑
                        _snapTargetTimeMs = null;
                        if (_isSnapEnabled && _snapPoints != null)
                        {
                            double thresholdMs = SnapVisualThresholdPx / PixelsPerMs;
                            double bestDist = double.MaxValue;
                            foreach (var pt in _snapPoints)
                            {
                                double dist = Math.Abs(newTimeMs - pt);
                                if (dist < bestDist && dist <= thresholdMs && dist > 0.5)
                                {
                                    bestDist = dist;
                                    _snapTargetTimeMs = pt;
                                }
                            }
                            if (_snapTargetTimeMs.HasValue)
                                newTimeMs = _snapTargetTimeMs.Value;
                        }

                        // 限制: 不允许为负
                        double localMs = Math.Max(newTimeMs - clip.StartMs, 0);
                        kf.TimeMs = localMs;

                        // Bool 轨道: 值必须严格为 0 或 1
                        if (
                            string.Equals(
                                CurveTracks[_selectedTrackIdx].ValueType,
                                "bool",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                            newValue = newValue >= 0.5f ? 1f : 0f;

                        kf.Value = newValue;

                        // 拖拽期间按时间排序, 防止曲线渲染/插值错误 (fix BUG-012/013)
                        SortClipKeyframesDuringDrag(clip, kf);

                        InvalidateVisual();
                    }
                    e.Handled = true;
                    break;

                case DragMode.MultiKeyframe:
                    if (CurveTracks != null && _multiDragSnapshot != null)
                    {
                        // 拖拽阈值检测
                        if (!_hasDragStarted)
                        {
                            double dist = Math.Sqrt(
                                (pos.X - _dragStart.X) * (pos.X - _dragStart.X)
                                    + (pos.Y - _dragStart.Y) * (pos.Y - _dragStart.Y)
                            );
                            if (dist < DragThreshold)
                                break;
                            _hasDragStarted = true;
                        }

                        double currentTimeMs = XToTimeMs(pos.X);
                        float currentValue = YToValue(pos.Y, viewH);
                        double deltaTimeMs = currentTimeMs - _dragStartTimeMs;
                        float deltaValue = currentValue - _dragStartValue;

                        // 收集需要排序的 clip (去重)
                        var clipsToSort = new HashSet<MotionClip>();

                        foreach (var (sti, sci, skf, origTime, origVal) in _multiDragSnapshot)
                        {
                            if (sti >= CurveTracks.Count)
                                continue;
                            var sclip = CurveTracks[sti].Clips[sci];

                            double newLocalMs = Math.Max(origTime + deltaTimeMs, 0);
                            float newVal = Math.Clamp(origVal + deltaValue, 0f, 1f);

                            // Bool 轨道: 值必须严格为 0 或 1
                            if (
                                string.Equals(
                                    CurveTracks[sti].ValueType,
                                    "bool",
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                                newVal = newVal >= 0.5f ? 1f : 0f;

                            skf.TimeMs = newLocalMs;
                            skf.Value = newVal;
                            clipsToSort.Add(sclip);
                        }

                        // 拖拽期间按时间排序, 防止曲线渲染/插值错误
                        foreach (var clip in clipsToSort)
                            clip.Keyframes.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));

                        // 排序后重建多选索引 (索引可能因排序改变)
                        _multiSelectedKfs.Clear();
                        foreach (var (sti, sci, skf2, _, _) in _multiDragSnapshot)
                        {
                            if (sti < CurveTracks.Count)
                            {
                                int newIdx = CurveTracks[sti].Clips[sci].Keyframes.IndexOf(skf2);
                                if (newIdx >= 0)
                                    _multiSelectedKfs.Add((sti, sci, newIdx));
                            }
                        }

                        InvalidateVisual();
                    }
                    e.Handled = true;
                    break;

                case DragMode.TangentIn:
                case DragMode.TangentOut:
                    if (CurveTracks != null && _selectedTrackIdx >= 0)
                    {
                        // 拖拽阈值检测
                        if (!_hasDragStarted)
                        {
                            double dist = Math.Sqrt(
                                (pos.X - _dragStart.X) * (pos.X - _dragStart.X)
                                    + (pos.Y - _dragStart.Y) * (pos.Y - _dragStart.Y)
                            );
                            if (dist < DragThreshold)
                                break;
                            _hasDragStarted = true;
                        }

                        var kf = CurveTracks[_selectedTrackIdx].Clips[_selectedClipIdx].Keyframes[
                            _selectedKfIdx
                        ];
                        double dy2 = -(pos.Y - _dragStart.Y) / TangentHandleLength;
                        double dx2 = (pos.X - _dragStart.X) / TangentHandleLength;
                        // TangentIn 绘制使用 kfY + tangent*len (正值=向下),
                        // 拖拽向上时 dy2 为正, 需要反转使手柄跟随鼠标方向
                        if (_dragMode == DragMode.TangentIn)
                        {
                            kf.TangentIn = Math.Clamp(_dragStartTangent - (float)dy2, -3f, 3f);
                            kf.Cp1x = Math.Clamp(_dragStartCpx - (float)dx2, 0.2f, 3f);
                        }
                        else
                        {
                            kf.TangentOut = Math.Clamp(_dragStartTangent + (float)dy2, -3f, 3f);
                            kf.Cp2x = Math.Clamp(_dragStartCpx + (float)dx2, 0.2f, 3f);
                        }
                        InvalidateVisual();
                    }
                    e.Handled = true;
                    break;

                case DragMode.BoxSelect:
                    double bx = Math.Min(_boxSelectStart.X, pos.X);
                    double by = Math.Min(_boxSelectStart.Y, pos.Y);
                    double bw = Math.Abs(pos.X - _boxSelectStart.X);
                    double bh = Math.Abs(pos.Y - _boxSelectStart.Y);
                    _boxSelectRect = new Rect(bx, by, bw, bh);

                    // 实时更新框选
                    _multiSelectedKfs.Clear();
                    if (CurveTracks != null)
                    {
                        for (int ti = 0; ti < CurveTracks.Count; ti++)
                        {
                            if (IsTrackHidden(ti))
                                continue;
                            for (int ci = 0; ci < CurveTracks[ti].Clips.Count; ci++)
                            {
                                var clip = CurveTracks[ti].Clips[ci];
                                for (int ki = 0; ki < clip.Keyframes.Count; ki++)
                                {
                                    var kf = clip.Keyframes[ki];
                                    double kx =
                                        (clip.StartMs + kf.TimeMs) * PixelsPerMs - ScrollOffsetX;
                                    double ky = ValueToY(kf.Value, viewH);
                                    if (_boxSelectRect.Contains(new Point(kx, ky)))
                                        _multiSelectedKfs.Add((ti, ci, ki));
                                }
                            }
                        }
                    }
                    InvalidateVisual();
                    e.Handled = true;
                    break;
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);

            if (
                _dragMode == DragMode.OverrideKeyframe
                && _hasDragStarted
                && CurveTracks != null
                && _selectedTrackIdx >= 0
            )
            {
                var ovList = CurveTracks[_selectedTrackIdx].OverrideKeyframes;
                if (
                    ovList != null
                    && _selectedOverrideKfIdx >= 0
                    && _selectedOverrideKfIdx < ovList.Count
                )
                {
                    var okf = ovList[_selectedOverrideKfIdx];
                    // 通知 VM 移动完成 (绝对时间/值)
                    OverrideKeyframeMoved?.Invoke(_selectedTrackIdx, okf.TimeMs, okf.Value);
                    // Undo: before 为按下时快照, after 为拖拽后值
                    if (
                        Math.Abs(_dragStartTimeMs - okf.TimeMs) > 0.01
                        || Math.Abs(_dragStartValue - okf.Value) > 0.0001f
                    )
                    {
                        OverrideKeyframeEditCommitted?.Invoke(
                            _selectedTrackIdx,
                            okf,
                            _dragStartTimeMs,
                            _dragStartValue,
                            okf.TimeMs,
                            okf.Value
                        );
                    }
                }
                _lastDragEndTick = Environment.TickCount64;
                _dragMode = DragMode.None;
                _isPanning = false;
                _snapTargetTimeMs = null;
                _boxSelectRect = default;
                e.Handled = true;
                InvalidateVisual();
                return;
            }

            if (
                _dragMode == DragMode.Keyframe
                && _hasDragStarted
                && CurveTracks != null
                && _selectedTrackIdx >= 0
            )
            {
                var clip = CurveTracks[_selectedTrackIdx].Clips[_selectedClipIdx];
                var kf = clip.Keyframes[_selectedKfIdx];
                KeyframeMoved?.Invoke(
                    _selectedTrackIdx,
                    _selectedClipIdx,
                    _selectedKfIdx,
                    clip.StartMs + kf.TimeMs,
                    kf.Value
                );
                // Undo: before 为按下时快照 (时间转相对 clip 起点)
                double t0 = _dragStartTimeMs - clip.StartMs;
                if (
                    Math.Abs(t0 - kf.TimeMs) > 0.01
                    || Math.Abs(_dragStartValue - kf.Value) > 0.0001f
                )
                {
                    KeyframeEditCommitted?.Invoke(
                        new List<(MotionKeyframe, double, float, double, float)>
                        {
                            (kf, t0, _dragStartValue, kf.TimeMs, kf.Value),
                        }
                    );
                }
            }
            else if (
                _dragMode == DragMode.MultiKeyframe
                && _hasDragStarted
                && CurveTracks != null
                && _multiDragSnapshot != null
            )
            {
                // 收集所有多选关键帧的最终位置
                var movedList =
                    new List<(
                        int TrackIdx,
                        int ClipIdx,
                        int KfIdx,
                        double AbsTimeMs,
                        float Value
                    )>();
                foreach (var (sti, sci, skf, _, _) in _multiDragSnapshot)
                {
                    if (sti < CurveTracks.Count)
                    {
                        var sclip = CurveTracks[sti].Clips[sci];
                        int kfIdx = sclip.Keyframes.IndexOf(skf);
                        if (kfIdx >= 0)
                            movedList.Add((sti, sci, kfIdx, sclip.StartMs + skf.TimeMs, skf.Value));
                    }
                }
                var snapshot = _multiDragSnapshot;
                _multiDragSnapshot = null;
                if (movedList.Count > 0)
                    MultiKeyframeMoved?.Invoke(movedList);

                // Undo: before 为按下时快照 (时间相对 clip 起点), after 为拖拽后当前值
                if (movedList.Count > 0 && snapshot != null)
                {
                    var edits = new List<(MotionKeyframe, double, float, double, float)>();
                    foreach (var (_, _, skf, ot, ov) in snapshot)
                    {
                        if (skf != null)
                            edits.Add((skf, ot, ov, skf.TimeMs, skf.Value));
                    }
                    if (edits.Count > 0)
                        KeyframeEditCommitted?.Invoke(edits);
                }
            }
            else if (
                (_dragMode == DragMode.TangentIn || _dragMode == DragMode.TangentOut)
                && _hasDragStarted
                && CurveTracks != null
                && _selectedTrackIdx >= 0
            )
            {
                var kf = CurveTracks[_selectedTrackIdx].Clips[_selectedClipIdx].Keyframes[
                    _selectedKfIdx
                ];
                float tin1 = kf.TangentIn ?? 0f;
                float tout1 = kf.TangentOut ?? 0f;
                float cp11 = kf.Cp1x ?? 1f;
                float cp21 = kf.Cp2x ?? 1f;
                TangentChanged?.Invoke(
                    _selectedTrackIdx,
                    _selectedClipIdx,
                    _selectedKfIdx,
                    tin1,
                    tout1,
                    kf.Cp1x,
                    kf.Cp2x
                );
                // Undo: before = 按下时快照 (被拖项来自 _dragStart*, 未拖项取当前)
                bool isIn = _dragMode == DragMode.TangentIn;
                float tin0 = isIn ? _dragStartTangent : tin1;
                float tout0 = isIn ? tout1 : _dragStartTangent;
                float cp10 = isIn ? _dragStartCpx : cp11;
                float cp20 = isIn ? cp21 : _dragStartCpx;
                TangentEditCommitted?.Invoke(
                    (
                        _selectedTrackIdx,
                        _selectedClipIdx,
                        _selectedKfIdx,
                        tin0,
                        tout0,
                        cp10,
                        cp20,
                        tin1,
                        tout1,
                        cp11,
                        cp21
                    )
                );
            }

            if (_dragMode == DragMode.Keyframe || _dragMode == DragMode.MultiKeyframe)
                _lastDragEndTick = Environment.TickCount64;

            bool wasBoxSelect = _dragMode == DragMode.BoxSelect;
            _dragMode = DragMode.None;
            _isPanning = false;
            _snapTargetTimeMs = null;
            _boxSelectRect = default; // 清除框选矩形残留
            e.Handled = true;
            InvalidateVisual();
            if (wasBoxSelect)
                NotifyMultiSelectionChanged(); // 框选结束 → 同步选择集
        }

        // 鼠标滚轮: 缩放值域
        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

            if (shift)
            {
                // Shift+滚轮: 调整轨道高度 (向上冒泡到窗口处理)
                double delta = e.Delta.Y > 0 ? 8 : -8;
                TrackHeightChangeRequested?.Invoke(delta);
                e.Handled = true;
            }
            else if (ctrl)
            {
                // Ctrl+Scroll: 水平时间缩放 (保持鼠标下的时间位置不变, 与标尺/轨道区同步)
                double factor = e.Delta.Y > 0 ? 1.2 : 1.0 / 1.2;
                double mouseX = e.GetPosition(this).X;
                double ppm = Math.Max(0.001, PixelsPerMs);
                double newPpm = Math.Clamp(ppm * factor, 0.001, 1.0);
                double timeAtMouse = (ScrollOffsetX + mouseX) / ppm;
                double newScroll = Math.Clamp(
                    timeAtMouse * newPpm - mouseX,
                    0,
                    Math.Max(0, DurationMs * newPpm - Math.Max(1, Bounds.Width))
                );
                PixelsPerMs = newPpm;
                ScrollOffsetX = newScroll;
                ZoomRequested?.Invoke(newPpm, newScroll);
                e.Handled = true;
            }
            else
            {
                // 普通滚轮: 值域缩放
                double factor = e.Delta.Y > 0 ? 1.15 : 1.0 / 1.15;
                ValueZoom = Math.Clamp(ValueZoom * factor, 0.1, 10.0);
            }

            InvalidateVisual();
            e.Handled = true;
        }

        // ═══════ 拖拽排序辅助 ═══════

        /// <summary>拖拽期间对关键帧按时间排序, 并更新 _selectedKfIdx</summary>
        private void SortClipKeyframesDuringDrag(MotionClip clip, MotionKeyframe draggedKf)
        {
            // 检查是否需要排序
            bool needSort = false;
            for (int i = 1; i < clip.Keyframes.Count; i++)
            {
                if (clip.Keyframes[i].TimeMs < clip.Keyframes[i - 1].TimeMs)
                {
                    needSort = true;
                    break;
                }
            }
            if (!needSort)
                return;

            clip.Keyframes.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
            _selectedKfIdx = clip.Keyframes.IndexOf(draggedKf);
        }

        // ═══════ 键盘事件 ═══════

        /// <summary>多选集合变化时通知外部 (同步到 VM 全局多选集, 供 Delete/批量插值/属性面板统一)。</summary>
        private void NotifyMultiSelectionChanged()
        {
            var snapshot = _multiSelectedKfs.Select(s => (s.trackIdx, s.clipIdx, s.kfIdx)).ToList();
            MultiSelectionChanged?.Invoke(snapshot);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == Key.Delete || e.Key == Key.Back)
            {
                // Overlay 覆盖关键帧删除 (优先级最高: 已选中空窗自由点)
                if (_selectedTrackIdx >= 0 && _selectedOverrideKfIdx >= 0 && CurveTracks != null)
                {
                    if (IsCurveTrackLocked(_selectedTrackIdx))
                    {
                        e.Handled = true;
                        return;
                    }
                    var ovList = CurveTracks[_selectedTrackIdx].OverrideKeyframes;
                    if (ovList != null && _selectedOverrideKfIdx < ovList.Count)
                    {
                        var okf = ovList[_selectedOverrideKfIdx];
                        ovList.RemoveAt(_selectedOverrideKfIdx);
                        OverrideKeyframeDeleteRequested?.Invoke(
                            _selectedTrackIdx,
                            okf.TimeMs,
                            okf.Value
                        );
                        _selectedOverrideKfIdx = -1;
                        _selectedTrackIdx = -1;
                        InvalidateVisual();
                        e.Handled = true;
                    }
                    return;
                }
                if (_multiSelectedKfs.Count > 0)
                {
                    if (HasLockedMultiSelection())
                    {
                        e.Handled = true;
                        return;
                    }
                    // 曲线编辑器框选的批量删除
                    var selection = _multiSelectedKfs
                        .Select(s => (s.trackIdx, s.clipIdx, s.kfIdx))
                        .ToList();
                    _multiSelectedKfs.Clear();
                    NotifyMultiSelectionChanged(); // 删除后选择集已空, 同步 VM
                    _selectedTrackIdx = -1;
                    _selectedClipIdx = -1;
                    _selectedKfIdx = -1;
                    InvalidateVisual();
                    DeleteMultiSelectedRequested?.Invoke(selection);
                    e.Handled = true;
                }
                else if (_selectedTrackIdx >= 0 && _selectedClipIdx >= 0 && _selectedKfIdx >= 0)
                {
                    if (IsCurveTrackLocked(_selectedTrackIdx))
                    {
                        e.Handled = true;
                        return;
                    }
                    // 单选关键帧删除 — 转发为包含单个元素的批量删除
                    var selection = new List<(int TrackIdx, int ClipIdx, int KfIdx)>
                    {
                        (_selectedTrackIdx, _selectedClipIdx, _selectedKfIdx)
                    };
                    _selectedTrackIdx = -1;
                    _selectedClipIdx = -1;
                    _selectedKfIdx = -1;
                    InvalidateVisual();
                    DeleteMultiSelectedRequested?.Invoke(selection);
                    e.Handled = true;
                }
            }
        }

        // ═══════ 命中测试 ═══════

        /// <summary>命中 Overlay 覆盖关键帧 (空窗自由数值点, 圆形标记)。返回 (trackIdx, overrideKfIdx)。</summary>
        private (int trackIdx, int kfIdx)? HitTestOverrideKeyframe(Point pos, double viewH)
        {
            var tracks = CurveTracks;
            if (tracks == null)
                return null;

            double bestDist = KfHitRadius;
            (int, int)? best = null;

            for (int ti = 0; ti < tracks.Count; ti++)
            {
                if (IsTrackHidden(ti))
                    continue;
                var ov = tracks[ti].OverrideKeyframes;
                if (ov == null || ov.Count == 0)
                    continue;
                for (int oi = 0; oi < ov.Count; oi++)
                {
                    var kf = ov[oi];
                    double x = kf.TimeMs * PixelsPerMs - ScrollOffsetX;
                    double y = ValueToY(kf.Value, viewH);
                    double d = Math.Sqrt((pos.X - x) * (pos.X - x) + (pos.Y - y) * (pos.Y - y));
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = (ti, oi);
                    }
                }
            }
            return best;
        }

        private (int trackIdx, int clipIdx, int kfIdx)? HitTestKeyframe(Point pos, double viewH)
        {
            var tracks = CurveTracks;
            if (tracks == null)
                return null;

            double bestDist = KfHitRadius;
            (int, int, int)? best = null;

            for (int ti = 0; ti < tracks.Count; ti++)
            {
                if (IsTrackHidden(ti))
                    continue;
                for (int ci = 0; ci < tracks[ti].Clips.Count; ci++)
                {
                    var clip = tracks[ti].Clips[ci];
                    for (int ki = 0; ki < clip.Keyframes.Count; ki++)
                    {
                        var kf = clip.Keyframes[ki];
                        double x = (clip.StartMs + kf.TimeMs) * PixelsPerMs - ScrollOffsetX;
                        bool isBool = string.Equals(
                            tracks[ti].ValueType,
                            "bool",
                            StringComparison.OrdinalIgnoreCase
                        );
                        float drawVal = isBool ? (kf.Value >= 0.5f ? 1f : 0f) : kf.Value;
                        double y = ValueToY(drawVal, viewH);
                        double dist = Math.Sqrt(
                            (pos.X - x) * (pos.X - x) + (pos.Y - y) * (pos.Y - y)
                        );
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            best = (ti, ci, ki);
                        }
                    }
                }
            }
            return best;
        }

        private (int trackIdx, int clipIdx, int kfIdx, bool isIn)? HitTestTangentHandle(
            Point pos,
            double viewH
        )
        {
            // 仅检测当前选中关键帧的切线手柄
            if (_selectedTrackIdx < 0 || _selectedClipIdx < 0 || _selectedKfIdx < 0)
                return null;
            if (CurveTracks == null)
                return null;
            if (_selectedTrackIdx >= CurveTracks.Count)
                return null;

            var track = CurveTracks[_selectedTrackIdx];
            if (_selectedClipIdx >= track.Clips.Count)
                return null;
            var clip = track.Clips[_selectedClipIdx];
            if (_selectedKfIdx >= clip.Keyframes.Count)
                return null;

            var kf = clip.Keyframes[_selectedKfIdx];
            if (!string.Equals(kf.Interpolation, "bezier", StringComparison.OrdinalIgnoreCase))
                return null;

            double kfX = (clip.StartMs + kf.TimeMs) * PixelsPerMs - ScrollOffsetX;
            double kfY = ValueToY(kf.Value, viewH);

            // In handle
            double inX = kfX - (kf.Cp1x ?? 1f) * TangentHandleLength;
            double inY = kfY + (kf.TangentIn ?? 0f) * TangentHandleLength;
            double distIn = Math.Sqrt(
                (pos.X - inX) * (pos.X - inX) + (pos.Y - inY) * (pos.Y - inY)
            );
            if (distIn < HandleHitRadius)
                return (_selectedTrackIdx, _selectedClipIdx, _selectedKfIdx, true);

            // Out handle
            double outX = kfX + (kf.Cp2x ?? 1f) * TangentHandleLength;
            double outY = kfY - (kf.TangentOut ?? 0f) * TangentHandleLength;
            double distOut = Math.Sqrt(
                (pos.X - outX) * (pos.X - outX) + (pos.Y - outY) * (pos.Y - outY)
            );
            if (distOut < HandleHitRadius)
                return (_selectedTrackIdx, _selectedClipIdx, _selectedKfIdx, false);

            return null;
        }

        // ═══════ 公共方法 ═══════

        /// <summary>设置选中的关键帧</summary>
        public void SelectKeyframe(int trackIdx, int clipIdx, int kfIdx)
        {
            _selectedTrackIdx = trackIdx;
            _selectedClipIdx = clipIdx;
            _selectedKfIdx = kfIdx;
            InvalidateVisual();
        }

        /// <summary>清除选择</summary>
        public void ClearSelection()
        {
            _selectedTrackIdx = -1;
            _selectedClipIdx = -1;
            _selectedKfIdx = -1;
            _multiSelectedKfs.Clear();
            InvalidateVisual();
        }

        /// <summary>设置当前选中的轨道索引 (由外部轨道面板选中时调用)</summary>
        public void SetSelectedTrack(int trackIdx)
        {
            _selectedTrackIdx = trackIdx;
            _trackPanelSelectedIdx = trackIdx;
        }

        /// <summary>从外部同步选中关键帧索引 (排序后索引变化时调用)</summary>
        public void SetSelectedKeyframeIndex(int trackIdx, int clipIdx, int kfIdx)
        {
            _selectedTrackIdx = trackIdx;
            _selectedClipIdx = clipIdx;
            _selectedKfIdx = kfIdx;
        }

        /// <summary>空白区域右键菜单: 添加关键帧</summary>
        private void ShowEmptyAreaContextMenu(Point pos, double viewH)
        {
            var menu = new Avalonia.Controls.ContextMenu();

            double timeMs = XToTimeMs(pos.X);

            // 负时间区域不提供添加关键帧选项
            if (timeMs < 0)
                return;

            float value = YToValue(pos.Y, viewH);

            // 如果有选中轨道, 在选中轨道上添加
            if (
                _selectedTrackIdx >= 0
                && CurveTracks != null
                && _selectedTrackIdx < CurveTracks.Count
            )
            {
                string trackLabel =
                    CurveTracks[_selectedTrackIdx].Label ?? $"轨道 {_selectedTrackIdx + 1}";
                var addItem = new Avalonia.Controls.MenuItem
                {
                    Header = $"在此处添加关键帧 ({timeMs:F0}ms) — {trackLabel}",
                    IsEnabled = !IsCurveTrackLocked(_selectedTrackIdx)
                };
                addItem.Click += (_, _) =>
                    AddKeyframeRequested?.Invoke(_selectedTrackIdx, timeMs, value);
                menu.Items.Add(addItem);
            }

            // 列出所有可见轨道供选择
            if (CurveTracks != null)
            {
                if (_selectedTrackIdx >= 0)
                    menu.Items.Add(new Avalonia.Controls.Separator());

                for (int ti = 0; ti < CurveTracks.Count; ti++)
                {
                    if (IsTrackHidden(ti))
                        continue;
                    if (ti == _selectedTrackIdx)
                        continue; // 已在上方添加

                    int trackIdx = ti;
                    string label = CurveTracks[ti].Label ?? $"轨道 {ti + 1}";
                    var item = new Avalonia.Controls.MenuItem
                    {
                        Header = $"添加关键帧到 {label} ({timeMs:F0}ms)",
                        IsEnabled = !IsCurveTrackLocked(trackIdx)
                    };
                    item.Click += (_, _) =>
                    {
                        _selectedTrackIdx = trackIdx;
                        AddKeyframeRequested?.Invoke(trackIdx, timeMs, value);
                    };
                    menu.Items.Add(item);
                }
            }

            if (menu.Items.Count > 0)
            {
                _activeContextMenu?.Close();
                _activeContextMenu = menu;
                menu.Open(this);
            }
        }

        /// <summary>显示关键帧插值类型右键菜单 (含曲线预设, 支持多选批量操作)</summary>
        private void ShowInterpolationContextMenu(Point pos, int trackIdx, int clipIdx, int kfIdx)
        {
            var menu = new Avalonia.Controls.ContextMenu();
            bool isMultiSelect = _multiSelectedKfs.Count > 1;
            bool editLocked = isMultiSelect
                ? HasLockedMultiSelection()
                : IsCurveTrackLocked(trackIdx);

            // 多选右键前先同步选择集到 VM, 保证批量插值/预设基于 _selectedKeyframes 可靠命中
            if (isMultiSelect)
                NotifyMultiSelectionChanged();

            // 获取当前关键帧的插值信息
            string currentInterp = "";
            float? currentTangIn = null,
                currentTangOut = null;
            if (CurveTracks != null && trackIdx < CurveTracks.Count)
            {
                var clip = CurveTracks[trackIdx].Clips[clipIdx];
                if (kfIdx < clip.Keyframes.Count)
                {
                    var kf = clip.Keyframes[kfIdx];
                    currentInterp = kf.Interpolation ?? "";
                    currentTangIn = kf.TangentIn;
                    currentTangOut = kf.TangentOut;
                }
            }

            // ── 插值类型 (当前项用勾选标记) ──
            string headerText = isMultiSelect
                ? $"批量设置 ({_multiSelectedKfs.Count} 个关键帧)"
                : "插值类型";
            var interpHeader = new Avalonia.Controls.MenuItem
            {
                Header = headerText,
                IsEnabled = false
            };
            menu.Items.Add(interpHeader);

            string[] types = { "linear", "bezier", "step", "ease_in_out" };
            string[] labels = { "线性 (Linear)", "贝塞尔 (Bezier)", "阶梯 (Step)", "缓入缓出 (Ease)" };

            for (int i = 0; i < types.Length; i++)
            {
                string interpType = types[i];
                bool isCurrent =
                    !isMultiSelect
                    && string.Equals(currentInterp, interpType, StringComparison.OrdinalIgnoreCase);
                var item = new Avalonia.Controls.MenuItem
                {
                    Header = $"{(isCurrent ? "✓ " : "")}{labels[i]}",
                    FontWeight = isCurrent
                        ? Avalonia.Media.FontWeight.Bold
                        : Avalonia.Media.FontWeight.Normal,
                    IsEnabled = !editLocked
                };

                item.Click += (_, _) =>
                {
                    if (isMultiSelect)
                    {
                        // 批量设置多选关键帧 → 交给 VM BatchSetInterpolation (带 Undo/MarkDirty/刷新)
                        BatchInterpolationRequested?.Invoke(interpType);
                    }
                    else if (CurveTracks != null && trackIdx < CurveTracks.Count)
                    {
                        var clip = CurveTracks[trackIdx].Clips[clipIdx];
                        if (kfIdx < clip.Keyframes.Count)
                        {
                            clip.Keyframes[kfIdx].Interpolation = interpType;
                            InterpolationChanged?.Invoke(trackIdx, clipIdx, kfIdx, interpType);
                        }
                    }
                    InvalidateVisual();
                };
                menu.Items.Add(item);
            }

            // ── 分隔线 ──
            menu.Items.Add(new Separator());

            // ── 曲线预设 (扁平化, 带 ✓ 标记当前匹配的预设) ──
            var presetHeader = new Avalonia.Controls.MenuItem
            {
                Header = "曲线预设",
                IsEnabled = false
            };
            menu.Items.Add(presetHeader);

            var categories = CurvePresetService.GetPresetsByCategory();
            foreach (var (category, presets) in categories)
            {
                foreach (var preset in presets)
                {
                    string presetName = preset.Name;
                    bool isCurrentPreset =
                        !isMultiSelect
                        && string.Equals(
                            currentInterp,
                            preset.Interpolation,
                            StringComparison.OrdinalIgnoreCase
                        )
                        && currentTangIn == preset.TangentIn
                        && currentTangOut == preset.TangentOut;

                    var presetItem = new Avalonia.Controls.MenuItem
                    {
                        Header = $"{(isCurrentPreset ? "✓ " : "")}{preset.DisplayName}",
                        FontWeight = isCurrentPreset
                            ? Avalonia.Media.FontWeight.Bold
                            : Avalonia.Media.FontWeight.Normal,
                        IsEnabled = !editLocked
                    };

                    presetItem.Click += (_, _) =>
                    {
                        if (isMultiSelect)
                        {
                            // 批量应用预设 → 交给 VM BatchApplyPreset (带 Undo/MarkDirty/刷新)
                            BatchPresetApplyRequested?.Invoke(presetName);
                        }
                        else if (CurveTracks != null && trackIdx < CurveTracks.Count)
                        {
                            var clip = CurveTracks[trackIdx].Clips[clipIdx];
                            if (kfIdx < clip.Keyframes.Count)
                            {
                                var kf = clip.Keyframes[kfIdx];
                                kf.Interpolation = preset.Interpolation;
                                kf.TangentIn = preset.TangentIn;
                                kf.TangentOut = preset.TangentOut;
                                InterpolationChanged?.Invoke(
                                    trackIdx,
                                    clipIdx,
                                    kfIdx,
                                    preset.Interpolation
                                );
                                PresetApplyRequested?.Invoke(trackIdx, clipIdx, kfIdx, presetName);
                            }
                        }
                        InvalidateVisual();
                    };
                    menu.Items.Add(presetItem);
                }
            }

            _activeContextMenu?.Close();
            _activeContextMenu = menu;
            menu.Open(this);
        }
    }
}
