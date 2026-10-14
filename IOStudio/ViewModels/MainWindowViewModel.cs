using Avalonia;
using Avalonia.Threading;
using DynamicData;
using HiMind.Distribution;
using IOStudio.Models;
using IOStudio.Views;
using IOStudio.Views.Timeline;
using IOStudio.ViewModels.Timeline;
using IOStudio.Services;
using IOToolkit;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Concurrency;

namespace IOStudio.ViewModels
{
    public class MainWindowViewModel : ViewModelBase, IActivatableViewModel
    {
        public ViewModelActivator Activator { get; private set; } = new ViewModelActivator();

        public string AppRoot => AppDomain.CurrentDomain.BaseDirectory;

        private readonly SourceList<Device> _devListSource = new SourceList<Device>();
        public ReadOnlyObservableCollection<Device> Devices { get; }

        // ── 设备页"弹出独立窗口"状态 ──
        private readonly HashSet<Device> _detachedDevices = new();

        /// <summary>
        /// 未被弹出的设备 (Tab 页显示集合)。detached 设备从标签页隐藏, 独立窗口关闭后自动回归。
        /// </summary>
        public ObservableCollection<Device> VisibleDevices { get; } = new();

        // 静态实例引用，用于外部调用重启设备
        private static MainWindowViewModel? _instance;

        /// <summary>
        /// 主窗口 ViewModel 实例 (设备表增删的唯一落库方)。
        ///
        /// <para>设备视图 (<c>IODeviceView</c>) 的 DataContext 是 <c>Device</c> 本身, 它要发起
        /// "删除我自己" 只能经这个入口回到应用级流程 (确认框 + 落盘 + 重启提示都在那边)。
        /// 未创建时为 null (CLI / 无头路径), 调用方自行兜底。</para>
        /// </summary>
        public static MainWindowViewModel? Instance => _instance;



        private bool isStarted = false;
        public bool IsStared
        {
            get => isStarted;
            set
            {
                this.RaiseAndSetIfChanged(ref isStarted, value);
                this.RaisePropertyChanged(nameof(OpenCloseMenuText));
                this.RaisePropertyChanged(nameof(RunStateText));
                this.RaisePropertyChanged(nameof(RunStateColor));
                this.RaisePropertyChanged(nameof(ConnectedDeviceCountText));
                if (Devices != null)
                    Devices.ToList().ForEach(device => device.IsRuntimeActive = value);
            }
        }

        private bool _isEditMode = false;
        public bool IsEditMode
        {
            get => _isEditMode;
            set
            {
                if (_isEditMode == value)
                    return;

                this.RaiseAndSetIfChanged(ref _isEditMode, value);
                this.RaisePropertyChanged(nameof(EditModeMenuText));
                this.RaisePropertyChanged(nameof(EditModeLabel));
                this.RaisePropertyChanged(nameof(EditModeHint));
                this.RaisePropertyChanged(nameof(EditModeTooltip));
            }
        }

        // 与 Avalonia 的 Default/System 主题保持一致：浅色系统环境启动时不出现菜单文案与实际颜色相反。
        private bool _isDarkTheme = false;
        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set
            {
                this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
                this.RaisePropertyChanged(nameof(ThemeMenuText));
            }
        }

        public string EditModeMenuText => IsEditMode ? "退出编辑模式(_M)" : "进入编辑模式(_M)";

        /// <summary>主窗口模式入口的短标签。</summary>
        public string EditModeLabel => IsEditMode ? "编辑模式" : "查看 / 测试";

        /// <summary>主窗口模式入口的辅助说明。</summary>
        public string EditModeHint => IsEditMode ? "可修改配置" : "配置已锁定";

        /// <summary>主窗口模式入口的无障碍提示。</summary>
        public string EditModeTooltip => IsEditMode
            ? "退出编辑模式，恢复只读保护"
            : "进入编辑模式，允许修改设备配置";

        public string OpenCloseMenuText => IsStared ? "停止设备服务" : "启动设备服务";

        public string ThemeMenuText => IsDarkTheme ? "切换到浅色主题" : "切换到深色主题";

        public ReactiveCommand<Unit, Device> OnTabChangedCommand { get; }

        public ReactiveCommand<Unit, Unit> NewSessionCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenConfigCommand { get; }
        public ReactiveCommand<Unit, Unit> ExitCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleThemeCommand { get; }
        public ReactiveCommand<Unit, Unit> AboutCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenCloseDeviceCommand { get; }
        public ReactiveCommand<Unit, Unit> ViewIOLogCommand { get; }

        /// <summary>打开 IOStudio 操作日志 (用户操作 + 异常, 排查"我点了什么"用)。</summary>
        public ReactiveCommand<Unit, Unit> ViewStudioLogCommand { get; }
        public ReactiveCommand<Unit, Unit> EditIOConfigCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenEventForwardConfigCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleEditModeCommand { get; }
        public ReactiveCommand<Unit, Unit> AddDeviceCommand { get; }
        public ReactiveCommand<Device, Unit> DeleteDeviceCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenTimelineEditorCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenInterfaceDebugCommand { get; }

        /// <summary>打开协议测试台 (TCP/UDP/WebSocket/串口 收发观察 + 场景运行)。</summary>
        public ReactiveCommand<Unit, Unit> OpenProtocolTestCommand { get; }
        public ReactiveCommand<Unit, Unit> CheckUpdateCommand { get; }
        public ReactiveCommand<Device, Unit> DetachDeviceCommand { get; }

        // ── 窗口菜单复选态 (由 IWindowManager 状态事件驱动) ──
        private bool _isTimelineOpen;
        public bool IsTimelineOpen
        {
            get => _isTimelineOpen;
            private set => this.RaiseAndSetIfChanged(ref _isTimelineOpen, value);
        }

