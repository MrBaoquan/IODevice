using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;

namespace IOStudio.ViewModels.Timeline
{
    /// <summary>
    /// 设备绑定丢失的检测与修复。
    ///
    /// <para><b>要解决的问题</b>: <c>.motion</c> 里的设备名是设计上写死的, 而
    /// <c>Config/IODevice.xml</c> 是它与真实插件之间的逻辑映射表。当映射表里没有这个名字时,
    /// 播放<b>不报错</b> —— native 侧直接 return, 轨道输出静默丢弃, 现场表现为"动作正常播放但设备不动"。
    /// 这里把那种沉默变成一次可见的提示 + 一次可撤销的批量修复。</para>
    /// </summary>
    public partial class TimelineEditorViewModel
    {
        /// <summary>打开文件后待做的绑定检查 (等窗口显示出来再弹, 否则弹窗没有宿主)。</summary>
        private bool _pendingBindingCheck;

        /// <summary>本次会话已自动提示过的 (文件路径, 未解析设备名集合) —— 不重复打扰。</summary>
        private readonly HashSet<string> _autoPromptedBindings = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 收集"设备名无人认领"的轨道, 按设备名聚合。
        /// 设备名为空的轨道不算 (那是 <c>track.no_device</c>, 由预检单独报)。
        /// </summary>
        internal List<UnresolvedDeviceBinding> CollectUnresolvedBindings()
        {
            var result = new List<UnresolvedDeviceBinding>();
            if (Timeline?.Tracks is null)
                return result;

            var byDevice = Tracks
                .Where(t => !string.IsNullOrWhiteSpace(t.DeviceName))
                .Where(t => !MotionDeviceCatalog.IsDeclared(t.DeviceName))
                .GroupBy(t => t.DeviceName, StringComparer.OrdinalIgnoreCase);

            foreach (var group in byDevice)
            {
                var binding = new UnresolvedDeviceBinding
                {
                    DeviceName = group.Key,
                    TrackCount = group.Count(),
                    TrackIds = group.Select(t => t.Id).ToList(),
                    Roles = group
                        .Select(t => t.Role)
                        .Where(r => !string.IsNullOrWhiteSpace(r))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    Channels = group
                        .Select(DescribeOutput)
                        .Where(c => !string.IsNullOrWhiteSpace(c))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    OutputTypeText = DescribeOutputTypes(group),
                };

                ApplyRoleSuggestion(binding);
                result.Add(binding);
            }

            return result;
        }

        private static string DescribeOutput(TrackViewModel track)
        {
            var model = track.Track;
            if (model is null)
                return "";
            return string.Equals(model.OutputType, "oaxis", StringComparison.OrdinalIgnoreCase)
                ? model.OAxisChannel
                : model.OActionName;
        }

        private static string DescribeOutputTypes(IEnumerable<TrackViewModel> tracks)
        {
            var types = tracks
                .Select(t => t.Track?.OutputType ?? "")
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (types.Count == 0)
                return "";
            if (types.Count == 1)
                return string.Equals(types[0], "oaxis", StringComparison.OrdinalIgnoreCase)
                    ? "OAxis 通道"
                    : "OAction 动作";
            return string.Join(" + ", types);
        }

        /// <summary>
        /// 按 role 给出建议目标设备 —— 同一设备表下所有 role 都建议同一个设备,
        /// 所以取第一个能匹配上的 role 即可; 匹配结果同时作为"这个建议为什么合理"的说明。
        /// </summary>
        private static void ApplyRoleSuggestion(UnresolvedDeviceBinding binding)
        {
            var parts = new List<string>();
            string? suggested = null;

            foreach (var role in binding.Roles.Take(4))
            {
                var hit = MotionDeviceCatalog.SuggestForRole(role);
                if (hit is null)
                    continue;

                suggested ??= hit.DeviceName;
                string channel = hit.IsOAxis ? hit.OAxisChannel ?? "" : hit.OActionName ?? "";
                parts.Add($"{role} → {hit.DeviceName}.{channel}");
            }

            binding.SuggestedDeviceName = suggested;
            binding.SuggestionText = parts.Count > 0 ? string.Join("   ", parts) : "";
        }

        /// <summary>打开文件后调用 —— 标记"窗口显示出来后要检查一次绑定"。</summary>
        internal void QueueBindingCheckAfterOpen() => _pendingBindingCheck = true;

        /// <summary>
        /// 由窗口的 Opened 事件调用: 窗口已经显示, 可以安全弹模态框了。
        /// </summary>
        public async Task RunPendingBindingCheckAsync()
        {
            if (!_pendingBindingCheck)
                return;

            _pendingBindingCheck = false;
            await CheckDeviceBindingsAsync(autoPrompt: true);
        }

