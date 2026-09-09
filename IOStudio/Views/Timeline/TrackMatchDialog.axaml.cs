using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Layout;
using AvaGrid = Avalonia.Controls.Grid;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;

namespace IOStudio.Views.Timeline;

/// <summary>
/// 预设通道 → 轨道 匹配确认对话框。
/// 当多通道预设落轨存在"需新建/未匹配"通道时弹出, 用户可逐通道选择目标轨道或新建。
/// </summary>
public partial class TrackMatchDialog : Window
{
    private readonly List<MotionTrack> _tracks;
    private readonly List<(string Role, ComboBox Combo)> _rows = new();

    /// <summary>用户是否确认 (未确认 = 取消落轨)。</summary>
    public bool Confirmed { get; private set; }

    public TrackMatchDialog()
    {
        InitializeComponent();
    }

    public TrackMatchDialog(MotionTimeline timeline, PresetPlacementPlan plan)
        : this()
    {
        _tracks = timeline?.Tracks?.ToList() ?? new List<MotionTrack>();

        var channelList = this.FindControl<ItemsControl>("ChannelList");
        if (channelList == null)
            return;

        foreach (var item in plan.Items)
        {
            var combo = new ComboBox
            {
                MinHeight = 26,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(6, 2),
            };
            foreach (var t in _tracks)
                combo.Items.Add(FormatTrack(t));
            int newTrackIndex = combo.Items.Add("＋ 新建轨道");

            // 默认选中: 建议轨道优先; 无建议时默认"新建轨道"
            int selectedIndex = newTrackIndex;
            if (item.SuggestedTrackId != null)
            {
                int idx = _tracks.FindIndex(t => t.Id == item.SuggestedTrackId);
                if (idx >= 0)
                    selectedIndex = idx;
            }
            combo.SelectedIndex = Math.Clamp(selectedIndex, 0, combo.Items.Count - 1);
            _rows.Add((item.Role, combo));

            // 行: [角色/通道] [轨道下拉]
            var grid = new AvaGrid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(96)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            var roleText = new TextBlock
            {
                Text = string.IsNullOrEmpty(item.Role) ? "(主通道)" : item.Role,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.Parse("#4FC3F7")),
            };
            AvaGrid.SetColumn(roleText, 0);
            AvaGrid.SetColumn(combo, 1);
            grid.Children.Add(roleText);
            grid.Children.Add(combo);
            channelList.Items.Add(grid);
        }
    }

    /// <summary>读取用户映射: role → trackId (null = 新建轨道)。</summary>
    public Dictionary<string, string?> GetMapping()
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (role, combo) in _rows)
        {
            int idx = combo.SelectedIndex;
            map[role] = idx >= 0 && idx < _tracks.Count ? _tracks[idx].Id : null;
        }
        return map;
    }

    private static string FormatTrack(MotionTrack t)
    {
        if (!string.IsNullOrEmpty(t.DeviceName) && !string.IsNullOrEmpty(t.OActionName))
            return $"{t.Label}  ({t.DeviceName}.{t.OActionName})";
        if (!string.IsNullOrEmpty(t.DeviceName) && !string.IsNullOrEmpty(t.OAxisChannel))
            return $"{t.Label}  ({t.DeviceName}.{t.OAxisChannel})";
        return string.IsNullOrEmpty(t.Label) ? "未命名轨道" : t.Label;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }
}