        private bool _isInterfaceDebugOpen;
        public bool IsInterfaceDebugOpen
        {
            get => _isInterfaceDebugOpen;
            private set => this.RaiseAndSetIfChanged(ref _isInterfaceDebugOpen, value);
        }

        private bool _isProtocolTestOpen;
        public bool IsProtocolTestOpen
        {
            get => _isProtocolTestOpen;
            private set => this.RaiseAndSetIfChanged(ref _isProtocolTestOpen, value);
        }

        private bool _isEventForwardOpen;
        public bool IsEventForwardOpen
        {
            get => _isEventForwardOpen;
            private set => this.RaiseAndSetIfChanged(ref _isEventForwardOpen, value);
        }

        // ── 底部状态栏 / 空状态引导 ──
        private string _statusText = "就绪";
        public string StatusText
        {
            get => _statusText;
            set => this.RaiseAndSetIfChanged(ref _statusText, value);
        }

        /// <summary>设备是否为空 (无设备时显示空状态引导页)。</summary>
        public bool HasNoDevices => Devices.Count == 0;

        /// <summary>设备数量摘要 (状态栏)。</summary>
        public string DeviceCountText => $"设备 {Devices.Count} 个";

        /// <summary>运行状态摘要 (状态栏)。</summary>
        public string RunStateText => IsStared ? "设备服务运行中" : "设备服务已停止";

        /// <summary>运行状态指示灯颜色 (状态栏)。</summary>
        public string RunStateColor => IsStared ? "#238b62" : "#747c86";

        /// <summary>在线设备数量摘要 (状态栏)。</summary>
        public string ConnectedDeviceCountText =>
            $"在线 {Devices.Count(d => d.IsValid)}/{Devices.Count}";

        /// <summary>推送到底部状态栏 (普通消息)。</summary>
        public void PushStatus(string message)
        {
            StatusText = message;
        }

        /// <summary>刷新状态栏与空状态派生属性。</summary>
        private void RefreshStatusDerived()
        {
            this.RaisePropertyChanged(nameof(HasNoDevices));
            this.RaisePropertyChanged(nameof(DeviceCountText));
            this.RaisePropertyChanged(nameof(RunStateText));
            this.RaisePropertyChanged(nameof(RunStateColor));
            this.RaisePropertyChanged(nameof(ConnectedDeviceCountText));
        }

        /// <summary>
        /// 重建标签页可见设备集合 = Devices − 已弹出为独立窗口的设备。
        /// 保证 detach 后标签页隐藏, 独立窗口关闭后自动恢复。
        /// </summary>
        private void SyncVisibleDevices()
        {
            var visible = Devices.Where(d => !_detachedDevices.Contains(d)).ToList();

            // 原地同步, 避免重建集合导致 Tab 闪断
            for (int i = VisibleDevices.Count - 1; i >= 0; i--)
            {
                if (!visible.Contains(VisibleDevices[i]))
                    VisibleDevices.RemoveAt(i);
            }
            int insertAt = 0;
            foreach (var d in visible)
            {
                if (insertAt < VisibleDevices.Count && VisibleDevices[insertAt] == d)
                {
                    insertAt++;
                    continue;
                }
                if (VisibleDevices.Contains(d))
                    continue;
                VisibleDevices.Insert(Math.Min(insertAt, VisibleDevices.Count), d);
                insertAt++;
            }
        }

        private readonly ISoftwareUpdateService _updateService;

        /// <summary>
        /// 启动时的更新提醒状态 (横幅显隐/文案/忽略规则)。
        /// 版本号用委托延迟取 —— 字段初始化时更新服务还没解析出来。
        /// </summary>
        private readonly UpdateNotice _updateNotice =
            new(() => ServiceLocator.Resolve<ISoftwareUpdateService>().CurrentVersion);

        /// <summary>
        /// 是否显示更新提醒横幅。
        ///
        /// <para>启动检查走<b>横幅</b>而不是弹窗 —— 弹窗会拦下用户正在做的事, 而且只要没更新
        /// 就每次启动都拦。横幅不阻塞, 且可"忽略此版本"永久消停。</para>
        /// </summary>
        public bool IsUpdateBannerVisible => _updateNotice.IsVisible;

        /// <summary>横幅文案 (版本 + 体积)。</summary>
        public string UpdateBannerText => _updateNotice.Text;

        /// <summary>
        /// 是否提供"忽略此版本"。强制更新时不给 —— 那种情况下忽略等于让用户停在有问题的版本上。
        /// </summary>
        public bool CanSkipUpdateVersion => _updateNotice.CanSkip;

        private CompositeDisposable? _recordingSubscriptions;

        private Device selectedIODevcie;
        public Device SelectedIODevcie
        {
            get => selectedIODevcie;
            set { this.RaiseAndSetIfChanged(ref selectedIODevcie, value); }
        }

        public ObservableCollection<Action> tempList { get; set; } =
            new ObservableCollection<Action>();

        /// <summary>
        /// 静态方法：重启所有设备（关闭后重新打开）
        /// </summary>
        public static void RestartDevices()
        {
            if (_instance == null)
                return;

            var wasStarted = _instance.IsStared;

            if (wasStarted)
            {
                // 先关闭设备
                EventForwardingService.Instance.Stop();
                IODeviceController.Unload();
                _instance.IsStared = false;
            }

            // 重新加载并启动
            _instance.Load();

            var configPath = Path.Combine(_instance.AppRoot, "Config", "EvtMapping.xml");
            EventForwardingService.Instance.LoadConfig(configPath);
            EventForwardingService.Instance.Start();

            _instance.SetupKeyBindings();
            _instance.SetupRecordingSubscriptions();

            _instance.IsStared = true;

            // 更新所有设备状态
            _instance.Devices
                .ToList()
                .ForEach(device =>
                {
                    _instance.UpdateRecordButtonVisibility(device);
                    device.Update();
                });
        }

