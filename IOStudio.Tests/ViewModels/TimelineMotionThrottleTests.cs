using Xunit;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using IOStudio.ViewModels.Timeline;

namespace IOStudio.Tests.ViewModels
{
    /// <summary>
    /// 设备下发节流 (随 .motion 走)。
    ///
    /// <para>节流间隔是**每部影片的配置** (dispatch_interval_ms): native MotionPlayer 的帧循环
    /// 默认使用它, 编排端与放映端因此共用同一节流语义。编排端菜单编辑该配置, 并即时作为运行时
    /// 覆盖推给已加载的 Slot (不改变 .motion 配置)。</para>
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
        public void Throttle_Command_Writes_Timeline_And_Engine()
        {
            var vm = new TimelineEditorViewModel(null);

            Assert.Equal(0, vm.MotionDispatchIntervalMs); // 新项目默认不节流
            Assert.True(vm.MotionThrottleNone);

            vm.SetMotionThrottleCommand.Execute("50").Subscribe(); // 20 Hz
            Assert.Equal(50, vm.MotionDispatchIntervalMs);
            Assert.Equal("20 Hz", vm.MotionThrottleText);
            Assert.True(vm.MotionThrottle20);
            Assert.False(vm.MotionThrottleNone);
            Assert.Equal(50, vm.Timeline!.DispatchIntervalMs); // 已写入 .motion 模型
            Assert.Equal(50, vm.EffectiveDispatchIntervalMs);  // 已同步到引擎 (运行时覆盖)

            vm.SetMotionThrottleCommand.Execute("0").Subscribe();
            Assert.True(vm.MotionThrottleNone);
            Assert.Equal(0, vm.Timeline!.DispatchIntervalMs);
            Assert.Equal(0, vm.EffectiveDispatchIntervalMs);
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
