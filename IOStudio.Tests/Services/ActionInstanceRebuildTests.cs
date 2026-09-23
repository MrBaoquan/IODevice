using System.Collections.Generic;
using System.Linq;
using Xunit;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using IOStudio.Tests.Helpers;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// 动作实例「按预设重建」测试。
    ///
    /// 背景: 预设是长片编排的核心手段 (动作由片段组合而成), 但 <c>RebuildInstance</c>
    /// 此前只有定义、没有任何调用点 —— 改了预设或手改了 clip 之后无法回灌, 只能删了重放。
    /// 这里锁定重建的行为与失败时的回滚语义。
    /// </summary>
    public class ActionInstanceRebuildTests
    {
        private static EffectPreset Preset(string id = "fx_test", int revision = 1) =>
            new()
            {
                Id = id,
                Revision = revision,
                Name = "测试预设",
                DurationMs = 1000,
                Keyframes = new List<MotionKeyframe>
                {
                    new() { TimeMs = 0, Value = 0.2f, Interpolation = "linear" },
                    new() { TimeMs = 1000, Value = 0.8f, Interpolation = "linear" },
                },
            };

        private static MotionTrack Track() =>
            new()
            {
                Id = "trk1",
                DeviceName = "Simulator-1",
                OutputType = "oaxis",
                OAxisChannel = "OAxis_01",
                Label = "俯仰",
                ValueType = "float",
            };

        private static MotionTimeline TimelineWithPlacement(out ActionPlacementResult placed)
        {
            var timeline = new MotionTimeline { Name = "t", DurationMs = 10000 };
            timeline.Tracks.Add(Track());
            placed = ActionInstanceService.PlacePreset(
                timeline,
                Preset(),
                new PresetApplyOptions { Intensity = 1f, Speed = 1.0 },
                0,
                "trk1"
            );
            return timeline;
        }

        private static List<MotionClip> InstanceClips(MotionTimeline tl, string instanceId) =>
            tl.Tracks
                .SelectMany(t => t.Clips)
                .Where(c => c.ActionInstanceId == instanceId)
                .ToList();

        // ── 核心用例: 手改后重建恢复 ───────────────────────

        [Fact]
        public void Rebuild_RestoresHandEditedKeyframes()
        {
            var tl = TimelineWithPlacement(out var placed);
            Assert.True(placed.Success);
            string instanceId = placed.Instance!.Id;

            var clips = InstanceClips(tl, instanceId);
            Assert.NotEmpty(clips);

            // 手改关键帧值 (模拟作者在曲线上拖过)
            foreach (var kf in clips.SelectMany(c => c.Keyframes))
                kf.Value = 0.999f;

            var result = ActionInstanceService.RebuildInstance(tl, instanceId);
            Assert.True(result.Success, result.Error);

            var rebuilt = InstanceClips(tl, instanceId).SelectMany(c => c.Keyframes).ToList();
            Assert.NotEmpty(rebuilt);
            // 值应回到预设定义 (0.2 → 0.8 的曲线), 而不是手改的 0.999
            Assert.Contains(rebuilt, k => System.Math.Abs(k.Value - 0.2f) < 0.01f);
            Assert.Contains(rebuilt, k => System.Math.Abs(k.Value - 0.8f) < 0.01f);
            Assert.DoesNotContain(rebuilt, k => System.Math.Abs(k.Value - 0.999f) < 0.001f);
        }

        [Fact]
        public void Rebuild_PreservesIdentityAndTiming()
        {
            var tl = TimelineWithPlacement(out var placed);
            string instanceId = placed.Instance!.Id;
            var before = tl.ActionInstances.First(i => i.Id == instanceId);
            before.Name = "我的俯冲";
            before.Intensity = 0.7f;
            before.PlaybackRate = 1.5;
            before.LoopCount = 3;
            before.Enabled = false;
            before.StartMs = 2500;
            before.DurationMs = 1500;

            var result = ActionInstanceService.RebuildInstance(tl, instanceId);
            Assert.True(result.Success, result.Error);

            var after = tl.ActionInstances.First(i => i.Id == instanceId);
            Assert.Equal(instanceId, after.Id); // Id 必须稳定 (clip 靠它关联)
            Assert.Equal("我的俯冲", after.Name);
            Assert.Equal(0.7f, after.Intensity, precision: 4);
            Assert.Equal(1.5, after.PlaybackRate, precision: 4);
            Assert.Equal(3, after.LoopCount);
            Assert.False(after.Enabled);
            Assert.Equal(2500, after.StartMs, precision: 1);
            Assert.Equal(1500, after.DurationMs, precision: 1);

            // clip 仍指向同一个实例
            Assert.NotEmpty(InstanceClips(tl, instanceId));
        }

        [Fact]
        public void Rebuild_DoesNotDuplicateClips()
        {
            var tl = TimelineWithPlacement(out var placed);
            string instanceId = placed.Instance!.Id;
            int before = InstanceClips(tl, instanceId).Count;

            ActionInstanceService.RebuildInstance(tl, instanceId);
            ActionInstanceService.RebuildInstance(tl, instanceId);

            Assert.Equal(before, InstanceClips(tl, instanceId).Count);
            Assert.Single(tl.ActionInstances, i => i.Id == instanceId);
        }

        // ── 失败与回滚 ────────────────────────────────────

        [Fact]
        public void Rebuild_WithoutSnapshot_FailsAndLeavesTimelineIntact()
        {
            var tl = TimelineWithPlacement(out var placed);
            string instanceId = placed.Instance!.Id;
            var clipIdsBefore = InstanceClips(tl, instanceId).Select(c => c.Id).ToList();

            // 快照丢失 (老项目/被清理)
            tl.EmbeddedPresets.Clear();

            var result = ActionInstanceService.RebuildInstance(tl, instanceId);
            Assert.False(result.Success);
            Assert.False(string.IsNullOrEmpty(result.Error));

            // 失败必须回滚: 实例与 clip 原样保留
            Assert.Single(tl.ActionInstances, i => i.Id == instanceId);
            Assert.Equal(clipIdsBefore, InstanceClips(tl, instanceId).Select(c => c.Id).ToList());
        }

        [Fact]
        public void Rebuild_UnknownInstance_Fails()
        {
            var tl = TimelineWithPlacement(out _);
            var result = ActionInstanceService.RebuildInstance(tl, "no_such_instance");
            Assert.False(result.Success);
        }

        [Fact]
        public void Rebuild_LockedTrack_Fails()
        {
            var tl = TimelineWithPlacement(out var placed);
            tl.Tracks[0].Locked = true;
            var result = ActionInstanceService.RebuildInstance(tl, placed.Instance!.Id);
            Assert.False(result.Success);
        }

        // ── 快照刷新 (从预设库更新并重建的前半段) ──────────

        [Fact]
        public void UpsertEmbeddedPreset_ReplacesSameRevision()
        {
            var tl = new MotionTimeline();
            var v1 = Preset();
            ActionInstanceService.UpsertEmbeddedPreset(tl, v1);
            Assert.Single(tl.EmbeddedPresets);

            // 同 (id, revision) 再写入 → 替换而非追加
            var v1b = Preset();
            v1b.Name = "改名后的预设";
            ActionInstanceService.UpsertEmbeddedPreset(tl, v1b);

            Assert.Single(tl.EmbeddedPresets);
            Assert.Equal("改名后的预设", tl.EmbeddedPresets[0].Name);
        }

        [Fact]
        public void UpsertEmbeddedPreset_AddsNewRevision()
        {
            var tl = new MotionTimeline();
            ActionInstanceService.UpsertEmbeddedPreset(tl, Preset(revision: 1));
            ActionInstanceService.UpsertEmbeddedPreset(tl, Preset(revision: 2));
            Assert.Equal(2, tl.EmbeddedPresets.Count);
        }

        [Fact]
        public void Rebuild_PicksSnapshotMatchingInstanceRevision()
        {
            // 修订号决定用哪份快照: 实例指向 rev2 时, 重建必须用 rev2 的曲线
            var tl = new MotionTimeline { DurationMs = 10000 };
            tl.Tracks.Add(Track());
            var rev1 = Preset(revision: 1);
            var rev2 = Preset(revision: 2);
            rev2.Keyframes = new List<MotionKeyframe>
            {
                new() { TimeMs = 0, Value = 0.9f, Interpolation = "linear" },
                new() { TimeMs = 1000, Value = 0.9f, Interpolation = "linear" },
            };
            ActionInstanceService.UpsertEmbeddedPreset(tl, rev1);
            ActionInstanceService.UpsertEmbeddedPreset(tl, rev2);

            var placed = ActionInstanceService.PlacePreset(
                tl,
                rev2,
                new PresetApplyOptions { Intensity = 1f, Speed = 1.0 },
                0,
                "trk1"
            );
            Assert.True(placed.Success, placed.Error);
            string instanceId = placed.Instance!.Id;
            Assert.Equal(2, tl.ActionInstances.First(i => i.Id == instanceId).DefinitionRevision);

            foreach (var kf in InstanceClips(tl, instanceId).SelectMany(c => c.Keyframes))
                kf.Value = 0.1f;

            Assert.True(ActionInstanceService.RebuildInstance(tl, instanceId).Success);
            var values = InstanceClips(tl, instanceId).SelectMany(c => c.Keyframes).Select(k => k.Value);
            Assert.All(values, v => Assert.True(System.Math.Abs(v - 0.9f) < 0.01f));
        }
    }
}
