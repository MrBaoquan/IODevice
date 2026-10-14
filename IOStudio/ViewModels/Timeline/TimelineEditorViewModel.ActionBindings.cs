using System;
using System.Collections.Generic;
using System.Linq;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using DeviceModel = IOStudio.ViewModels.Device;
using IORootModel = IOStudio.ViewModels.IORoot;
using KeyModel = IOStudio.ViewModels.Key;
using OActionModel = IOStudio.ViewModels.OAction;

namespace IOStudio.ViewModels.Timeline
{
    /// <summary>
    /// 「动作未在设备上声明」的检测与补全 —— 与 <see cref="TimelineEditorViewModel"/> 的设备名那条
    /// (见 <c>TimelineEditorViewModel.DeviceBindings.cs</c>) 并列的第二类静默丢失。
    ///
    /// <para><b>为什么单列一条</b>: 设备名查不到时 native 侧 <c>MotionPlayer::DispatchChannel</c> 会累加
    /// 一个未解析计数 (至少在 HUD 上看得见); 而动作名查不到时
    /// <c>ExternalIO::SetDO(const char*, float, bool)</c> 直接 <c>return 0</c> —— 无日志、无计数,
    /// 从任何角度看都是"播放正常、设备不动"。补法也不同: 那条改设备名, 这条往设备表里加结点。</para>
    /// </summary>
    public partial class TimelineEditorViewModel
    {
        /// <summary>
        /// 为每个"没人认领的设备"生成一份自动新建计划 —— 用 .motion 里实际用到的动作,
        /// 去现有设备里找一台最像的照抄 (驱动名与动作→通道映射都照抄)。
        ///
        /// <para>为什么不"凭空造": <c>DllName</c> 与"动作→通道"这两样 .motion 里都没有,
        /// 而 native 对 <c>DllName</c> 无效的 External 设备是<b>直接不建</b>。照抄一台已经在工作的
        /// 设备, 是唯一能造出"真的能用"的设备的办法。设备表里没有可抄的, 计划的
        /// <c>NeedsDriverChoice</c> 为 true, 由对话框让用户指定驱动。</para>
        /// </summary>
        internal Dictionary<string, DeviceCreationPlan> BuildDeviceCreationPlans()
        {
            var result = new Dictionary<string, DeviceCreationPlan>(StringComparer.OrdinalIgnoreCase);
            if (Timeline?.Tracks is null)
                return result;

            foreach (var group in Tracks.Where(t => !string.IsNullOrWhiteSpace(t.DeviceName))
                .Where(t => !MotionDeviceCatalog.IsDeclared(t.DeviceName))
                .GroupBy(t => t.DeviceName, StringComparer.OrdinalIgnoreCase))
            {
                result[group.Key] = DeviceCreationPlanner.Build(
                    group.Key,
                    CollectNeededActions(group),
                    MotionDeviceCatalog.Devices
                );
            }

            return result;
        }

