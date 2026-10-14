using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ReactiveUI.Avalonia;
using IOStudio.Controls;
using IOStudio.ViewModels;
using ReactiveUI;
using System;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace IOStudio.Views
{
    public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
    {
        public MainWindow()
        {
            this.WhenActivated(disposeables =>
            {
                disposeables.Add(
                    ViewModel.OnTabChangedCommand.Subscribe(_ =>
                    {
                        this.InvalidateMeasure();
                        this.InvalidateArrange();
                    })
                );
            });
            InitializeComponent();

            // 设置帮助菜单点击事件
            var helpMenuItem = this.FindControl<MenuItem>("HelpReferenceMenuItem");
            if (helpMenuItem != null)
            {
                helpMenuItem.Click += HelpReferenceMenuItem_Click;
            }

            // 设备表子菜单: 项由运行期枚举 Config\IODevice*.xml 生成。
            // 构造时先填一次 —— 只靠 SubmenuOpened 的话, 某些环境下该事件不触发,
            // 结果是"菜单在、里面空的"; 打开时再刷一次以反映期间新增/删除的预设。
            if (this.FindControl<MenuItem>("DeviceTableMenu") is { } tableMenu)
            {
                PopulateDeviceTableMenu(tableMenu);
                tableMenu.SubmenuOpened += (_, _) => PopulateDeviceTableMenu(tableMenu);
            }

            //this.ref_TabControl.DataContextChanged += (e, sender) =>
            //{
            //    Debug.WriteLine(sender.GetType());
            //};
            // AvaloniaXamlLoader.Load(this);

            // this.FindControl<IONodeView>("ionode").ViewModel = new IONodeViewModel { Title = "��!!!!" };
        }

        // ── 更新提醒横幅 (非模态) ──
        //
        // 三个动作都只作用于 ViewModel; 真正的下载/安装仍在 ViewModel 里走独立下载窗口。

        private async void OnUpdateBannerView(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (ViewModel is { } vm)
                await vm.ViewPendingUpdateAsync();
        }

        private void OnUpdateBannerSkip(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
            => ViewModel?.SkipUpdateVersion();

        private void OnUpdateBannerDismiss(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
            => ViewModel?.DismissUpdateBanner();

        // ── 设备表切换 (Config\IODevice*.xml) ──
        //
        // 切换的语义是"下次加载用哪一份" —— 立即重载设备表使切换立刻生效, 但**不改写任何文件**。
        // 于是现场可以在"影动力台 / 仿真台 / 调试台"之间来回切, 而不必互相覆盖配置。

        private void PopulateDeviceTableMenu(MenuItem menu)
        {
            menu.Items.Clear();

            var tables = Models.DeviceTableLocator.Discover();
            string activeName = System.IO.Path.GetFileName(
                Models.DeviceTableLocator.ResolveEffectivePath()
            );

            if (tables.Count == 0)
            {
                menu.Items.Add(new MenuItem { Header = "（Config 目录下没有设备表）", IsEnabled = false });
            }
            else
            {
                foreach (var name in tables)
                {
                    bool isActive = name.Equals(activeName, StringComparison.OrdinalIgnoreCase);
                    var item = new MenuItem
                    {
                        Header = Models.DeviceTableLocator.DescribeLabel(name),
                        Tag = name,
                        Icon = isActive ? new TextBlock { Text = "✓" } : null,
                        // 已经是当前这份就没必要再点一次 (重载会打断设备服务)
                        IsEnabled = !isActive,
                    };
                    item.Click += OnSwitchDeviceTable;
                    menu.Items.Add(item);
                }
            }

            menu.Items.Add(new Separator());
            menu.Items.Add(
                new MenuItem { Header = $"当前: {activeName}", IsEnabled = false }
            );
            var openDir = new MenuItem { Header = "打开设备表所在目录" };
            openDir.Click += OnOpenDeviceTableDir;
            menu.Items.Add(openDir);
            var openCur = new MenuItem { Header = "用编辑器打开当前设备表" };
            openCur.Click += OnOpenCurrentDeviceTable;
            menu.Items.Add(openCur);
        }

        private void OnSwitchDeviceTable(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is not MenuItem { Tag: string fileName } || ViewModel is not { } vm)
                return;

            vm.SwitchDeviceTable(fileName);
        }

        private void OnOpenDeviceTableDir(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
            => Models.EditorLauncher.OpenDirectory(Models.DeviceTableLocator.ConfigDir);

        private void OnOpenCurrentDeviceTable(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
            => Models.EditorLauncher.OpenWithPreferredEditor(
                Models.DeviceTableLocator.ResolveEffectivePath()
            );

        private void HelpReferenceMenuItem_Click(
            object? sender,
            Avalonia.Interactivity.RoutedEventArgs e
        )
        {
            var helpWindow = new HelpWindow();
            helpWindow.ShowDialog(this);
        }
    }
}
