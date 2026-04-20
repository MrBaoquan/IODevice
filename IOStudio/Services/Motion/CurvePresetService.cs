using System;
using System.Collections.Generic;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 关键帧曲线预设服务
    /// 提供常用插值曲线的快速应用功能 (如 Ease-In, Ease-Out, Bounce 等)
    /// </summary>
    public static class CurvePresetService
    {
        /// <summary>预设定义</summary>
        public record CurvePreset(
            string Name,
            string DisplayName,
            string Interpolation,
            float? TangentIn,
            float? TangentOut,
            string Category
        );

        /// <summary>所有内置预设</summary>
        public static IReadOnlyList<CurvePreset> BuiltInPresets { get; } =
            new List<CurvePreset>
            {
                // ── 基础插值 ──
                new("linear", "线性", "linear", null, null, "基础"),
                new("step", "阶梯", "step", null, null, "基础"),
                new("ease_in_out", "缓入缓出", "ease_in_out", null, null, "基础"),
                // ── Ease 系列 (贝塞尔) ──
                new("ease_in", "缓入 (慢→快)", "bezier", 0f, 0.8f, "缓动"),
                new("ease_out", "缓出 (快→慢)", "bezier", 0.8f, 0f, "缓动"),
                new("ease_in_strong", "强缓入", "bezier", 0f, 1.2f, "缓动"),
                new("ease_out_strong", "强缓出", "bezier", 1.2f, 0f, "缓动"),
                // ── 弹性/动感 (贝塞尔近似) ──
                new("overshoot", "过冲", "bezier", 0.6f, -0.4f, "动感"),
                new("anticipation", "蓄力", "bezier", -0.3f, 0.8f, "动感"),
                new("snap", "急停", "bezier", 1.5f, 0f, "动感"),
            };

        /// <summary>按类别分组获取预设</summary>
        public static Dictionary<string, List<CurvePreset>> GetPresetsByCategory()
        {
            var result = new Dictionary<string, List<CurvePreset>>();
            foreach (var preset in BuiltInPresets)
            {
                if (!result.ContainsKey(preset.Category))
                    result[preset.Category] = new List<CurvePreset>();
                result[preset.Category].Add(preset);
            }
            return result;
        }

        /// <summary>根据名称查找预设</summary>
        public static CurvePreset? FindPreset(string name)
        {
            foreach (var p in BuiltInPresets)
                if (p.Name == name)
                    return p;
            return null;
        }

        /// <summary>
        /// 将预设应用到关键帧
        /// </summary>
        /// <param name="context">时间轴上下文</param>
        /// <param name="track">目标轨道</param>
        /// <param name="clipIdx">Clip 索引</param>
        /// <param name="kfIdx">关键帧索引</param>
        /// <param name="preset">要应用的预设</param>
        public static void ApplyPreset(
            ITimelineContext context,
            ViewModels.Timeline.TrackViewModel track,
            int clipIdx,
            int kfIdx,
            CurvePreset preset
        )
        {
            if (clipIdx < 0 || clipIdx >= track.Clips.Count)
                return;
            var clipVm = track.Clips[clipIdx];
            if (kfIdx < 0 || kfIdx >= clipVm.Keyframes.Count)
                return;

            var kf = clipVm.Keyframes[kfIdx];
            var oldInterp = kf.Interpolation;
            var oldTangIn = kf.TangentIn;
            var oldTangOut = kf.TangentOut;

            context.UndoRedo.Execute(
                new LambdaCommand(
                    $"应用预设 {preset.DisplayName}",
                    () =>
                    {
                        kf.Interpolation = preset.Interpolation;
                        kf.TangentIn = preset.TangentIn;
                        kf.TangentOut = preset.TangentOut;
                        context.NotifyTrackDataChanged(track);
                    },
                    () =>
                    {
                        kf.Interpolation = oldInterp;
                        kf.TangentIn = oldTangIn;
                        kf.TangentOut = oldTangOut;
                        context.NotifyTrackDataChanged(track);
                    }
                )
            );
            context.MarkDirty();
        }

        /// <summary>
        /// 批量应用预设到多个关键帧
        /// </summary>
        public static void ApplyPresetBatch(
            ITimelineContext context,
            IReadOnlyList<(
                ViewModels.Timeline.TrackViewModel Track,
                int ClipIdx,
                int KfIdx
            )> keyframes,
            CurvePreset preset
        )
        {
            if (keyframes.Count == 0)
                return;

            // 保存旧值
            var snapshots =
                new List<(
                    ViewModels.Timeline.KeyframeViewModel Kf,
                    string OldInterp,
                    float? OldTangIn,
                    float? OldTangOut
                )>();

            foreach (var (track, clipIdx, kfIdx) in keyframes)
            {
                if (clipIdx < 0 || clipIdx >= track.Clips.Count)
                    continue;
                var clipVm = track.Clips[clipIdx];
                if (kfIdx < 0 || kfIdx >= clipVm.Keyframes.Count)
                    continue;
                var kf = clipVm.Keyframes[kfIdx];
                snapshots.Add((kf, kf.Interpolation, kf.TangentIn, kf.TangentOut));
            }

            if (snapshots.Count == 0)
                return;

            var affectedTracks = new HashSet<ViewModels.Timeline.TrackViewModel>();
            foreach (var (track, _, _) in keyframes)
                affectedTracks.Add(track);

            context.UndoRedo.Execute(
                new LambdaCommand(
                    $"批量应用预设 {preset.DisplayName}",
                    () =>
                    {
                        foreach (var (kf, _, _, _) in snapshots)
                        {
                            kf.Interpolation = preset.Interpolation;
                            kf.TangentIn = preset.TangentIn;
                            kf.TangentOut = preset.TangentOut;
                        }
                        foreach (var t in affectedTracks)
                            context.NotifyTrackDataChanged(t);
                    },
                    () =>
                    {
                        foreach (var (kf, oldInterp, oldTangIn, oldTangOut) in snapshots)
                        {
                            kf.Interpolation = oldInterp;
                            kf.TangentIn = oldTangIn;
                            kf.TangentOut = oldTangOut;
                        }
                        foreach (var t in affectedTracks)
                            context.NotifyTrackDataChanged(t);
                    }
                )
            );
            context.MarkDirty();
        }
    }
}
