using System;
using System.Collections.Generic;
using IOStudio.Models.Motion;

namespace IOStudio.Tests.Helpers
{
    /// <summary>
    /// Motion 测试数据构造工厂 — 提供可复用的 Timeline/Track/Clip/Keyframe 构造方法
    /// </summary>
    public static class MotionTestFactory
    {
        // ═══════ 关键帧 ═══════

        /// <summary>创建线性关键帧</summary>
        public static MotionKeyframe LinearKf(double timeMs, float value) =>
            new()
            {
                TimeMs = timeMs,
                Value = value,
                Interpolation = "linear"
            };

        /// <summary>创建阶梯关键帧</summary>
        public static MotionKeyframe StepKf(double timeMs, float value) =>
            new()
            {
                TimeMs = timeMs,
                Value = value,
                Interpolation = "step"
            };

        /// <summary>创建贝塞尔关键帧</summary>
        public static MotionKeyframe BezierKf(
            double timeMs,
            float value,
            float? tangentIn = null,
            float? tangentOut = null,
            float? cp1x = null,
            float? cp2x = null
        ) =>
            new()
            {
                TimeMs = timeMs,
                Value = value,
                Interpolation = "bezier",
                TangentIn = tangentIn,
                TangentOut = tangentOut,
                Cp1x = cp1x,
                Cp2x = cp2x
            };

        /// <summary>创建缓入缓出关键帧</summary>
        public static MotionKeyframe EaseKf(double timeMs, float value) =>
            new()
            {
                TimeMs = timeMs,
                Value = value,
                Interpolation = "ease_in_out"
            };

        // ═══════ Clip ═══════

        /// <summary>创建包含指定关键帧的 Clip</summary>
        public static MotionClip Clip(
            double startMs,
            double endMs,
            params MotionKeyframe[] keyframes
        ) =>
            new()
            {
                StartMs = startMs,
                EndMs = endMs,
                Keyframes = new List<MotionKeyframe>(keyframes)
            };

        /// <summary>创建从 0 开始的单 Clip (最常见场景)</summary>
        public static MotionClip FullClip(double durationMs, params MotionKeyframe[] keyframes) =>
            Clip(0, durationMs, keyframes);

        // ═══════ 轨道 ═══════

        /// <summary>创建 Float OAction 轨道</summary>
        public static MotionTrack FloatTrack(
            string device,
            string oaction,
            string label,
            params MotionClip[] clips
        ) =>
            new()
            {
                DeviceName = device,
                OActionName = oaction,
                Label = label,
                ValueType = "float",
                OutputType = "oaction",
                Clips = new List<MotionClip>(clips)
            };

        /// <summary>创建 Bool OAction 轨道</summary>
        public static MotionTrack BoolTrack(
            string device,
            string oaction,
            string label,
            params MotionClip[] clips
        ) =>
            new()
            {
                DeviceName = device,
                OActionName = oaction,
                Label = label,
                ValueType = "bool",
                OutputType = "oaction",
                Clips = new List<MotionClip>(clips)
            };

        /// <summary>创建 OAxis 轨道</summary>
        public static MotionTrack OAxisTrack(
            string device,
            string oaction,
            string oaxisChannel,
            string label,
            params MotionClip[] clips
        ) =>
            new()
            {
                DeviceName = device,
                OActionName = oaction,
                OAxisChannel = oaxisChannel,
                OutputType = "oaxis",
                Label = label,
                ValueType = "float",
                Clips = new List<MotionClip>(clips)
            };

        // ═══════ 时间轴 ═══════

        /// <summary>创建包含指定轨道的时间轴</summary>
        public static MotionTimeline Timeline(
            string name,
            double durationMs,
            params MotionTrack[] tracks
        ) =>
            new()
            {
                Name = name,
                DurationMs = durationMs,
                Tracks = new List<MotionTrack>(tracks)
            };

        /// <summary>创建带事件和标记的完整时间轴</summary>
        public static MotionTimeline TimelineWithEvents(
            string name,
            double durationMs,
            List<MotionTrack> tracks,
            List<TimelineEvent>? events = null,
            List<TimelineMarker>? markers = null,
            List<TrackGroup>? groups = null
        ) =>
            new()
            {
                Name = name,
                DurationMs = durationMs,
                Tracks = tracks,
                Events = events ?? new List<TimelineEvent>(),
                Markers = markers,
                Groups = groups
            };

        // ═══════ 事件 / 标记 ═══════

        public static TimelineEvent Event(double timeMs, string name, string? data = null) =>
            new()
            {
                TimeMs = timeMs,
                EventName = name,
                EventData = data
            };

        public static TimelineMarker Marker(double timeMs, string name, string? note = null) =>
            new()
            {
                TimeMs = timeMs,
                Name = name,
                Note = note
            };

        // ═══════ 预置场景 ═══════

