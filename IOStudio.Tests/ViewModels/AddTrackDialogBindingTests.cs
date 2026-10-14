using System.Collections.Generic;
using System.Linq;
using Xunit;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using IOStudio.ViewModels.Timeline;

namespace IOStudio.Tests.ViewModels
{
    /// <summary>
    /// 「编辑轨道属性」对话框对**设备表里没有的**设备/输出的处理。
    ///
    /// <para><b>守的是什么</b>: 打开对话框、什么都不改、按保存 —— 设备名与输出名必须原样写回。
    /// 曾经的实现里, 匹配不上候选列表就<b>什么都不做</b> (下拉停在第一个候选项上),
    /// 于是这一次"只看了一眼"的编辑会把内容悄悄改掉: 设备被换成表里第一个设备,
    /// 或者 OAction 名被换成下拉里的第一个动作。这类改写没有任何提示, 属于最难查的那种内容损坏。</para>
    ///
    /// <para>判据用注入的 <see cref="IDeviceSchemaService"/>, 不碰真实的
    /// <c>Config/IODevice.xml</c> —— 所以本类<b>不需要</b> <c>NativeMotionState</c> 串行化,
    /// 也不会被别的用例换表影响 (对比 <c>TimelineDeviceBindingTests</c>)。</para>
    /// </summary>
    public class AddTrackDialogBindingTests
    {
        private sealed class StubSchemaService : IDeviceSchemaService
        {
            private readonly List<DeviceSchemaInfo> _devices;

            public StubSchemaService(params DeviceSchemaInfo[] devices) => _devices = devices.ToList();

            public List<DeviceSchemaInfo> LoadDevices() => _devices;
        }

        private static DeviceSchemaInfo Device(string name, params (string Action, string Channel)[] actions)
        {
            var device = new DeviceSchemaInfo { DeviceName = name };
            foreach (var (action, channel) in actions)
            {
                device.OActions.Add(
                    new OActionInfo
                    {
                        DeviceName = name,
                        OActionName = action,
                        Label = action,
                        DisplayName = $"{action} ({action})  [{channel}]",
                        KeyNames = new List<string> { channel },
                    }
                );
            }
            for (int i = 0; i <= 31; i++)
            {
                device.OAxisChannels.Add(
                    new OAxisChannelInfo
                    {
                        DeviceName = name,
                        ChannelName = $"OAxis_{i:D2}",
                        DisplayName = $"OAxis_{i:D2}  (通道 {i})",
                    }
                );
            }
            return device;
        }

        private static AddTrackDialogViewModel Vm(params DeviceSchemaInfo[] devices) =>
            new(new StubSchemaService(devices));

        private static MotionTrack Track(
            string device,
            string outputType,
            string? channel = null,
            string? oaction = null
        ) =>
            new()
            {
                Id = "t1",
                DeviceName = device,
                OutputType = outputType,
                OAxisChannel = channel ?? "",
                OActionName = oaction ?? "",
                Label = "标签",
                ValueType = "float",
            };

        private static readonly DeviceSchemaInfo Ydl = Device(
            "YDL-0",
            ("Heave", "OAxis_6"),
            ("Wind", "OAxis_18")
        );

        // ═══════ 输出名 ═══════

        [Fact]
        public void Edit_UndeclaredOAction_KeepsItsNameOnSave()
        {
            // 设备在表里, 但它这条轨道的动作不在 —— 保存后动作名必须还是 surge
            var vm = Vm(Ydl);
            vm.LoadFromTrack(Track("YDL-0", "oaction", oaction: "surge"));

            var result = vm.BuildResult();

            Assert.Equal("YDL-0", result.DeviceName);
            Assert.Equal("surge", result.OActionName);
            Assert.Equal("oaction", result.OutputType);
        }

        [Fact]
        public void Edit_UndeclaredOAction_EchoesTheRealValueInTheList()
        {
            // 回显项要带标记: 让它和真候选在下拉里长得一样, 用户就分不出这一项没被设备表声明
            var vm = Vm(Ydl);
            vm.LoadFromTrack(Track("YDL-0", "oaction", oaction: "surge"));

            Assert.Equal("surge（设备表未声明）", vm.SelectedOutputDisplay);
            Assert.Contains("surge（设备表未声明）", vm.OutputItems);
            // 真候选也还在, 用户可以主动改成一个声明过的动作
            Assert.Contains(vm.OutputItems, item => item.Contains("Heave"));
        }

