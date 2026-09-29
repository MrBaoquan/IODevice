using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using IOStudio.ViewModels.Timeline;

namespace IOStudio.Tests.ViewModels
{
    /// <summary>
    /// 快速编排回归 — 吸附点补全、方向键微调、全选/剪切、以及
    /// "检查器绝对时间 ↔ 关键帧 clip 局部时间" 的换算 (偏移 clip 不得被改写时间)。
    /// </summary>
    public class TimelineRapidAuthoringTests
    {
        private static MotionKeyframe Kf(double t, float v) =>
            new()
            {
                TimeMs = t,
                Value = v,
                Interpolation = "linear",
            };

        /// <summary>clip A 起点 0; clip B 起点 32000 (用于验证绝对/局部换算)。</summary>
        private static string WriteTempTimeline()
        {
            var track = new MotionTrack
            {
                Id = "trk",
                DeviceName = "Simulator-1",
                OutputType = "oaxis",
                OAxisChannel = "OAxis_01",
                Label = "俯仰",
            };
            track.Clips.Add(
                new MotionClip
                {
                    Id = "a",
                    StartMs = 0,
                    EndMs = 5000,
                    Keyframes = new List<MotionKeyframe> { Kf(0, 0.2f), Kf(1000, 0.6f) },
                }
            );
            track.Clips.Add(
                new MotionClip
                {
                    Id = "b",
                    StartMs = 32000,
                    EndMs = 35000,
                    Keyframes = new List<MotionKeyframe> { Kf(0, 0.3f), Kf(3000, 0.9f) },
                }
            );

            var timeline = new MotionTimeline
            {
                Version = "3.0",
                Name = "rapid",
                DurationMs = 35000,
                Tracks = new List<MotionTrack> { track },
            };

            string path = Path.Combine(Path.GetTempPath(), $"rapid_{Guid.NewGuid():N}.motion");
            Assert.True(MotionFileReader.Write(timeline, path));
            return path;
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                /* ignore */
            }
        }

