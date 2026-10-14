using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using AvaGrid = Avalonia.Controls.Grid;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;

namespace IOStudio.Views.Timeline;

/// <summary>
/// 「设备与动作绑定问题」对话框 —— 把 .motion 里两类静默失效一起摆出来, 各给一个一键修复:
///
/// <list type="bullet">
/// <item>A. 设备名没人认领 → 批量映射到本机真实设备, 或用这个名字新建设备。
/// 默认语义: <b>只改设备名</b>, 通道与输出类型原样保留 —— 设备表推断出的通道集不完整
/// (只到 <c>OAxis_31</c>), 拿它去"纠正"通道会误杀正确内容。</item>
/// <item>B. 设备在、动作没声明 → 往 <c>Config/IODevice.xml</c> 补一个 <c>&lt;OAction&gt;</c> 结点。
/// 这一类在 native 侧<b>连"未解析"计数都没有</b> (SetDO 查不到动作名直接 return 0)。</item>
/// </list>
///
/// <para>两类都按 (设备 / 设备+动作) 聚合而不是逐轨道列: 一个未解析名字通常影响一整组轨道,
/// 修复动作本来也是一次改到位。</para>
/// </summary>
public partial class DeviceBindingDialog : Window
{
    private const string NoChangeLabel = "（不修改）";

    /// <summary>通道名提示里最多列几个 —— 再多就成噪音了。</summary>
    private const int VocabularyHintLimit = 12;

    private readonly List<(UnresolvedDeviceBinding Binding, ComboBox Combo)> _rows = new();
    private readonly List<(UndeclaredOActionBinding Binding, TextBox ChannelBox)> _actionRows = new();

    /// <summary>每行的"自动新建设备"下拉项下标 (-1 = 该行没有这一项) 与它对应的计划。</summary>
    private readonly Dictionary<int, DeviceCreationPlan> _createItemIndexes = new();

    /// <summary>本对话框里出现过的驱动下拉: 行下标 → 下拉。用于读出用户选的驱动。</summary>
    private readonly Dictionary<int, ComboBox> _driverCombos = new();

    /// <summary>驱动下拉的显示文本 → 真实 DllName (每个对话框各自一份, 不跨实例共享)。</summary>
    private readonly Dictionary<string, string> _driverChoices = new(StringComparer.Ordinal);

    /// <summary>本对话框拿到的自动新建计划 (按设备名)。</summary>
    private IReadOnlyDictionary<string, DeviceCreationPlan> _creationPlans =
        new Dictionary<string, DeviceCreationPlan>(StringComparer.OrdinalIgnoreCase);

    /// <summary>用户是否确认 (未确认 = 不做任何修改)。</summary>
    public bool Confirmed { get; private set; }

    public DeviceBindingDialog()
    {
        InitializeComponent();
    }

    public DeviceBindingDialog(
        IReadOnlyList<UnresolvedDeviceBinding> bindings,
        IReadOnlyList<UndeclaredOActionBinding> actionBindings,
        bool tableAvailable
    )
        : this(
            bindings,
            actionBindings,
            tableAvailable,
            new Dictionary<string, DeviceCreationPlan>(StringComparer.OrdinalIgnoreCase)
        )
    { }

    public DeviceBindingDialog(
        IReadOnlyList<UnresolvedDeviceBinding> bindings,
        IReadOnlyList<UndeclaredOActionBinding> actionBindings,
        bool tableAvailable,
        IReadOnlyDictionary<string, DeviceCreationPlan> creationPlans
    )
        : this()
    {
        bindings ??= Array.Empty<UnresolvedDeviceBinding>();
        actionBindings ??= Array.Empty<UndeclaredOActionBinding>();
        _creationPlans = creationPlans ?? _creationPlans;

        BuildHeader(bindings, actionBindings, tableAvailable);

        if (this.FindControl<Border>("NoTableBanner") is { } banner)
            banner.IsVisible = !tableAvailable;

        if (this.FindControl<Button>("ApplyButton") is { } applyBtn)
        {
            // 没有可用目标设备时"批量映射"无从谈起; 但若 B 段有货、或能自动新建设备, "应用"仍然可用。
            // 冷启动计划 (要靠驱动下拉选) 的 IsViable 是 false, 所以这里看的是"有没有计划"。
            applyBtn.IsEnabled =
                tableAvailable || actionBindings.Count > 0 || _creationPlans.Count > 0;
        }

        BuildDeviceRows(bindings, tableAvailable);
        BuildOActionRows(actionBindings);

        if (this.FindControl<TextBlock>("FooterHint") is { } footer)
        {
            footer.Text = tableAvailable
                ? "改名只改设备名（通道与输出类型保持原样）；补全会往设备表写入 OAction 结点。二者都可整体撤销 (Ctrl+Z)，补全需重启设备服务后生效。"
                : "设备表里没有可映射的目标。选「自动新建设备」可以由 .motion 里实际用到的动作反推出一台设备（复制现有设备的驱动与动作映射）；表为空时需指定驱动插件。";
        }
    }

