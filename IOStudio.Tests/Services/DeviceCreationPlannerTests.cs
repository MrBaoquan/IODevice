using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// 「自动新建设备」的计划生成 —— 从 .motion 反推一台设备该怎么建。
    ///
    /// <para><b>守的是什么</b>: 设备结点里有两样东西 .motion 里根本没有 —— <c>DllName</c>
    /// (驱动插件名) 与 <c>OAction → 通道</c> 的映射。native 侧对 <c>DllName</c> 解析不出来的
    /// External 设备是<b>直接不建</b>, 所以凭空捏造会得到"面板不报警、输出照样丢"的假状态。
    /// 唯一能造出真能用的设备的办法是照抄一台已在工作的设备, 这些用例盯的就是"照抄得对不对"。</para>
    /// </summary>
    public class DeviceCreationPlannerTests
    {
        private static DeviceSchemaInfo Device(
            string name,
            string dllName,
            int index = 0,
            params (string Action, string[] Channels)[] actions
        )
        {
            var device = new DeviceSchemaInfo
            {
                DeviceName = name,
                DllName = dllName,
                Index = index,
            };
            foreach (var (action, channels) in actions)
            {
                device.OActions.Add(
                    new OActionInfo
                    {
                        DeviceName = name,
                        OActionName = action,
                        Label = action,
                        KeyNames = channels.ToList(),
                    }
                );
            }
            return device;
        }

        private static readonly DeviceSchemaInfo Ydl = Device(
            "YDL-0",
            "MP-YDL",
            index: 7,
            ("Heave", new[] { "OAxis_6" }),
            ("Wind", new[] { "OAxis_18" }),
            ("Shoot", new[] { "OAxis_48" })
        );

        private static readonly DeviceSchemaInfo Sim = Device(
            "Simulator-1",
            "Simulator",
            index: 0,
            ("MoveX", new[] { "OAxis_01" })
        );

        // ═══════ 选源 ═══════

        [Fact]
        public void FindSource_PicksTheDeviceWithTheMostOverlappingActions()
        {
            // 设备名对不上时, 靠"用到的动作"找同类设备 —— 这才是能照抄的对象
            var source = DeviceCreationPlanner.FindSource(new[] { Ydl, Sim }, new[] { "Heave", "Wind" });
            Assert.Equal("YDL-0", source?.DeviceName);
        }

        [Fact]
        public void FindSource_IsCaseInsensitive_AndDeterministicOnTies()
        {
            var source = DeviceCreationPlanner.FindSource(new[] { Ydl, Sim }, new[] { "heave" });
            Assert.Equal("YDL-0", source?.DeviceName);

            // 重合度相同时按设备名序 —— 同样的输入必须永远得到同样的建议, 不能随机漂移
            var a = Device("B-1", "AAA", 0, ("X", new[] { "c1" }));
            var b = Device("A-1", "BBB", 0, ("X", new[] { "c2" }));
            Assert.Equal("A-1", DeviceCreationPlanner.FindSource(new[] { a, b }, new[] { "X" })?.DeviceName);
        }

        [Fact]
        public void FindSource_ReturnsNullWhenTableIsEmpty()
        {
            Assert.Null(DeviceCreationPlanner.FindSource(new DeviceSchemaInfo[0], new[] { "Heave" }));
        }

        // ═══════ 建计划 ═══════

        [Fact]
        public void Build_ClonesDriverAndChannelMappings()
        {
            // 关键点: 驱动名与通道都必须是"照抄来的", 不是编出来的
            var plan = DeviceCreationPlanner.Build("YDL-9", new[] { "Heave", "Wind" }, new[] { Ydl, Sim });

            Assert.True(plan.IsViable);
            Assert.Equal("YDL-9", plan.DeviceName);
            Assert.Equal("MP-YDL", plan.DllName);
            Assert.Equal(7, plan.Index);
            Assert.Equal("YDL-0", plan.SourceDeviceName);

            var heave = plan.Actions.Single(a => a.OActionName == "Heave");
            Assert.Equal(new[] { "OAxis_6" }, heave.Channels);
        }

        [Fact]
        public void Build_LeavesOutActionsThatCannotBeGivenChannels()
        {
            // 源设备没有同名动作 → 通道推不出来。这种动作必须<b>不写</b>:
            // 没有 <Key> 的 OAction 在 native 侧会通过存在性判断却一个通道都不下发,
            // 比不建更能骗人 (面板不再报警, 输出照样丢)。
            var plan = DeviceCreationPlanner.Build("YDL-9", new[] { "Heave", "Scent" }, new[] { Ydl });

            Assert.Equal(new[] { "Heave" }, plan.Actions.Select(a => a.OActionName));
            Assert.Equal(new[] { "Scent" }, plan.ActionsWithoutChannels);
        }

        [Fact]
        public void Build_IsNotViableWhenNothingCanBeCloned()
        {
            // 冷启动: 表为空 → 没有驱动可抄。这时不能悄悄造一台 DllName 为空的设备
            // (native 直接不建), 而要让界面接手去问驱动。
            var plan = DeviceCreationPlanner.Build("YDL-9", new[] { "Heave" }, new DeviceSchemaInfo[0]);

            Assert.False(plan.IsViable);
            Assert.True(plan.NeedsDriverChoice);
            Assert.Empty(plan.Actions);
        }

        [Fact]
        public void Build_IsNotViableWhenTheSourceDeviceHasNoDriver()
        {
            // 表里有设备但没写 DllName —— 那台设备本身在 native 侧也建不起来, 不能拿它当样板
            var broken = Device("BROKEN", "", 0, ("Heave", new[] { "OAxis_6" }));
            var plan = DeviceCreationPlanner.Build("YDL-9", new[] { "Heave" }, new[] { broken });

            Assert.False(plan.IsViable);
        }

        [Fact]
        public void Build_CopiesAllChannelsOfAMultiChannelAction()
        {
            var multi = Device("M-1", "MODBUS", 0, ("Both", new[] { "OAxis_6", "OAxis_7" }));
            var plan = DeviceCreationPlanner.Build("M-2", new[] { "Both" }, new[] { multi });

            Assert.Equal(new[] { "OAxis_6", "OAxis_7" }, plan.Actions.Single().Channels);
        }

        [Fact]
        public void BuildFromDriver_KeepsOnlyActionsWithChannels()
        {
            var plan = DeviceCreationPlanner.BuildFromDriver(
                "YDL-9",
                "MP-YDL",
                3,
                new List<OActionCompletion>
                {
                    new() { OActionName = "Heave", Channels = new List<string> { "OAxis_6" } },
                    new() { OActionName = "Empty", Channels = new List<string>() },
                }
            );

            Assert.True(plan.IsViable);
            Assert.Equal("MP-YDL", plan.DllName);
            Assert.Equal(3, plan.Index);
            Assert.Equal(new[] { "Heave" }, plan.Actions.Select(a => a.OActionName));
            Assert.Equal(new[] { "Empty" }, plan.ActionsWithoutChannels);
        }
    }
}