        [Fact]
        public void SnapPoints_Include_Playhead_ClipEdges_And_Markers()
        {
            string path = WriteTempTimeline();
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                vm.CurrentTimeMs = 1234;
                vm.MarkerService.AddMarker(2000);

                var points = vm.GetSnapPoints();

                Assert.Contains(0.0, points); // 起点
                Assert.Contains(5000.0, points); // clip A 末端
                Assert.Contains(32000.0, points); // clip B 起点
                Assert.Contains(1234.0, points); // 播放头
                Assert.Contains(2000.0, points); // 标记
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void SnapPoints_Include_BeatMarkers()
        {
            string path = WriteTempTimeline();
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                vm.BeatMarkers = new List<double> { 800, 1600 };

                var points = vm.GetSnapPoints();

                Assert.Contains(800.0, points);
                Assert.Contains(1600.0, points);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void NudgeTime_Moves_Selected_Keyframe_By_10ms()
        {
            string path = WriteTempTimeline();
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                var track = vm.Tracks[0];
                vm.SelectKeyframe(track, 0, 1); // clip A, kf @1000ms

                Assert.Equal(
                    ExecuteShortcutResult.HandledNeedsMultiSelectRefresh,
                    vm.ExecuteShortcut(ShortcutAction.NudgeTimeForward)
                );
                Assert.Equal(1010.0, track.Clips[0].Keyframes[1].TimeMs, precision: 3);

                vm.ExecuteShortcut(ShortcutAction.NudgeTimeBackwardLarge);
                Assert.Equal(910.0, track.Clips[0].Keyframes[1].TimeMs, precision: 3);

                // 撤销后回到 1000
                vm.Undo();
                Assert.Equal(1010.0, track.Clips[0].Keyframes[1].TimeMs, precision: 3);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void NudgeTime_Does_Not_Go_Below_Zero()
        {
            string path = WriteTempTimeline();
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                var track = vm.Tracks[0];
                vm.SelectKeyframe(track, 0, 0); // kf @0ms

                vm.ExecuteShortcut(ShortcutAction.NudgeTimeBackward);

                Assert.Equal(0.0, track.Clips[0].Keyframes[0].TimeMs, precision: 3);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void NudgeValue_Adjusts_And_Clamps()
        {
            string path = WriteTempTimeline();
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                var track = vm.Tracks[0];
                vm.SelectKeyframe(track, 0, 1); // value 0.6

                vm.ExecuteShortcut(ShortcutAction.NudgeValueUp);
                Assert.Equal(0.61f, track.Clips[0].Keyframes[1].Value, precision: 4);

                vm.ExecuteShortcut(ShortcutAction.NudgeValueUpLarge); // +0.1 → 0.71
                Assert.Equal(0.71f, track.Clips[0].Keyframes[1].Value, precision: 4);

                // 钳位到 1.0
                for (int i = 0; i < 40; i++)
                    vm.ExecuteShortcut(ShortcutAction.NudgeValueUp);
                Assert.Equal(1.0f, track.Clips[0].Keyframes[1].Value, precision: 4);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void SelectAllKeyframes_SelectsWholeTrack()
        {
            string path = WriteTempTimeline();
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                var track = vm.Tracks[0];
                vm.SelectKeyframe(track, 0, 0);

                vm.ExecuteShortcut(ShortcutAction.SelectAllKeyframes);

                // clip A 有 2 个 + clip B 有 2 个 = 4
                Assert.Equal(4, vm.SelectedKeyframeCount);
                Assert.True(vm.IsMultiSelectMode);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void Cut_Removes_Selected_Keyframes_And_Keeps_Clipboard()
        {
            string path = WriteTempTimeline();
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                var track = vm.Tracks[0];
                vm.SelectKeyframe(track, 0, 0);
                vm.AddToSelection(track, 0, 1);
                Assert.Equal(2, vm.SelectedKeyframeCount);

                vm.ExecuteShortcut(ShortcutAction.Cut);

                Assert.Equal(0, track.Clips[0].Keyframes.Count); // A 的 2 个被删空
                Assert.True(vm.HasClipboardKeyframes); // 已复制到剪贴板

                vm.Undo(); // 删除可撤销
                Assert.Equal(2, track.Clips[0].Keyframes.Count);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void UpdateKeyframeProperties_Keeps_Local_Time_On_Offset_Clip()
        {
            string path = WriteTempTimeline();
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                var track = vm.Tracks[0];

                // clip B 起点 32000, 其 kf 局部时间 3000 → 绝对时间 35000
                vm.SelectKeyframe(track, 1, 1);
                Assert.Equal(3000.0, track.Clips[1].Keyframes[1].TimeMs, precision: 3);

                // 检查器传入的是绝对时间 (播放头)。旧实现会把它直接写进模型 → 时间被改写成 35000。
                vm.UpdateKeyframeProperties(35000, 0.42f, "linear", null, null);

                Assert.Equal(0.42f, track.Clips[1].Keyframes[1].Value, precision: 4);
                Assert.Equal(3000.0, track.Clips[1].Keyframes[1].TimeMs, precision: 3); // 时间保持不变
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void UpdateKeyframeProperties_Applies_New_Absolute_Time_As_Local()
        {
            string path = WriteTempTimeline();
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                var track = vm.Tracks[0];

                vm.SelectKeyframe(track, 1, 1); // 绝对 35000 / 局部 3000

                // 输入新的绝对时间 33500 → 局部应为 1500
                vm.UpdateKeyframeProperties(33500, 0.9f, "linear", null, null);

                Assert.Equal(1500.0, track.Clips[1].Keyframes[1].TimeMs, precision: 3);
            }
            finally
            {
                TryDelete(path);
            }
        }

        // ── P1: 时间结构操作 ──

        [Fact]
        public void TimeOp_Align_Moves_Selection_Start_To_Playhead()
        {
            string path = WriteTempTimeline();
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                var track = vm.Tracks[0];

                // clip A 两个 kf @0 和 @1000; 对齐到 5000 → 变为 5000 / 6000
                vm.CurrentTimeMs = 5000;
                vm.BatchTransformKeyframeTimes(track, 0, null, "align");

                Assert.Equal(5000.0, track.Clips[0].Keyframes[0].TimeMs, precision: 3);
                Assert.Equal(6000.0, track.Clips[0].Keyframes[1].TimeMs, precision: 3);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void TimeOp_Scale_Anchors_At_Selection_Start()
        {
            string path = WriteTempTimeline();
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                var track = vm.Tracks[0];

                // clip A: 0 和 1000, ×2 → 0 和 2000
                vm.BatchTransformKeyframeTimes(track, 0, null, "scale", 2.0);

                Assert.Equal(0.0, track.Clips[0].Keyframes[0].TimeMs, precision: 3);
                Assert.Equal(2000.0, track.Clips[0].Keyframes[1].TimeMs, precision: 3);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void TimeOp_Reverse_Mirrors_And_Keeps_Sorted()
        {
            string path = WriteTempTimeline();
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                var track = vm.Tracks[0];

                // clip A: 0 和 1000 → 反转后仍是 {0, 1000}, 值顺序互换
                vm.BatchTransformKeyframeTimes(track, 0, null, "reverse");

                var kfs = track.Clips[0].Keyframes;
                Assert.Equal(2, kfs.Count);
                Assert.True(kfs[0].TimeMs <= kfs[1].TimeMs); // 仍按时间升序
                Assert.Equal(0.6f, kfs[0].Value, precision: 4); // 原 1000ms 的 0.6 移到 0ms
                Assert.Equal(0.2f, kfs[1].Value, precision: 4);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void TimeOp_Quantize_Snaps_To_Nearest_Beat()
        {
            string path = WriteTempTimeline();
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                var track = vm.Tracks[0];
                vm.BeatMarkers = new List<double> { 900, 1100 };

                // clip A kf @1000 → 最近节拍 1100 (等距时取先遇到的; 这里 900/1100 距 1000 均为 100)
                vm.BatchTransformKeyframeTimes(track, 0, null, "quantize");

                double t = track.Clips[0].Keyframes[1].TimeMs;
                Assert.True(Math.Abs(t - 900) < 0.01 || Math.Abs(t - 1100) < 0.01);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void TimeOp_Quantize_NoBeat_Is_NoOp()
        {
            string path = WriteTempTimeline();
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                var track = vm.Tracks[0];
                vm.BeatMarkers = null;

                vm.BatchTransformKeyframeTimes(track, 0, null, "quantize");

                Assert.Equal(1000.0, track.Clips[0].Keyframes[1].TimeMs, precision: 3);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void TimeOp_Reverse_Is_Undoable()
        {
            string path = WriteTempTimeline();
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                var track = vm.Tracks[0];

                vm.BatchTransformKeyframeTimes(track, 1, null, "reverse"); // clip B: 0 和 3000
                Assert.Equal(0.9f, track.Clips[1].Keyframes[0].Value, precision: 4);

                vm.Undo();
                Assert.Equal(0.3f, track.Clips[1].Keyframes[0].Value, precision: 4);
                Assert.Equal(0.0, track.Clips[1].Keyframes[0].TimeMs, precision: 3);
                Assert.Equal(3000.0, track.Clips[1].Keyframes[1].TimeMs, precision: 3);
            }
            finally
            {
                TryDelete(path);
            }
        }

        // ── P1: 轨道复制 ──

        [Fact]
        public void DuplicateTrack_Copies_Keyframes_With_New_Ids()
        {
            string path = WriteTempTimeline();
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                var source = vm.Tracks[0];

                vm.DuplicateTrack(source);

                Assert.Equal(2, vm.Tracks.Count);
                Assert.Equal(2, vm.Timeline!.Tracks.Count);

                var copy = vm.Tracks[1];
                Assert.NotEqual(source.Id, copy.Id);
                Assert.Equal(source.Label + " 副本", copy.Label);
                Assert.Equal(source.Tracks0_KeyframeCount(), copy.Tracks0_KeyframeCount());
                // 关键帧是深拷贝 (不同实例)
                Assert.NotSame(
                    source.Clips[0].Keyframes[0],
                    copy.Clips[0].Keyframes[0]
                );

                vm.Undo();
                Assert.Equal(1, vm.Tracks.Count);
            }
            finally
            {
                TryDelete(path);
            }
        }

        // ── P1: AutoKey 单步撤销 ──

        [Fact]
        public void AddKeyframeAtTime_WithValue_Is_Single_Undo_Step()        {
            string path = WriteTempTimeline();
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                var track = vm.Tracks[0];
                int before = track.Clips[0].Keyframes.Count;

                vm.AddKeyframeAtTime(track, 500, 0.77f);
                Assert.Equal(before + 1, track.Clips[0].Keyframes.Count);
                var added = track.Clips[0].Keyframes.First(k => Math.Abs(k.TimeMs - 500) < 0.01);
                Assert.Equal(0.77f, added.Value, precision: 4);

                // 一次撤销即完全移除 (旧实现是"建帧 + 改值"两步)
                vm.Undo();
                Assert.Equal(before, track.Clips[0].Keyframes.Count);
            }
            finally
            {
                TryDelete(path);
            }
        }

        // ── Bug 回归: 轨道高度是视图状态, 不得标脏文档 ──

        [Fact]
        public void TrackHeight_Changes_Do_Not_Mark_Document_Dirty()
        {
            string path = WriteTempTimeline();
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                var track = vm.Tracks[0];
                vm.IsDirty = false;

                vm.SetTrackIndividualHeight(track, 80);
                vm.AdjustAllTrackHeights(8);
                vm.TrackHeightLevel = 2;
                vm.ResetTrackHeight(track);
                vm.ResetAllTrackHeights();

                Assert.False(vm.IsDirty); // 高度不写入内容, 不应置脏
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void Track_Content_Edit_Still_Marks_Dirty()
        {
            string path = WriteTempTimeline();
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                vm.IsDirty = false;

                vm.AddKeyframeAtTime(vm.Tracks[0], 500, 0.6f);

                Assert.True(vm.IsDirty);
            }
            finally
            {
                TryDelete(path);
            }
        }

        // ── 网格吸附 ──

        [Fact]
        public void ApplySnap_Snaps_To_Grid_When_Enabled()
        {
            string path = WriteTempTimeline();
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                vm.IsSnapEnabled = true;
                vm.PixelsPerMs = 1.0; // 吸附阈值 ≈ 10ms, 便于精确断言
                vm.SelectedSnapGrid = vm.SnapGridOptions.First(o => o.Ms == 500);
                vm.CurrentTimeMs = 999999;

                Assert.Equal(1500.0, vm.ApplySnap(1504), precision: 3);
                Assert.Equal(1000.0, vm.ApplySnap(1004), precision: 3);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void ApplySnap_NoGrid_Leaves_Far_Time_Unchanged()
        {
            string path = WriteTempTimeline();
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                vm.IsSnapEnabled = true;
                vm.PixelsPerMs = 1.0;
                vm.SelectedSnapGrid = vm.SnapGridOptions.First(o => o.Ms == 0);
                vm.CurrentTimeMs = 999999;

                // 无网格 + 附近无吸附点 → 保持原值
                Assert.Equal(1510.0, vm.ApplySnap(1510), precision: 3);
            }
            finally
            {
                TryDelete(path);
            }
        }

        // ── 反向选择 / 工作区全选 ──

        [Fact]
        public void InvertSelection_Toggles_Membership()
        {
            string path = WriteTempTimeline();
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                var track = vm.Tracks[0];

                vm.SelectKeyframe(track, 0, 0); // 选中 1 个 (共 4 个)
                Assert.Equal(1, vm.SelectedKeyframeCount);

                vm.InvertKeyframeSelection(allTracks: false);

                Assert.Equal(3, vm.SelectedKeyframeCount);
                Assert.False(vm.IsKeyframeSelected(track, 0, 0));
                Assert.True(vm.IsKeyframeSelected(track, 0, 1));

                vm.InvertKeyframeSelection(allTracks: false);
                Assert.Equal(1, vm.SelectedKeyframeCount);
                Assert.True(vm.IsKeyframeSelected(track, 0, 0));
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void SelectInWorkArea_Picks_Keyframes_Across_Clips()
        {
            string path = WriteTempTimeline();
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);

                // 工作区 [32000, 35000] → 命中 clip B 的 2 个关键帧 (绝对 32000 / 35000)
                vm.WorkAreaInMs = 32000;
                vm.WorkAreaOutMs = 35000;

                vm.SelectKeyframesInWorkArea();

                Assert.Equal(2, vm.SelectedKeyframeCount);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void SelectInWorkArea_NoWorkArea_Is_NoOp()
        {
            string path = WriteTempTimeline();
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                vm.SelectKeyframe(vm.Tracks[0], 0, 0);

                vm.SelectKeyframesInWorkArea();

                Assert.Equal(1, vm.SelectedKeyframeCount); // 保持原选择
            }
            finally
            {
                TryDelete(path);
            }
        }

        // ── P2: 标记编辑走 MarkerService (带撤销) ──
        [Fact]
        public void MarkerService_UpdateMarker_Is_Undoable()
        {
            string path = WriteTempTimeline();
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                var marker = vm.MarkerService.AddMarker(1000, "M1", "#f59e0b");

                vm.MarkerService.UpdateMarker(marker, name: "开场", color: "#22c55e", note: "第一幕");

                Assert.Equal("开场", marker.Name);
                Assert.Equal("#22c55e", marker.Color);
                Assert.Equal("第一幕", marker.Note);

                vm.Undo();
                Assert.Equal("M1", marker.Name);
                Assert.Equal("#f59e0b", marker.Color);
            }
            finally
            {
                TryDelete(path);
            }
        }
    }

    internal static class TrackVmTestExtensions
    {
        /// <summary>首个 clip 的关键帧数 (测试用简写)。</summary>
        public static int Tracks0_KeyframeCount(this TrackViewModel track) =>
            track.Clips.Count == 0 ? 0 : track.Clips[0].Keyframes.Count;
    }
}