    private void BuildHeader(
        IReadOnlyList<UnresolvedDeviceBinding> bindings,
        IReadOnlyList<UndeclaredOActionBinding> actionBindings,
        bool tableAvailable
    )
    {
        if (this.FindControl<TextBlock>("HeaderText") is { } header)
        {
            header.Text = (bindings.Count, actionBindings.Count) switch
            {
                (1, 0) => $"未解析设备 {bindings[0].DeviceName}",
                (> 0, 0) => $"{bindings.Count} 个设备未在设备表中声明",
                (0, 1) => $"{actionBindings[0].DeviceName} 未声明动作 {actionBindings[0].OActionName}",
                (0, > 0) => $"{actionBindings.Count} 个动作未在所属设备上声明",
                _ => "设备与动作绑定问题",
            };
        }

        if (this.FindControl<TextBlock>("SubText") is { } sub)
        {
            var parts = new List<string>();
            if (bindings.Count > 0)
                parts.Add($"设备名必须与设备表 (Config/IODevice.xml) 一致，当前影响 {bindings.Sum(b => b.TrackCount)} 条轨道");
            if (actionBindings.Count > 0)
                parts.Add($"动作名必须是所属设备已声明的 OAction，当前影响 {actionBindings.Sum(b => b.TrackCount)} 条轨道");
            if (!tableAvailable)
                parts.Add("设备表为空或读不到 —— 本机没有任何真实设备");

            sub.Text = string.Join("；", parts) + "。这些轨道的输出在播放时会被静默丢弃。";
        }

        if (this.FindControl<TextBlock>("DeviceSectionHeader") is { } deviceHeader)
        {
            deviceHeader.IsVisible = bindings.Count > 0 && actionBindings.Count > 0;
            deviceHeader.Text = "■■ 设备未在设备表中声明（改设备名，或新建设备）";
        }

        if (this.FindControl<TextBlock>("OActionSectionHeader") is { } actionHeader)
        {
            actionHeader.IsVisible = actionBindings.Count > 0;
            actionHeader.Text = bindings.Count > 0
                ? "■■ 设备已声明，但动作没声明（一键写入设备表）"
                : "动作未在设备上声明 —— 可以一键写入设备表";
        }
    }

