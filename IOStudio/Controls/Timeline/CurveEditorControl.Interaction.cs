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
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
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
                        KeyframeSelected?.Invoke(rti, rci, rki);
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
                }
                else if (shift)
                {
                    _multiSelectedKfs.Add((ti, ci, ki));
                    _trackPanelSelectedIdx = ti;
                }
                else
                {
                    // 如果点击的关键帧已在多选中, 进入多选拖拽模式
                    if (_multiSelectedKfs.Count > 1 && _multiSelectedKfs.Contains((ti, ci, ki)))
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
                        _multiSelectedKfs.Clear();
                        _selectedTrackIdx = ti;
                        _trackPanelSelectedIdx = ti; // 记住最近交互的轨道, 双击添加关键帧时使用
                        _selectedClipIdx = ci;
                        _selectedKfIdx = ki;
                        _dragMode = DragMode.Keyframe;
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
                _dragMode = tangentHit.Value.isIn ? DragMode.TangentIn : DragMode.TangentOut;
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
                    double timeMs = XToTimeMs(pos.X);
                    float value = YToValue(pos.Y, viewH);

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
                    double usableH = viewH - GridPaddingTop - GridPaddingBottom;
                    if (usableH > 0)
                        ValueOffset = _panStartValueOffset + dy / usableH / ValueZoom;
                    InvalidateVisual();
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
                            float newVal = origVal + deltaValue;

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
                _multiDragSnapshot = null;
                if (movedList.Count > 0)
                    MultiKeyframeMoved?.Invoke(movedList);
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
                TangentChanged?.Invoke(
                    _selectedTrackIdx,
                    _selectedClipIdx,
                    _selectedKfIdx,
                    kf.TangentIn ?? 0f,
                    kf.TangentOut ?? 0f,
                    kf.Cp1x,
                    kf.Cp2x
                );
            }

            if (_dragMode == DragMode.Keyframe || _dragMode == DragMode.MultiKeyframe)
                _lastDragEndTick = Environment.TickCount64;

            _dragMode = DragMode.None;
            _isPanning = false;
            _snapTargetTimeMs = null;
            _boxSelectRect = default; // 清除框选矩形残留
            e.Handled = true;
            InvalidateVisual();
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
                // Ctrl+Scroll: 水平时间缩放
                double factor = e.Delta.Y > 0 ? 1.2 : 1.0 / 1.2;
                PixelsPerMs = Math.Clamp(PixelsPerMs * factor, 0.001, 1.0);
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

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == Key.Delete || e.Key == Key.Back)
            {
                if (_multiSelectedKfs.Count > 0)
                {
                    // 曲线编辑器框选的批量删除
                    var selection = _multiSelectedKfs
                        .Select(s => (s.trackIdx, s.clipIdx, s.kfIdx))
                        .ToList();
                    _multiSelectedKfs.Clear();
                    _selectedTrackIdx = -1;
                    _selectedClipIdx = -1;
                    _selectedKfIdx = -1;
                    InvalidateVisual();
                    DeleteMultiSelectedRequested?.Invoke(selection);
                    e.Handled = true;
                }
                else if (_selectedTrackIdx >= 0 && _selectedClipIdx >= 0 && _selectedKfIdx >= 0)
                {
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
                    Header = $"在此处添加关键帧 ({timeMs:F0}ms) — {trackLabel}"
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
                        Header = $"添加关键帧到 {label} ({timeMs:F0}ms)"
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

            // ── 插值类型 (带 ✓ 标记 + emoji 图标) ──
            string headerText = isMultiSelect
                ? $"🎯 批量设置 ({_multiSelectedKfs.Count} 个关键帧)"
                : "插值类型";
            var interpHeader = new Avalonia.Controls.MenuItem
            {
                Header = headerText,
                IsEnabled = false
            };
            menu.Items.Add(interpHeader);

            string[] types = { "linear", "bezier", "step", "ease_in_out" };
            string[] icons = { "📏", "〰️", "⬛", "🔄" };
            string[] labels = { "线性 (Linear)", "贝塞尔 (Bezier)", "阶梯 (Step)", "缓入缓出 (Ease)" };

            for (int i = 0; i < types.Length; i++)
            {
                string interpType = types[i];
                bool isCurrent =
                    !isMultiSelect
                    && string.Equals(currentInterp, interpType, StringComparison.OrdinalIgnoreCase);
                var item = new Avalonia.Controls.MenuItem
                {
                    Header = $"  {icons[i]} {labels[i]}{(isCurrent ? " ✓" : "")}",
                    FontWeight = isCurrent
                        ? Avalonia.Media.FontWeight.Bold
                        : Avalonia.Media.FontWeight.Normal
                };

                item.Click += (_, _) =>
                {
                    if (isMultiSelect)
                    {
                        // 批量设置多选关键帧
                        foreach (var sel in _multiSelectedKfs)
                        {
                            if (CurveTracks != null && sel.trackIdx < CurveTracks.Count)
                            {
                                var c = CurveTracks[sel.trackIdx].Clips[sel.clipIdx];
                                if (sel.kfIdx < c.Keyframes.Count)
                                    c.Keyframes[sel.kfIdx].Interpolation = interpType;
                            }
                        }
                        // 通知所有受影响的轨道
                        foreach (var ti in _multiSelectedKfs.Select(s => s.trackIdx).Distinct())
                            InterpolationChanged?.Invoke(ti, -1, -1, interpType);
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
                Header = "🎨 曲线预设",
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
                        Header = $"  {preset.DisplayName}{(isCurrentPreset ? " ✓" : "")}",
                        FontWeight = isCurrentPreset
                            ? Avalonia.Media.FontWeight.Bold
                            : Avalonia.Media.FontWeight.Normal
                    };

                    presetItem.Click += (_, _) =>
                    {
                        if (isMultiSelect)
                        {
                            // 批量应用预设
                            foreach (var sel in _multiSelectedKfs)
                            {
                                if (CurveTracks != null && sel.trackIdx < CurveTracks.Count)
                                {
                                    var c = CurveTracks[sel.trackIdx].Clips[sel.clipIdx];
                                    if (sel.kfIdx < c.Keyframes.Count)
                                    {
                                        c.Keyframes[sel.kfIdx].Interpolation = preset.Interpolation;
                                        c.Keyframes[sel.kfIdx].TangentIn = preset.TangentIn;
                                        c.Keyframes[sel.kfIdx].TangentOut = preset.TangentOut;
                                    }
                                }
                            }
                            foreach (var ti in _multiSelectedKfs.Select(s => s.trackIdx).Distinct())
                            {
                                InterpolationChanged?.Invoke(ti, -1, -1, preset.Interpolation);
                                PresetApplyRequested?.Invoke(ti, -1, -1, presetName);
                            }
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
