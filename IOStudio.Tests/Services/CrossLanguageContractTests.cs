using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;
using IOToolkit;
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
    }
}