    private void BuildDeviceRows(
        IReadOnlyList<UnresolvedDeviceBinding> bindings,
        bool tableAvailable
    )
    {
        var list = this.FindControl<ItemsControl>("BindingList");
        if (list is null)
            return;

        foreach (var binding in bindings)
        {
            var combo = new ComboBox
            {
                MinHeight = 26,
                FontSize = 12,
                MinWidth = 220,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(6, 2),
            };

            combo.Items.Add(NoChangeLabel);

            // 自动新建: 作为下拉里的一项, 而不是另开一个按钮 ——
            // "这台设备表里没有" 与 "把它建成什么" 本来就是同一件事的两面。
            _creationPlans.TryGetValue(binding.DeviceName, out var plan);
            ComboBox? driverCombo = null;
            if (plan is not null && plan.NeedsDriverChoice)
            {
                // 冷启动 (设备表里没有可复制的设备): 驱动只能由用户点。
                // 设备骨架先建出来, 动作之后走 B 段逐条补 —— 因为没有源可抄, 通道推不出来。
                driverCombo = BuildDriverCombo();
            }

            int createItemIndex = -1;
            if (plan is not null && (plan.IsViable || driverCombo is not null))
            {
                createItemIndex = combo.Items.Count;
                combo.Items.Add(FormatCreateItem(plan, driverCombo));
            }

            int deviceOffset = combo.Items.Count;
            foreach (var device in MotionDeviceCatalog.Devices)
                combo.Items.Add(FormatDevice(device));

            int selected = 0;
            if (
                tableAvailable
                && !string.IsNullOrEmpty(binding.SuggestedDeviceName)
                && MotionDeviceCatalog.IsDeclared(binding.SuggestedDeviceName)
            )
            {
                int idx = MotionDeviceCatalog
                    .Devices.Select((d, i) => (d, i))
                    .Where(x =>
                        string.Equals(
                            x.d.DeviceName,
                            binding.SuggestedDeviceName,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .Select(x => x.i)
                    .DefaultIfEmpty(-1)
                    .First();
                if (idx >= 0)
                    selected = deviceOffset + idx;
            }

            // 一台可映射的设备都没有、但能自动新建时, 默认就落在"自动新建"上 ——
            // 这正是用户打开这个对话框要解决的事。
            if (MotionDeviceCatalog.Devices.Count == 0 && createItemIndex >= 0)
                selected = createItemIndex;

            combo.SelectedIndex = Math.Clamp(selected, 0, combo.Items.Count - 1);
            if (createItemIndex >= 0)
            {
                _createItemIndexes[_rows.Count] = plan!;
                if (driverCombo is not null)
                    _driverCombos[_rows.Count] = driverCombo;
            }

            _rows.Add((binding, combo));
            list.Items.Add(
                BuildRow(binding, combo, plan, deviceOffset, createItemIndex, driverCombo)
            );
        }
    }

    /// <summary>驱动插件下拉 —— 只用本机真实安装过的插件, 不给可选项里塞不存在的名字。</summary>
    private ComboBox BuildDriverCombo()
    {
        var combo = new ComboBox
        {
            MinHeight = 26,
            FontSize = 12,
            MinWidth = 220,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(6, 2),
            PlaceholderText = "选择驱动插件...",
        };

        var plugins = Services.ExternalDeviceService.Instance.GetAvailableDevices();
        foreach (var plugin in plugins)
        {
            var text = string.IsNullOrWhiteSpace(plugin.DisplayName)
                ? plugin.DllName
                : $"{plugin.DisplayName}  ({plugin.DllName})";
            combo.Items.Add(text);
            _driverChoices[text] = plugin.DllName;
        }

        combo.SelectedIndex = plugins.Count > 0 ? 0 : -1;
        return combo;
    }

    private static string FormatCreateItem(DeviceCreationPlan plan, ComboBox? driverCombo) =>
        plan.NeedsDriverChoice || driverCombo is not null
            ? "【自动新建：需指定驱动】"
            : $"【自动新建：复制 {plan.SourceDeviceName}】";

    private void BuildOActionRows(IReadOnlyList<UndeclaredOActionBinding> actionBindings)
    {
        var list = this.FindControl<ItemsControl>("OActionList");
        if (list is null)
            return;

        foreach (var binding in actionBindings)
        {
            var box = new TextBox
            {
                MinHeight = 26,
                FontSize = 12,
                MinWidth = 220,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(6, 2),
                Text = binding.SuggestedChannel,
                Watermark = "如 OAxis_06（多个用逗号分隔）",
            };

            _actionRows.Add((binding, box));
            list.Items.Add(BuildOActionRow(binding, box));
        }
    }

    /// <summary>读出 A 段的批量映射: 原设备名 → 目标设备名。选了"不修改"与"自动新建"的项不在结果里。</summary>
    public Dictionary<string, string> GetRepairMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var devices = MotionDeviceCatalog.Devices;

        for (int row = 0; row < _rows.Count; row++)
        {
            var (binding, combo) = _rows[row];
            if (_createItemIndexes.ContainsKey(row))
            {
                // 有自动新建项时设备项整体后移一位
                if (combo.SelectedIndex == 1)
                    continue;
            }

            int deviceOffset = 1 + (_createItemIndexes.ContainsKey(row) ? 1 : 0);
            int idx = combo.SelectedIndex - deviceOffset;
            if (idx < 0 || idx >= devices.Count)
                continue;

            string target = devices[idx].DeviceName;
            if (!string.Equals(target, binding.DeviceName, StringComparison.Ordinal))
                map[binding.DeviceName] = target;
        }

        return map;
    }

    /// <summary>读出被选中"自动新建"的设备计划 (用户没选就为空)。</summary>
    public List<DeviceCreationPlan> GetSelectedCreationPlans()
    {
        var plans = new List<DeviceCreationPlan>();

        for (int row = 0; row < _rows.Count; row++)
        {
            if (!_createItemIndexes.TryGetValue(row, out var plan))
                continue;
            if (_rows[row].Combo.SelectedIndex != 1)
                continue;

            // 冷启动: 驱动取自用户在那个下拉里点的插件
            if (_driverCombos.TryGetValue(row, out var driverCombo))
            {
                string? display = driverCombo.SelectedItem as string;
                if (
                    display is null
                    || !_driverChoices.TryGetValue(display, out string? dllName)
                    || string.IsNullOrWhiteSpace(dllName)
                )
                {
                    continue; // 没选驱动 = 没法建, 静默跳过而不是造一台 native 不认的设备
                }

                plan = DeviceCreationPlanner.BuildFromDriver(
                    plan.DeviceName,
                    dllName,
                    plan.Index,
                    plan.Actions
                );
            }

            plans.Add(plan);
        }

        return plans;
    }

    /// <summary>读出 B 段要写入设备表的结点。没填通道的行不在结果里 (Key 为空的 OAction 不能写)。</summary>
    public List<OActionCompletion> GetCompletionPlan()
    {
        var plan = new List<OActionCompletion>();

        foreach (var (binding, box) in _actionRows)
        {
            var channels = OActionCompletion.ParseChannels(box.Text);
            if (channels.Count == 0)
                continue;

            plan.Add(
                new OActionCompletion
                {
                    DeviceName = binding.DeviceName,
                    OActionName = binding.OActionName,
                    Channels = channels,
                }
            );
        }

        return plan;
    }

    private Control BuildRow(
        UnresolvedDeviceBinding binding,
        ComboBox combo,
        DeviceCreationPlan? plan,
        int deviceOffset,
        int createItemIndex,
        ComboBox? driverCombo
    )
    {
        var panel = new StackPanel { Spacing = 5 };

        // 第 1 行: 设备名 + 影响轨道数
        var titleGrid = new AvaGrid();
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var title = new TextBlock
        {
            Text = $"未解析设备 {binding.DeviceName}",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = new SolidColorBrush(Color.Parse("#E57373")),
        };
        AvaGrid.SetColumn(title, 0);

        var count = new TextBlock
        {
            Text = $"影响 {binding.TrackCount} 条轨道",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse("#9E9E9E")),
        };
        AvaGrid.SetColumn(count, 1);

        titleGrid.Children.Add(title);
        titleGrid.Children.Add(count);
        panel.Children.Add(titleGrid);

        // 第 2 行: 智能建议
        if (!string.IsNullOrEmpty(binding.SuggestionText))
        {
            panel.Children.Add(
                new TextBlock
                {
                    Text = $"按 role 建议: {binding.SuggestionText}",
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.Parse("#4FC3F7")),
                }
            );
        }

