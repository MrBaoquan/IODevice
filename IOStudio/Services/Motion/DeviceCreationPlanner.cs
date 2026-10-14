using System;
using System.Collections.Generic;
using System.Linq;
using IOStudio.Models.Motion;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 一条「自动新建设备」的计划 —— 从 .motion 反推出这台设备该长什么样。
    ///
    /// <para><b>为什么必须是"复制已有设备"而不是凭空造</b>: 设备结点里有两个东西从 .motion
    /// 推不出来 —— <c>DllName</c> (驱动插件名) 与 <c>OAction → 通道</c> 的映射。
    /// native 侧对 <c>DllName</c> 解析不出来的 External 设备<b>直接不建</b>, 而动作到通道的映射
    /// 只存在于设备表里。所以唯一能造出"真的能用"的设备的办法, 就是照抄一台已经在工作的设备:
    /// 驱动名照抄、动作用 .motion 里实际用到的那几个、通道从源设备同名动作上照抄。</para>
    /// </summary>
    public sealed class DeviceCreationPlan
    {
        /// <summary>要新建的设备名 (来自 .motion 里那个没人认领的名字)。</summary>
        public string DeviceName { get; set; } = "";

        /// <summary>驱动插件名 (复制自源设备)。</summary>
        public string DllName { get; set; } = "";

        /// <summary>槽位号 (复制自源设备)。</summary>
        public int Index { get; set; }

        /// <summary>复制来源的设备名; 为空表示无从复制 (设备表里没有可克隆的设备)。</summary>
        public string SourceDeviceName { get; set; } = "";

        /// <summary>要一并建出的动作 (通道已尽量从源设备同名动作补齐)。</summary>
        public List<OActionCompletion> Actions { get; set; } = new();

        /// <summary>
        /// 源设备上没有同名动作、因而补不出通道的动作名。
        /// 这些会被写成没有 <c>&lt;Key&gt;</c> 的结点吗 —— <b>不会</b>: 见 <see cref="DeviceCreationPlanner"/>。
        /// </summary>
        public List<string> ActionsWithoutChannels { get; set; } = new();

        /// <summary>没有可复制来源 (只能靠用户指定驱动) 时为 true。</summary>
        public bool NeedsDriverChoice => string.IsNullOrEmpty(SourceDeviceName);

        /// <summary>
        /// 计划是否可执行 —— 有驱动名即可。动作为空也算可执行:
        /// 冷启动 (设备表为空) 时推不出任何通道, 先建出设备骨架, 之后再用「补全」逐条补动作。
        /// </summary>
        public bool IsViable => !string.IsNullOrWhiteSpace(DllName);

        /// <summary>建成后还需要人工补动作 (没推断出任何带通道的动作)。</summary>
        public bool NeedsActionsLater => Actions.Count == 0;
    }

    /// <summary>
    /// 「自动新建设备」的计划生成 —— 纯逻辑, 不碰文件。
    ///
    /// <para>与 <see cref="DeviceSchemaMatcher"/> 同为可测的判定层: 选错源设备会造出一台
    /// 驱动不对、通道不对的设备, 那比不做更坏 (面板不再报警, 输出照样丢), 值得有测试盯着。</para>
    /// </summary>
    public static class DeviceCreationPlanner
    {
        /// <summary>
        /// 在现有设备里挑一台最值得复制的 —— 按"与所需动作的重合度"计分。
        ///
        /// <para>决策顺序: 重合动作多的优先 → 动作总数多的优先 (更可能是功能完整的同类设备)
        /// → 设备名序 (保证同样的输入永远得到同样的结果, 不让建议随机漂移)。</para>
        /// </summary>
        public static DeviceSchemaInfo? FindSource(
            IReadOnlyList<DeviceSchemaInfo> table,
            IReadOnlyList<string> neededActions
        )
        {
            if (table is null || table.Count == 0)
                return null;

            var needed = new HashSet<string>(
                (neededActions ?? Array.Empty<string>()).Where(a => !string.IsNullOrWhiteSpace(a)),
                StringComparer.OrdinalIgnoreCase
            );

            return table
                .Select(d => (Device: d, Score: CountOverlap(d, needed)))
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Device.OActions.Count)
                .ThenBy(x => x.Device.DeviceName, StringComparer.Ordinal)
                .Select(x => x.Device)
                .FirstOrDefault();
        }

        private static int CountOverlap(DeviceSchemaInfo device, HashSet<string> needed) =>
            needed.Count(a =>
                device.OActions.Any(oa =>
                    string.Equals(oa.OActionName, a, StringComparison.OrdinalIgnoreCase)
                )
            );

        /// <summary>
        /// 生成计划。设备表为空 (或没有可用驱动) 时返回的计划的 <see cref="DeviceCreationPlan.IsViable"/>
        /// 为 false —— 由界面接手让用户指定驱动, 而不是悄悄造一台 native 不会加载的设备。
        /// </summary>
        public static DeviceCreationPlan Build(
            string deviceName,
            IReadOnlyList<string> neededActions,
            IReadOnlyList<DeviceSchemaInfo> table
        )
        {
            var plan = new DeviceCreationPlan { DeviceName = deviceName ?? "" };
            var source = FindSource(table, neededActions);

            if (source is null || string.IsNullOrWhiteSpace(source.DllName))
                return plan; // 无从复制 —— 驱动留给用户选

            plan.SourceDeviceName = source.DeviceName;
            plan.DllName = source.DllName;
            plan.Index = source.Index;

            foreach (var action in (neededActions ?? Array.Empty<string>()).Where(a => !string.IsNullOrWhiteSpace(a)))
            {
                var match = source.OActions.FirstOrDefault(oa =>
                    string.Equals(oa.OActionName, action, StringComparison.OrdinalIgnoreCase)
                );

                var channels = (match?.KeyNames ?? new List<string>())
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // 源设备没有这个动作 → 补不出通道。这种动作不进计划:
                // 没有 <Key> 的 OAction 会通过 native 的存在性判断却一个通道都不下发,
                // 比不建更能骗人 (面板不再报警, 输出照样丢)。
                if (channels.Count == 0)
                {
                    plan.ActionsWithoutChannels.Add(action);
                    continue;
                }

                plan.Actions.Add(
                    new OActionCompletion
                    {
                        DeviceName = plan.DeviceName,
                        OActionName = action,
                        Channels = channels,
                    }
                );
            }

            return plan;
        }

        /// <summary>
        /// 设备表里没有可复制的设备时, 用用户指定的驱动插件补一份计划。
        /// 同样只收有通道的动作 —— 理由见 <see cref="Build"/>。
        /// </summary>
        public static DeviceCreationPlan BuildFromDriver(
            string deviceName,
            string dllName,
            int index,
            IReadOnlyList<OActionCompletion> actionsWithChannels
        )
        {
            var plan = new DeviceCreationPlan
            {
                DeviceName = deviceName ?? "",
                DllName = dllName ?? "",
                Index = index,
            };

            foreach (var action in actionsWithChannels ?? Array.Empty<OActionCompletion>())
            {
                var channels = (action.Channels ?? new List<string>())
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (channels.Count == 0)
                {
                    plan.ActionsWithoutChannels.Add(action.OActionName);
                    continue;
                }

                plan.Actions.Add(
                    new OActionCompletion
                    {
                        DeviceName = plan.DeviceName,
                        OActionName = action.OActionName,
                        Channels = channels,
                    }
                );
            }

            return plan;
        }
    }
}