        /// <summary>
        /// 这台设备在 .motion 里实际用到的动作名 (去重)。
        ///
        /// <para>oaxis 轨道不算: 它不经过 OActionMappings, 补 OAction 结点对它没有意义。
        /// (oaxis 的通道名不在这里建结点 —— 那部分是插件按 outputCount 判定的。)</para>
        /// </summary>
        private static List<string> CollectNeededActions(IEnumerable<TrackViewModel> tracks) =>
            tracks
                .Select(t => t.Track)
                .Where(m => m is not null && !IsOAxis(m))
                .Select(m => m!.OActionName)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>
        /// 执行自动新建设备: 把设备结点连同它的 OAction 一起写进 <c>Config/IODevice.xml</c>,
        /// 并同步内存模型, 一步可撤销。
        /// </summary>
        internal void ApplyDeviceCreation(DeviceCreationPlan plan)
        {
            if (plan is null || !plan.IsViable)
            {
                PushStatus("无法新建设备 —— 缺少驱动插件名或带通道的动作");
                return;
            }

            byte[]? before = DeviceTableWriter.ReadSnapshot();
            if (before is null)
            {
                PushStatus("设备表 (Config/IODevice.xml) 不存在或读不到, 无法新建设备");
                return;
            }

            if (!DeviceTableWriter.TryAddDevice(plan, out string error))
            {
                PushStatus($"新建设备失败: {error}");
                return;
            }

            var mirrored = MirrorDeviceIntoRoot(plan);
            MotionDeviceCatalog.Invalidate();

            byte[]? after = DeviceTableWriter.ReadSnapshot();
            string label = string.IsNullOrEmpty(plan.SourceDeviceName)
                ? plan.DeviceName
                : $"{plan.DeviceName} ← {plan.SourceDeviceName}";

            // 文件与内存模型必须一起回滚: 只回滚文件的话, 下一次 IORoot.Save()
            // 会按内存里那份把设备又整份写回来, 撤销就成了假的。
            void Redo()
            {
                if (after is not null)
                    DeviceTableWriter.WriteSnapshot(after);
                ApplyMirror(plan, mirrored);
                MotionDeviceCatalog.Invalidate();
                RefreshTrackBadges();
            }

            void Undo()
            {
                if (before is not null)
                    DeviceTableWriter.WriteSnapshot(before);
                RemoveMirror(mirrored);
                MotionDeviceCatalog.Invalidate();
                RefreshTrackBadges();
            }

            _undoRedo.Execute(new LambdaCommand($"自动新建设备 ({label})", Redo, Undo));

            // 先跑预检 (它反映的是设备表写完后的状态), 再推这条动作摘要 ——
            // 顺序反过来摘要会被预检覆盖, 而"需重启设备服务后生效"才是用户接下来要看的。
            ReportValidationSummary();

            string hint = plan.NeedsDriverChoice ? "" : $"（复制 {plan.SourceDeviceName} 的驱动与动作映射）";
            string missing =
                plan.ActionsWithoutChannels.Count > 0
                    ? $"；{plan.ActionsWithoutChannels.Count} 个动作补不出通道, 未写入: "
                        + string.Join(", ", plan.ActionsWithoutChannels)
                    : "";
            string followUp = plan.NeedsActionsLater ? "；动作通道推不出来, 请为它补动作" : "";

            PushStatus(
                $"已新建设备 {plan.DeviceName}{hint}, 含 {plan.Actions.Count} 个动作结点"
                    + $"{missing}{followUp}；需重启设备服务后生效"
            );
        }

