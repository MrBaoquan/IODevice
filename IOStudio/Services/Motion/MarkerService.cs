using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using IOStudio.Models.Motion;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 时间轴标记管理服务 — 支持增删改和导航
    /// 所有修改操作都通过 UndoRedo 注册
    /// </summary>
    public class MarkerService
    {
        private readonly ITimelineContext _context;

        /// <summary>当前标记列表 (Observable, 供 UI 绑定)</summary>
        public ObservableCollection<TimelineMarker> Markers { get; } = new();

        /// <summary>当前选中的标记</summary>
        public TimelineMarker? SelectedMarker { get; set; }

        /// <summary>标记变更事件 (用于通知 TimeRuler 重绘)</summary>
        public event Action? MarkersChanged;

        public MarkerService(ITimelineContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>从 Timeline 模型加载标记</summary>
        public void LoadFromTimeline(MotionTimeline timeline)
        {
            Markers.Clear();
            if (timeline.Markers != null)
            {
                foreach (var m in timeline.Markers.OrderBy(m => m.TimeMs))
                    Markers.Add(m);
            }
            MarkersChanged?.Invoke();
        }

        /// <summary>同步标记回 Timeline 模型</summary>
        public void SyncToTimeline()
        {
            if (_context.Timeline == null)
                return;
            _context.Timeline.Markers =
                Markers.Count > 0 ? Markers.OrderBy(m => m.TimeMs).ToList() : null;
        }

        // ── 预设颜色 ──
        private static readonly string[] _presetColors =
        {
            "#f59e0b",
            "#ef4444",
            "#22c55e",
            "#3b82f6",
            "#a855f7",
            "#ec4899",
            "#14b8a6",
            "#f97316"
        };
        private int _colorIndex;

        /// <summary>添加标记 (支持撤销)</summary>
        public TimelineMarker AddMarker(double timeMs, string? name = null, string? color = null)
        {
            var marker = new TimelineMarker
            {
                TimeMs = timeMs,
                Name = name ?? $"M{Markers.Count + 1}",
                Color = color ?? _presetColors[_colorIndex++ % _presetColors.Length]
            };

            _context.UndoRedo.Execute(
                new LambdaCommand(
                    "添加标记",
                    () =>
                    {
                        Markers.Add(marker);
                        SortMarkers();
                        SyncToTimeline();
                        MarkersChanged?.Invoke();
                    },
                    () =>
                    {
                        Markers.Remove(marker);
                        SyncToTimeline();
                        MarkersChanged?.Invoke();
                    }
                )
            );
            _context.MarkDirty();
            return marker;
        }

        /// <summary>删除标记 (支持撤销)</summary>
        public void RemoveMarker(TimelineMarker marker)
        {
            int idx = Markers.IndexOf(marker);
            if (idx < 0)
                return;

            _context.UndoRedo.Execute(
                new LambdaCommand(
                    "删除标记",
                    () =>
                    {
                        Markers.Remove(marker);
                        SyncToTimeline();
                        MarkersChanged?.Invoke();
                    },
                    () =>
                    {
                        Markers.Insert(Math.Min(idx, Markers.Count), marker);
                        SortMarkers();
                        SyncToTimeline();
                        MarkersChanged?.Invoke();
                    }
                )
            );
            _context.MarkDirty();
        }

        /// <summary>更新标记属性 (支持撤销)</summary>
        public void UpdateMarker(
            TimelineMarker marker,
            double? timeMs = null,
            string? name = null,
            string? color = null,
            string? note = null
        )
        {
            var oldTime = marker.TimeMs;
            var oldName = marker.Name;
            var oldColor = marker.Color;
            var oldNote = marker.Note;

            _context.UndoRedo.Execute(
                new LambdaCommand(
                    "修改标记",
                    () =>
                    {
                        if (timeMs.HasValue)
                            marker.TimeMs = timeMs.Value;
                        if (name != null)
                            marker.Name = name;
                        if (color != null)
                            marker.Color = color;
                        if (note != null)
                            marker.Note = note;
                        SortMarkers();
                        SyncToTimeline();
                        MarkersChanged?.Invoke();
                    },
                    () =>
                    {
                        marker.TimeMs = oldTime;
                        marker.Name = oldName;
                        marker.Color = oldColor;
                        marker.Note = oldNote;
                        SortMarkers();
                        SyncToTimeline();
                        MarkersChanged?.Invoke();
                    }
                )
            );
            _context.MarkDirty();
        }

        /// <summary>导航到上一个标记</summary>
        public TimelineMarker? NavigatePrevious()
        {
            if (Markers.Count == 0)
                return null;
            double current = _context.CurrentTimeMs;
            TimelineMarker? prev = null;
            foreach (var m in Markers.OrderByDescending(m => m.TimeMs))
            {
                if (m.TimeMs < current - 1.0)
                {
                    prev = m;
                    break;
                }
            }
            if (prev != null)
            {
                _context.CurrentTimeMs = prev.TimeMs;
                SelectedMarker = prev;
            }
            return prev;
        }

        /// <summary>导航到下一个标记</summary>
        public TimelineMarker? NavigateNext()
        {
            if (Markers.Count == 0)
                return null;
            double current = _context.CurrentTimeMs;
            TimelineMarker? next = null;
            foreach (var m in Markers.OrderBy(m => m.TimeMs))
            {
                if (m.TimeMs > current + 1.0)
                {
                    next = m;
                    break;
                }
            }
            if (next != null)
            {
                _context.CurrentTimeMs = next.TimeMs;
                SelectedMarker = next;
            }
            return next;
        }

        /// <summary>获取标记列表 (供 TimeRuler 渲染)</summary>
        public IReadOnlyList<TimelineMarker> GetMarkers() => Markers;

        private void SortMarkers()
        {
            var sorted = Markers.OrderBy(m => m.TimeMs).ToList();
            Markers.Clear();
            foreach (var m in sorted)
                Markers.Add(m);
        }

        /// <summary>清空所有标记</summary>
        public void Clear()
        {
            Markers.Clear();
            SelectedMarker = null;
            MarkersChanged?.Invoke();
        }
    }
}
