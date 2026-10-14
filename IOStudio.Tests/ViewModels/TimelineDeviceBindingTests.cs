using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Xunit;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using IOStudio.ViewModels.Timeline;

namespace IOStudio.Tests.ViewModels
{
    /// <summary>
    /// 设备绑定丢失的检测与修复。
    ///
    /// <para>这类问题在播放期是<b>完全静默</b>的 (native 侧查不到设备直接丢弃输出, 不报错,
    /// 状态仍是 playing), 现场只表现为"动作在走、设备不动"。所以这里断言的重点不是"能不能跑",
    /// 而是<b>该修的都报出来、修复只动该动的东西、且整体可撤销</b>。</para>
    ///
    /// <para>设备表是 <c>&lt;BaseDirectory&gt;/Config/IODevice.xml</c> (native 与 C# 读同一份),
    /// 所以这些用例必须<b>临时替换该文件</b>并还原 —— 与 <c>ProtocolTestUiTests</c> 换工作区文件同理。
    /// 测试进程里它默认是空表, 不替换就全是"未声明"。</para>
    ///
    /// <para>正因为替换的是<b>共享单文件</b>, 本类被显式串行化: 见 <see cref="NativeMotionStateCollection"/>。
    /// 否则设备表会在别的用例读到一半时被换掉, 让那些用例以"设备不存在"的方式莫名其妙地红。</para>
    /// </summary>
    [Collection("NativeMotionState")]
    public class TimelineDeviceBindingTests
    {
        // ═══════ 设备表夹具 ═══════

        private static string TablePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "IODevice.xml");

        /// <summary>YDL-0 声明了 heave/roll 两个 OAction 与两个 OAxis 通道。</summary>
        private const string YdlTable = """
            <?xml version="1.0" encoding="utf-8"?>
            <IORoot>
              <Device Name="YDL-0">
                <OAction Name="heave" Label="升降">
                  <Key Name="heave" />
                </OAction>
                <OAction Name="roll" Label="横滚">
                  <Key Name="roll" />
                </OAction>
              </Device>
              <Device Name="YDL-1">
                <OAction Name="heave" Label="升降">
                  <Key Name="heave" />
                </OAction>
              </Device>
            </IORoot>
            """;

        /// <summary>空表 —— 文件存在但没有任何设备声明 (比"文件缺失"更容易被忽略的形态)。</summary>
        private const string EmptyTable = """<?xml version="1.0" encoding="utf-8"?><IORoot />""";

        /// <summary>
        /// 带注释、且动作名会落到 OAxis_6 的设备表 —— 用来证明补全不会动用户手写的东西。
        /// 注释是这里的重点: 整份重写 (IORoot.Save / XmlSerializer) 会把它们抹掉。
        /// </summary>
        private const string CommentedTable = """
            <?xml version="1.0" encoding="utf-8"?>
            <!-- 手工维护: 通道编号按现场接线表 -->
            <IORoot>
              <!-- YDL 平台 -->
              <Device Name="YDL-0">
                <OAction Name="Heave" Label="电缸升降">
                  <Key Name="OAxis_6" />
                </OAction>
              </Device>
            </IORoot>
            """;

        /// <summary>统一行尾 —— 源码文件的行尾不该决定断言的成败。</summary>
        private static string ToCrlf(string text) => text.Replace("\r\n", "\n").Replace("\n", "\r\n");

        private static string ToLf(string text) => text.Replace("\r\n", "\n");

        /// <summary>临时替换设备表, 结束后还原并让目录缓存失效。</summary>
        private sealed class DeviceTableGuard : IDisposable
        {
            private readonly string? _backup;

            public DeviceTableGuard(string xml)
            {
                var path = TablePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                _backup = File.Exists(path) ? File.ReadAllText(path) : null;
                File.WriteAllText(path, xml);
                MotionDeviceCatalog.Invalidate();
            }

