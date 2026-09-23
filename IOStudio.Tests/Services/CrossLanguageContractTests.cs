using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;
using IOToolkit;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// 跨语言契约测试 — C++ IODevice MotionPlayer (运行时权威) 与 C# TrackValueEvaluator (编辑器镜像)
    /// 必须对同一份 .motion 文档给出<b>逐点一致</b>的求值结果。
    ///
    /// 存在意义: 实播路径是 C# (NativeMotionPlaybackEngine), 而原生/UNIPlayer 路径是 C++。
    /// 两侧若漂移, 编辑器预览与实播就会不同 —— 这类缺陷不会报错, 只会"值不对"。
    ///
    /// 本用例覆盖回归热点: 部分覆盖的 clip + idle 待机循环 + blend 交叉淡化。
    /// (曾出现 "clip 起始 ≠ 0 + 启用 idle 时输出归零" 的疑似缺陷, 见 TrackValueEvaluatorIdleTests)
    ///
    /// 注意: 原生 DLL 不可用时<b>必须失败</b>而不是静默跳过 —— 否则"契约测试全绿"会变成
    /// 假安全感 (测的是一个不参与实播的实现)。
    /// </summary>
    public class CrossLanguageContractTests
    {
        private readonly ITestOutputHelper _out;

        public CrossLanguageContractTests(ITestOutputHelper output) => _out = output;

        /// <summary>覆盖: 纯 idle / blend 淡入 / clip 窗口 / blend 淡出 / 再度 idle</summary>
        private static readonly double[] SampleTimes =
        {
            0, 500, 1500, 2500, 4500,          // 纯 idle 循环
            5700, 5850, 5950,                  // blend 淡入带 (clip.Start - 300 .. clip.Start)
            6000, 6500, 7500, 8800, 9000,      // clip 窗口
            9150, 9300, 9400,                  // blend 淡出带 (clip.End .. clip.End + 300)
            9600, 10500, 13500,                // 再度 idle
        };

        /// <summary>调用原生求值: 原生不可用 → 直接失败 (契约无从验证)。</summary>
        private float[] NativeEvaluate(MotionSlot slot, float timeMs)
        {
            try
            {
                return slot.EvaluateAt(timeMs);
            }
            catch (Exception ex) when (
                ex is DllNotFoundException
                || ex is BadImageFormatException
                || ex is EntryPointNotFoundException
            )
            {
                throw new InvalidOperationException(
                    "原生 IODevice.dll 不可用, 跨语言契约测试无法执行 (非契约违例, 但必须暴露): "
                        + ex.GetType().Name
                        + ": "
                        + ex.Message,
                    ex
                );
            }
        }

        [Fact]
        public void Cpp_And_CSharp_Agree_On_PartialClip_With_Idle()
        {
            var slot = MotionPlayer.LoadSlotFromJson(
                "contract_idle_slot",
                TrackValueEvaluatorIdleTests.ProbeJson
            );
            try
            {
                var timeline = MotionFileReader.ReadFromJson(TrackValueEvaluatorIdleTests.ProbeJson)!;
                var track = timeline.Tracks[0];

                var mismatches = new List<string>();
                int checks = 0;

                foreach (double t in SampleTimes)
                {
                    var values = NativeEvaluate(slot, (float)t);
                    Assert.NotEmpty(values); // 原生侧必须真的解析出了轨道
                    float cpp = values[0];

                    float csharp = TrackValueEvaluator.Evaluate(
                        track.Clips,
                        track.ValueType,
                        track.NeutralValue,
                        track.IdleLoop,
                        track.OverrideKeyframes,
                        t
                    );

                    _out.WriteLine($"t={t,6}ms  cpp={cpp:F4}  csharp={csharp:F4}");
                    checks++;

                    if (Math.Abs(cpp - csharp) > 0.001f)
                        mismatches.Add($"t={t}ms cpp={cpp:F4} csharp={csharp:F4}");
                }

                _out.WriteLine($"native 求值命中 {checks} 个采样点 (证明原生路径确实执行)");
                Assert.Equal(SampleTimes.Length, checks);

                Assert.True(
                    mismatches.Count == 0,
                    "C++ 运行时与 C# 镜像求值不一致:\n  " + string.Join("\n  ", mismatches)
                );
            }
            finally
            {
                slot.Unload();
            }
        }

        [Fact]
        public void Cpp_PartialClip_Window_Is_Not_Neutralized()
        {
            // 单独固化"clip 窗口必须输出 clip 值"——防止 (部分覆盖 + idle) 组合回归为中性/0
            var slot = MotionPlayer.LoadSlotFromJson(
                "contract_clip_window_slot",
                TrackValueEvaluatorIdleTests.ProbeJson
            );
            try
            {
                float[] values = NativeEvaluate(slot, 7500f);
                Assert.NotEmpty(values);
                _out.WriteLine($"native clip-window value @7500ms = {values[0]:F4}");
                Assert.Equal(0.3f, values[0], precision: 3);
            }
            finally
            {
                slot.Unload();
            }
        }

        // ═══════ 随机对拍: 把"镜像"钉死在权威上 ═══════

        /// <summary>
        /// 随机对拍 —— 比"若干手挑采样点"强得多: 随机生成时间轴 (含 idle 的 continuous/restart、
        /// blend、overlay、空窗、边界、步进/缓动/贝塞尔插值), 逐点比对两侧。
        ///
        /// <para>这是后续任何"让 C++ 更权威"改动的前提: 先证明镜像现在真的是准的。</para>
        /// </summary>
        [Fact]
        public void Cpp_And_CSharp_Agree_On_RandomizedTimelines()
        {
            var rnd = new Random(20260923); // 固定种子 → 失败可复现
            // 组合矩阵: 值类型(2) × idle 模式(无|continuous|restart = 3) × blend(2) × overlay(2)
            const int caseCount = 24;
            const int samplesPerCase = 140;
            var report = new List<string>();
            int totalMismatch = 0;

            for (int ci = 0; ci < caseCount; ci++)
            {
                var (timeline, label) = BuildRandomTimeline(rnd, ci);
                string json = MotionFileReader.ToJson(timeline);

                var slot = MotionPlayer.LoadSlotFromJson($"fuzz_{ci}", json);
                try
                {
                    var track = timeline.Tracks[0];
                    int caseMismatch = 0;
                    string firstExample = "";

                    for (int s = 0; s < samplesPerCase; s++)
                    {
                        double t = rnd.NextDouble() * timeline.DurationMs;
                        var native = NativeEvaluate(slot, (float)t);
                        if (native.Length == 0)
                        {
                            report.Add($"case{ci} [{label}] 原生未解析出轨道");
                            break;
                        }

                        float csharp = TrackValueEvaluator.Evaluate(
                            track.Clips,
                            track.ValueType,
                            track.NeutralValue,
                            track.IdleLoop,
                            track.OverrideKeyframes,
                            t
                        );

                        if (Math.Abs(native[0] - csharp) > 1e-3f)
                        {
                            caseMismatch++;
                            if (firstExample.Length == 0)
                                firstExample =
                                    $"首例 t={t:F0}ms native={native[0]:F4} csharp={csharp:F4}";
                        }
                    }

                    totalMismatch += caseMismatch;
                    report.Add(
                        $"case{ci} [{label}] 采样={samplesPerCase} 不一致={caseMismatch}"
                            + (caseMismatch > 0 ? $"  {firstExample}" : "")
                    );
                }
                finally
                {
                    slot.Unload();
                }
            }

            foreach (var line in report)
                _out.WriteLine(line);

            Assert.True(
                totalMismatch == 0,
                $"随机对拍发现 C++(权威) 与 C#(镜像) 不一致, 共 {totalMismatch} 处:\n  "
                    + string.Join("\n  ", report)
            );
        }

        /// <summary>
        /// 随机生成一条"有代表性"的时间轴, 并返回用于诊断的维度标签。
        ///
        /// <para><b>组合由 caseIndex 确定性决定</b> (而不是靠随机撞): 值类型 × idle 模式
        /// (无 / continuous / restart) × 是否有 blend × 是否有 overlay 的完整矩阵 ——
        /// 否则某些组合可能一直没被测到 (之前就漏过 float+continuous+blend)。
        /// 其余 (时长/数量/取值/插值) 仍然随机。</para>
        ///
        /// <para>刻意保证 clip 不重叠 (否则"取第一个命中"的顺序语义会引入无关歧义)。</para>
        /// </summary>
        private static (MotionTimeline Timeline, string Label) BuildRandomTimeline(
            Random rnd,
            int caseIndex
        )
        {
            int selector = caseIndex;
            bool isBool = selector % 2 == 0;
            selector /= 2;
            int idleMode = selector % 3; // 0=无 idle, 1=continuous, 2=restart
            selector /= 3;
            bool wantBlend = selector % 2 == 1;
            selector /= 2;
            bool wantOverlay = selector % 2 == 1;

            var interps = new[] { "linear", "step", "ease_in_out" };
            var parts = new List<string> { isBool ? "bool" : "float" };

            double duration = 6000 + rnd.NextDouble() * 12000;

            var track = new MotionTrack
            {
                Id = $"t{caseIndex}",
                DeviceName = "Simulator-1",
                OutputType = "oaxis",
                OAxisChannel = "OAxis_01",
                Label = $"随机{caseIndex}",
                ValueType = isBool ? "bool" : "float",
                Clips = new List<MotionClip>(),
            };

            float RandValue() =>
                isBool ? (rnd.Next(2) == 0 ? 0f : 1f) : (float)(0.05 + rnd.NextDouble() * 0.9);

            // ── 不重叠 clip (中间留空窗, 让 idle/overlay 有作用域) ──
            double cursor = 0;
            int clipCount = 1 + rnd.Next(3);
            for (int i = 0; i < clipCount; i++)
            {
                cursor += rnd.NextDouble() * 900;
                double span = 600 + rnd.NextDouble() * 2600;
                var clip = new MotionClip { Id = $"c{i}", StartMs = cursor, EndMs = cursor + span };
                int kfCount = 2 + rnd.Next(3);
                for (int k = 0; k < kfCount; k++)
                {
                    clip.Keyframes.Add(
                        new MotionKeyframe
                        {
                            TimeMs = span * k / (kfCount - 1),
                            Value = RandValue(),
                            Interpolation = interps[rnd.Next(interps.Length)],
                        }
                    );
                }
                track.Clips.Add(clip);
                cursor += span;
            }
            parts.Add($"clips={clipCount}");

            // ── 待机循环 (是否存在/相位模式/blend 由矩阵决定, 不是随机撞) ──
            if (idleMode > 0)
            {
                double period = 800 + rnd.NextDouble() * 3000;
                bool blend = wantBlend;
                bool restart = idleMode == 2;
                var idle = new IdleLoop
                {
                    Enabled = true,
                    PeriodMs = period,
                    BlendMs = blend ? rnd.NextDouble() * 400 : 0,
                    PhaseMode = restart ? "restart" : "continuous",
                    PhaseOffsetMs = rnd.NextDouble() < 0.4 ? rnd.NextDouble() * 500 : 0,
                };
                int ikf = 2 + rnd.Next(3);
                for (int k = 0; k < ikf; k++)
                {
                    idle.Keyframes.Add(
                        new MotionKeyframe
                        {
                            TimeMs = period * k / (ikf - 1),
                            Value = RandValue(),
                            Interpolation = interps[rnd.Next(interps.Length)],
                        }
                    );
                }
                track.IdleLoop = idle;
                parts.Add($"idle={idle.PhaseMode}"
                    + (blend ? $"+blend{idle.BlendMs:F0}" : "")
                    + (idle.PhaseOffsetMs > 0 ? $"+off{idle.PhaseOffsetMs:F0}" : ""));
            }

            // ── 空窗覆盖点 (由矩阵决定) ──
            if (wantOverlay)
            {
                int okf = 1 + rnd.Next(3);
                var ov = new List<MotionKeyframe>();
                for (int k = 0; k < okf; k++)
                {
                    ov.Add(
                        new MotionKeyframe
                        {
                            TimeMs = 400 + rnd.NextDouble() * Math.Max(500, duration - 800),
                            Value = RandValue(),
                            Interpolation = "linear",
                        }
                    );
                }
                ov.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
                track.OverrideKeyframes = ov;
                parts.Add($"overlay={okf}");
            }

            var timeline = new MotionTimeline
            {
                Name = $"fuzz{caseIndex}",
                DurationMs = Math.Max(duration, cursor + 200),
                Tracks = new List<MotionTrack> { track },
            };
            return (timeline, string.Join(" ", parts));
        }
    }
}
