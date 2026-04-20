using Avalonia;
using DynamicData;
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

        // 静态实例引用，用于外部调用重启设备
        private static MainWindowViewModel? _instance;

        private bool isStarted = false;
        public bool IsStared
        {
            get => isStarted;
            set { this.RaiseAndSetIfChanged(ref isStarted, value); }
        }

        private bool _isEditMode = false;
        public bool IsEditMode
        {
            get => _isEditMode;
            set { this.RaiseAndSetIfChanged(ref _isEditMode, value); }
        }

        public string EditModeMenuText => IsEditMode ? "退出编辑模式(_M)" : "启用编辑模式(_M)";

        public ReactiveCommand<Unit, Device> OnTabChangedCommand { get; }

        public ReactiveCommand<Unit, Unit> OpenCloseDeviceCommand { get; }
        public ReactiveCommand<Unit, Unit> ViewIOLogCommand { get; }
        public ReactiveCommand<Unit, Unit> EditIOConfigCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenEventForwardConfigCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleEditModeCommand { get; }
        public ReactiveCommand<Unit, Unit> AddDeviceCommand { get; }
        public ReactiveCommand<Device, Unit> DeleteDeviceCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenTimelineEditorCommand { get; }

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

            IODeviceController.Load();
        }

        /// <summary>
        /// 添加新设备
        /// </summary>
        private async System.Threading.Tasks.Task AddDeviceAsync()
        {
            // 创建新设备实例
            var newDevice = new Device
            {
                Name = $"Device_{Devices.Count + 1}",
                Type = "External",
                DllName = "MODBUS",
                Index = 0
            };

            // 打开设备属性配置窗口
            var window = new DevicePropertiesWindow();
            window.SetDevice(newDevice);

            // 获取主窗口作为父窗口
            var mainWindow = Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (mainWindow != null)
            {
                await window.ShowDialog(mainWindow);
            }
            else
            {
                window.Show();
                return;
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
                var _configPath = Path.Combine(AppRoot, "Config/IODevice.xml");
                EditorLauncher.OpenWithPreferredEditor(_configPath);
            });

            OpenEventForwardConfigCommand = ReactiveCommand.Create(() =>
            {
                var window = new EventForwardConfigWindow
                {
                    ViewModel = new EventForwardConfigViewModel()
                };
                window.Show();
            });

            ToggleEditModeCommand = ReactiveCommand.Create(() =>
            {
                IsEditMode = !IsEditMode;
                this.RaisePropertyChanged(nameof(EditModeMenuText));
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
                var vm = ViewLocator.CreateViewModel<Timeline.TimelineEditorViewModel>();
                var window = new TimelineEditorWindow { DataContext = vm };
                window.Show();
            });

            _devListSource
                .Connect()
                // .AutoRefresh()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Bind(out var readOnlyDevList)
                .Subscribe();

            Devices = readOnlyDevList;

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
    }
}