        /// <summary>把新设备同步进内存的设备表模型 (否则下一次 IORoot.Save() 会把它整份重写掉)。</summary>
        private static (ViewModels.Device Device, List<ViewModels.OAction> Actions)? MirrorDeviceIntoRoot(
            DeviceCreationPlan plan
        )
        {
            var root = IORootModel.Instance;
            if (root?.Devices is null)
                return null;

            if (
                root.Devices.Any(d =>
                    string.Equals(d.Name, plan.DeviceName, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                return null;
            }

            var actions = new List<OActionModel>();
            var device = new DeviceModel
            {
                Name = plan.DeviceName,
                Type = "External",
                DllName = plan.DllName,
                Index = plan.Index,
            };

            foreach (var action in plan.Actions)
            {
                var node = new OActionModel { Name = action.OActionName };
                foreach (var channel in action.Channels)
                {
                    if (!string.IsNullOrWhiteSpace(channel))
                        node.Keys.Add(new KeyModel { Name = channel.Trim() });
                }
                actions.Add(node);
                device.OActions.Add(node);
            }

            return (device, actions);
        }

        private static void ApplyMirror(
            DeviceCreationPlan plan,
            (DeviceModel Device, List<OActionModel> Actions)? mirrored
        )
        {
            if (mirrored is not { } m || IORootModel.Instance?.Devices is not { } devices)
                return;

            if (!devices.Any(d => string.Equals(d.Name, plan.DeviceName, StringComparison.OrdinalIgnoreCase)))
                devices.Add(m.Device);
        }

        private static void RemoveMirror((DeviceModel Device, List<OActionModel> Actions)? mirrored)
        {
            if (mirrored is not { } m || IORootModel.Instance?.Devices is not { } devices)
                return;

            devices.Remove(m.Device);
        }

        /// <summary>
        /// 收集"设备已声明、但动作没声明"的轨道, 按 (设备, 动作) 聚合。
        ///
        /// <para>设备本身没声明的轨道不算 —— 那归设备名那条修复, 且当务之急是改设备名。
        /// oaxis 轨道也不算: 它不经过 <c>OActionMappings</c>, 走的是
        /// <c>SetDO(const FKey&amp;, float)</c> 的 <c>IsValidChannel</c>, 判据在插件自己的
        /// outputCount 里, 设备表补不了。</para>
        /// </summary>
        internal List<UndeclaredOActionBinding> CollectUndeclaredOActions()
        {
            var result = new List<UndeclaredOActionBinding>();
            if (Timeline?.Tracks is null)
                return result;

            var byAction = Tracks
                .Where(t => t.Track is not null)
                .Where(t => !IsOAxis(t.Track))
                .Where(t => !string.IsNullOrWhiteSpace(t.DeviceName))
                .Where(t => !string.IsNullOrWhiteSpace(t.Track.OActionName))
                .Where(t => MotionDeviceCatalog.IsDeclared(t.DeviceName))
                .Where(t => !MotionDeviceCatalog.IsOActionDeclared(t.DeviceName, t.Track.OActionName))
                .GroupBy(
                    t => t.DeviceName + "\u0000" + t.Track.OActionName,
                    StringComparer.OrdinalIgnoreCase
                );

            foreach (var group in byAction)
            {
                var first = group.First();
                var binding = new UndeclaredOActionBinding
                {
                    DeviceName = first.DeviceName,
                    OActionName = first.Track.OActionName,
                    TrackCount = group.Count(),
                    TrackIds = group.Select(t => t.Id).ToList(),
                    Roles = group
                        .Select(t => t.Role)
                        .Where(r => !string.IsNullOrWhiteSpace(r))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    DeviceChannelVocabulary = CollectChannelVocabulary(first.DeviceName),
                };

                // 唯一敢给的默认值: 动作名本身就是该设备的一个通道名时, 直接用同一个字符串。
                // 别的都不猜 —— 通道填错就是换个名字继续静默丢, 而且 Key 为空的 OAction 更坏:
                // 它会通过 native 的存在性判断, 却一个通道都不下发 (预检不再报警, 输出照样丢)。
                if (
                    binding.DeviceChannelVocabulary.Any(c =>
                        string.Equals(c, binding.OActionName, StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    binding.SuggestedChannel = binding.OActionName;
                }

                result.Add(binding);
            }

            return result;
        }

        /// <summary>
        /// 该设备<b>已经在用</b>的通道名 (取自既有 OAction 的 Key), 按自然序 (OAxis_6 在 OAxis_48 之前)。
        ///
        /// <para>刻意不用 <c>DeviceSchemaService.GenerateOAxisChannels</c> 合成的 <c>OAxis_00..31</c>
        /// 当候选: 真实内容会用 <c>OAxis_48/49</c> 这类越界通道, 拿一个不完整的集合当"可选项"
        /// 只会把作者引向错的通道。</para>
        /// </summary>
        private static List<string> CollectChannelVocabulary(string deviceName)
        {
            var device = MotionDeviceCatalog.Find(deviceName);
            if (device is null)
                return new List<string>();

            var names = device
                .OActions.SelectMany(oa => oa.KeyNames ?? new List<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            return NaturalOrder(names).ToList();
        }

        /// <summary>自然序: 把尾部的数字当数字比 (OAxis_6 &lt; OAxis_16 &lt; OAxis_48)。</summary>
        private static IEnumerable<string> NaturalOrder(IEnumerable<string> names) =>
            names
                .Select(n =>
                {
                    int i = n.Length;
                    while (i > 0 && char.IsDigit(n[i - 1]))
                        i--;
                    int number =
                        i < n.Length && int.TryParse(n.AsSpan(i), out int parsed) ? parsed : -1;
                    return (Name: n, Prefix: n[..i], Number: number);
                })
                .OrderBy(x => x.Prefix, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Number)
                .Select(x => x.Name);

        private static bool IsOAxis(MotionTrack track) =>
            string.Equals(track.OutputType, "oaxis", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 把补全计划写进 <c>Config/IODevice.xml</c>, 并同步内存中的设备表模型。
        ///
        /// <para><b>两个都要做</b>: 只写文件的话, 用户之后在设备管理里点任意一次保存,
        /// <c>IORoot.Save()</c> 会按内存模型把刚补出来的结点整份重写掉; 只改内存的话, 不落盘就白补。
        /// 文件写入是<b>最小结点插入</b> (见 <see cref="DeviceTableWriter"/>), 不重写整份文件。</para>
        ///
        /// <para><b>生效时机</b>: native 侧的 <c>OActionMappings</c> 是设备 <c>Load()</c> 时建的,
        /// 所以补完结点要重启设备服务才生效 —— 状态栏明说这一点, 否则用户会以为"补了没用"。</para>
        /// </summary>
        internal void ApplyOActionCompletion(IReadOnlyList<OActionCompletion> completions)
        {
            if (completions is null || completions.Count == 0)
                return;

            var plan = completions
                .Where(c =>
                    !string.IsNullOrWhiteSpace(c.DeviceName)
                    && !string.IsNullOrWhiteSpace(c.OActionName)
                    && c.Channels.Any(ch => !string.IsNullOrWhiteSpace(ch))
                )
                .ToList();

            if (plan.Count == 0)
            {
                PushStatus("没有可补全的动作 —— 未指定通道的项不会写入");
                return;
            }

            byte[]? before = DeviceTableWriter.ReadSnapshot();
            if (before is null)
            {
                PushStatus("设备表 (Config/IODevice.xml) 不存在或读不到, 无法补全");
                return;
            }

            int written = 0;
            var failures = new List<string>();
            var mirrored = new List<(DeviceModel Device, OActionModel Action)>();

            foreach (var item in plan)
            {
                if (
                    !DeviceTableWriter.TryAddOAction(
                        item.DeviceName,
                        item.OActionName,
                        null,
                        item.Channels,
                        out string error
                    )
                )
                {
                    failures.Add($"{item.DeviceName}.{item.OActionName}: {error}");
                    continue;
                }

                written++;
                var mirror = MirrorOActionIntoRoot(item);
                if (mirror is not null)
                    mirrored.Add(mirror.Value);
            }

            if (written == 0)
            {
                PushStatus($"设备表未改动 —— {string.Join("；", failures)}");
                return;
            }

            MotionDeviceCatalog.Invalidate();

            byte[]? after = DeviceTableWriter.ReadSnapshot();
            string summary = DescribePlan(plan);

            // 撤销/重做要同时回滚"文件"和"内存模型": 只回滚文件的话, 下一次
            // IORoot.Save() 会按内存里那份把结点又写回来, 撤销就成了假的。
            void Redo()
            {
                if (after is not null)
                    DeviceTableWriter.WriteSnapshot(after);
                foreach (var (device, action) in mirrored)
                {
                    if (!device.OActions.Contains(action))
                        device.OActions.Add(action);
                }
                MotionDeviceCatalog.Invalidate();
                RefreshTrackBadges();
            }

            void Undo()
            {
                if (before is not null)
                    DeviceTableWriter.WriteSnapshot(before);
                foreach (var (device, action) in mirrored)
                    device.OActions.Remove(action);
                MotionDeviceCatalog.Invalidate();
                RefreshTrackBadges();
            }

            _undoRedo.Execute(new LambdaCommand($"补全设备表动作 ({summary})", Redo, Undo));

            // 先跑预检 (它反映的是设备表写完后的状态), 再推这条动作摘要 ——
            // 顺序反过来摘要会被预检覆盖, 而"需重启设备服务后生效"才是用户接下来要看的。
            ReportValidationSummary();

            string failed =
                failures.Count > 0 ? $"；{failures.Count} 项未写入: {string.Join("；", failures)}" : "";
            PushStatus(
                $"已在设备表中补全 {written} 个动作结点 ({summary}){failed}；需重启设备服务后生效"
            );
        }

        /// <summary>
        /// 把新结点同步进内存的设备表模型 —— 不做这步, 下一次 <c>IORoot.Save()</c>
        /// (设备管理里点保存、或新建设备) 就会把它整份重写掉。
        /// 设备不在内存模型里 (表未加载 / 名字对不上) 时返回 null, 不算失败。
        /// </summary>
        private static (DeviceModel Device, OActionModel Action)? MirrorOActionIntoRoot(
            OActionCompletion item
        )
        {
            var devices = IORootModel.Instance?.Devices;
            if (devices is null)
                return null;

            var device = devices.FirstOrDefault(d =>
                !string.IsNullOrWhiteSpace(d.Name)
                && string.Equals(d.Name, item.DeviceName, StringComparison.OrdinalIgnoreCase)
            );
            if (device is null)
                return null;

            if (
                device.OActions.Any(oa =>
                    string.Equals(oa.Name, item.OActionName, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                return null;
            }

            var node = new OActionModel { Name = item.OActionName };
            foreach (var channel in item.Channels)
            {
                if (!string.IsNullOrWhiteSpace(channel))
                    node.Keys.Add(new KeyModel { Name = channel.Trim() });
            }
            device.OActions.Add(node);

            return (device, node);
        }

        /// <summary>设备表变了 → 轨道头上的绑定警示徽章要跟着重算。</summary>
        private void RefreshTrackBadges()
        {
            foreach (var track in Tracks)
                track.RaiseAllBindingsChanged();
        }

        private static string DescribePlan(IEnumerable<OActionCompletion> plan) =>
            string.Join(", ", plan.Select(c => $"{c.DeviceName}.{c.OActionName}"));
    }
}