        /// <summary>
        /// CLI 入口：打开动作编排编辑器（可指定初始加载文件），并返回该编辑器的 ViewModel。
        /// 与菜单「动作编排」共用同一套窗口创建逻辑（IWindowManager + ViewLocator），
        /// 避免 CLI 与 GUI 两条路径各写一份窗口创建而分叉。
        /// </summary>
        public Timeline.TimelineEditorViewModel? OpenTimelineEditorForCli(string? filePath)
        {
            var windowManager = ServiceLocator.Resolve<IWindowManager>();
            Timeline.TimelineEditorViewModel? captured = null;
            windowManager.ShowOrActivate<TimelineEditorWindow>(() =>
            {
                var vm = ViewLocator.CreateViewModel<Timeline.TimelineEditorViewModel>();
                captured = vm;
                var win = new TimelineEditorWindow { DataContext = vm };
                if (!string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath))
                    vm.LoadFromFile(filePath);
                return win;
            });
            return captured;
        }

        public async void Load()
        {
            // 加载应用配置
            IOStudioSettings.LoadOrCreate();

            var _errorMsg = IORoot.Instance
                .SetConfig(Models.DeviceTableLocator.ResolveEffectivePath())
                .Load();
            if (_errorMsg != string.Empty)
            {
                var box = MessageBoxManager.GetMessageBoxStandard("提示", _errorMsg, ButtonEnum.Ok);

                var result = await box.ShowAsync();
            }
            IORoot.Instance.AfterDeserialization();

            _devListSource.Edit(_source =>
            {
                _source.Clear();

                _source.AddRange(
                    IORoot.Instance.Devices
                        .Where(_dev => _dev.Type != "Standard")
                        .GroupBy(io => io.Name)
                        .Select(g => g.First())
                );
            });

            // 重新加载配置时保留当前的全局模式，避免编辑模式状态只同步到旧设备。
            Devices
                .ToList()
                .ForEach(device =>
                {
                    device.IsEditMode = IsEditMode;
                    UpdateRecordButtonVisibility(device);
                });

            IODeviceController.Load();

            // 设备表刚被重新读入，编排侧的设备名判据缓存一并作废。
            Services.Motion.MotionDeviceCatalog.Invalidate();
        }

        /// <summary>
        /// 添加新设备 (设备管理入口: 名字自动生成)
        /// </summary>
        private async System.Threading.Tasks.Task AddDeviceAsync()
        {
            if (!IsEditMode)
            {
                PushStatus("当前为查看 / 测试模式，进入编辑模式后才能添加设备");
                return;
            }

            await ShowDeviceEditorAndCommitAsync(
                new Device
                {
                    Name = $"Device_{Devices.Count + 1}",
                    Type = "External",
                    DllName = "MODBUS",
                    Index = 0,
                    IsRuntimeActive = IsStared,
                },
                null
            );
        }

        /// <summary>打开设备编辑器并在确认后落库 (IORoot + 设备列表 + 磁盘)。</summary>
        private async System.Threading.Tasks.Task<bool> ShowDeviceEditorAndCommitAsync(
            Device newDevice,
            Avalonia.Controls.Window? owner
        )
        {
            var window = new DevicePropertiesWindow();
            window.SetDevice(newDevice);

            // 宿主窗口: 优先调用方给的 (编排面板), 否则主窗口
            var host =
                owner
                ?? (
                    Avalonia.Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                        ? desktop.MainWindow
                        : null
                );

            // 统一处理: 优先模态, 无宿主(如独立进程/测试)时回退非模态 + Closed 事件, 避免确认后设备丢失
            if (host != null)
            {
                await window.ShowDialog(host);
            }
            else
            {
                var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
                window.Closed += (_, _) => tcs.TrySetResult(true);
                window.Show();
                await tcs.Task; // 等待用户关闭窗口后判断确认结果
            }

            if (!window.IsConfirmed)
                return false;

            // 添加到 IORoot
            IORoot.Instance.Devices.Add(newDevice);

            // 添加到设备列表
            _devListSource.Add(newDevice);

            // 选中新添加的设备
            SelectedIODevcie = newDevice;

            // 保存配置到文件
            SaveDeviceConfig();
            return true;
        }


        /// <summary>
        /// 删除设备
        /// </summary>
        private async System.Threading.Tasks.Task DeleteDeviceAsync(Device device)
        {
            if (device == null)
            {
                // 静默 return 会让"点了没反应"看起来像坏了 —— 说清楚为什么没删
                PushStatus("请先选中要删除的设备（点击上方设备标签页）");
                return;
            }

            if (!IsEditMode)
            {
                PushStatus("当前为查看 / 测试模式，进入编辑模式后才能删除设备");
                return;
            }

            // 弹出确认对话框
            var box = MessageBoxManager.GetMessageBoxStandard(
                "确认删除",
                $"确定要删除设备 \"{device.Title}\" 吗？\n此操作不可撤销。",
                ButtonEnum.YesNo,
                MsBox.Avalonia.Enums.Icon.Question
            );

            var result = await box.ShowAsync();
            if (result != ButtonResult.Yes)
                return;

            // 从 IORoot 中移除
            IORoot.Instance.Devices.Remove(device);

            // 从设备列表中移除
            _devListSource.Remove(device);

            // 若该设备已弹出为独立窗口, 一并清除 detach 状态 (防止残留引用)
            _detachedDevices.Remove(device);

            // 如果删除的是当前选中的设备，选择第一个
            if (SelectedIODevcie == device)
            {
                SelectedIODevcie = Devices.FirstOrDefault()!;
            }

            // 保存配置 —— 结果必须如实回报: 写失败时若仍说"删除成功",
            // 用户会以为文件已同步, 而实际设备还在表里, 下次启动又回来了。
            string tableName = System.IO.Path.GetFileName(
                Models.DeviceTableLocator.ResolveEffectivePath()
            );
            bool saved = SaveDeviceConfig();

            // 提示需要重启 / 或写入失败
            var restartBox = MessageBoxManager.GetMessageBoxStandard(
                saved ? "删除成功" : "删除未落盘",
                saved
                    ? $"设备 \"{device.Title}\" 已从设备表 {tableName} 中删除。\n\n为使更改完全生效，建议重启应用程序。"
                    : $"设备 \"{device.Title}\" 已从内存移除，但写入设备表 {tableName} 失败（详见状态栏）。\n\n"
                        + "文件里仍有该设备，重启后会恢复。",
                ButtonEnum.Ok,
                saved ? MsBox.Avalonia.Enums.Icon.Info : MsBox.Avalonia.Enums.Icon.Warning
            );
            await restartBox.ShowAsync();
        }

        /// <summary>
        /// 保存设备配置到文件
        /// </summary>
        /// <returns>是否真的写入了文件。</returns>
        private bool SaveDeviceConfig()
        {
            try
            {
                IORoot.Instance.Save();

                // 设备表已改写，编排侧缓存的设备名判据随之失效 —— 否则增删设备后
                // 编排面板仍按旧表判断"设备是否被认领"。
                Services.Motion.MotionDeviceCatalog.Invalidate();
                return true;
            }
            catch (Exception ex)
            {
                // 必须说出来: 本应用是 WinExe 没有控制台, Debug.WriteLine 等于丢弃。
                // 写失败时界面上还显示"删除成功", 而文件里设备还在 —— 用户以为改了、其实没改。
                PushStatus($"⚠ 设备表写入失败：{ex.Message}");
                return false;
            }
        }

        public MainWindowViewModel()
        {
            _instance = this;

            OnTabChangedCommand = ReactiveCommand.Create(() =>
            {
                return SelectedIODevcie;
            });

            OpenCloseDeviceCommand = ReactiveCommand.Create(() =>
            {
                if (IsStared)
                {
                    // 停止设备时，停止事件转发服务
                    EventForwardingService.Instance.Stop();
                    IODeviceController.Unload();
                }
                else
                {
                    Load();
                    // 重新加载并启动事件转发服务
                    var configPath = Path.Combine(AppRoot, "Config", "EvtMapping.xml");
                    EventForwardingService.Instance.LoadConfig(configPath);
                    EventForwardingService.Instance.Start();

                    // 重新设置按键绑定监听
                    SetupKeyBindings();

                    // 重新设置录制命令订阅
                    SetupRecordingSubscriptions();
                }
                IsStared = !IsStared;
                PushStatus(IsStared ? $"设备服务已启动 ({Devices.Count} 个设备)" : "设备服务已停止");

                // 更新所有设备的录制按钮显示状态
                Devices
                    .ToList()
                    .ForEach(device =>
                    {
                        UpdateRecordButtonVisibility(device);
                        device.Update();
                    });
            });

            ViewIOLogCommand = ReactiveCommand.Create(() =>
            {
                var _logPath = Path.Combine(AppRoot, "Logs/IODevice.log");
                EditorLauncher.OpenWithPreferredEditor(_logPath);
            });

            ViewStudioLogCommand = ReactiveCommand.Create(() =>
            {
                // 今天的文件可能尚未产生 (刚启动无操作) —— 此时退化为打开日志目录
                var logFile = Services.AppLog.CurrentFile;
                if (File.Exists(logFile))
                    EditorLauncher.OpenWithPreferredEditor(logFile);
                else
                    EditorLauncher.OpenWithPreferredEditor(Services.AppLog.LogDir);
            });

            EditIOConfigCommand = ReactiveCommand.Create(() =>
            {
                if (!IsEditMode)
                {
                    PushStatus("当前为查看 / 测试模式，进入编辑模式后才能编辑配置文件");
                    return;
                }

                var _configPath = Models.DeviceTableLocator.ResolveEffectivePath();
                EditorLauncher.OpenWithPreferredEditor(_configPath);
            });

            // ── 文件菜单 ──
            NewSessionCommand = ReactiveCommand.Create(() =>
            {
                var wasStarted = IsStared;
                EventForwardingService.Instance.Stop();
                IODeviceController.Unload();
                IsStared = false;
                Load();
                if (wasStarted)
                {
                    var configPath = Path.Combine(AppRoot, "Config", "EvtMapping.xml");
                    EventForwardingService.Instance.LoadConfig(configPath);
                    EventForwardingService.Instance.Start();
                    SetupKeyBindings();
                    SetupRecordingSubscriptions();
                    IsStared = true;
                }
                PushStatus("已重新加载当前配置");
            });

            OpenConfigCommand = ReactiveCommand.Create(() =>
            {
                if (!IsEditMode)
                {
                    PushStatus("当前为查看 / 测试模式，进入编辑模式后才能编辑配置文件");
                    return;
                }

                var cfg = Models.DeviceTableLocator.ResolveEffectivePath();
                EditorLauncher.OpenWithPreferredEditor(cfg);
            });

            ExitCommand = ReactiveCommand.Create(() =>
            {
                var lifetime =
                    Avalonia.Application.Current?.ApplicationLifetime
                    as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
                lifetime?.MainWindow?.Close();
            });

            // ── 视图菜单 ──
            ToggleThemeCommand = ReactiveCommand.Create(() =>
            {
                IsDarkTheme = !IsDarkTheme;
                if (Avalonia.Application.Current != null)
                {
                    Avalonia.Application.Current.RequestedThemeVariant = IsDarkTheme
                        ? Avalonia.Styling.ThemeVariant.Dark
                        : Avalonia.Styling.ThemeVariant.Light;
                }
            });

            // ── 工具/窗口菜单: 走 IWindowManager 单例聚焦 ──
            var windowManager = ServiceLocator.Resolve<IWindowManager>();

            OpenEventForwardConfigCommand = ReactiveCommand.Create(() =>
            {
                windowManager.ShowOrActivate<EventForwardConfigWindow>(
                    () =>
                        new EventForwardConfigWindow
                        {
                            ViewModel = new EventForwardConfigViewModel()
                        }
                );
            });

            OpenInterfaceDebugCommand = ReactiveCommand.Create(() =>
            {
                windowManager.ShowOrActivate<InterfaceDebugWindow>(
                    () => new InterfaceDebugWindow()
                );
            });

            OpenProtocolTestCommand = ReactiveCommand.Create(() =>
            {
                windowManager.ShowOrActivate<ProtocolTestWindow>(
                    () => new ProtocolTestWindow()
                );
            });

            ToggleEditModeCommand = ReactiveCommand.Create(() =>
            {
                IsEditMode = !IsEditMode;
                // 同步所有设备的编辑模式和录制按钮显示
                Devices
                    .ToList()
                    .ForEach(device =>
                    {
                        device.IsEditMode = IsEditMode;
                        UpdateRecordButtonVisibility(device);
                        device.Update();
                    });
            });

            AddDeviceCommand = ReactiveCommand.CreateFromTask(AddDeviceAsync);

            DeleteDeviceCommand = ReactiveCommand.CreateFromTask<Device>(DeleteDeviceAsync);

            OpenTimelineEditorCommand = ReactiveCommand.Create(() =>
            {
                OpenTimelineEditor(null);
            });

            // 窗口菜单复选态驱动
            windowManager.WindowStateChanged += (type, isOpen) =>
            {
                if (type == typeof(TimelineEditorWindow))
                    IsTimelineOpen = isOpen;
                else if (type == typeof(InterfaceDebugWindow))
                    IsInterfaceDebugOpen = isOpen;
                else if (type == typeof(ProtocolTestWindow))
                    IsProtocolTestOpen = isOpen;
                else if (type == typeof(EventForwardConfigWindow))
                    IsEventForwardOpen = isOpen;
            };

            // ── 打开动作编排编辑器 (可指定初始加载文件) ──
            // 默认打开空编辑器; 传 filePath 且窗口为新建时加载该文件。
            void OpenTimelineEditor(string? filePath)
            {
                windowManager.ShowOrActivate<TimelineEditorWindow>(() =>
                {
                    var vm = ViewLocator.CreateViewModel<Timeline.TimelineEditorViewModel>();
                    var win = new TimelineEditorWindow { DataContext = vm };
                    if (!string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath))
                        vm.LoadFromFile(filePath);
                    return win;
                });
            }

            // ── 帮助菜单 ──
            AboutCommand = ReactiveCommand.Create(() =>
            {
                var help = new HelpWindow();
                help.Show();
            });

            _updateService = ServiceLocator.Resolve<ISoftwareUpdateService>();
            CheckUpdateCommand = ReactiveCommand.CreateFromTask(
                () => CheckForUpdateAsync(showNoUpdate: true)
            );

            _devListSource
                .Connect()
                // .AutoRefresh()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Bind(out var readOnlyDevList)
                .Subscribe();

            Devices = readOnlyDevList;

            // 状态栏/空状态: 设备集合变化时刷新派生属性
            ((System.Collections.Specialized.INotifyCollectionChanged)Devices).CollectionChanged +=
                (_, _) =>
                {
                    RefreshStatusDerived();
                    SyncVisibleDevices();
                };
            RefreshStatusDerived();
            SyncVisibleDevices();

            // 设备页"弹出独立窗口": 打开独立 DeviceDetachedWindow, 关闭后恢复标签页
            DetachDeviceCommand = ReactiveCommand.Create<Device>(device =>
            {
                if (device == null || _detachedDevices.Contains(device))
                    return;
                _detachedDevices.Add(device);
                SyncVisibleDevices();

                var win = new Views.DeviceDetachedWindow { DataContext = device };
                win.Closed += (_, _) =>
                {
                    _detachedDevices.Remove(device);
                    SyncVisibleDevices();
                    PushStatus($"设备 \"{device.Title}\" 已回到标签页");
                };
                win.Show();
                PushStatus($"设备 \"{device.Title}\" 已弹出为独立窗口");
            });

            this.WhenAnyValue(_ => _.SelectedIODevcie)
                .Subscribe(_ =>
                {
                    if (_ == null)
                        return;
                    _.TriggerUIUpdate();
                    OnTabChangedCommand.Execute().Subscribe();
                });

            this.WhenActivated(
                (CompositeDisposable disposables) =>
                {
                    Load();

                    // 启动后静默检查更新（不打扰用户，仅在存在更新时提示）
                    _ = CheckForUpdateAsync(showNoUpdate: false);

                    Devices
                        .ToList()
                        .ForEach(dev =>
                        {
                            // 初始化录制按钮显示状态
                            UpdateRecordButtonVisibility(dev);
                        });

                    // 设置按键绑定监听
                    SetupKeyBindings();

                    IsStared = true;

                    // 加载并启动事件转发服务
                    var configPath = Path.Combine(AppRoot, "Config", "EvtMapping.xml");
                    EventForwardingService.Instance.LoadConfig(configPath);
                    EventForwardingService.Instance.Start();

                    // 订阅录制完成事件
                    RecordingManager.Instance.OnRecordingComplete
                        .ObserveOn(RxApp.MainThreadScheduler)
                        .Subscribe(result =>
                        {
                            var (device, node, keyName) = result;
                            Debug.WriteLine(
                                $"[Recording] Device:{device.Name} Node:{node.Name} Key:{keyName}"
                            );

                            // 检查是否已经存在该按键
                            if (!node.Keys.Any(k => k.Name == keyName))
                            {
                                var newKey = new Key { Name = keyName, InvertEvent = "False" };
                                node.Keys.Add(newKey);
                                node.KeyList.Add(newKey);

                                // 重建active订阅以包含新Key
                                node.RebuildActiveSubscription();

                                // 保存配置
                                IORoot.Instance.Save();

                                Debug.WriteLine($"[Recording] Added key {keyName} to {node.Name}");
                            }
                            else
                            {
                                Debug.WriteLine(
                                    $"[Recording] Key {keyName} already exists in {node.Name}"
                                );
                            }

                            // 确保录制状态被重置
                            node.IsRecording = false;
                            node.RaisePropertyChanged(nameof(node.RecordingToolTip));
                        });

                    RecordingManager.Instance.OnRecordingComplete
                        .ObserveOn(RxApp.MainThreadScheduler)
                        .Subscribe(result =>
                        {
                            var (device, node, keyName) = result;
                            Debug.WriteLine(
                                $"[Recording] Device:{device.Name} Node:{node.Name} Key:{keyName}"
                            );

                            // 检查是否已经存在该按键
                            if (!node.Keys.Any(k => k.Name == keyName))
                            {
                                var newKey = new Key { Name = keyName, InvertEvent = "False" };
                                node.Keys.Add(newKey);
                                node.KeyList.Add(newKey);

                                // 重建active订阅以包含新Key
                                node.RebuildActiveSubscription();

                                // 保存配置
                                IORoot.Instance.Save();

                                Debug.WriteLine($"[Recording] Added key {keyName} to {node.Name}");
                            }
                            else
                            {
                                Debug.WriteLine(
                                    $"[Recording] Key {keyName} already exists in {node.Name}"
                                );
                            }

                            // 确保录制状态被重置
                            node.IsRecording = false;
                            node.RaisePropertyChanged(nameof(node.RecordingToolTip));
                        })
                        .DisposeWith(disposables);

                    // 设置录制命令订阅
                    SetupRecordingSubscriptions();

                    // 使用可配置的刷新率，在后台线程执行设备轮询
                    var refreshInterval = IOStudioSettings.Instance.UIRefreshIntervalMs;
                    var _tickHandler = Observable
                        .Interval(TimeSpan.FromMilliseconds(refreshInterval))
                        .ObserveOn(RxApp.TaskpoolScheduler) // 后台线程执行
                        .Subscribe(_ =>
                        {
                            // 轮询体必须自带兜底: Rx 的 Subscribe 一旦抛出异常就会**终止整个订阅**,
                            // 此后设备轮询永久停止且没有任何提示 —— 现场表现为"设备全都不刷新/不响应",
                            // 与真正死锁的现象极为相似 (但 tick 仍在走)。这里吞掉异常以保住订阅,
                            // 同时写日志, 避免又一次静默失效。
                            try
                            {
                                // C++ 设备轮询在后台线程执行
                                var swPoll = System.Diagnostics.Stopwatch.StartNew();
                                IODeviceController.Update();
                                long pollMs = swPoll.ElapsedMilliseconds;
                                Services.Motion.PollDiagnostics.UpdateCount++;
                                Services.Motion.PollDiagnostics.TotalUpdateMs += pollMs;
                                if (pollMs > Services.Motion.PollDiagnostics.MaxUpdateMs)
                                    Services.Motion.PollDiagnostics.MaxUpdateMs = pollMs;

                                if (IsStared == false)
                                    return;

                                // 只在 UI 更新时切换到主线程。
                                // 这里显式用 Avalonia 的 Dispatcher, 而不是 RxApp.MainThreadScheduler:
                                // Schedule 在"当前线程已被判定为可执行"时会**内联执行**,
                                // 结果 Device.Update() 在后台线程改了 IsValid 等绑定属性,
                                // 触发 ReactiveCommand.CanExecuteChanged 在后台线程发布,
                                // 绑定该命令的 Avalonia Button 随即抛
                                // "InvalidOperationException: Call from invalid thread",
                                // 并且该异常会沿调用栈弄死订阅它的设备轮询。
                                // 原生读取放在轮询（后台）线程执行：UI 线程直读会与
                                // IODeviceController.Update() 整轮持有的原生 RegistryMutex 争用，
                                // 造成窗口长时间“未响应”（UI 线程栈阻塞在 GetKey）。这里只在
                                // 后台线程取纯数据快照，回 UI 线程仅做托管赋值。
                                var _selectedDevice = SelectedIODevcie;
                                if (_selectedDevice != null)
                                {
                                    Device.DeviceSnapshot _deviceSnapshot;
                                    try
                                    {
                                        _deviceSnapshot = _selectedDevice.ReadSnapshot();
                                    }
                                    catch (Exception exSnapshot)
                                    {
                                        // 录制等会并发修改 Key 集合，快照读取可能抛；跳过本次，
                                        // 下一周期自动恢复，也不影响订阅存活。
                                        Debug.WriteLine($"[DevicePoll] 值快照读取跳过: {exSnapshot.Message}");
                                        _deviceSnapshot = null;
                                    }

                                    if (_deviceSnapshot != null)
                                    {
                                        Dispatcher.UIThread.Post(() =>
                                        {
                                            _selectedDevice.ApplySnapshot(_deviceSnapshot);
                                            RefreshStatusDerived();
                                        });
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                // 去重: 该异常可能每个轮询周期都发生 (例如某个命令的 CanExecute
                                // 绑定到设备状态, 后台线程变更时 Avalonia 会抛"Call from invalid thread"),
                                // 不能每个周期都写一条, 否则日志被刷满。
                                string sig = ex.GetType().Name + ": " + ex.Message;
                                if (sig != s_lastPollErrorSignature)
                                {
                                    s_lastPollErrorSignature = sig;
                                    Services.AppLog.Error(
                                        "DevicePoll",
                                        "设备轮询单次失败 (已保住订阅, 同类错误不再重复记录)",
                                        ex
                                    );
                                }
                            }
                        });

                    Disposable
                        .Create(() =>
                        {
                            _tickHandler.Dispose();
                            IODeviceController.Unload();
                            Debug.WriteLine("Dispose");
                        })
                        .DisposeWith(disposables);
                }
            );
        }

        /// <summary>设备轮询异常去重用的签名 (避免同一异常每周期刷屏)。</summary>
        private static string? s_lastPollErrorSignature;

        /// <summary>
        /// 更新设备的录制按钮显示状态
        /// 录制按钮仅在编辑模式且设备启动时显示
        /// </summary>
        private void UpdateRecordButtonVisibility(Device device)
        {
            bool shouldShow = IsEditMode && IsStared;
            device.Actions.ForEach(action =>
            {
                action.ShowRecordBtn = shouldShow;
                action.RaisePropertyChanged(nameof(action.ShowRecordBtn));
            });
        }

        /// <summary>
        /// 设置按键绑定监听
        /// </summary>
        private void SetupKeyBindings()
        {
            Devices
                .ToList()
                .ForEach(dev =>
                {
                    var _ioDev = IODeviceController.GetIODevice(dev.Name);
                    _ioDev.BindKey(
                        IOKeyCode.AnyKey,
                        InputEvent.IE_Pressed,
                        _key =>
                        {
                            Debug.WriteLine(
                                $"[EventForwarding] Device:{dev.Name} Key:{_key.ToString()} Pressed"
                            );

                            // 处理录制模式下的按键捕获
                            RecordingManager.Instance.HandleKeyPressed(dev, _key.ToString());
                        }
                    );
                });
        }

        /// <summary>
        /// 设置录制命令订阅
        /// </summary>
        private void SetupRecordingSubscriptions()
        {
            // 释放旧的订阅
            _recordingSubscriptions?.Dispose();
            _recordingSubscriptions = new CompositeDisposable();

            // 监听所有节点的录制命令
            Devices
                .ToList()
                .ForEach(dev =>
                {
                    foreach (var action in dev.Actions)
                    {
                        action.RecordKeyCommand.Subscribe(_ =>
                        {
                            if (action.IsRecording)
                            {
                                // 停止其他正在录制的节点
                                Devices
                                    .ToList()
                                    .ForEach(d =>
                                    {
                                        d.Actions.ForEach(a =>
                                        {
                                            if (a != action && a.IsRecording)
                                            {
                                                a.IsRecording = false;
                                                a.RaisePropertyChanged(nameof(a.RecordingToolTip));
                                            }
                                        });
                                    });

                                RecordingManager.Instance.StartRecording(dev, action);
                                Debug.WriteLine(
                                    $"[Recording] Started for {dev.Name}.{action.Name}"
                                );
                            }
                            else
                            {
                                RecordingManager.Instance.StopRecording();
                                Debug.WriteLine($"[Recording] Stopped");
                            }
                        });
                        _recordingSubscriptions.Add(
                            action.RecordKeyCommand.Subscribe(_ =>
                            {
                                if (action.IsRecording)
                                {
                                    // 停止其他正在录制的节点
                                    Devices
                                        .ToList()
                                        .ForEach(d =>
                                        {
                                            d.Actions.ForEach(a =>
                                            {
                                                if (a != action && a.IsRecording)
                                                {
                                                    a.IsRecording = false;
                                                    a.RaisePropertyChanged(
                                                        nameof(a.RecordingToolTip)
                                                    );
                                                }
                                            });
                                        });

                                    RecordingManager.Instance.StartRecording(dev, action);
                                    Debug.WriteLine(
                                        $"[Recording] Started for {dev.Name}.{action.Name}"
                                    );
                                }
                                else
                                {
                                    RecordingManager.Instance.StopRecording();
                                    Debug.WriteLine($"[Recording] Stopped");
                                }
                            })
                        );
                    }
                });
        }

        /// <summary>
        /// 检查软件更新：有更新时提示用户下载并启动安装；安装完成后退出当前应用。
        /// </summary>
        /// <param name="showNoUpdate">为 true 时，即使没有更新也提示用户"当前已是最新版本"。</param>
        /// <summary>横幅「稍后」: 本次会话不再显示 (下次启动再说)。</summary>
        public void DismissUpdateBanner()
        {
            _updateNotice.Dismiss();
            RaiseUpdateBannerChanged();
        }

        /// <summary>
        /// 横幅「忽略此版本」: 持久化版本号, 那个版本永久不再提醒。
        /// <b>出现更新的版本仍会提醒</b> —— 忽略的是版本, 不是更新这件事。
        /// </summary>
        public void SkipUpdateVersion()
        {
            if (_updateNotice.Skip() is { } version)
                PushStatus($"已忽略版本 {version}，该版本不再提醒（出现更新的版本仍会提示）");

            RaiseUpdateBannerChanged();
        }

        /// <summary>横幅「查看更新」: 走手动流程 (有对话框确认)。</summary>
        public System.Threading.Tasks.Task ViewPendingUpdateAsync()
        {
            if (_updateNotice.Take() is not { } info)
                return System.Threading.Tasks.Task.CompletedTask;
            RaiseUpdateBannerChanged();
            return RunUpdateFlowAsync(info);
        }

        /// <summary>测试用: 直接挂上待提醒的更新 (不联网)。</summary>
        internal void SetPendingUpdateForTest(SoftwareUpdateManifest info)
        {
            _updateNotice.Set(info);
            RaiseUpdateBannerChanged();
        }

        private void RaiseUpdateBannerChanged()
        {
            this.RaisePropertyChanged(nameof(IsUpdateBannerVisible));
            this.RaisePropertyChanged(nameof(UpdateBannerText));
            this.RaisePropertyChanged(nameof(CanSkipUpdateVersion));
        }

        ///
        /// <para><b>三个阶段各自报错</b>: 检查失败是网络/配置问题, 下载失败是传输问题,
        /// 安装启动失败是本机更新组件缺失 —— 原实现把三者都报成"检查更新失败", 会把人
        /// 引向错误方向。而且下载进度原本写进 <c>Console</c>, 本应用是 <c>WinExe</c> 没有控制台,
        /// 于是 150MB 下载全程界面一片空白。</para>
        /// </summary>
        /// <param name="showNoUpdate">
        /// true = 用户手动点的「检查更新」: 任何结果都要给回执 (含"已是最新"与失败原因);
        /// false = 启动时静默检查: 只在**确有更新**时打断, 失败写状态栏 (不打扰但看得见)。
        /// </param>
        /// <summary>
        /// 切换设备表并立即重载。
        ///
        /// <para><b>只改"加载哪一份", 不改写任何文件</b> —— 切换不存在覆盖配置的风险。
        /// 切完必须重载: 设备实例是按表建出来的, 不重载就还是旧表那批设备。</para>
        ///
        /// <para>查看 / 测试模式下也允许切换 —— "换一份表来看"与"换项目"同性质, 不是编辑动作;
        /// 真正的编辑限制仍由各编辑入口自己把关。</para>
        /// </summary>
        public void SwitchDeviceTable(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return;

            string path = Models.DeviceTableLocator.ResolvePath(fileName);
            if (!System.IO.File.Exists(path))
            {
                PushStatus($"设备表不存在: {fileName}");
                return;
            }

            IOStudioSettings.Instance.DeviceTableFile = System.IO.Path.GetFileName(path);
            try
            {
                IOStudioSettings.Instance.Save();
            }
            catch (Exception ex)
            {
                PushStatus($"记住设备表选择失败：{ex.Message}");
            }

            // 重载: 重新读表 + 重建设备实例 (Load 内部会 IODeviceController.Load 并作废设备目录缓存)
            Load();
            PushStatus(
                $"已切换到设备表 {Models.DeviceTableLocator.DescribeLabel(fileName)}"
                    + $"（{Devices.Count} 个设备）"
            );
        }

        private async System.Threading.Tasks.Task CheckForUpdateAsync(bool showNoUpdate)
        {
            if (!_updateService.IsConfigured)
            {
                if (showNoUpdate)
                {
                    await MessageBoxManager
                        .GetMessageBoxStandard(
                            "检查更新",
                            "软件分发尚未配置，请先部署 distribution.json 后再试。",
                            ButtonEnum.Ok,
                            Icon.Info
                        )
                        .ShowAsync();
                }
                return;
            }

            SoftwareUpdateManifest? info;
            try
            {
                info = await _updateService.CheckForUpdateAsync();
            }
            catch (Exception ex)
            {
                // 静默检查失败原先被整段吞掉 —— 网络不通/未登录/配置错时界面毫无迹象。
                // 不弹框 (启动时不该被打断), 但状态栏必须说, 否则等于没有检查。
                if (showNoUpdate)
                {
                    await MessageBoxManager
                        .GetMessageBoxStandard(
                            "检查更新",
                            $"检查更新失败：{ex.Message}",
                            ButtonEnum.Ok,
                            Icon.Warning
                        )
                        .ShowAsync();
                }
                else
                {
                    PushStatus($"检查更新失败：{ex.Message}");
                }
                return;
            }

            if (info is null)
            {
                if (showNoUpdate)
                {
                    await MessageBoxManager
                        .GetMessageBoxStandard(
                            "检查更新",
                            $"当前已是最新版本（{_updateService.CurrentVersion}）。",
                            ButtonEnum.Ok,
                            Icon.Info
                        )
                        .ShowAsync();
                }
                return;
            }

            // 启动路径: 挂横幅 (非模态)。被忽略过的版本不再出现 —— 除非是强制更新。
            if (!showNoUpdate)
            {
                if (!UpdateNotice.ShouldNotify(info, IOStudioSettings.Instance.SkippedUpdateVersion))
                    return;

                _updateNotice.Set(info);
                RaiseUpdateBannerChanged();
                return;
            }

            await RunUpdateFlowAsync(info);
        }

        /// <summary>询问 → 下载(独立窗口) → 启动安装。</summary>
        private async System.Threading.Tasks.Task RunUpdateFlowAsync(SoftwareUpdateManifest info)
        {
            var ask =
                $"检测到新版本 {info.Version}（当前 {_updateService.CurrentVersion}）\n"
                + $"{(string.IsNullOrWhiteSpace(info.ReleaseName) ? "" : info.ReleaseName + "\n")}"
                + $"{(info.Size > 0 ? $"更新包体积 {ViewModels.UpdateDownloadViewModel.FormatBytes(info.Size)}\n" : "")}"
                + "是否现在下载并安装？";
            var result = await MessageBoxManager
                .GetMessageBoxStandard("发现新版本", ask, ButtonEnum.YesNo, Icon.Question)
                .ShowAsync();
            if (result != ButtonResult.Yes)
                return;

            // 下载走独立窗口: 有进度条/速度/剩余时间/取消, 且不冻结主界面。
            var window = new UpdateDownloadWindow { DataContext = new UpdateDownloadViewModel() };
            if (GetMainWindow() is { } owner)
                window.Show(owner);
            else
                window.Show();

            bool installed = await window.RunAsync(_updateService, info);
            if (installed)
            {
                PushStatus($"更新 {info.Version} 已开始安装，应用即将退出");
                if (
                    Avalonia.Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktopLifetime
                )
                {
                    desktopLifetime.Shutdown();
                }
                else
                {
                    Environment.Exit(0);
                }
                return;
            }

            // 失败/取消的细节由那个窗口显示, 这里只补一条状态栏留痕
            PushStatus($"更新未完成：{window.ViewModel.HeaderText}");
        }

        /// <summary>取主窗口 (用于给对话框定父窗口)。</summary>
        private static Avalonia.Controls.Window? GetMainWindow() =>
            Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
    }
}
