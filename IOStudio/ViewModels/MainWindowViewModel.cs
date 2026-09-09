using Avalonia;
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
        public ReactiveCommand<Unit, Unit> OpenDevicePropertiesCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleThemeCommand { get; }
        public ReactiveCommand<Unit, Unit> AboutCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenCloseDeviceCommand { get; }
        public ReactiveCommand<Unit, Unit> ViewIOLogCommand { get; }
        public ReactiveCommand<Unit, Unit> EditIOConfigCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenEventForwardConfigCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleEditModeCommand { get; }
        public ReactiveCommand<Unit, Unit> AddDeviceCommand { get; }
        public ReactiveCommand<Device, Unit> DeleteDeviceCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenTimelineEditorCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenInterfaceDebugCommand { get; }
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

        public async void Load()
        {
            // 加载应用配置
            IOStudioSettings.LoadOrCreate();

            var _errorMsg = IORoot.Instance
                .SetConfig(Path.Combine(AppRoot, "Config\\IODevice.xml"))
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
        }

        /// <summary>
        /// 添加新设备
        /// </summary>
        private async System.Threading.Tasks.Task AddDeviceAsync()
        {
            if (!IsEditMode)
            {
                PushStatus("当前为查看 / 测试模式，进入编辑模式后才能添加设备");
                return;
            }

            // 创建新设备实例
            var newDevice = new Device
            {
                Name = $"Device_{Devices.Count + 1}",
                Type = "External",
                DllName = "MODBUS",
                Index = 0,
                IsRuntimeActive = IsStared
            };

            // 打开设备属性配置窗口
            var window = new DevicePropertiesWindow();
            window.SetDevice(newDevice);

            // 获取主窗口作为父窗口
            var mainWindow = Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            // 统一处理: 优先模态, 无主窗口(如独立进程/测试)时回退非模态 + Closed 事件, 避免确认后设备丢失
            if (mainWindow != null)
            {
                await window.ShowDialog(mainWindow);
            }
            else
            {
                var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
                window.Closed += (_, _) => tcs.TrySetResult(true);
                window.Show();
                await tcs.Task; // 等待用户关闭窗口后判断确认结果
            }

            // 如果用户确认添加
            if (window.IsConfirmed)
            {
                // 添加到 IORoot
                IORoot.Instance.Devices.Add(newDevice);

                // 添加到设备列表
                _devListSource.Add(newDevice);

                // 选中新添加的设备
                SelectedIODevcie = newDevice;

                // 保存配置到文件
                SaveDeviceConfig();
            }
        }

        /// <summary>
        /// 删除设备
        /// </summary>
        private async System.Threading.Tasks.Task DeleteDeviceAsync(Device device)
        {
            if (device == null)
                return;

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

            // 保存配置
            SaveDeviceConfig();

            // 提示需要重启
            var restartBox = MessageBoxManager.GetMessageBoxStandard(
                "删除成功",
                $"设备 \"{device.Title}\" 已删除。\n\n为使更改完全生效，建议重启应用程序。",
                ButtonEnum.Ok,
                MsBox.Avalonia.Enums.Icon.Info
            );
            await restartBox.ShowAsync();
        }

        /// <summary>
        /// 保存设备配置到文件
        /// </summary>
        private void SaveDeviceConfig()
        {
            try
            {
                IORoot.Instance.Save();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存配置失败: {ex.Message}");
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

            EditIOConfigCommand = ReactiveCommand.Create(() =>
            {
                if (!IsEditMode)
                {
                    PushStatus("当前为查看 / 测试模式，进入编辑模式后才能编辑配置文件");
                    return;
                }

                var _configPath = Path.Combine(AppRoot, "Config/IODevice.xml");
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

                var cfg = Path.Combine(AppRoot, "Config/IODevice.xml");
                EditorLauncher.OpenWithPreferredEditor(cfg);
            });

            ExitCommand = ReactiveCommand.Create(() =>
            {
                var lifetime =
                    Avalonia.Application.Current?.ApplicationLifetime
                    as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
                lifetime?.MainWindow?.Close();
            });

            // ── 设备菜单 ──
            OpenDevicePropertiesCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                if (SelectedIODevcie != null && IsEditMode)
                {
                    var win = new DevicePropertiesWindow();
                    win.SetDevice(SelectedIODevcie);
                    var mainWindow = Avalonia.Application.Current?.ApplicationLifetime
                        is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                        ? desktop.MainWindow
                        : null;
                    if (mainWindow != null)
                        await win.ShowDialog(mainWindow);
                }
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
                            // C++ 设备轮询在后台线程执行
                            IODeviceController.Update();

                            if (IsStared == false)
                                return;

                            // 只在 UI 更新时切换到主线程
                            RxApp.MainThreadScheduler.Schedule(() =>
                            {
                                SelectedIODevcie?.Update();
                                RefreshStatusDerived();
                            });
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
        private async System.Threading.Tasks.Task CheckForUpdateAsync(bool showNoUpdate)
        {
            try
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

                var info = await _updateService.CheckForUpdateAsync();
                if (info != null)
                {
                    var msg =
                        $"检测到新版本 {info.Version}（当前 {_updateService.CurrentVersion}）\n"
                        + $"{(string.IsNullOrWhiteSpace(info.ReleaseName) ? "" : info.ReleaseName + "\n")}"
                        + "是否现在下载并安装？";
                    var result = await MessageBoxManager
                        .GetMessageBoxStandard("发现新版本", msg, ButtonEnum.YesNo, Icon.Question)
                        .ShowAsync();
                    if (result == ButtonResult.Yes)
                    {
                        var progress = new Progress<DownloadProgress>(p =>
                        {
                            Console.WriteLine(
                                $"正在下载更新: {p.Percentage:F0}% ({p.BytesReceived}/{p.TotalBytes})"
                            );
                        });
                        var downloaded = await _updateService.DownloadAsync(info, progress);
                        if (_updateService.LaunchInstaller(downloaded))
                        {
                            // 启动独立 Updater 后，退出当前应用等待替换
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
                        }
                        else
                        {
                            await MessageBoxManager
                                .GetMessageBoxStandard(
                                    "更新失败",
                                    "更新 Helper 不可用，请重新安装当前版本后再试。",
                                    ButtonEnum.Ok,
                                    Icon.Warning
                                )
                                .ShowAsync();
                        }
                    }
                }
                else if (showNoUpdate)
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
            }
            catch (Exception ex)
            {
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
            }
        }
    }
}
