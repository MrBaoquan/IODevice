using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace IOStudio.Views.Timeline
{
    /// <summary>速查面板的一条键位。</summary>
    public sealed class ShortcutItem
    {
        public string Keys { get; init; } = "";
        public string Description { get; init; } = "";
    }

    /// <summary>速查面板的一个分组。</summary>
    public sealed class ShortcutGroup
    {
        public string Title { get; init; } = "";
        public List<ShortcutItem> Items { get; init; } = new();
    }

    /// <summary>
    /// 快捷键速查面板 (F1)。集中展示编排器全部键位 —— 避免"功能有但没人知道怎么按"。
    /// 内容为静态清单, 与 KeyboardShortcutService 的映射保持同步。
    /// </summary>
    public partial class ShortcutsWindow : Window
    {
        public ShortcutsWindow()
        {
            InitializeComponent();
            GroupsHost.ItemsSource = BuildGroups();
        }

        private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

        private static List<ShortcutGroup> BuildGroups() =>
            new()
            {
                new ShortcutGroup
                {
                    Title = "选择",
                    Items =
                    {
                        new ShortcutItem { Keys = "点击", Description = "选中关键帧 (单选)" },
                        new ShortcutItem { Keys = "Shift+点击", Description = "加入选择" },
                        new ShortcutItem { Keys = "Ctrl+点击", Description = "切换选择 / 轨道多选" },
                        new ShortcutItem { Keys = "拖拽空白", Description = "框选 (当前片段)" },
                        new ShortcutItem { Keys = "Ctrl+A", Description = "全选当前轨道关键帧" },
                        new ShortcutItem { Keys = "Ctrl+Shift+A", Description = "全选所有轨道关键帧" },
                        new ShortcutItem { Keys = "Ctrl+Shift+I", Description = "反向选择 (当前轨道)" },
                        new ShortcutItem { Keys = "Ctrl+Shift+W", Description = "选中工作区 (In/Out) 内全部关键帧" },
                    },
                },
                new ShortcutGroup
                {
                    Title = "微调 (选中关键帧)",
                    Items =
                    {
                        new ShortcutItem { Keys = "Alt+← / →", Description = "时间微调 ∓10ms" },
                        new ShortcutItem { Keys = "Alt+Shift+← / →", Description = "时间微调 ∓100ms" },
                        new ShortcutItem { Keys = "Alt+↓ / ↑", Description = "值微调 ∓0.01" },
                        new ShortcutItem { Keys = "Alt+Shift+↓ / ↑", Description = "值微调 ∓0.1" },
                        new ShortcutItem { Keys = "检查器 · 时间", Description = "输入绝对时间 (毫秒或 00:03.250)" },
                        new ShortcutItem { Keys = "检查器 · 时间操作", Description = "对齐播放头 / ÷2 / ×2 / 反向 / 量化节拍" },
                    },
                },
                new ShortcutGroup
                {
                    Title = "编辑",
                    Items =
                    {
                        new ShortcutItem { Keys = "Ctrl+Z / Ctrl+Y", Description = "撤销 / 重做" },
                        new ShortcutItem { Keys = "Ctrl+C / Ctrl+V", Description = "复制 / 粘贴关键帧" },
                        new ShortcutItem { Keys = "Ctrl+X", Description = "剪切关键帧" },
                        new ShortcutItem { Keys = "Ctrl+D", Description = "在播放头复制最近关键帧" },
                        new ShortcutItem { Keys = "K", Description = "在播放头添加关键帧" },
                        new ShortcutItem { Keys = "Delete / Backspace", Description = "删除选中关键帧 / 事件" },
                    },
                },
                new ShortcutGroup
                {
                    Title = "播放与导航",
                    Items =
                    {
                        new ShortcutItem { Keys = "Space", Description = "播放 / 暂停" },
                        new ShortcutItem { Keys = "Home / End", Description = "跳到起点 / 末尾" },
                        new ShortcutItem { Keys = "← / →", Description = "上一个 / 下一个关键帧" },
                        new ShortcutItem { Keys = "Shift+← / →", Description = "上一个 / 下一个关键帧" },
                        new ShortcutItem { Keys = "Ctrl+← / →", Description = "上一个 / 下一个标记" },
                        new ShortcutItem { Keys = "↑ / ↓", Description = "选择上一条 / 下一条轨道" },
                    },
                },
                new ShortcutGroup
                {
                    Title = "视图",
                    Items =
                    {
                        new ShortcutItem { Keys = "+ / -", Description = "时间轴放大 / 缩小" },
                        new ShortcutItem { Keys = "F / Ctrl+0", Description = "缩放适配窗口" },
                        new ShortcutItem { Keys = "H", Description = "循环切换轨道高度" },
                        new ShortcutItem { Keys = "Shift+滚轮", Description = "调整轨道高度" },
                    },
                },
                new ShortcutGroup
                {
                    Title = "标记 / 事件 / 工作区",
                    Items =
                    {
                        new ShortcutItem { Keys = "M", Description = "在播放头添加标记" },
                        new ShortcutItem { Keys = "E", Description = "在播放头添加事件" },
                        new ShortcutItem { Keys = "I / O", Description = "设置工作区入点 / 出点" },
                        new ShortcutItem { Keys = "Alt+拖拽", Description = "拖动标记 / 事件" },
                        new ShortcutItem { Keys = "Esc", Description = "清除工作区 / 退出视频全屏" },
                        new ShortcutItem { Keys = "F2", Description = "重命名选中项" },
                    },
                },
                new ShortcutGroup
                {
                    Title = "文件",
                    Items =
                    {
                        new ShortcutItem { Keys = "Ctrl+N / Ctrl+O", Description = "新建 / 打开" },
                        new ShortcutItem { Keys = "Ctrl+S / Ctrl+Shift+S", Description = "保存 / 另存为" },
                        new ShortcutItem { Keys = "Ctrl+E", Description = "导出 (CSV / JSON)" },
                        new ShortcutItem { Keys = "F1", Description = "打开本速查面板" },
                    },
                },
            };
    }
}