        [Fact]
        public void Edit_UndeclaredOAction_DisplayMarkerNeverLeaksIntoTheModel()
        {
            var vm = Vm(Ydl);
            vm.LoadFromTrack(Track("YDL-0", "oaction", oaction: "surge"));

            var result = vm.BuildResult();

            // 标记文本是给眼睛看的, 绝不能进 .motion
            Assert.DoesNotContain("未声明", result.OActionName);
        }

        [Fact]
        public void Edit_UndeclaredOAxisChannel_KeepsItsChannelOnSave()
        {
            // oaxis 的候选只到 OAxis_31, 但真实内容会用 OAxis_48 ——
            // 匹配不上时不能退化成 OAxis_00
            var vm = Vm(Ydl);
            vm.LoadFromTrack(Track("YDL-0", "oaxis", channel: "OAxis_48"));

            var result = vm.BuildResult();

            Assert.Equal("OAxis_48", result.OAxisChannel);
            Assert.Equal("oaxis", result.OutputType);
        }

        [Fact]
        public void Edit_DeclaredOAction_PicksTheRealCandidate_NotAnEcho()
        {
            // 声明过的照常匹配真候选 (回显是兜底, 不该抢正常路径)
            var vm = Vm(Ydl);
            vm.LoadFromTrack(Track("YDL-0", "oaction", oaction: "Heave"));

            Assert.DoesNotContain("未声明", vm.SelectedOutputDisplay);
            Assert.Equal("Heave", vm.BuildResult().OActionName);
        }

        // ═══════ 设备名 ═══════

        [Fact]
        public void Edit_UndeclaredDevice_KeepsItsNameOnSave()
        {
            var vm = Vm(Ydl);
            vm.LoadFromTrack(Track("Device_1", "oaction", oaction: "Heave"));

            var result = vm.BuildResult();

            // 设备名要被换成表里第一个设备 (YDL-0) 就完蛋了 —— 这是静默改写最严重的一种
            Assert.Equal("Device_1", result.DeviceName);
        }

        [Fact]
        public void Edit_UndeclaredDevice_EchoesTheRealNameInTheList()
        {
            var vm = Vm(Ydl);
            vm.LoadFromTrack(Track("Device_1", "oaction", oaction: "Heave"));

            Assert.Equal("Device_1（设备表未声明）", vm.SelectedDeviceName);
            Assert.Contains("Device_1（设备表未声明）", vm.DeviceNames);
        }

        [Fact]
        public void Edit_UndeclaredDeviceWithUndeclaredOutput_KeepsBoth()
        {
            // 两个都缺 (真实场景: 换了一台设备、表还没跟上) —— 保存后必须一字不差
            var vm = Vm(Ydl);
            vm.LoadFromTrack(Track("Device_1", "oaction", oaction: "surge"));

            var result = vm.BuildResult();

            Assert.Equal("Device_1", result.DeviceName);
            Assert.Equal("surge", result.OActionName);
        }

        [Fact]
        public void Edit_SwitchingAwayAndBack_DoesNotSilentlyRewriteTheOutput()
        {
            // 用户把设备切到 YDL-0 又切回回显项 —— 回来时输出还得是原来那个未声明的 surge,
            // 不能变成 YDL-0 的第一个候选 (那就等于借"切来切去"把内容改掉了)
            var vm = Vm(Ydl);
            vm.LoadFromTrack(Track("Device_1", "oaction", oaction: "surge"));

            vm.SelectedDeviceName = "YDL-0";
            vm.SelectedDeviceName = "Device_1（设备表未声明）";

            Assert.Equal("surge", vm.BuildResult().OActionName);
        }

        // ═══════ 新建模式不受影响 ═══════

        [Fact]
        public void NewTrack_SelectsTheFirstDeclaredOutput()
        {
            // 新建轨道走的是正常路径: 没有"原值"要回显, 默认选第一个候选
            var vm = Vm(Ydl);

            Assert.Equal("YDL-0", vm.SelectedDeviceName);
            Assert.DoesNotContain("未声明", vm.SelectedOutputDisplay);
            Assert.Equal("Heave", vm.BuildResult().OActionName);
        }
        // ═══════ 值显示规则 · 往返不丢 ═══════

        [Fact]
        public void Edit_ValueDisplayFields_SurviveTheRoundTrip()
        {
            // 打开属性对话框、什么都不改、按保存 —— 这三个字段必须原样写回。
            // 漏一个就是"看一眼就把配置清掉", 与本文档开头那类静默改写同源。
            var vm = Vm(Ydl);
            var track = Track("YDL-0", "oaction", oaction: "Heave");
            track.ValueDisplay = "symmetric";
            track.EngineeringRange = 1000f;
            track.NeutralValue = 0.5f;

            vm.LoadFromTrack(track);
            var result = vm.BuildResult();

            Assert.Equal("symmetric", result.ValueDisplay);
            Assert.Equal(1000f, result.EngineeringRange);
            Assert.Equal(0.5f, result.NeutralValue);
        }