        /// <summary>
        /// 检查设备与动作绑定并 (在需要时) 弹出修复对话框。
        ///
        /// <para>两类问题一起报: 设备名没人认领 (改设备名 / 新建设备) 与动作名没在该设备上声明
        /// (往设备表补结点)。它们都是"播放正常、设备不动"的静默失效, 分两处报只会让人修一半。</para>
        /// </summary>
        /// <param name="autoPrompt">
        /// true = 自动触发: 只在确有问题且本会话没提示过才弹窗 (否则每次打开/播放都被拦);
        /// false = 手动入口: 即使没问题也给一句确认反馈。
        /// </param>
        public async Task CheckDeviceBindingsAsync(bool autoPrompt)
        {
            var bindings = CollectUnresolvedBindings();
            var actionBindings = CollectUndeclaredOActions();

            if (bindings.Count == 0 && actionBindings.Count == 0)
            {
                if (!autoPrompt)
                    PushStatus("所有轨道的设备与动作绑定正常");
                return;
            }

            if (autoPrompt)
            {
                string key =
                    (CurrentFilePath ?? "")
                    + "|"
                    + string.Join(",", bindings.Select(b => b.DeviceName).OrderBy(n => n))
                    + "|"
                    + string.Join(
                        ",",
                        actionBindings
                            .Select(b => b.DeviceName + "." + b.OActionName)
                            .OrderBy(n => n)
                    );
                if (!_autoPromptedBindings.Add(key))
                    return;
            }

            if (_dialogService is null)
            {
                // 无对话框服务 (如 CLI / 无头路径): 只把问题说清楚, 不静默。
                PushStatus(DescribeBindingProblems(bindings, actionBindings));
                return;
            }

            var dialog = new IOStudio.Views.Timeline.DeviceBindingDialog(
                bindings,
                actionBindings,
                MotionDeviceCatalog.IsTableAvailable,
                BuildDeviceCreationPlans()
            );
            await _dialogService.ShowDialogAsync(dialog);

            if (!dialog.Confirmed)
                return;

            // 顺序有讲究, 三步都按"前一步可能改变后一步的输入"排:
            // 1) 补动作: 按"设备已声明"筛选, 而新建/改名会让设备变得可补
            // 2) 建设备: 新设备自带它在 .motion 里用到的动作, 改名则不改动设备表
            // 3) 改设备名: 最后做, 让前两步都基于原始名字判断, 不会互相干扰
            ApplyOActionCompletion(dialog.GetCompletionPlan());
            foreach (var plan in dialog.GetSelectedCreationPlans())
                ApplyDeviceCreation(plan);
            ApplyDeviceRepair(dialog.GetRepairMap(), bindings);
        }

        private static string DescribeBindingProblems(
            IReadOnlyList<UnresolvedDeviceBinding> bindings,
            IReadOnlyList<UndeclaredOActionBinding> actionBindings
        )
        {
            var parts = new List<string>();

            if (bindings.Count > 0)
            {
                int affected = bindings.Sum(b => b.TrackCount);
                parts.Add(
                    $"{bindings.Count} 个设备未在设备表中声明, 影响 {affected} 条轨道"
                        + $" (首个: {bindings[0].DeviceName})"
                );
            }

            if (actionBindings.Count > 0)
            {
                int affected = actionBindings.Sum(b => b.TrackCount);
                parts.Add(
                    $"{actionBindings.Count} 个动作未在设备上声明, 影响 {affected} 条轨道"
                        + $" (首个: {actionBindings[0].DeviceName}.{actionBindings[0].OActionName})"
                );
            }

            return "绑定问题: " + string.Join("；", parts) + " —— 播放时这些轨道输出会被丢弃";
        }


        /// <summary>
        /// 应用批量映射。默认语义 (用户已确认):<b>只改设备名</b>, 保留通道与输出类型不变,
        /// 一次操作整体可撤销。
        ///
        /// <para>不改通道是刻意的: 通道是内容语义, 且设备表推断出的通道集不完整
        /// (只生成 <c>OAxis_00..31</c>, 而实际内容可能用 <c>OAxis_48</c>),
        /// 拿它去"纠正"会误杀正确内容。</para>
        /// </summary>
        internal void ApplyDeviceRepair(
            IReadOnlyDictionary<string, string> repairMap,
            IReadOnlyList<UnresolvedDeviceBinding> bindings
        )
        {
            if (Timeline is null || repairMap.Count == 0)
                return;

            var effective = repairMap
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            if (effective.Count == 0)
                return;

            string beforeJson = MotionFileReader.ToJson(Timeline);
            var candidate = MotionFileReader.ReadFromJson(beforeJson);
            if (candidate?.Tracks is null)
                return;

            int changed = 0;
            foreach (var track in candidate.Tracks)
            {
                if (
                    !string.IsNullOrWhiteSpace(track.DeviceName)
                    && effective.TryGetValue(track.DeviceName, out var newName)
                    && !string.Equals(track.DeviceName, newName, StringComparison.Ordinal)
                )
                {
                    track.DeviceName = newName;
                    changed++;
                }
            }

            if (changed == 0)
            {
                PushStatus("设备绑定未发生变更");
                return;
            }

            string afterJson = MotionFileReader.ToJson(candidate);
            string label = string.Join(
                ", ",
                effective.Select(kv => $"{kv.Key}→{kv.Value}")
            );

            _undoRedo.Execute(
                new LambdaCommand(
                    $"修复设备绑定 ({label})",
                    () =>
                    {
                        var redoTimeline = MotionFileReader.ReadFromJson(afterJson);
                        if (redoTimeline is not null)
                            LoadTimeline(redoTimeline);
                    },
                    () =>
                    {
                        var undoTimeline = MotionFileReader.ReadFromJson(beforeJson);
                        if (undoTimeline is not null)
                            LoadTimeline(undoTimeline);
                    }
                )
            );

            MarkDirty();

            // 先跑预检 (它看到的是修复后的状态), 再推摘要 —— 顺序反过来摘要会被预检覆盖。
            ReportValidationSummary();

            PushStatus($"已修复 {changed} 条轨道的设备绑定 ({label})，通道与输出类型未改动");
        }
    }
}