            public void Dispose()
            {
                var path = TablePath;
                if (_backup is null)
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch
                    {
                        /* ignore */
                    }
                }
                else
                {
                    File.WriteAllText(path, _backup);
                }
                MotionDeviceCatalog.Invalidate();
            }
        }

        // ═══════ 时间轴夹具 ═══════

        private static MotionKeyframe Kf(double t, float v) =>
            new()
            {
                TimeMs = t,
                Value = v,
                Interpolation = "linear",
            };

        private static MotionTrack Track(
            string id,
            string device,
            string outputType = "oaxis",
            string? channel = "OAxis_48",
            string? oaction = "heave",
            string? role = "heave",
            string label = "升降"
        )
        {
            var track = new MotionTrack
            {
                Id = id,
                DeviceName = device,
                OutputType = outputType,
                OAxisChannel = channel ?? "",
                OActionName = oaction ?? "",
                Role = role ?? "",
                Label = label,
                ValueType = "float",
            };
            track.Clips.Add(
                new MotionClip
                {
                    Id = $"c_{id}",
                    StartMs = 0,
                    EndMs = 1000,
                    Keyframes = new List<MotionKeyframe> { Kf(0, 0.2f), Kf(1000, 0.8f) },
                }
            );
            return track;
        }

        private static MotionTimeline Timeline(params MotionTrack[] tracks) =>
            new()
            {
                Version = "3.0",
                Name = "binding",
                DurationMs = 1000,
                Tracks = new List<MotionTrack>(tracks),
            };

        private static string WriteTemp(MotionTimeline timeline)
        {
            string path = Path.Combine(Path.GetTempPath(), $"bind_{Guid.NewGuid():N}.motion");
            Assert.True(MotionFileReader.Write(timeline, path));
            return path;
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                /* ignore */
            }
        }

        // ═══════ 收集 (检测) ═══════

        [Fact]
        public void Collect_GroupsByIdentityAndSkipsDeclaredDevices()
        {
            using var _ = new DeviceTableGuard(YdlTable);
            string path = WriteTemp(
                Timeline(
                    Track("t1", "Device_1", label: "升降"),
                    Track("t2", "Device_1", label: "横滚"),
                    Track("t3", "YDL-0", label: "正常轨道")
                )
            );
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);

                var bindings = vm.CollectUnresolvedBindings();

                var b = Assert.Single(bindings); // 已声明的 YDL-0 不该出现
                Assert.Equal("Device_1", b.DeviceName);
                Assert.Equal(2, b.TrackCount);
                Assert.Equal(new[] { "t1", "t2" }, b.TrackIds);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void Collect_DeviceNameMatchingIsCaseInsensitive()
        {
            using var _ = new DeviceTableGuard(YdlTable);
            string path = WriteTemp(Timeline(Track("t1", "ydl-0")));
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                Assert.Empty(vm.CollectUnresolvedBindings());
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void Collect_IgnoresTracksWithoutDeviceName()
        {
            // 空设备名是 track.no_device 的事, 由预检报; 在这里再报一次会变成两条错误
            using var _ = new DeviceTableGuard(YdlTable);
            string path = WriteTemp(Timeline(Track("t1", "")));
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                Assert.Empty(vm.CollectUnresolvedBindings());
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void Collect_DescribesWhatIsAtStake()
        {
            using var _ = new DeviceTableGuard(YdlTable);
            string path = WriteTemp(Timeline(Track("t1", "Device_1", channel: "OAxis_48")));
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);

                var b = Assert.Single(vm.CollectUnresolvedBindings());

                // 「保留通道 … / 不改输出类型」这两句说明靠它们支撑 —— 少一个用户就不敢点应用
                Assert.Equal(new[] { "OAxis_48" }, b.Channels);
                Assert.Equal(new[] { "heave" }, b.Roles);
                Assert.Equal("OAxis 通道", b.OutputTypeText);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void Collect_SuggestsDeviceByRole()
        {
            using var _ = new DeviceTableGuard(YdlTable);
            string path = WriteTemp(
                Timeline(
                    Track("t1", "Device_1", role: "heave", label: "升降"),
                    Track("t2", "Device_1", role: "roll", label: "横滚")
                )
            );
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);

                var b = Assert.Single(vm.CollectUnresolvedBindings());

                Assert.Equal("YDL-0", b.SuggestedDeviceName);
                Assert.Contains("heave → YDL-0.heave", b.SuggestionText);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void Collect_NoSuggestionWhenNoRoleMatches()
        {
            using var _ = new DeviceTableGuard(YdlTable);
            string path = WriteTemp(Timeline(Track("t1", "Device_1", role: "pitch", label: "俯仰")));
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);

                var b = Assert.Single(vm.CollectUnresolvedBindings());

                Assert.Null(b.SuggestedDeviceName); // 不猜 —— 猜错就是换个名字继续静默丢
                Assert.Equal("", b.SuggestionText);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void Collect_EmptyTable_ReportsEverythingUnresolved_ButOffersNoRepairTarget()
        {
            // 表存在但没有任何设备: 文件存在性骗不过判定 —— 这种形态下"可映射目标"一个都没有,
            // 对话框必须禁用应用, 否则就是把静默丢弃换个名字 (合成的 Platform-0) 继续。
            using var _ = new DeviceTableGuard(EmptyTable);
            string path = WriteTemp(Timeline(Track("t1", "YDL-0")));
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);

                Assert.Single(vm.CollectUnresolvedBindings());
                Assert.False(MotionDeviceCatalog.IsTableAvailable);
                Assert.Empty(MotionDeviceCatalog.Devices);
                Assert.False(MotionDeviceCatalog.IsDeclared("Platform-0"));
            }
            finally
            {
                TryDelete(path);
            }
        }

        // ═══════ 修复 ═══════

        [Fact]
        public void Repair_ChangesOnlyDeviceName()
        {
            using var _ = new DeviceTableGuard(YdlTable);
            string path = WriteTemp(
                Timeline(
                    Track("t1", "Device_1", channel: "OAxis_48", oaction: "heave", role: "heave"),
                    Track("t2", "YDL-0")
                )
            );
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                var bindings = vm.CollectUnresolvedBindings();

                vm.ApplyDeviceRepair(
                    new Dictionary<string, string> { ["Device_1"] = "YDL-0" },
                    bindings
                );

                var fixedTrack = vm.Timeline!.Tracks.Single(t => t.Id == "t1");
                Assert.Equal("YDL-0", fixedTrack.DeviceName);

                // 通道与输出类型是内容语义, 一律不动 ——
                // 设备表推断出的通道集不完整 (只到 OAxis_31, 内容用了 OAxis_48), 拿它纠正会误杀
                Assert.Equal("OAxis_48", fixedTrack.OAxisChannel);
                Assert.Equal("oaxis", fixedTrack.OutputType);
                Assert.Equal("heave", fixedTrack.OActionName);

                // 片段与关键帧原样保留
                var clip = Assert.Single(fixedTrack.Clips);
                Assert.Equal(2, clip.Keyframes.Count);
                Assert.Equal(0.8f, clip.Keyframes[1].Value, 3);

                Assert.Empty(vm.CollectUnresolvedBindings());
                Assert.True(vm.IsDirty);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void Repair_IsUndoableInOneStep()
        {
            using var _ = new DeviceTableGuard(YdlTable);
            string path = WriteTemp(
                Timeline(
                    Track("t1", "Device_1", label: "升降"),
                    Track("t2", "Device_1", label: "横滚")
                )
            );
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                var bindings = vm.CollectUnresolvedBindings();

                vm.ApplyDeviceRepair(
                    new Dictionary<string, string> { ["Device_1"] = "YDL-0" },
                    bindings
                );
                Assert.All(vm.Timeline!.Tracks, t => Assert.Equal("YDL-0", t.DeviceName));

                // 用 VM 的 Undo() 而不是 UndoCommand: 命令体就是这一个调用, 而
                // ReactiveCommand 在无 Avalonia 调度器的测试进程里不保证同步执行。
                vm.Undo();

                // 一次撤销要整体回退 (8 条轨道的场景下逐条撤销是不可接受的)
                Assert.All(vm.Timeline!.Tracks, t => Assert.Equal("Device_1", t.DeviceName));
                Assert.Equal(2, vm.CollectUnresolvedBindings().Single().TrackCount);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void Repair_KeepsUndoLabelsMeaningful()
        {
            using var _ = new DeviceTableGuard(YdlTable);
            string path = WriteTemp(Timeline(Track("t1", "Device_1")));
            try
            {
                var undo = new UndoRedoService();
                var vm = new TimelineEditorViewModel(null, undo);
                vm.LoadFromFile(path);

                vm.ApplyDeviceRepair(
                    new Dictionary<string, string> { ["Device_1"] = "YDL-0" },
                    vm.CollectUnresolvedBindings()
                );

                Assert.Equal("修复设备绑定 (Device_1→YDL-0)", undo.UndoDescription);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void Repair_NoOpWhenMapEmptyOrMappingUnchanged()
        {
            using var _ = new DeviceTableGuard(YdlTable);
            string path = WriteTemp(Timeline(Track("t1", "Device_1")));
            try
            {
                var undo = new UndoRedoService();
                var vm = new TimelineEditorViewModel(null, undo);
                vm.LoadFromFile(path);
                var bindings = vm.CollectUnresolvedBindings();

                vm.ApplyDeviceRepair(new Dictionary<string, string>(), bindings);
                Assert.False(undo.CanUndo); // 空图不该留下一条空撤销

                // 映射到同名 = 没有变化, 不该进撤销栈、也不该标脏
                vm.ApplyDeviceRepair(
                    new Dictionary<string, string> { ["Device_1"] = "Device_1" },
                    bindings
                );
                Assert.False(undo.CanUndo);
                Assert.False(vm.IsDirty);
                Assert.Equal("设备绑定未发生变更", vm.StatusMessage);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void Repair_TouchesOnlyDevicesNamedInTheMap()
        {
            // 映射是"设备名 → 设备名", 只对图里出现的名字生效 ——
            // 同一次修复里未被点名的已声明设备必须原样不动 (批量操作最容易误伤这里)
            using var _ = new DeviceTableGuard(YdlTable);
            string path = WriteTemp(
                Timeline(
                    Track("t1", "Device_1"),
                    Track("t2", "YDL-1", label: "不该被动")
                )
            );
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);

                vm.ApplyDeviceRepair(
                    new Dictionary<string, string> { ["Device_1"] = "YDL-0" },
                    vm.CollectUnresolvedBindings()
                );

                Assert.Equal("YDL-0", vm.Timeline!.Tracks.Single(t => t.Id == "t1").DeviceName);
                Assert.Equal("YDL-1", vm.Timeline!.Tracks.Single(t => t.Id == "t2").DeviceName);
            }
            finally
            {
                TryDelete(path);
            }
        }

        // ═══════ 检查入口 (无对话框服务 = CLI/无头路径) ═══════

        [Fact]
        public async Task Check_WithoutDialogService_ReportsToStatusInsteadOfSilence()
        {
            using var _ = new DeviceTableGuard(YdlTable);
            string path = WriteTemp(Timeline(Track("t1", "Device_1")));
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);

                await vm.CheckDeviceBindingsAsync(autoPrompt: false);

                Assert.Contains("未在设备表中声明", vm.StatusMessage);
                Assert.Contains("1", vm.StatusMessage);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public async Task Check_ManualEntry_ConfirmsWhenEverythingIsFine()
        {
            using var _ = new DeviceTableGuard(YdlTable);
            string path = WriteTemp(Timeline(Track("t1", "YDL-0")));
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);

                await vm.CheckDeviceBindingsAsync(autoPrompt: false);

                // 手动入口必须给回执, 否则用户不知道"检查了"还是"没反应"
                Assert.Equal("所有轨道的设备与动作绑定正常", vm.StatusMessage);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public async Task Check_AutoPrompt_DoesNotRepeatForTheSameFile()
        {
            using var _ = new DeviceTableGuard(YdlTable);
            string path = WriteTemp(Timeline(Track("t1", "Device_1")));
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);

                await vm.CheckDeviceBindingsAsync(autoPrompt: true);
                string first = vm.StatusMessage;
                Assert.Contains("未在设备表中声明", first);

                // 同一文件 + 同一组未解析设备: 自动提示只出现一次 (打开和每次播放都弹会把人逼疯)
                vm.StatusMessage = "";
                await vm.CheckDeviceBindingsAsync(autoPrompt: true);
                Assert.Equal("", vm.StatusMessage);

                // 手动入口不受去重影响 —— 用户主动问, 必须答
                await vm.CheckDeviceBindingsAsync(autoPrompt: false);
                Assert.Contains("未在设备表中声明", vm.StatusMessage);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public async Task Check_OpenRegistersPendingCheck_ButDoesNotPopBeforeWindowShows()
        {
            using var _ = new DeviceTableGuard(YdlTable);
            string path = WriteTemp(Timeline(Track("t1", "Device_1")));
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);

                // 打开文件时只登记, 不立刻检查 —— 那时窗口可能还没显示, 弹模态框没有宿主
                string afterLoad = vm.StatusMessage;
                Assert.DoesNotContain("未在设备表中声明", afterLoad);

                await vm.RunPendingBindingCheckAsync();
                Assert.Contains("未在设备表中声明", vm.StatusMessage);

                // 登记是一次性的
                vm.StatusMessage = "";
                await vm.RunPendingBindingCheckAsync();
                Assert.Equal("", vm.StatusMessage);
            }
            finally
            {
                TryDelete(path);
            }
        }
        // ═══════ 动作未声明 (第二类静默丢失) ═══════

        [Fact]
        public void CollectOAction_OnlyForDeclaredDevicesWithUndeclaredActions()
        {
            using var _ = new DeviceTableGuard(YdlTable);
            string path = WriteTemp(
                Timeline(
                    Track("t1", "YDL-0", outputType: "oaction", channel: null, oaction: "surge"),
                    Track("t2", "YDL-0", outputType: "oaction", channel: null, oaction: "roll"),
                    Track("t3", "Device_1", outputType: "oaction", channel: null, oaction: "surge")
                )
            );
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);

                var binding = Assert.Single(vm.CollectUndeclaredOActions());
                Assert.Equal("YDL-0", binding.DeviceName);
                Assert.Equal("surge", binding.OActionName);
                Assert.Equal(1, binding.TrackCount);

                // 已声明的 roll 不该出现; 设备整个没声明的 t3 归"设备名"那条 ——
                // 两条一起报只会让人修一半 (先补结点, 再发现设备名还是错的)
                Assert.Single(vm.CollectUnresolvedBindings());
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void CollectOAction_IgnoresOAxisTracks()
        {
            // oaxis 轨道不经过 OActionMappings (它走 SetDO(const FKey&) 的 IsValidChannel),
            // 判据在插件自己的 outputCount 里 —— 往设备表补结点对它毫无作用, 报出来就是误报。
            using var _ = new DeviceTableGuard(YdlTable);
            string path = WriteTemp(
                Timeline(Track("t1", "YDL-0", channel: "OAxis_48", oaction: "surge"))
            );
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                Assert.Empty(vm.CollectUndeclaredOActions());
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void CollectOAction_GroupsByDeviceAndAction_IgnoringCase()
        {
            using var _ = new DeviceTableGuard(YdlTable);
            string path = WriteTemp(
                Timeline(
                    Track("t1", "ydl-0", outputType: "oaction", channel: null, oaction: "SURGE"),
                    Track("t2", "YDL-0", outputType: "oaction", channel: null, oaction: "surge")
                )
            );
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);

                // 一个 (设备, 动作) 只补一个结点, 所以大小写不同也必须聚成一组
                var binding = Assert.Single(vm.CollectUndeclaredOActions());
                Assert.Equal(2, binding.TrackCount);
                Assert.Equal(new[] { "t1", "t2" }, binding.TrackIds);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void CollectOAction_OffersOnlyInUseChannels_AndDoesNotGuess()
        {
            // 通道是内容语义, 而且"动作名 → 通道"的映射<b>只</b>存在于设备表里 (动作文件里没有),
            // 猜错就是换个名字继续静默丢。唯一敢给的默认值: 动作名本身就是该设备在用的通道名。
            using var _ = new DeviceTableGuard(CommentedTable);
            string path = WriteTemp(
                Timeline(
                    Track("t1", "YDL-0", outputType: "oaction", channel: null, oaction: "Wind"),
                    Track("t2", "YDL-0", outputType: "oaction", channel: null, oaction: "OAxis_6")
                )
            );
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                var bindings = vm.CollectUndeclaredOActions();
                Assert.Equal(2, bindings.Count);

                var wind = bindings.Single(b => b.OActionName == "Wind");
                Assert.Equal("", wind.SuggestedChannel);
                // 候选只取"该设备已经在用的通道", 不是合成的 OAxis_00..31 ——
                // 真实内容会用 OAxis_48 这类越界通道, 不完整的候选集只会把人引偏。
                Assert.Equal(new[] { "OAxis_6" }, wind.DeviceChannelVocabulary);

                var sameName = bindings.Single(b => b.OActionName == "OAxis_6");
                Assert.Equal("OAxis_6", sameName.SuggestedChannel);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void Validator_ReportsUndeclaredOAction_ButNotWhenTheDeviceIsUndeclared()
        {
            using var _ = new DeviceTableGuard(YdlTable);

            var options = new MotionValidationOptions
            {
                IsDeviceDeclared = MotionDeviceCatalog.IsDeclared,
                IsOActionDeclared = MotionDeviceCatalog.IsOActionDeclared,
            };

            var declared = MotionTimelineValidator.Validate(
                Timeline(Track("t1", "YDL-0", outputType: "oaction", channel: null, oaction: "surge")),
                options
            );
            Assert.Contains(
                declared,
                i => i.Code == "track.oaction_undeclared" && i.Severity == MotionIssueSeverity.Error
            );
            Assert.DoesNotContain(declared, i => i.Code == "track.device_undeclared");

            // 设备压根没声明时不叠这条: 那是 track.device_undeclared 的事,
            // 而那时当务之急是改设备名, 不是往表里补结点
            var undeclared = MotionTimelineValidator.Validate(
                Timeline(Track("t1", "Device_1", outputType: "oaction", channel: null, oaction: "surge")),
                options
            );
            Assert.Contains(undeclared, i => i.Code == "track.device_undeclared");
            Assert.DoesNotContain(undeclared, i => i.Code == "track.oaction_undeclared");

            // 声明过的动作不报
            var ok = MotionTimelineValidator.Validate(
                Timeline(Track("t1", "YDL-0", outputType: "oaction", channel: null, oaction: "heave")),
                options
            );
            Assert.DoesNotContain(ok, i => i.Severity == MotionIssueSeverity.Error);
        }

        // ═══════ 补全 (写入设备表) ═══════

        private static List<OActionCompletion> Plan(string device, string action, params string[] channels) =>
            new()
            {
                new OActionCompletion
                {
                    DeviceName = device,
                    OActionName = action,
                    Channels = channels.ToList(),
                },
            };

        [Fact]
        public void Completion_WritesTheNode_AndLeavesEverythingElseAlone()
        {
            using var _ = new DeviceTableGuard(ToCrlf(CommentedTable));
            string path = WriteTemp(
                Timeline(Track("t1", "YDL-0", outputType: "oaction", channel: null, oaction: "Wind"))
            );
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                vm.ApplyOActionCompletion(Plan("YDL-0", "Wind", "OAxis_18"));

                var doc = new System.Xml.XmlDocument();
                doc.Load(TablePath);

                var key = doc.SelectSingleNode("//Device[@Name='YDL-0']/OAction[@Name='Wind']/Key");
                Assert.Equal("OAxis_18", key?.Attributes?["Name"]?.Value);

                string table = File.ReadAllText(TablePath);
                // 用户手写的注释必须还在 —— 这就是不用整份重写 (IORoot.Save) 的理由
                Assert.Contains("手工维护", table);
                Assert.Contains("YDL 平台", table);
                // 行尾也不能被换掉 (XmlDocument 往返会把 CRLF 归一成 LF)
                Assert.Contains("\r\n", table);
                // 原有结点不受影响
                Assert.Contains("<Key Name=\"OAxis_6\" />", table);

                // 补完这个问题就不再报了
                Assert.Empty(vm.CollectUndeclaredOActions());
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void Completion_DoesNotChangeLineEndings()
        {
            using var _ = new DeviceTableGuard(ToLf(CommentedTable));
            string path = WriteTemp(
                Timeline(Track("t1", "YDL-0", outputType: "oaction", channel: null, oaction: "Wind"))
            );
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                vm.ApplyOActionCompletion(Plan("YDL-0", "Wind", "OAxis_18"));

                Assert.DoesNotContain("\r", File.ReadAllText(TablePath));
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void Completion_WritesOneKeyPerChannel_AndIsIdempotent()
        {
            using var _ = new DeviceTableGuard(YdlTable);
            string path = WriteTemp(
                Timeline(Track("t1", "YDL-0", outputType: "oaction", channel: null, oaction: "surge"))
            );
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                vm.ApplyOActionCompletion(Plan("YDL-0", "surge", "OAxis_20", "OAxis_22"));

                var doc = new System.Xml.XmlDocument();
                doc.Load(TablePath);
                var keys = doc.SelectNodes("//Device[@Name='YDL-0']/OAction[@Name='surge']/Key");
                Assert.Equal(2, keys?.Count);
                Assert.Equal("OAxis_20", keys![0].Attributes?["Name"]?.Value);
                Assert.Equal("OAxis_22", keys[1].Attributes?["Name"]?.Value);

                // 再补一次不该多出一个同名结点 (重复的 OAction 会让 native 侧落到哪一个变得不确定)
                byte[] afterFirst = File.ReadAllBytes(TablePath);
                vm.ApplyOActionCompletion(Plan("YDL-0", "surge", "OAxis_24"));
                Assert.Equal(afterFirst, File.ReadAllBytes(TablePath));
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void Completion_RefusesRowsWithoutChannels()
        {
            // Key 为空的 OAction 比"不写"更坏: native 侧 OActionMappings.count() 命中, 却一个通道都不下发
            // —— 预检不再报警, 输出照样丢。所以宁可拒绝。
            using var _ = new DeviceTableGuard(YdlTable);
            byte[] before = File.ReadAllBytes(TablePath);
            string path = WriteTemp(
                Timeline(Track("t1", "YDL-0", outputType: "oaction", channel: null, oaction: "surge"))
            );
            try
            {
                var undo = new UndoRedoService();
                var vm = new TimelineEditorViewModel(null, undo);
                vm.LoadFromFile(path);

                vm.ApplyOActionCompletion(
                    new List<OActionCompletion>
                    {
                        new() { DeviceName = "YDL-0", OActionName = "surge" },
                        new()
                        {
                            DeviceName = "YDL-0",
                            OActionName = "surge",
                            Channels = new List<string> { "  " },
                        },
                    }
                );

                Assert.Equal(before, File.ReadAllBytes(TablePath));
                Assert.False(undo.CanUndo);
                Assert.Contains("未指定通道", vm.StatusMessage);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void Completion_IsUndoableInOneStep_ByteForByte()
        {
            using var _ = new DeviceTableGuard(ToCrlf(CommentedTable));
            byte[] before = File.ReadAllBytes(TablePath);
            string path = WriteTemp(
                Timeline(Track("t1", "YDL-0", outputType: "oaction", channel: null, oaction: "Wind"))
            );
            try
            {
                var undo = new UndoRedoService();
                var vm = new TimelineEditorViewModel(null, undo);
                vm.LoadFromFile(path);
                Assert.Single(vm.CollectUndeclaredOActions());

                vm.ApplyOActionCompletion(Plan("YDL-0", "Wind", "OAxis_18"));

                Assert.NotEqual(before, File.ReadAllBytes(TablePath));
                Assert.Equal("补全设备表动作 (YDL-0.Wind)", undo.UndoDescription);
                Assert.Contains("重启设备服务后生效", vm.StatusMessage);
                Assert.Empty(vm.CollectUndeclaredOActions());

                vm.Undo();

                // 逐字节还原 —— 设备表是用户手工维护的文件, 撤销必须做到"像没点过一样"
                Assert.Equal(before, File.ReadAllBytes(TablePath));
                Assert.Single(vm.CollectUndeclaredOActions());
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void Completion_MirrorsIntoTheInMemoryDeviceTable()
        {
            // 只写文件是不够的: 用户之后在设备管理里点一次保存, IORoot.Save() 会按内存模型
            // 把刚补出来的结点整份重写掉 —— 于是"补了、又没了"。
            using var _ = new DeviceTableGuard(YdlTable);
            string path = WriteTemp(
                Timeline(Track("t1", "YDL-0", outputType: "oaction", channel: null, oaction: "surge"))
            );

            var root = IOStudio.ViewModels.IORoot.Instance;
            var savedDevices = root.Devices;
            try
            {
                root.SetConfig(TablePath).Load();

                var vm = new TimelineEditorViewModel(null, new UndoRedoService());
                vm.LoadFromFile(path);
                vm.ApplyOActionCompletion(Plan("YDL-0", "surge", "OAxis_20"));

                var node = root
                    .Devices.Single(d => d.Name == "YDL-0")
                    .OActions.Single(oa => oa.Name == "surge");
                Assert.Equal(new[] { "OAxis_20" }, node.Keys.Select(k => k.Name));

                vm.Undo();

                // 撤销要连内存模型一起回滚, 否则下一次保存会把结点又写回磁盘, 撤销就成了假的
                Assert.DoesNotContain(
                    root.Devices.Single(d => d.Name == "YDL-0").OActions,
                    oa => oa.Name == "surge"
                );
            }
            finally
            {
                root.Devices = savedDevices;
                MotionDeviceCatalog.Invalidate();
                TryDelete(path);
            }
        }

        // ═══════ 自动新建设备 ═══════

        /// <summary>带 DllName 的设备表 —— 自动新建要靠"照抄"一台已在工作的设备。</summary>
        private const string CloneableTable = """
            <?xml version="1.0" encoding="utf-8"?>
            <!-- 现场接线: 通道编号按接线表 -->
            <IORoot>
              <Device Name="ExtDev" Type="External" DllName="DEBUGGER" Index="1">
                <Properties />
              </Device>
              <Device Name="YDL-0" Type="External" DllName="MP-YDL" Index="7">
                <Properties />
                <OAction Name="Heave" Label="电缸升降">
                  <Key Name="OAxis_6" />
                </OAction>
                <OAction Name="Wind" Label="风扇">
                  <Key Name="OAxis_18" />
                </OAction>
              </Device>
            </IORoot>
            """;

        [Fact]
        public void CreationPlan_ClonesTheMostSimilarDeclaredDevice()
        {
            using var _ = new DeviceTableGuard(CloneableTable);
            string path = WriteTemp(
                Timeline(
                    Track("t1", "YDL-9", outputType: "oaction", channel: null, oaction: "Heave"),
                    Track("t2", "YDL-9", outputType: "oaction", channel: null, oaction: "Wind")
                )
            );
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);

                var plan = vm.BuildDeviceCreationPlans()["YDL-9"];

                Assert.True(plan.IsViable);
                Assert.Equal("YDL-0", plan.SourceDeviceName);
                Assert.Equal("MP-YDL", plan.DllName);
                Assert.Equal(7, plan.Index);
                Assert.Equal(
                    new[] { "Heave", "Wind" },
                    plan.Actions.Select(a => a.OActionName).OrderBy(n => n)
                );
                Assert.Equal(
                    new[] { "OAxis_18" },
                    plan.Actions.Single(a => a.OActionName == "Wind").Channels
                );
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void Creation_WritesTheDeviceAndItsActions()
        {
            using var _ = new DeviceTableGuard(CloneableTable);
            string path = WriteTemp(
                Timeline(Track("t1", "YDL-9", outputType: "oaction", channel: null, oaction: "Heave"))
            );
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                Assert.Single(vm.CollectUnresolvedBindings());

                vm.ApplyDeviceCreation(vm.BuildDeviceCreationPlans()["YDL-9"]);

                var doc = new System.Xml.XmlDocument();
                doc.Load(TablePath);

                var device = doc.SelectSingleNode("//Device[@Name='YDL-9']");
                Assert.NotNull(device);
                Assert.Equal("MP-YDL", device!.Attributes?["DllName"]?.Value);
                Assert.Equal("7", device.Attributes?["Index"]?.Value);

                // 驱动与通道都是照抄来的, 不是编的 —— 这样 native 才真的会建、真的会下发
                var key = doc.SelectSingleNode("//Device[@Name='YDL-9']/OAction[@Name='Heave']/Key");
                Assert.Equal("OAxis_6", key?.Attributes?["Name"]?.Value);

                string table = File.ReadAllText(TablePath);
                Assert.Contains("现场接线", table); // 用户手写的注释还在
                Assert.Contains("<Device Name=\"ExtDev\"", table); // 原设备没被动

                // 建完就不该再报"未解析设备"
                Assert.Empty(vm.CollectUnresolvedBindings());
                Assert.Empty(vm.CollectUndeclaredOActions());
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void Creation_LeavesOutActionsWithNoKnownChannels()
        {
            // 源设备没有同名动作 → 通道推不出来。这种情况宁可不写那个 OAction:
            // 没有 <Key> 的结点会通过 native 的存在性判断却一个通道都不下发。
            using var _ = new DeviceTableGuard(CloneableTable);
            string path = WriteTemp(
                Timeline(
                    Track("t1", "YDL-9", outputType: "oaction", channel: null, oaction: "Heave"),
                    Track("t2", "YDL-9", outputType: "oaction", channel: null, oaction: "Scent")
                )
            );
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                vm.ApplyDeviceCreation(vm.BuildDeviceCreationPlans()["YDL-9"]);

                var doc = new System.Xml.XmlDocument();
                doc.Load(TablePath);
                Assert.NotNull(doc.SelectSingleNode("//Device[@Name='YDL-9']/OAction[@Name='Heave']"));
                Assert.Null(doc.SelectSingleNode("//Device[@Name='YDL-9']/OAction[@Name='Scent']"));
                Assert.Contains("Scent", vm.StatusMessage);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void Creation_IsUndoableInOneStep_ByteForByte()
        {
            using var _ = new DeviceTableGuard(ToCrlf(CloneableTable));
            byte[] before = File.ReadAllBytes(TablePath);
            string path = WriteTemp(
                Timeline(Track("t1", "YDL-9", outputType: "oaction", channel: null, oaction: "Heave"))
            );
            try
            {
                var undo = new UndoRedoService();
                var vm = new TimelineEditorViewModel(null, undo);
                vm.LoadFromFile(path);
                vm.ApplyDeviceCreation(vm.BuildDeviceCreationPlans()["YDL-9"]);

                Assert.NotEqual(before, File.ReadAllBytes(TablePath));
                Assert.Equal("自动新建设备 (YDL-9 ← YDL-0)", undo.UndoDescription);
                Assert.Contains("重启设备服务后生效", vm.StatusMessage);

                vm.Undo();

                // 逐字节还原 —— 设备表是用户手工维护的, 撤销要像没点过一样
                Assert.Equal(before, File.ReadAllBytes(TablePath));
                Assert.Single(vm.CollectUnresolvedBindings());
                Assert.Contains("\r\n", File.ReadAllText(TablePath));
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void Creation_MirrorsIntoTheInMemoryDeviceTable_AndUndoRemovesIt()
        {
            // 只写文件不够: 用户之后在设备管理里点一次保存, IORoot.Save() 会按内存模型整份重写
            using var _ = new DeviceTableGuard(CloneableTable);
            string path = WriteTemp(
                Timeline(Track("t1", "YDL-9", outputType: "oaction", channel: null, oaction: "Heave"))
            );

            var root = IOStudio.ViewModels.IORoot.Instance;
            var saved = root.Devices;
            try
            {
                root.SetConfig(TablePath).Load();

                var vm = new TimelineEditorViewModel(null, new UndoRedoService());
                vm.LoadFromFile(path);
                vm.ApplyDeviceCreation(vm.BuildDeviceCreationPlans()["YDL-9"]);

                var added = root.Devices.Single(d => d.Name == "YDL-9");
                Assert.Equal("MP-YDL", added.DllName);
                Assert.Equal(new[] { "OAxis_6" }, added.OActions.Single().Keys.Select(k => k.Name));

                vm.Undo();
                Assert.DoesNotContain(root.Devices, d => d.Name == "YDL-9");
            }
            finally
            {
                root.Devices = saved;
                MotionDeviceCatalog.Invalidate();
                TryDelete(path);
            }
        }

        [Fact]
        public void Creation_RefusesDuplicateDeviceName()
        {
            using var _ = new DeviceTableGuard(CloneableTable);
            byte[] before = File.ReadAllBytes(TablePath);
            string path = WriteTemp(Timeline(Track("t1", "YDL-0")));
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);

                var plan = new DeviceCreationPlan
                {
                    DeviceName = "YDL-0",
                    DllName = "MP-YDL",
                    Actions = new List<OActionCompletion>
                    {
                        new() { OActionName = "Heave", Channels = new List<string> { "OAxis_6" } },
                    },
                };
                vm.ApplyDeviceCreation(plan);

                Assert.Equal(before, File.ReadAllBytes(TablePath));
                Assert.Contains("已存在", vm.StatusMessage);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void Creation_OnEmptyTable_BuildsOnlyTheShell_AndAsksForADriver()
        {
            // 冷启动: 表为空 → 没有驱动可抄, 也推不出任何通道。
            // 计划必须"不可执行", 由界面去问驱动 —— 绝不能悄悄造一台 DllName 为空的设备
            // (native 侧直接不建, 那是"配好了但不工作"的假状态)。
            using var _ = new DeviceTableGuard(EmptyTable);
            string path = WriteTemp(
                Timeline(Track("t1", "YDL-9", outputType: "oaction", channel: null, oaction: "Heave"))
            );
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);

                var plan = vm.BuildDeviceCreationPlans()["YDL-9"];
                Assert.False(plan.IsViable);
                Assert.True(plan.NeedsDriverChoice);
                Assert.Empty(plan.Actions);

                vm.ApplyDeviceCreation(plan);
                Assert.Contains("无法新建设备", vm.StatusMessage);

                // 只有用户明确点了驱动, 才允许建出骨架
                vm.ApplyDeviceCreation(
                    DeviceCreationPlanner.BuildFromDriver(
                        "YDL-9",
                        "Simulator",
                        0,
                        new List<OActionCompletion>()
                    )
                );

                var doc = new System.Xml.XmlDocument();
                doc.Load(TablePath);
                var device = doc.SelectSingleNode("//Device[@Name='YDL-9']");
                Assert.NotNull(device);
                Assert.Equal("Simulator", device!.Attributes?["DllName"]?.Value);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void Creation_OnEmptyTable_KeepsTheExplicitlyChosenDriver()
        {
            // 空表 + 用户选了驱动: 骨架建出来, 但没有动作 —— 状态栏要说清楚还得补动作
            using var _ = new DeviceTableGuard(EmptyTable);
            string path = WriteTemp(
                Timeline(Track("t1", "YDL-9", outputType: "oaction", channel: null, oaction: "Heave"))
            );
            try
            {
                var vm = new TimelineEditorViewModel(null);
                vm.LoadFromFile(path);
                vm.ApplyDeviceCreation(
                    DeviceCreationPlanner.BuildFromDriver(
                        "YDL-9",
                        "MODBUS",
                        0,
                        new List<OActionCompletion>()
                    )
                );

                Assert.Contains("YDL-9", vm.StatusMessage);
                Assert.Contains("0 个动作结点", vm.StatusMessage);
            }
            finally
            {
                TryDelete(path);
            }
        }

        // ═══════ 补全计划解析 (对话框输入) ═══════

        [Fact]
        public void ChannelInput_AcceptsWhatPeopleActuallyType()
        {
            Assert.Equal(
                new[] { "OAxis_18", "OAxis_20" },
                OActionCompletion.ParseChannels("OAxis_18, OAxis_20")
            );
            Assert.Equal(
                new[] { "OAxis_18", "OAxis_20" },
                OActionCompletion.ParseChannels("OAxis_18，OAxis_20")
            );
            Assert.Equal(
                new[] { "OAxis_18" },
                OActionCompletion.ParseChannels("  OAxis_18  ")
            );
            // 同一个通道写两遍只算一个 (否则会写出重复的 Key 结点)
            Assert.Equal(
                new[] { "OAxis_18" },
                OActionCompletion.ParseChannels("OAxis_18 OAxis_18")
            );
            Assert.Empty(OActionCompletion.ParseChannels(null));
            Assert.Empty(OActionCompletion.ParseChannels("   "));
        }
    }
}
