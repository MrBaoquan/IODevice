using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;
using IOToolkit;
using IOStudio.Services.Motion;
using IOStudio.Tests.Helpers;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// 「未送达设备的通道」计数与汇报 —— native 累加, 宿主读取。
    ///
    /// <para><b>为什么需要这一层</b>: <c>DispatchChannel</c> 查不到设备时直接 <c>return</c>,
    /// 输出被<b>静默</b>丢弃 (无日志、无 UI, 状态仍是 <c>playing</c>), 现场只表现为
    /// "动作在走、设备不动" —— 这是最难排查的一种失效。native 现在按<b>完整通道键</b>累加丢弃次数
    /// (不逐帧打日志), 宿主在停播时读一次汇总。</para>
    ///
    /// <para><b>钉住四件事</b>: (1) 通道键与 .motion 写法同形
    /// (<c>设备.oaxis.OAxis_N</c> / <c>设备.oaction名</c>), 人一眼能对上轨道;
    /// (2) 多次 Tick 逐帧累加; (3) 计数按<b>场次</b>统计 —— 起播清零、<b>卸载不清零</b>
    /// (宿主是"停播后读一次", 而停播路径会先 Unload 再读, 在那清零会把统计抹成空);
    /// (4) 宿主侧 (net8) 能把 native 的 JSON 汇总解析成字典。</para>
    ///
    /// <para><b>并行说明</b>: native 的计数表与 Slot 表都是<b>进程级全局</b>, 且起播会清空
    /// 其它用例的计数 —— 必须挂到 <see cref="NativeMotionStateCollection"/> 串行跑
    /// (随机设备名只挡得住"撞名", 挡不住"起播清表")。</para>
    /// </summary>
    [Collection("NativeMotionState")]
    public class UnresolvedChannelReportingTests
    {
        // ═══════ 构造 ═══════

        /// <summary>本用例专用的随机设备名 —— 本机不可能有同名设备实例。</summary>
        private static string UnknownDevice(string tag) =>
            $"Unres-{tag}-" + Guid.NewGuid().ToString("N").Substring(0, 8);

        /// <summary>
        /// 单轨道 .motion。同一段 JSON 里把 <paramref name="channel"/> 同时写进
        /// <c>oaxis_channel</c> 与 <c>oaction</c> —— 通道键由 <c>output_type</c> 决定走哪个,
        /// 这样调用方只需关心"期望的键长什么样"。
        /// </summary>
        private static string TrackJson(string device, string channel, string outputType = "oaxis") =>
            FormattableString.Invariant(
                $@"{{
      ""device"": ""{device}"",
      ""output_type"": ""{outputType}"",
      ""oaxis_channel"": ""{channel}"",
      ""oaction"": ""{channel}"",
      ""value_type"": ""float"",
      ""neutral_value"": 0.5,
      ""clips"": [
        {{
          ""start_ms"": 0,
          ""end_ms"": 1000,
          ""keyframes"": [
            {{ ""time_ms"": 0, ""value"": 0.25, ""interpolation"": ""linear"" }},
            {{ ""time_ms"": 1000, ""value"": 0.75, ""interpolation"": ""linear"" }}
          ]
        }}
      ]
    }}");

        private static string MotionJson(params string[] tracks) =>
            "{\"duration_ms\": 1000, \"tracks\": [" + string.Join(",", tracks) + "]}";

        /// <summary>native 汇总 (原始 JSON 串) → 字典。键不存在即"没丢过"。</summary>
        private static Dictionary<string, long> ReadSummary()
        {
            string json = IOToolkit.MotionPlayer.GetUnresolvedChannelsJson();
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);

            var result = new Dictionary<string, long>();
            foreach (var prop in doc.RootElement.EnumerateObject())
                result[prop.Name] = prop.Value.GetInt64();
            return result;
        }

        /// <summary>Tick 若干帧 (每帧都会派发: 未设 dispatch_interval_ms → 不节流)。</summary>
        private static void TickFrames(int count)
        {
            for (int i = 0; i < count; i++)
                IOToolkit.MotionPlayer.Tick(1f / 60f);
        }

        // ═══════ native: 计数与键形 ═══════

        [Fact]
        public void UnknownDevice_OAxisOutput_CountsPerFrame_WithFileShapedKey()
        {
            string dev = UnknownDevice("axis");
            string expectedKey = dev + ".oaxis.OAxis_6";
            var slot = IOToolkit.MotionPlayer.LoadSlotFromJson(
                "unres_axis_" + dev, MotionJson(TrackJson(dev, "OAxis_6")));
            try
            {
                slot.Play();
                TickFrames(3);

                var summary = ReadSummary();
                Assert.True(
                    summary.ContainsKey(expectedKey),
                    $"未送达通道应按 .motion 里的通道写法成键 ('{expectedKey}'), 实际: {Show(summary)}"
                );
                // 不节流 → 逐帧累加, 3 帧至少 3 次 (同一毫秒内也不会被跳过)
                Assert.True(
                    summary[expectedKey] >= 3,
                    $"逐帧派发应逐帧累加, 3 帧后仅 {summary[expectedKey]}: {Show(summary)}"
                );
            }
            finally
            {
                Cleanup(slot);
            }
        }

        [Fact]
        public void UnknownDevice_OActionOutput_KeyIsDeviceDotOActionName()
        {
            string dev = UnknownDevice("act");
            string expectedKey = dev + ".Light";
            var slot = IOToolkit.MotionPlayer.LoadSlotFromJson(
                "unres_act_" + dev, MotionJson(TrackJson(dev, "Light", outputType: "oaction")));
            try
            {
                slot.Play();
                TickFrames(1);

                var summary = ReadSummary();
                Assert.True(
                    summary.ContainsKey(expectedKey),
                    $"oaction 轨的键应是 '设备.oaction名' ('{expectedKey}'), 实际: {Show(summary)}"
                );
            }
            finally
            {
                Cleanup(slot);
            }
        }

        [Fact]
        public void MultipleUnknownChannels_AllCountedSeparately()
        {
            // 现场形态: 一份动作有 8 条轨道指向同一台没建出来的设备 —— 逐通道成键才能看出
            // "是哪几个通道没送出去", 而不是只有一个笼统的总数。
            string dev = UnknownDevice("ydl");
            string[] channels = { "OAxis_6", "OAxis_16", "OAxis_18", "OAxis_20" };
            string expectedPrefix = dev + ".oaxis.";

            var slot = IOToolkit.MotionPlayer.LoadSlotFromJson(
                "unres_multi_" + dev,
                MotionJson(channels.Select(ch => TrackJson(dev, ch)).ToArray()));
            try
            {
                slot.Play();
                TickFrames(1);

                var summary = ReadSummary();
                foreach (string ch in channels)
                {
                    Assert.True(
                        summary.ContainsKey(expectedPrefix + ch),
                        $"通道 {ch} 应独立成键, 实际: {Show(summary)}"
                    );
                }
            }
            finally
            {
                Cleanup(slot);
            }
        }

        // ═══════ native: 清零点 (按场次统计) ═══════

        [Fact]
        public void ResetUnresolvedCounters_ClearsSummary()
        {
            string dev = UnknownDevice("reset");
            var slot = IOToolkit.MotionPlayer.LoadSlotFromJson(
                "unres_reset_" + dev, MotionJson(TrackJson(dev, "OAxis_7")));
            try
            {
                slot.Play();
                TickFrames(1);
                Assert.NotEmpty(ReadSummary());

                IOToolkit.MotionPlayer.ResetUnresolvedCounters();
                Assert.Empty(ReadSummary());
            }
            finally
            {
                Cleanup(slot);
            }
        }

        [Fact]
        public void NextPlaySession_StartsEmpty_ButUnloadKeepsPreviousSession()
        {
            string first = UnknownDevice("sess1");
            string second = UnknownDevice("sess2");
            string firstKey = first + ".oaxis.OAxis_8";
            string secondKey = second + ".oaxis.OAxis_9";

            var slotA = IOToolkit.MotionPlayer.LoadSlotFromJson(
                "unres_sess_a_" + first, MotionJson(TrackJson(first, "OAxis_8")));
            var slotB = IOToolkit.MotionPlayer.LoadSlotFromJson(
                "unres_sess_b_" + second, MotionJson(TrackJson(second, "OAxis_9")));
            try
            {
                // 场次 1
                slotA.Play();
                TickFrames(1);
                Assert.True(ReadSummary().ContainsKey(firstKey));

                // 卸载**不**清零: 宿主是"停播后读一次", 而停播路径 (ForceStop) 会先 Unload
                // 再读汇总 —— 若在 Unload 里清零, 宿主永远读到空, 现场就继续静默。
                slotA.Unload();
                Assert.True(
                    ReadSummary().ContainsKey(firstKey),
                    "卸载不得清空本场次统计 (宿主会在 Unload 之后才读汇总)"
                );

                // 场次 2: 起播清零 → 上一场的键消失, 本场次的键出现
                slotB.Play();
                TickFrames(1);
                var summary = ReadSummary();
                Assert.False(
                    summary.ContainsKey(firstKey),
                    $"起播应清零上一场次的统计, 实际: {Show(summary)}"
                );
                Assert.True(summary.ContainsKey(secondKey), $"本场次通道应被计数, 实际: {Show(summary)}");
            }
            finally
            {
                try { slotA.Unload(); } catch { /* 可能已卸载 */ }
                Cleanup(slotB);
            }
        }

        // ═══════ 宿主 (net8): JSON 汇总 → 字典 ═══════

        [Fact]
        public void Engine_AfterTick_ExposesUnresolvedChannels()
        {
            string dev = UnknownDevice("eng");
            string expectedKey = dev + ".oaxis.OAxis_10";

            var timeline = MotionTestFactory.Timeline(
                "Unresolved_Test",
                1000,
                MotionTestFactory.OAxisTrack(
                    dev,
                    "MoveX",
                    "OAxis_10",
                    "未送达轨道",
                    MotionTestFactory.FullClip(
                        1000,
                        MotionTestFactory.LinearKf(0, 0.0f),
                        MotionTestFactory.LinearKf(1000, 1.0f)
                    )
                )
            );

            var engine = new NativeMotionPlaybackEngine(new DeviceDispatcher())
            {
                LiveOutputToDevice = true, // 关掉时 native 根本不派发, 计数必为空
            };

            try
            {
                Assert.True(engine.LoadTimeline(timeline));

                engine.Play();
                engine.Tick(); // 把当前时间推给 native 并驱动一次 Tick → 派发 → 计数

                var unresolved = engine.UnresolvedChannels;
                Assert.True(
                    unresolved.ContainsKey(expectedKey),
                    $"宿主应把 native 的 JSON 汇总解析成通道字典, 实际: {Show(unresolved)}"
                );
                Assert.True(unresolved[expectedKey] >= 1);
            }
            finally
            {
                engine.ForceStop();
                IOToolkit.MotionPlayer.ResetUnresolvedCounters();
                engine.Dispose();
            }
        }

        [Fact]
        public void Engine_WithoutPlay_ExposesNoUnresolvedChannels()
        {
            // 不驱动 native 就不该有"未送达" —— 否则"设备不动"会被误报成绑定问题,
            // 而真实原因是"设备输出没开" (那是设置问题, 另有提示)。
            string dev = UnknownDevice("quiet");
            var timeline = MotionTestFactory.Timeline(
                "Unresolved_Quiet",
                1000,
                MotionTestFactory.OAxisTrack(
                    dev,
                    "MoveX",
                    "OAxis_11",
                    "未派发轨道",
                    MotionTestFactory.FullClip(1000, MotionTestFactory.LinearKf(0, 0.0f))
                )
            );

            var engine = new NativeMotionPlaybackEngine(new DeviceDispatcher());
            try
            {
                Assert.True(engine.LoadTimeline(timeline));
                // 本用例不驱动 native, 也就没有"起播清零"这一步 —— 先手动清掉前面用例留下的
                // 计数, 让断言只反映"本用例有没有派发过"。
                IOToolkit.MotionPlayer.ResetUnresolvedCounters();

                // LiveOutputToDevice 保持默认 false (纯预览)
                engine.Play();
                engine.Tick();

                Assert.Empty(engine.UnresolvedChannels);
            }
            finally
            {
                engine.ForceStop();
                IOToolkit.MotionPlayer.ResetUnresolvedCounters();
                engine.Dispose();
            }
        }

        // ═══════ 收尾 ═══════

        private static void Cleanup(MotionSlot? slot)
        {
            try { slot?.Unload(); } catch { /* ignore */ }
            try { IOToolkit.MotionPlayer.ResetUnresolvedCounters(); } catch { /* ignore */ }
        }

        private static string Show<TValue>(IReadOnlyDictionary<string, TValue> map) =>
            map.Count == 0
                ? "{}"
                : "{" + string.Join(", ", map.Select(kv => $"{kv.Key}={kv.Value}")) + "}";
    }
}
