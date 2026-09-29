using System;
using System.Linq;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;

namespace IOStudio.ViewModels.Timeline
{
    /// <summary>
    /// Ripple 编辑 — 结构性插入/删除时间。
    ///
    /// <para>跨轨操作会同时影响 关键帧 / 片段边界 / Override 关键帧 / 独立事件 / 标记 / 动作实例 /
    /// 总时长, 逐字段写撤销极易漏项。因此统一走"整时间轴 JSON 快照"做撤销 (与动作实例编辑同一套路),
    /// 保证 Ctrl+Z 一定回到操作前的完整状态。</para>
    /// </summary>
    public partial class TimelineEditorViewModel
    {
        /// <summary>在播放头插入 <paramref name="deltaMs"/> 毫秒 (其后所有内容右移)。</summary>
        public void RippleInsertAtPlayhead(double deltaMs)
        {
            if (Timeline is null || deltaMs <= 0)
                return;
            double at = CurrentTimeMs;
            ApplyTimelineTransform(
                "插入时间",
                $"已在 {FormatMs(at)} 处插入 {deltaMs:0}ms",
                tl => RippleInsert(tl, at, deltaMs)
            );
        }

        /// <summary>删除工作区 [In, Out] 时间段, 其后内容左移。</summary>
        public void RippleDeleteWorkArea()
        {
            if (Timeline is null || !HasWorkArea || WorkAreaInMs is null || WorkAreaOutMs is null)
                return;
            double from = Math.Min(WorkAreaInMs.Value, WorkAreaOutMs.Value);
            double to = Math.Max(WorkAreaInMs.Value, WorkAreaOutMs.Value);
            if (to - from < 0.5)
                return;

            ApplyTimelineTransform(
                "删除时间段",
                $"已删除 {FormatMs(from)} ~ {FormatMs(to)} 时间段",
                tl => RippleDelete(tl, from, to)
            );
        }

        /// <summary>把变换应用到时间轴 (整时间轴快照撤销)。</summary>
        private void ApplyTimelineTransform(string undoLabel, string status, Action<MotionTimeline> mutate)
        {
            string beforeJson = MotionFileReader.ToJson(Timeline!);
            var candidate = MotionFileReader.ReadFromJson(beforeJson);
            if (candidate is null)
                return;

            mutate(candidate);

            // 仅当内容真的变了才登记撤销 (例如区间内无可删内容)
            string afterJson = MotionFileReader.ToJson(candidate);
            if (afterJson == beforeJson)
            {
                PushStatus("没有可变换的内容");
                return;
            }

            double playhead = CurrentTimeMs;
            _undoRedo.Execute(
                new LambdaCommand(
                    undoLabel,
                    () => RestoreTimelineSnapshot(afterJson, playhead),
                    () => RestoreTimelineSnapshot(beforeJson, playhead)
                )
            );
            MarkDirty();
            PushStatus(status);
        }

        // ── 纯模型变换 (便于单测, 不触碰 UI 状态) ──

        /// <summary>在 <paramref name="atMs"/> 处插入一段空白时间。</summary>
        public static void RippleInsert(MotionTimeline tl, double atMs, double deltaMs)
        {
            if (deltaMs <= 0)
                return;

            foreach (var track in tl.Tracks)
            {
                foreach (var clip in track.Clips)
                {
                    if (clip.StartMs >= atMs - 0.5)
                    {
                        // 整条片段都在插入点之后 → 平移
                        clip.StartMs += deltaMs;
                        clip.EndMs += deltaMs;
                    }
                    else
                    {
                        // 跨越插入点 → 只右移插入点之后的关键帧, 并延长片段以覆盖空档
                        foreach (var kf in clip.Keyframes)
                        {
                            if (clip.StartMs + kf.TimeMs >= atMs - 0.5)
                                kf.TimeMs += deltaMs;
                        }
                        if (clip.EndMs >= atMs - 0.5)
                            clip.EndMs += deltaMs;
                    }
                }

                if (track.OverrideKeyframes is { Count: > 0 } overrides)
                {
                    foreach (var kf in overrides)
                    {
                        if (kf.TimeMs >= atMs - 0.5)
                            kf.TimeMs += deltaMs;
                    }
                }
            }

            if (tl.Events is { Count: > 0 })
            {
                foreach (var evt in tl.Events)
                {
                    if (evt.TimeMs >= atMs - 0.5)
                        evt.TimeMs += deltaMs;
                }
            }

            if (tl.Markers is { Count: > 0 })
            {
                foreach (var marker in tl.Markers)
                {
                    if (marker.TimeMs >= atMs - 0.5)
                        marker.TimeMs += deltaMs;
                }
            }

            if (tl.ActionInstances is { Count: > 0 })
            {
                foreach (var instance in tl.ActionInstances)
                {
                    if (instance.StartMs >= atMs - 0.5)
                        instance.StartMs += deltaMs;
                }
            }

            tl.DurationMs += deltaMs;
        }

