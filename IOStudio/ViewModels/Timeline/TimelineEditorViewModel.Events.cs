using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Threading;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using ReactiveUI;

namespace IOStudio.ViewModels.Timeline
{
    public partial class TimelineEditorViewModel
    {
        // ---- 独立事件管理 ----

        private ObservableCollection<TimelineEvent> _events = new();

        /// <summary>独立事件列表 (不绑定到关键帧)</summary>
        public ObservableCollection<TimelineEvent> Events
        {
            get => _events;
            set => this.RaiseAndSetIfChanged(ref _events, value);
        }

        private TimelineEvent? _selectedEvent;

        /// <summary>当前选中的独立事件</summary>
        public TimelineEvent? SelectedEvent
        {
            get => _selectedEvent;
            set => this.RaiseAndSetIfChanged(ref _selectedEvent, value);
        }

        /// <summary>
        /// 检查事件名称是否唯一
        /// </summary>
        /// <param name="name">待检查名称</param>
        /// <param name="exclude">排除的事件 (用于编辑时排除自身)</param>
        /// <returns>名称唯一返回 true</returns>
        public bool IsEventNameUnique(string name, TimelineEvent? exclude = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                return true;
            foreach (var e in Events)
            {
                if (e != exclude && string.Equals(e.EventName, name, StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 生成唯一的事件名称 (基于 baseName 自动追加 _1, _2, ...)
        /// </summary>
        private string GenerateUniqueEventName(string baseName = "event")
        {
            if (IsEventNameUnique(baseName))
                return baseName;
            int suffix = 1;
            while (!IsEventNameUnique($"{baseName}_{suffix}"))
                suffix++;
            return $"{baseName}_{suffix}";
        }

        /// <summary>
        /// 在指定时间添加独立事件
        /// </summary>
        public void AddEvent(double timeMs, string eventName = "event", string? eventData = null)
        {
            if (Timeline == null)
                return;

            // 自动生成唯一名称
            string uniqueName = GenerateUniqueEventName(eventName);

            var evt = new TimelineEvent
            {
                TimeMs = timeMs,
                EventName = uniqueName,
                EventData = eventData
            };

            _undoRedo.Execute(
                new LambdaCommand(
                    "添加事件",
                    () =>
                    {
                        Timeline.Events.Add(evt);
                        Events.Add(evt);
                        SortEvents();
                        SelectedEvent = evt;
                    },
                    () =>
                    {
                        Timeline.Events.Remove(evt);
                        Events.Remove(evt);
                        if (SelectedEvent == evt)
                            SelectedEvent = null;
                    }
                )
            );
            MarkDirty();
        }

        /// <summary>
        /// 在播放头位置添加独立事件
        /// </summary>
        public void AddEventAtPlayhead()
        {
            AddEvent(CurrentTimeMs);
        }

        /// <summary>
        /// 在指定时间位置添加独立事件 (来自右键菜单)
        /// </summary>
        public void AddEventAtTime(double timeMs)
        {
            AddEvent(timeMs);
        }

        /// <summary>
        /// 删除指定的独立事件
        /// </summary>
        public void RemoveEvent(TimelineEvent evt)
        {
            if (Timeline == null)
                return;

            int idx = Events.IndexOf(evt);
            var wasSelected = (SelectedEvent == evt);

            _undoRedo.Execute(
                new LambdaCommand(
                    "删除事件",
                    () =>
                    {
                        Timeline.Events.Remove(evt);
                        Events.Remove(evt);
                        if (SelectedEvent == evt)
                            SelectedEvent = null;
                    },
                    () =>
                    {
                        Timeline.Events.Add(evt);
                        Events.Insert(Math.Min(idx, Events.Count), evt);
                        SortEvents();
                        if (wasSelected)
                            SelectedEvent = evt;
                    }
                )
            );
            MarkDirty();
        }

        /// <summary>
        /// 更新独立事件属性 (事件名重复时拒绝更新名称, 支持 Undo/Redo)
        /// </summary>
        /// <returns>true 更新成功, false 名称重复被拒绝</returns>
        public bool UpdateEvent(
            TimelineEvent evt,
            double? timeMs = null,
            string? eventName = null,
            string? eventData = null,
            string? dataType = null
        )
        {
            if (eventName is not null && !IsEventNameUnique(eventName, evt))
                return false; // 名称重复, 拒绝更新

            // 快照旧属性
            double oldTime = evt.TimeMs;
            string oldName = evt.EventName ?? "";
            string oldData = evt.EventData ?? "";
            string oldDataType = evt.DataType ?? "string";

            _undoRedo.Execute(
                new LambdaCommand(
                    "修改事件属性",
                    () =>
                    {
                        if (timeMs.HasValue)
                            evt.TimeMs = timeMs.Value;
                        if (eventName is not null)
                            evt.EventName = eventName;
                        if (eventData is not null)
                            evt.EventData = eventData;
                        if (dataType is not null)
                            evt.DataType = dataType;
                        SortEvents();
                        this.RaisePropertyChanged(nameof(Events));
                    },
                    () =>
                    {
                        evt.TimeMs = oldTime;
                        evt.EventName = oldName;
                        evt.EventData = oldData;
                        evt.DataType = oldDataType;
                        SortEvents();
                        this.RaisePropertyChanged(nameof(Events));
                    }
                )
            );
            MarkDirty();
            return true;
        }

        private void SortEvents()
        {
            var sorted = Events.OrderBy(e => e.TimeMs).ToList();
            Events.Clear();
            foreach (var e in sorted)
                Events.Add(e);
        }

        /// <summary>
        /// 通知轨道数据变化 (触发 TrackClipControl 重绘)
        /// </summary>
        public void NotifyTrackDataChanged(TrackViewModel track)
        {
            // 触发 Clips 集合通知, 让 FuncValueConverter 重新转换
            track.RaiseClipsChanged();
        }
    }
}