        [Fact]
        public void Edit_ValueDisplayIndex_ReflectsTheTrack()
        {
            var vm = Vm(Ydl);
            var track = Track("YDL-0", "oaction", oaction: "Heave");

            track.ValueDisplay = "engineering";
            vm.LoadFromTrack(track);
            Assert.Equal("engineering", vm.SelectedValueDisplayTagForTest());

            // 未声明 = 自动 (下标 0)
            track.ValueDisplay = null;
            vm.LoadFromTrack(track);
            Assert.Null(vm.SelectedValueDisplayTagForTest());
            Assert.Equal(0, vm.ValueDisplayModeIndex);
        }

        [Fact]
        public void Edit_UnknownValueDisplayTag_FallsBackToAuto()
        {
            // 手改 JSON 打错标签不能让这个字段"卡住" —— 回落到自动
            var vm = Vm(Ydl);
            var track = Track("YDL-0", "oaction", oaction: "Heave");
            track.ValueDisplay = "bogus";

            vm.LoadFromTrack(track);

            Assert.Equal(0, vm.ValueDisplayModeIndex);
            Assert.Null(vm.BuildResult().ValueDisplay);
        }

        [Fact]
        public void Edit_EmptyRangeText_MeansUndefined_NotZero()
        {
            // 空输入 = 未声明 (null)。写成 0 会让量程变成"直通", 是两种完全不同的语义。
            var vm = Vm(Ydl);
            vm.LoadFromTrack(Track("YDL-0", "oaction", oaction: "Heave"));

            vm.EngineeringRangeText = "";
            vm.NeutralValueText = "   ";

            var result = vm.BuildResult();
            Assert.Null(result.EngineeringRange);
            Assert.Null(result.NeutralValue);
        }

        [Fact]
        public void NewTrack_CarriesTheChosenDisplayRule()
        {
            var vm = Vm(Ydl);
            vm.ValueDisplayModeIndex = 1; // percent
            vm.EngineeringRangeText = "300";
            vm.NeutralValueText = "0";

            var result = vm.BuildResult();

            Assert.Equal("percent", result.ValueDisplay);
            Assert.Equal(300f, result.EngineeringRange);
            Assert.Equal(0f, result.NeutralValue);
        }

        // ═══════ 撤销往返: 显示规则也必须能回滚 ═══════

        [Fact]
        public void UpdateTrackProperties_UndoRestoresTheDisplayFields()
        {
            // 撤销快照漏了这三个字段的话, "撤销"会把它们抹掉而不是恢复 ——
            // 那是比不改更糟的行为。这里锁住 old/Apply 的往返。
            var vm = Vm(Ydl);
            var track = Track("YDL-0", "oaction", oaction: "Heave");
            track.ValueDisplay = "symmetric";
            track.EngineeringRange = 1000f;
            track.NeutralValue = 0.5f;

            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"displayrule_{Guid.NewGuid():N}.motion"
            );
            Assert.True(
                MotionFileReader.Write(
                    new MotionTimeline
                    {
                        Version = "3.0",
                        Name = "display",
                        DurationMs = 1000,
                        Tracks = new List<MotionTrack> { track },
                    },
                    path
                )
            );

            try
            {
                var editor = new TimelineEditorViewModel(null, new UndoRedoService());
                editor.LoadFromFile(path);
                var trackVm = editor.Tracks.Single();

                editor.UpdateTrackProperties(
                    trackVm,
                    new AddTrackResult
                    {
                        DeviceName = trackVm.DeviceName,
                        OActionName = trackVm.OActionName,
                        OutputType = trackVm.Track.OutputType,
                        Label = trackVm.Label,
                        Color = trackVm.Color,
                        ValueType = trackVm.ValueType,
                        ValueDisplay = "percent",
                        EngineeringRange = 300f,
                        NeutralValue = 0f,
                    }
                );

                Assert.Equal("percent", trackVm.Track.ValueDisplay);
                Assert.Equal(300f, trackVm.Track.EngineeringRange);
                Assert.Equal(0f, trackVm.Track.NeutralValue);

                editor.Undo();

                Assert.Equal("symmetric", trackVm.Track.ValueDisplay);
                Assert.Equal(1000f, trackVm.Track.EngineeringRange);
                Assert.Equal(0.5f, trackVm.Track.NeutralValue);
            }
            finally
            {
                try
                {
                    System.IO.File.Delete(path);
                }
                catch
                {
                    /* ignore */
                }
            }
        }
    }
}
