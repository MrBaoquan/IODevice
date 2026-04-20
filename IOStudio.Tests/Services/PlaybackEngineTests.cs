using System;
using System.Collections.Generic;
using Xunit;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using IOStudio.Tests.Helpers;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// NativeMotionPlaybackEngine 播放引擎测试
    /// 覆盖: 加载/播放/暂停/停止状态机, Seek 求值, Solo/Mute 逻辑, Bool 轨道阶梯求值
    /// 注意: 使用 EnsureTimelineLoaded 绕过 C++ interop (不依赖物理设备/DLL)
    /// </summary>
    public class PlaybackEngineTests
    {
        private NativeMotionPlaybackEngine CreateEngine()
        {
            return new NativeMotionPlaybackEngine(new DeviceDispatcher());
        }

        /// <summary>创建引擎并通过 EnsureTimelineLoaded 设置 Timeline (绕过 C++ interop)</summary>
        private NativeMotionPlaybackEngine CreateEngineWith(MotionTimeline timeline)
        {
            var engine = new NativeMotionPlaybackEngine(new DeviceDispatcher());
            engine.EnsureTimelineLoaded(timeline);
            return engine;
        }

        // ═══════ 初始状态 ═══════

        [Fact]
        public void InitialState_IsIdle()
        {
            var engine = CreateEngine();
            Assert.Equal(PlaybackState.Idle, engine.State);
        }

        [Fact]
        public void InitialState_LoopDisabled()
        {
            var engine = CreateEngine();
            Assert.False(engine.Loop);
        }

        [Fact]
        public void InitialState_SpeedIsOne()
        {
            var engine = CreateEngine();
            Assert.Equal(1.0, engine.Speed);
        }

        // ═══════ LoadTimeline null 检测 ═══════

        [Fact]
        public void LoadTimeline_NullTimeline_ReturnsFalse()
        {
            var engine = CreateEngine();
            Assert.False(engine.LoadTimeline(null!));
        }

        // ═══════ Play / Pause / Resume / Stop 状态机 ═══════

        [Fact]
        public void Play_WithoutLoad_StaysIdle()
        {
            var engine = CreateEngine();
            engine.Play();
            Assert.Equal(PlaybackState.Idle, engine.State);
        }

        [Fact]
        public void Play_AfterLoad_TransitionsToPlaying()
        {
            var engine = CreateEngineWith(MotionTestFactory.CreateMinimalTimeline());
            engine.Play();
            Assert.Equal(PlaybackState.Playing, engine.State);
        }

        [Fact]
        public void Pause_WhilePlaying_TransitionsToPaused()
        {
            var engine = CreateEngineWith(MotionTestFactory.CreateMinimalTimeline());
            engine.Play();
            engine.Pause();
            Assert.Equal(PlaybackState.Paused, engine.State);
        }

        [Fact]
        public void Pause_WhileIdle_StaysIdle()
        {
            var engine = CreateEngineWith(MotionTestFactory.CreateMinimalTimeline());
            engine.Pause(); // 未 Play 就 Pause
            Assert.Equal(PlaybackState.Idle, engine.State);
        }

        [Fact]
        public void Resume_AfterPause_TransitionsToPlaying()
        {
            var engine = CreateEngineWith(MotionTestFactory.CreateMinimalTimeline());
            engine.Play();
            engine.Pause();
            engine.Resume();
            Assert.Equal(PlaybackState.Playing, engine.State);
        }

        [Fact]
        public void Resume_WhileIdle_StaysIdle()
        {
            var engine = CreateEngineWith(MotionTestFactory.CreateMinimalTimeline());
            engine.Resume();
            Assert.Equal(PlaybackState.Idle, engine.State);
        }

        [Fact]
        public void Stop_WhilePlaying_TransitionsToIdle()
        {
            var engine = CreateEngineWith(MotionTestFactory.CreateMinimalTimeline());
            engine.LiveOutputToDevice = false; // 无设备 → 直接 Idle (不经过 Stopping)
            engine.Play();
            engine.Stop();
            Assert.Equal(PlaybackState.Idle, engine.State);
        }

        [Fact]
        public void Stop_WhileIdle_RemainsIdle()
        {
            var engine = CreateEngineWith(MotionTestFactory.CreateMinimalTimeline());
            engine.Stop();
            Assert.Equal(PlaybackState.Idle, engine.State);
        }

        [Fact]
        public void ForceStop_AnyState_TransitionsToIdle()
        {
            var engine = CreateEngineWith(MotionTestFactory.CreateMinimalTimeline());
            engine.Play();
            engine.ForceStop();
            Assert.Equal(PlaybackState.Idle, engine.State);
        }

        // ═══════ StateChanged 事件 ═══════

        [Fact]
        public void StateChanged_FiresOnTransition()
        {
            var engine = CreateEngineWith(MotionTestFactory.CreateMinimalTimeline());

            var transitions = new List<(PlaybackState from, PlaybackState to)>();
            engine.StateChanged += (from, to) => transitions.Add((from, to));

            engine.Play();
            engine.Pause();
            engine.Resume();
            engine.Stop();

            Assert.Equal(4, transitions.Count);
            Assert.Equal((PlaybackState.Idle, PlaybackState.Playing), transitions[0]);
            Assert.Equal((PlaybackState.Playing, PlaybackState.Paused), transitions[1]);
            Assert.Equal((PlaybackState.Paused, PlaybackState.Playing), transitions[2]);
            Assert.Equal((PlaybackState.Playing, PlaybackState.Idle), transitions[3]);
        }

        // ═══════ Seek ═══════

        [Fact]
        public void Seek_FiresPositionChanged()
        {
            var engine = CreateEngineWith(MotionTestFactory.CreateMinimalTimeline());

            double? reported = null;
            engine.PositionChanged += pos => reported = pos;

            engine.Seek(2500);
            Assert.Equal(2500, reported);
        }

        [Fact]
        public void Seek_FiresLiveValues()
        {
            var timeline = MotionTestFactory.CreateMinimalTimeline(5000);
            var engine = CreateEngineWith(timeline);

            IReadOnlyList<(string, string, float)>? batch = null;
            engine.LiveValuesBatchUpdated += b => batch = b;

            engine.Seek(2500); // 中点 — value=1.0 (线性 0→1)

            Assert.NotNull(batch);
            Assert.Single(batch!);
            Assert.Equal("TestDevice", batch![0].Item1);
            Assert.Equal("TestAction", batch[0].Item2);
            Assert.Equal(1.0f, batch[0].Item3, precision: 2);
        }

        // ═══════ Seek 求值精度 ═══════

        [Fact]
        public void Seek_LinearTrack_CorrectInterpolation()
        {
            // 0ms=0.0, 2500ms=1.0, 5000ms=0.0
            var timeline = MotionTestFactory.CreateMinimalTimeline(5000);
            var engine = CreateEngineWith(timeline);

            var values = new List<float>();
            engine.LiveValuesBatchUpdated += b =>
            {
                if (b.Count > 0)
                    values.Add(b[0].Item3);
            };

            // 测试关键点
            engine.Seek(0); // 起点 = 0.0
            engine.Seek(1250); // 1/4 处 = 0.5
            engine.Seek(2500); // 中点 = 1.0
            engine.Seek(3750); // 3/4 处 = 0.5
            engine.Seek(5000); // 终点 = 0.0

            Assert.Equal(5, values.Count);
            Assert.Equal(0.0f, values[0], precision: 2);
            Assert.Equal(0.5f, values[1], precision: 2);
            Assert.Equal(1.0f, values[2], precision: 2);
            Assert.Equal(0.5f, values[3], precision: 2);
            Assert.Equal(0.0f, values[4], precision: 2);
        }

        [Fact]
        public void Seek_BoolTrack_StepEvaluation()
        {
            var timeline = MotionTestFactory.Timeline(
                "BoolTest",
                5000,
                MotionTestFactory.BoolTrack(
                    "Dev",
                    "Switch",
                    "开关",
                    MotionTestFactory.FullClip(
                        5000,
                        MotionTestFactory.StepKf(0, 0f),
                        MotionTestFactory.StepKf(1000, 1f),
                        MotionTestFactory.StepKf(3000, 0f)
                    )
                )
            );
            var engine = CreateEngineWith(timeline);

            var values = new List<float>();
            engine.LiveValuesBatchUpdated += b =>
            {
                if (b.Count > 0)
                    values.Add(b[0].Item3);
            };

            engine.Seek(0); // 0ms: value=0 → bool=0
            engine.Seek(500); // 500ms: still 0 (step 未到 1000)
            engine.Seek(1000); // 1000ms: value=1 → bool=1
            engine.Seek(2000); // 2000ms: still 1 (step 未到 3000)
            engine.Seek(3000); // 3000ms: value=0 → bool=0
            engine.Seek(4000); // 4000ms: still 0

            Assert.Equal(6, values.Count);
            Assert.Equal(0f, values[0]); // t=0
            Assert.Equal(0f, values[1]); // t=500
            Assert.Equal(1f, values[2]); // t=1000
            Assert.Equal(1f, values[3]); // t=2000
            Assert.Equal(0f, values[4]); // t=3000
            Assert.Equal(0f, values[5]); // t=4000
        }

        // ═══════ Solo / Mute ═══════

        [Fact]
        public void Seek_MutedTrack_NotInBatch()
        {
            var timeline = MotionTestFactory.CreateSoloMuteTimeline();
            timeline.Tracks[0].Muted = true; // Mute 通道1
            var engine = CreateEngineWith(timeline);

            IReadOnlyList<(string, string, float)>? batch = null;
            engine.LiveValuesBatchUpdated += b => batch = b;

            engine.Seek(0);

            Assert.NotNull(batch);
            // 通道1 被 Mute → 仅输出通道2 和通道3 (共 2 条)
            Assert.Equal(2, batch!.Count);
            Assert.Equal("Ch2", batch[0].Item2);
            Assert.Equal("Ch3", batch[1].Item2);
        }

        [Fact]
        public void Seek_SoloTrack_OnlySoloInBatch()
        {
            var timeline = MotionTestFactory.CreateSoloMuteTimeline();
            timeline.Tracks[1].Solo = true; // Solo 通道2
            var engine = CreateEngineWith(timeline);

            IReadOnlyList<(string, string, float)>? batch = null;
            engine.LiveValuesBatchUpdated += b => batch = b;

            engine.Seek(0);

            Assert.NotNull(batch);
            // 通道2 Solo → 仅输出通道2
            Assert.Single(batch!);
            Assert.Equal("Ch2", batch[0].Item2);
        }

        [Fact]
        public void Seek_DisabledTrack_NotInBatch()
        {
            var timeline = MotionTestFactory.CreateSoloMuteTimeline();
            timeline.Tracks[2].Enabled = false; // 禁用通道3
            var engine = CreateEngineWith(timeline);

            IReadOnlyList<(string, string, float)>? batch = null;
            engine.LiveValuesBatchUpdated += b => batch = b;

            engine.Seek(0);

            Assert.NotNull(batch);
            Assert.Equal(2, batch!.Count);
            Assert.Equal("Ch1", batch[0].Item2);
            Assert.Equal("Ch2", batch[1].Item2);
        }

        // ═══════ Speed ═══════

        [Fact]
        public void Speed_ClampsToMinimum()
        {
            var engine = CreateEngine();
            engine.Speed = -5;
            Assert.True(engine.Speed >= 0.01);
        }

        [Fact]
        public void Speed_AcceptsValidValues()
        {
            var engine = CreateEngine();
            engine.Speed = 2.0;
            Assert.Equal(2.0, engine.Speed);
        }

        // ═══════ Loop ═══════

        [Fact]
        public void Loop_CanBeToggled()
        {
            var engine = CreateEngine();
            engine.Loop = true;
            Assert.True(engine.Loop);
            engine.Loop = false;
            Assert.False(engine.Loop);
        }

        // ═══════ EnsureTimelineLoaded ═══════

        [Fact]
        public void EnsureTimelineLoaded_SetsReference_WhenNull()
        {
            var engine = CreateEngine();
            var timeline = MotionTestFactory.CreateMinimalTimeline();
            engine.EnsureTimelineLoaded(timeline);

            // 验证 Seek 可以工作 (说明 timeline 引用已设置)
            double? pos = null;
            engine.PositionChanged += p => pos = p;
            engine.Seek(100);
            Assert.Equal(100, pos);
        }

        // ═══════ 多轨道混合求值 ═══════

        [Fact]
        public void Seek_MultiTrack_AllTracksEvaluated()
        {
            var timeline = MotionTestFactory.CreateExtDevTestTimeline(10000);
            var engine = CreateEngineWith(timeline);

            IReadOnlyList<(string deviceName, string channelName, float value)>? batch = null;
            engine.LiveValuesBatchUpdated += b => batch = b;

            engine.Seek(5000);

            Assert.NotNull(batch);
            Assert.Equal(4, batch!.Count); // 4 条轨道

            // 验证每条轨道都有输出
            var channelNames = new HashSet<string>();
            foreach (var (_, ch, _) in batch)
                channelNames.Add(ch);

            Assert.Contains("Light", channelNames);
            Assert.Contains("Fire", channelNames);
            Assert.Contains("OAxis_00", channelNames); // MoveX 的 oaxis channel
            Assert.Contains("OAxis_01", channelNames); // MoveY 的 oaxis channel
        }

        [Fact]
        public void Seek_ExtDevTimeline_ValuesInRange()
        {
            var timeline = MotionTestFactory.CreateExtDevTestTimeline(10000);
            var engine = CreateEngineWith(timeline);

            var allValues = new List<float>();
            engine.LiveValuesBatchUpdated += b =>
            {
                foreach (var (_, _, v) in b)
                    allValues.Add(v);
            };

            // 在多个时间点求值
            for (double t = 0; t <= 10000; t += 500)
                engine.Seek(t);

            // 所有值应在 0~1 范围内
            foreach (var v in allValues)
                Assert.InRange(v, 0f, 1f);
        }
    }
}