        // 第 3 行: 映射目标
        var mapGrid = new AvaGrid();
        mapGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(72)));
        mapGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

        var mapLabel = new TextBlock
        {
            Text = "映射到:",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse("#B0BEC5")),
        };
        AvaGrid.SetColumn(mapLabel, 0);
        AvaGrid.SetColumn(combo, 1);
        mapGrid.Children.Add(mapLabel);
        mapGrid.Children.Add(combo);
        panel.Children.Add(mapGrid);

        // 第 4 行: 保留项回显 (让"什么都不改"是可见的, 而不是靠信任)
        var keepParts = new List<string>();
        if (binding.Channels.Count > 0)
            keepParts.Add($"保留通道 {string.Join(" / ", binding.Channels)}");
        if (!string.IsNullOrEmpty(binding.OutputTypeText))
            keepParts.Add($"不改输出类型 ({binding.OutputTypeText})");

        if (keepParts.Count > 0)
        {
            panel.Children.Add(
                new TextBlock
                {
                    Text = string.Join("，", keepParts),
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.Parse("#78909C")),
                }
            );
        }

        // 第 5 行: 后果
        panel.Children.Add(
            new TextBlock
            {
                Text = "⚠ 播放时这些轨道的输出会被丢弃",
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.Parse("#FFB74D")),
            }
        );

        // 第 6 行: 自动新建的预告 —— 让"会造出什么"在按下应用之前就看得见。
        // 不从 .motion 凭空造设备: DllName 与动作→通道映射都推不出来, 而 native 侧对
        // DllName 无效的 External 设备是"直接不建" —— 所以只能照抄一台已在工作的设备。
        if (plan is not null)
        {
            panel.Children.Add(
                new TextBlock
                {
                    Text = DescribePlan(plan),
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(
                        Color.Parse(plan.IsViable ? "#A5D6A7" : "#FFB74D")
                    ),
                }
            );

            if (driverCombo is not null)
            {
                // 冷启动: 没有可复制的设备, 驱动必须由人点。
                // 把选错驱动的代价写在旁边 —— native 对 DllName 无效的设备是直接不建。
                var driverGrid = new AvaGrid { Margin = new Thickness(0, 2, 0, 0) };
                driverGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(72)));
                driverGrid.ColumnDefinitions.Add(
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star))
                );

                var driverLabel = new TextBlock
                {
                    Text = "驱动插件:",
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.Parse("#B0BEC5")),
                };
                AvaGrid.SetColumn(driverLabel, 0);
                AvaGrid.SetColumn(driverCombo, 1);
                driverGrid.Children.Add(driverLabel);
                driverGrid.Children.Add(driverCombo);
                panel.Children.Add(driverGrid);

                panel.Children.Add(
                    new TextBlock
                    {
                        Text =
                            "设备表里没有可复制的设备，动作通道推不出来 —— 这里只建出设备骨架，"
                            + "之后再为它补动作。驱动必须是本机已安装的插件，选错等于没建。",
                        FontSize = 10,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(Color.Parse("#78909C")),
                    }
                );
            }

            if (!plan.IsViable && driverCombo is null)
            {
                panel.Children.Add(
                    new TextBlock
                    {
                        Text = "⚠ 设备表里没有可复制的设备，也无法指定驱动 —— 请先在「设备管理」里添加设备。",
                        FontSize = 10,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(Color.Parse("#FFB74D")),
                    }
                );
            }
        }
        else
        {
            // 连"自动新建"都无从谈起 (设备表读不到): 至少给一句出路, 别让人对着空下拉发呆。
            panel.Children.Add(
                new TextBlock
                {
                    Text = "⚠ 设备表不可用，无法自动新建设备 —— 请先在「设备管理」里添加设备。",
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.Parse("#FFB74D")),
                }
            );
        }

        return new Border
        {
            Padding = new Thickness(10, 8),
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.Parse("#1E252B")),
            Child = panel,
        };
    }

    /// <summary>把计划说成人话: 建成什么设备、复制谁的驱动、会写出哪几个动作。</summary>
    private static string DescribePlan(DeviceCreationPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.DllName))
            return $"自动新建 {plan.DeviceName}：设备表里没有可复制的设备，无法自动确定驱动插件。";

        var parts = new List<string>
        {
            $"自动新建 {plan.DeviceName}：驱动 {plan.DllName}（复制自 {plan.SourceDeviceName}）",
        };

        if (plan.Actions.Count > 0)
        {
            parts.Add(
                "动作 "
                    + string.Join(
                        "、",
                        plan.Actions.Select(a =>
                            $"{a.OActionName}→{string.Join("/", a.Channels)}"
                        )
                    )
            );
        }

        if (plan.ActionsWithoutChannels.Count > 0)
        {
            parts.Add(
                $"（{string.Join("、", plan.ActionsWithoutChannels)} 在源设备上没有同名动作，"
                    + "补不出通道，不会写入）"
            );
        }

        return string.Join("；", parts);
    }

    private static Control BuildOActionRow(UndeclaredOActionBinding binding, TextBox channelBox)
    {
        var panel = new StackPanel { Spacing = 5 };

        // 第 1 行: 设备.动作 + 影响轨道数
        var titleGrid = new AvaGrid();
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var title = new TextBlock
        {
            Text = $"{binding.DeviceName} 未声明动作 {binding.OActionName}",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = new SolidColorBrush(Color.Parse("#E57373")),
        };
        AvaGrid.SetColumn(title, 0);

        var count = new TextBlock
        {
            Text = $"影响 {binding.TrackCount} 条轨道",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse("#9E9E9E")),
        };
        AvaGrid.SetColumn(count, 1);

        titleGrid.Children.Add(title);
        titleGrid.Children.Add(count);
        panel.Children.Add(titleGrid);

        // 第 2 行: role 回显 (帮人认出这是哪个动作)
        if (binding.Roles.Count > 0)
        {
            panel.Children.Add(
                new TextBlock
                {
                    Text = $"轨道 role: {string.Join(" / ", binding.Roles)}",
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.Parse("#4FC3F7")),
                }
            );
        }

        // 第 3 行: Key 通道输入
        var inputGrid = new AvaGrid();
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(72)));
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

        var inputLabel = new TextBlock
        {
            Text = "Key 通道:",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse("#B0BEC5")),
        };
        AvaGrid.SetColumn(inputLabel, 0);
        AvaGrid.SetColumn(channelBox, 1);
        inputGrid.Children.Add(inputLabel);
        inputGrid.Children.Add(channelBox);
        panel.Children.Add(inputGrid);

        // 第 4 行: 该设备已经在用的通道名 —— 唯一可靠的参照。
        // 刻意不列合成的 OAxis_00..31: 真实内容会用 OAxis_48 这类越界通道。
        panel.Children.Add(
            new TextBlock
            {
                Text =
                    binding.DeviceChannelVocabulary.Count > 0
                        ? "该设备已在用: "
                            + string.Join(
                                ", ",
                                binding.DeviceChannelVocabulary.Take(VocabularyHintLimit)
                            )
                            + (
                                binding.DeviceChannelVocabulary.Count > VocabularyHintLimit
                                    ? $" …（共 {binding.DeviceChannelVocabulary.Count} 个）"
                                    : ""
                            )
                        : "该设备目前没有任何已声明的动作 —— 通道名请参照设备手册填写",
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.Parse("#78909C")),
            }
        );

        // 第 5 行: 将要写入的结点预览 (填了才有, 让人看得见自己落下什么)
        var preview = new TextBlock
        {
            FontSize = 10,
            FontFamily = new FontFamily("Consolas, monospace"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#A5D6A7")),
        };
        panel.Children.Add(preview);

        channelBox.TextChanged += (_, _) => preview.Text = DescribePreview(binding, channelBox.Text);
        preview.Text = DescribePreview(binding, channelBox.Text);

        // 第 6 行: 后果 + 生效时机
        panel.Children.Add(
            new TextBlock
            {
                Text = "⚠ 该动作的输出会被静默丢弃（连未解析计数都没有）。补全后需重启设备服务才生效。",
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.Parse("#FFB74D")),
            }
        );

        return new Border
        {
            Padding = new Thickness(10, 8),
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.Parse("#1E252B")),
            Child = panel,
        };
    }

    private static string DescribePreview(UndeclaredOActionBinding binding, string? channelText)
    {
        var channels = OActionCompletion.ParseChannels(channelText);
        if (channels.Count == 0)
            return "（填了 Key 通道才会写入 —— 没有 Key 的 OAction 不会下发任何通道）";

        var sb = new StringBuilder();
        sb.Append("<OAction Name=\"").Append(binding.OActionName).Append("\">");
        foreach (var channel in channels)
            sb.Append("<Key Name=\"").Append(channel).Append("\" />");
        sb.Append("</OAction>");
        return "将写入: " + sb;
    }

    private static string FormatDevice(DeviceSchemaInfo device)
    {
        int actions = device.OActions?.Count ?? 0;
        return actions > 0
            ? $"{device.DeviceName}  ({actions} 个动作)"
            : device.DeviceName;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }
}
