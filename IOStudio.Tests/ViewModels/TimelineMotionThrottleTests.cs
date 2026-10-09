using Xunit;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using IOStudio.ViewModels.Timeline;

namespace IOStudio.Tests.ViewModels
{
    /// <summary>
    /// 设备下发节流 (动作编辑运行时的输出设置)。
    ///
    /// <para>节流语义分两层: .motion 的 dispatch_interval_ms 是**动作文件自带的默认值**
    /// (native MotionPlayer 帧循环使用, 使编排端与放映端共用同一节流语义); 编辑器的「设备下发节流」
    /// 是**应用级运行时覆盖**, 只推给 native、**不写回** .motion。这里验证: 文件字段往返 + 应用级
    /// 设置只作用于运行时覆盖。</para>
    /// </summary>
    public class TimelineMotionThrottleTests
    {
        [Fact]
        public void Motion_File_RoundTrips_Dispatch_Interval()
        {
            var timeline = new MotionTimeline
            {
                Name = "t",
                DurationMs = 1000,
                DispatchIntervalMs = 50,
            };

            string json = MotionFileReader.ToJson(timeline);
            Assert.Contains("dispatch_interval_ms", json);

            var back = MotionFileReader.ReadFromJson(json);
            Assert.NotNull(back);
            Assert.Equal(50, back!.DispatchIntervalMs);
        }

        [Fact]
        public void Motion_File_Omits_Zero_Dispatch_Interval()
        {
            var timeline = new MotionTimeline { Name = "t", DurationMs = 1000 };

            string json = MotionFileReader.ToJson(timeline);
            Assert.DoesNotContain("dispatch_interval_ms", json);

            var back = MotionFileReader.ReadFromJson(json);
            Assert.NotNull(back);
            Assert.Equal(0, back!.DispatchIntervalMs);
        }

        [Fact]
        public void Throttle_Setting_Is_App_Level_And_Drives_Engine()
        {
            var settings = IOStudio.Models.IOStudioSettings.Instance;
            int original = settings.MotionDispatchIntervalMs;
            try
            {
                var vm = new TimelineEditorViewModel(null);
                int baseTimelineMs = vm.Timeline!.DispatchIntervalMs; // 文件自带配置, 应保持不变

                // 档位: 0=跟随文件 1=不节流 2=40Hz 3=30Hz 4=20Hz 5=15Hz 6=10Hz
                Assert.Equal(7, vm.MotionThrottleOptions.Count);
                Assert.Equal("40 Hz", vm.MotionThrottleOptions[2]);
                Assert.Equal("15 Hz", vm.MotionThrottleOptions[5]);

                vm.MotionThrottleIndex = 4; // 20 Hz
                Assert.Equal(50, vm.MotionDispatchIntervalMs);
                Assert.Equal(50, vm.EffectiveDispatchIntervalMs); // 已同步到引擎 (运行时覆盖)
                Assert.Equal(50, settings.MotionDispatchIntervalMs); // 已持久化到应用设置
                Assert.Equal(baseTimelineMs, vm.Timeline!.DispatchIntervalMs); // 不写回 .motion

                vm.MotionThrottleIndex = 2; // 40 Hz
                Assert.Equal(25, vm.MotionDispatchIntervalMs);
                Assert.Equal(25, vm.EffectiveDispatchIntervalMs);

                vm.MotionThrottleIndex = 5; // 15 Hz
                Assert.Equal(67, vm.MotionDispatchIntervalMs);

                vm.MotionThrottleIndex = 0; // 跟随动作文件 (清除覆盖)
                Assert.Equal(-1, vm.MotionDispatchIntervalMs);
                Assert.Equal(-1, vm.EffectiveDispatchIntervalMs);
                Assert.Equal(baseTimelineMs, vm.Timeline!.DispatchIntervalMs); // 仍不写回 .motion
            }
            finally
            {
                settings.MotionDispatchIntervalMs = original;
            }
        }

        /// <summary>
        /// 端到端验证 native 侧: .motion 的 dispatch_interval_ms 被解析为默认值,
        /// C ABI 运行时覆盖生效且可清除 (&lt;0 → 回到配置), 全程不改变配置。
        /// </summary>
        [Fact]
        public void Native_Slot_Dispatch_Interval_Override_RoundTrips()
        {
            const string slotId = "__ut_throttle__";
            // 必须含至少一条轨道, 否则 native LoadSlotFromJson 直接返回 (不建 Slot)。
            string json =
                "{\"version\":\"3.0\",\"name\":\"ut\",\"duration_ms\":1000,\"fps\":60," +
                "\"dispatch_interval_ms\":33," +
                "\"tracks\":[{\"device\":\"\",\"oaction\":\"\",\"output_type\":\"oaction\"," +
                "\"value_type\":\"float\",\"clips\":[]}]}";

            IOToolkit.MotionSlot? slot = null;
            try
            {
                slot = IOToolkit.MotionPlayer.LoadSlotFromJson(
                    slotId, json, 0, IOToolkit.MixPolicy.Priority);

                // 默认使用 .motion 配置 (native 已解析 dispatch_interval_ms)。
                Assert.Equal(33, slot.DispatchIntervalMs);

                // 运行时覆盖 (不改变 .motion 配置)。
                slot.DispatchIntervalMs = 50;
                Assert.Equal(50, slot.DispatchIntervalMs);

                // <0 = 清除覆盖 → 回到 .motion 配置 33。
                slot.DispatchIntervalMs = -1;
                Assert.Equal(33, slot.DispatchIntervalMs);
            }
            catch (DllNotFoundException) { return; }        // 无 native 环境
            catch (BadImageFormatException) { return; }     // 位数不匹配
            catch (EntryPointNotFoundException) { return; } // 旧 native 未导出
            finally
            {
                slot?.Unload();
            }
        }
    }
}