        /// <summary>
        /// 创建基于 IODevice.xml ExtDev 设备的测试时间轴 —
        /// 包含 OAction Light 轨道 + MoveX/MoveY OAxis 轨道 + Fire Bool 轨道
        /// </summary>
        public static MotionTimeline CreateExtDevTestTimeline(double durationMs = 10000)
        {
            return TimelineWithEvents(
                "ExtDev_Test",
                durationMs,
                tracks: new List<MotionTrack>
                {
                    // OAction: Light (氛围灯) — Float 连续轨道
                    FloatTrack(
                        "ExtDev",
                        "Light",
                        "氛围灯",
                        FullClip(
                            durationMs,
                            LinearKf(0, 0.0f),
                            EaseKf(2000, 0.8f),
                            BezierKf(5000, 0.3f, tangentIn: 0.2f, tangentOut: -0.1f),
                            LinearKf(8000, 0.7f),
                            LinearKf(durationMs, 0.5f)
                        )
                    ),
                    // OAction: Fire (开火状态) — Bool 开关轨道
                    BoolTrack(
                        "ExtDev",
                        "Fire",
                        "开火状态",
                        FullClip(
                            durationMs,
                            StepKf(0, 0f),
                            StepKf(1000, 1f),
                            StepKf(2000, 0f),
                            StepKf(4000, 1f),
                            StepKf(6000, 0f),
                            StepKf(8000, 1f),
                            StepKf(9000, 0f)
                        )
                    ),
                    // OAxis: MoveX (左右移动) — Float 连续轨道
                    OAxisTrack(
                        "ExtDev",
                        "MoveX",
                        "OAxis_00",
                        "左右移动",
                        FullClip(
                            durationMs,
                            LinearKf(0, 0.5f),
                            LinearKf(2500, 0.9f),
                            EaseKf(5000, 0.1f),
                            LinearKf(7500, 0.8f),
                            LinearKf(durationMs, 0.5f)
                        )
                    ),
                    // OAxis: MoveY (前后移动) — Float 连续轨道
                    OAxisTrack(
                        "ExtDev",
                        "MoveY",
                        "OAxis_01",
                        "前后移动",
                        FullClip(
                            durationMs,
                            LinearKf(0, 0.5f),
                            BezierKf(3000, 0.7f, tangentOut: 0.3f),
                            EaseKf(6000, 0.2f),
                            LinearKf(durationMs, 0.5f)
                        )
                    ),
                },
                events: new List<TimelineEvent>
                {
                    Event(1000, "fire_start", "{\"type\":\"weapon\",\"id\":1}"),
                    Event(2000, "fire_stop"),
                    Event(4000, "fire_start", "{\"type\":\"weapon\",\"id\":2}"),
                    Event(6000, "fire_stop"),
                    Event(8000, "fire_start", "{\"type\":\"weapon\",\"id\":3}"),
                    Event(9000, "fire_stop"),
                },
                markers: new List<TimelineMarker>
                {
                    Marker(0, "开始"),
                    Marker(5000, "中场"),
                    Marker(durationMs, "结束"),
                }
            );
        }

        /// <summary>
        /// 创建最小测试时间轴 (单轨道、3 关键帧) — 用于单元测试
        /// </summary>
        public static MotionTimeline CreateMinimalTimeline(double durationMs = 5000)
        {
            return Timeline(
                "Minimal_Test",
                durationMs,
                FloatTrack(
                    "TestDevice",
                    "TestAction",
                    "测试轨道",
                    FullClip(
                        durationMs,
                        LinearKf(0, 0f),
                        LinearKf(durationMs / 2, 1f),
                        LinearKf(durationMs, 0f)
                    )
                )
            );
        }

        /// <summary>
        /// 创建多插值类型混合时间轴 — 覆盖所有插值算法
        /// </summary>
        public static MotionTimeline CreateMixedInterpolationTimeline(double durationMs = 8000)
        {
            return Timeline(
                "MixedInterp_Test",
                durationMs,
                FloatTrack(
                    "TestDevice",
                    "Mixed",
                    "混合插值",
                    FullClip(
                        durationMs,
                        LinearKf(0, 0.5f),
                        LinearKf(2000, 0.8f),
                        BezierKf(
                            4000,
                            0.2f,
                            tangentIn: 0.3f,
                            tangentOut: -0.2f,
                            cp1x: 0.25f,
                            cp2x: 0.75f
                        ),
                        StepKf(6000, 0.9f),
                        EaseKf(8000, 0.5f)
                    )
                )
            );
        }

        /// <summary>
        /// 创建 Solo/Mute 测试时间轴 — 3 条轨道用于测试静音/独奏逻辑
        /// </summary>
        public static MotionTimeline CreateSoloMuteTimeline()
        {
            var t1 = FloatTrack(
                "Dev",
                "Ch1",
                "通道1",
                FullClip(5000, LinearKf(0, 0.1f), LinearKf(5000, 0.1f))
            );
            var t2 = FloatTrack(
                "Dev",
                "Ch2",
                "通道2",
                FullClip(5000, LinearKf(0, 0.2f), LinearKf(5000, 0.2f))
            );
            var t3 = FloatTrack(
                "Dev",
                "Ch3",
                "通道3",
                FullClip(5000, LinearKf(0, 0.3f), LinearKf(5000, 0.3f))
            );

            return Timeline("SoloMute_Test", 5000, t1, t2, t3);
        }
    }
}