        /// <summary>删除 [fromMs, toMs] 时间段, 其后内容左移。</summary>
        public static void RippleDelete(MotionTimeline tl, double fromMs, double toMs)
        {
            double span = toMs - fromMs;
            if (span <= 0)
                return;

            foreach (var track in tl.Tracks)
            {
                foreach (var clip in track.Clips)
                {
                    // 1) 移除落在区间内的关键帧
                    clip.Keyframes.RemoveAll(kf =>
                    {
                        double abs = clip.StartMs + kf.TimeMs;
                        return abs >= fromMs - 0.5 && abs <= toMs + 0.5;
                    });

                    // 2) 区间之后的关键帧左移
                    foreach (var kf in clip.Keyframes)
                    {
                        if (clip.StartMs + kf.TimeMs > toMs + 0.5)
                            kf.TimeMs = Math.Max(0, kf.TimeMs - span);
                    }

                    // 3) 片段边界调整
                    if (clip.StartMs >= toMs - 0.5)
                    {
                        clip.StartMs -= span;
                        clip.EndMs -= span;
                    }
                    else if (clip.EndMs > toMs + 0.5)
                    {
                        clip.EndMs = Math.Max(clip.StartMs, clip.EndMs - span);
                    }
                }

                // 4) 完全落在区间内的片段整体删除
                track.Clips.RemoveAll(c =>
                    c.StartMs >= fromMs - 0.5 && c.EndMs <= toMs + 0.5
                );

                if (track.OverrideKeyframes is { Count: > 0 } overrides)
                {
                    overrides.RemoveAll(kf => kf.TimeMs >= fromMs - 0.5 && kf.TimeMs <= toMs + 0.5);
                    foreach (var kf in overrides)
                    {
                        if (kf.TimeMs > toMs + 0.5)
                            kf.TimeMs = Math.Max(0, kf.TimeMs - span);
                    }
                }
            }

            if (tl.Events is { Count: > 0 })
            {
                tl.Events.RemoveAll(e => e.TimeMs >= fromMs - 0.5 && e.TimeMs <= toMs + 0.5);
                foreach (var evt in tl.Events)
                {
                    if (evt.TimeMs > toMs + 0.5)
                        evt.TimeMs = Math.Max(0, evt.TimeMs - span);
                }
            }

            if (tl.Markers is { Count: > 0 })
            {
                tl.Markers.RemoveAll(m => m.TimeMs >= fromMs - 0.5 && m.TimeMs <= toMs + 0.5);
                foreach (var marker in tl.Markers)
                {
                    if (marker.TimeMs > toMs + 0.5)
                        marker.TimeMs = Math.Max(0, marker.TimeMs - span);
                }
            }

            if (tl.ActionInstances is { Count: > 0 })
            {
                tl.ActionInstances.RemoveAll(i => i.StartMs >= fromMs - 0.5 && i.StartMs <= toMs + 0.5);
                foreach (var instance in tl.ActionInstances)
                {
                    if (instance.StartMs > toMs + 0.5)
                        instance.StartMs = Math.Max(0, instance.StartMs - span);
                }
            }

            tl.DurationMs = Math.Max(0, tl.DurationMs - span);
        }

        private static string FormatMs(double ms) =>
            TimeSpan.FromMilliseconds(Math.Max(0, ms)).ToString(@"mm\:ss\.fff");
    }
}
