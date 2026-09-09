using Avalonia.Controls;
using DynamicData;
using IOStudio.Extensions;
using IOStudio.Models;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Text.Json;
using IOToolkit;
using System.Xml.Serialization;

namespace IOStudio.ViewModels
{
    /// <summary>
    /// 事件转发配置窗口ViewModel
    /// </summary>
    public class EventForwardConfigViewModel : ViewModelBase, IActivatableViewModel
    {
        public ViewModelActivator Activator { get; } = new ViewModelActivator();

        // 协议组管理（树形结构，每个组包含其下的映射）
        private readonly SourceList<ProtocolGroup> _protocolGroupsSource =
            new SourceList<ProtocolGroup>();
        public ReadOnlyObservableCollection<ProtocolGroup> ProtocolGroups { get; }

        // 标志：是否正在从映射选择中设置协议组
        private bool _isSettingProtocolGroupFromMapping = false;

        private ProtocolGroup? selectedProtocolGroup;
        public ProtocolGroup? SelectedProtocolGroup
        {
            get => selectedProtocolGroup;
            set
            {
                this.RaiseAndSetIfChanged(ref selectedProtocolGroup, value);

                if (value != null)
                {
                    // 只有在用户主动选择协议组时才显示配置面板
                    // 如果是因为选择映射而自动设置的，则不显示
                    if (!_isSettingProtocolGroupFromMapping)
                    {
                        IsGroupPanelVisible = true;
                        // 切换协议组时，清除映射选择
                        SelectedMapping = null;
                    }
                }
                else
                {
                    // 未选中协议组时，隐藏协议组配置面板
                    IsGroupPanelVisible = false;
                }
            }
        }

        // 当前选中的映射
        private MappingDto? selectedMapping;
        public MappingDto? SelectedMapping
        {
            get => selectedMapping;
            set
            {
                this.RaiseAndSetIfChanged(ref selectedMapping, value);

                if (value != null)
                {
                    // 自动找到该映射所属的协议组并选中
                    var parentGroup = ProtocolGroups.FirstOrDefault(
                        g => g.Mappings.Contains(value)
                    );
                    if (parentGroup != null && SelectedProtocolGroup != parentGroup)
                    {
                        // 设置标志，表示这是从映射选择触发的
                        _isSettingProtocolGroupFromMapping = true;
                        SelectedProtocolGroup = parentGroup;
                        _isSettingProtocolGroupFromMapping = false;
                    }

                    // 注意：不再自动显示编辑面板，只有通过右键编辑命令才显示
                }
            }
        }

        private string configFilePath = string.Empty;
        public string ConfigFilePath
        {
            get => configFilePath;
            set => this.RaiseAndSetIfChanged(ref configFilePath, value);
        }

        private bool isEditPanelVisible = false;
        public bool IsEditPanelVisible
        {
            get => isEditPanelVisible;
            set => this.RaiseAndSetIfChanged(ref isEditPanelVisible, value);
        }

        // 可用的设备列表
        public ObservableCollection<string> AvailableDevices { get; } =
            new ObservableCollection<string>();

        // 可用的协议列表
        public ObservableCollection<string> AvailableProtocols { get; } =
            new ObservableCollection<string> { "NetIO", "Modbus-RTU", "Custom", "DirectOutput" };

        // 可用的自定义协议类型列表
        public ObservableCollection<string> AvailableCustomProtocols { get; } =
            new ObservableCollection<string> { "TCP-Client", "TCP-Server", "UDP", "Serial" };

        // 可用的数据格式列表
        public ObservableCollection<string> AvailableDataFormats { get; } =
            new ObservableCollection<string> { "ASCII", "HEX" };

        // 可用的映射类型列表
        public ObservableCollection<string> AvailableMappingTypes { get; } =
            new ObservableCollection<string> { "开关量", "模拟量" };

        // 可用的串口列表（动态获取）
        public ObservableCollection<string> AvailablePorts { get; } =
            new ObservableCollection<string>();

        // 可用的波特率列表
        public ObservableCollection<int> AvailableBaudRates { get; } =
            new ObservableCollection<int> { 9600, 19200, 38400, 57600, 115200 };

        // 可用的数据位列表
        public ObservableCollection<int> AvailableDataBits { get; } =
            new ObservableCollection<int> { 7, 8 };

        // 可用的校验位列表
        public ObservableCollection<string> AvailableParities { get; } =
            new ObservableCollection<string> { "None", "Odd", "Even", "Mark", "Space" };

        // 可用的停止位列表
        public ObservableCollection<int> AvailableStopBits { get; } =
            new ObservableCollection<int> { 1, 2 };

        private bool isGroupPanelVisible = false;
        public bool IsGroupPanelVisible
        {
            get => isGroupPanelVisible;
            set => this.RaiseAndSetIfChanged(ref isGroupPanelVisible, value);
        }

        // 映射命令
        public ReactiveCommand<Unit, Unit> AddMappingCommand { get; }
        public ReactiveCommand<Unit, Unit> DeleteMappingCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveConfigCommand { get; }
        public ReactiveCommand<Unit, Unit> LoadConfigCommand { get; }
        public ReactiveCommand<Unit, Unit> MoveUpCommand { get; }
        public ReactiveCommand<Unit, Unit> MoveDownCommand { get; }
        public ReactiveCommand<Unit, Unit> CloseEditPanelCommand { get; }

        // 协议组命令
        public ReactiveCommand<Unit, Unit> AddProtocolGroupCommand { get; }
        public ReactiveCommand<Unit, Unit> DeleteProtocolGroupCommand { get; }
        public ReactiveCommand<Unit, Unit> CloseGroupPanelCommand { get; }
        public ReactiveCommand<ProtocolGroup, Unit> ToggleGroupExpandCommand { get; }
        public ReactiveCommand<ProtocolGroup, Unit> SelectProtocolGroupCommand { get; }

        // 上下文菜单命令
        public ReactiveCommand<ProtocolGroup, Unit> AddMappingToGroupCommand { get; }
        public ReactiveCommand<ProtocolGroup, Unit> DeleteSpecificProtocolGroupCommand { get; }
        public ReactiveCommand<ProtocolGroup, Unit> ClearMappingsCommand { get; }
        public ReactiveCommand<ProtocolGroup, Unit> DeduplicateMappingsCommand { get; }
        public ReactiveCommand<MappingDto, Unit> DeleteSpecificMappingCommand { get; }
        public ReactiveCommand<MappingDto, Unit> MoveMappingUpCommand { get; }
        public ReactiveCommand<MappingDto, Unit> MoveMappingDownCommand { get; }
        public ReactiveCommand<MappingDto, Unit> EditMappingCommand { get; }
        public ReactiveCommand<ProtocolGroup, Unit> EditProtocolGroupCommand { get; }

        // 批量范围映射命令
        public ReactiveCommand<Unit, Unit> AddRangeMappingCommand { get; }

        // 刷新COM口命令
        public ReactiveCommand<Unit, Unit> RefreshComPortsCommand { get; }

        public EventForwardConfigViewModel()
        {
            // 设置默认配置文件路径
            ConfigFilePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Config",
                "EvtMapping.xml"
            );

            // 加载可用设备列表
            LoadAvailableDevices();

            // 刷新可用COM口列表
            RefreshAvailablePorts();

            // 绑定协议组数据源
            _protocolGroupsSource
                .Connect()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Bind(out var protocolGroups)
                .Subscribe();
            ProtocolGroups = protocolGroups;

            // 添加映射命令（添加到当前选中的协议组）
            var canAddMapping = this.WhenAnyValue(x => x.SelectedProtocolGroup)
                .Select(x => x != null);
            AddMappingCommand = ReactiveCommand.Create(
                () =>
                {
                    if (SelectedProtocolGroup != null)
                    {
                        var newMapping = new MappingDto
                        {
                            SourceKey = "Button_01",
                            TargetKey = "Button_01",
                            IsEnabled = true,
                            Description = "新建映射",
                            Type = "开关量"
                        };
                        SelectedProtocolGroup.Mappings.Add(newMapping);
                        SelectedMapping = newMapping;
                        SelectedProtocolGroup.IsExpanded = true; // 自动展开
                    }
                },
                canAddMapping
            );

            // 删除映射命令（从所属协议组中删除）
            var canDelete = this.WhenAnyValue(x => x.SelectedMapping).Select(x => x != null);
            DeleteMappingCommand = ReactiveCommand.Create(
                () =>
                {
                    if (SelectedMapping != null && SelectedProtocolGroup != null)
                    {
                        SelectedProtocolGroup.Mappings.Remove(SelectedMapping);
                        SelectedMapping = null;
                    }
                },
                canDelete
            );

            // 上移命令（在协议组内调整顺序）
            var canMoveUp = this.WhenAnyValue(x => x.SelectedMapping, x => x.SelectedProtocolGroup)
                .Select(
                    tuple =>
                        tuple.Item1 != null
                        && tuple.Item2 != null
                        && tuple.Item2.Mappings.IndexOf(tuple.Item1) > 0
                );
            MoveUpCommand = ReactiveCommand.Create(
                () =>
                {
                    if (SelectedMapping != null && SelectedProtocolGroup != null)
                    {
                        int index = SelectedProtocolGroup.Mappings.IndexOf(SelectedMapping);
                        if (index > 0)
                        {
                            SelectedProtocolGroup.Mappings.Move(index, index - 1);
                        }
                    }
                },
                canMoveUp
            );

            // 下移命令（在协议组内调整顺序）
            var canMoveDown = this.WhenAnyValue(
                    x => x.SelectedMapping,
                    x => x.SelectedProtocolGroup
                )
                .Select(
                    tuple =>
                        tuple.Item1 != null
                        && tuple.Item2 != null
                        && tuple.Item2.Mappings.IndexOf(tuple.Item1)
                            < tuple.Item2.Mappings.Count - 1
                );
            MoveDownCommand = ReactiveCommand.Create(
                () =>
                {
                    if (SelectedMapping != null && SelectedProtocolGroup != null)
                    {
                        int index = SelectedProtocolGroup.Mappings.IndexOf(SelectedMapping);
                        if (index < SelectedProtocolGroup.Mappings.Count - 1)
                        {
                            SelectedProtocolGroup.Mappings.Move(index, index + 1);
                        }
                    }
                },
                canMoveDown
            );

            // 保存配置命令
            SaveConfigCommand = ReactiveCommand.Create(() =>
            {
                try
                {
                    SaveConfig();
                }
                catch (Exception ex)
                {
                    // 这里可以添加错误提示
                    System.Diagnostics.Debug.WriteLine($"保存配置失败: {ex.Message}");
                }
            });

            // 加载配置命令
            LoadConfigCommand = ReactiveCommand.Create(() =>
            {
                try
                {
                    LoadConfig();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"加载配置失败: {ex.Message}");
                }
            });

            // 关闭编辑面板命令
            CloseEditPanelCommand = ReactiveCommand.Create(() =>
            {
                IsEditPanelVisible = false;
            });

            // 添加协议组命令
            AddProtocolGroupCommand = ReactiveCommand.Create(() =>
            {
                var newGroup = new ProtocolGroup
                {
                    GroupName = "新建协议组",
                    ProtocolType = "NetIO",
                    TargetIP = "127.0.0.1",
                    TargetPort = 8000,
                    Description = "协议组描述",
                    IsCustomMode = false,
                    SourceDevice = AvailableDevices.FirstOrDefault() ?? ""
                };
                _protocolGroupsSource.Add(newGroup);
                SelectedProtocolGroup = newGroup;
            });

            // 选择协议组命令（从UI触发）
            SelectProtocolGroupCommand = ReactiveCommand.Create<ProtocolGroup>(group =>
            {
                if (group != null)
                {
                    // 设置标志，防止自动显示配置面板
                    _isSettingProtocolGroupFromMapping = true;
                    SelectedProtocolGroup = group;
                    _isSettingProtocolGroupFromMapping = false;
                }
            });

            // 删除协议组命令（会同时删除其下所有映射）
            var canDeleteGroup = this.WhenAnyValue(x => x.SelectedProtocolGroup)
                .Select(x => x != null);
            DeleteProtocolGroupCommand = ReactiveCommand.Create(
                () =>
                {
                    if (SelectedProtocolGroup != null)
                    {
                        // 如果有映射，可以显示警告
                        if (SelectedProtocolGroup.Mappings.Count > 0)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"删除协议组将同时删除其下的 {SelectedProtocolGroup.Mappings.Count} 个映射"
                            );
                        }

                        _protocolGroupsSource.Remove(SelectedProtocolGroup);
                        SelectedProtocolGroup = null;
                    }
                },
                canDeleteGroup
            );

            // 关闭协议组面板命令
            CloseGroupPanelCommand = ReactiveCommand.Create(() =>
            {
                SelectedProtocolGroup = null;
            });

            // 切换协议组展开/折叠命令
            ToggleGroupExpandCommand = ReactiveCommand.Create<ProtocolGroup>(group =>
            {
                if (group != null)
                {
                    group.IsExpanded = !group.IsExpanded;
                }
            });

            // 上下文菜单命令实现

            // 1. 添加映射到指定组
            AddMappingToGroupCommand = ReactiveCommand.Create<ProtocolGroup>(group =>
            {
                if (group != null)
                {
                    var newMapping = new MappingDto
                    {
                        SourceKey = "Button_01",
                        TargetKey = "Button_01",
                        IsEnabled = true,
                        Description = "新建映射",
                        Type = "开关量"
                    };
                    group.Mappings.Add(newMapping);
                    group.IsExpanded = true;

                    // 选中新映射
                    SelectedProtocolGroup = group;
                    SelectedMapping = newMapping;
                }
            });

            // 2. 删除指定协议组
            DeleteSpecificProtocolGroupCommand = ReactiveCommand.Create<ProtocolGroup>(group =>
            {
                if (group != null)
                {
                    _protocolGroupsSource.Remove(group);
                    if (SelectedProtocolGroup == group)
                    {
                        SelectedProtocolGroup = null;
                    }
                }
            });

            // 3. 删除指定映射
            DeleteSpecificMappingCommand = ReactiveCommand.Create<MappingDto>(mapping =>
            {
                if (mapping != null)
                {
                    // 查找所属组
                    var parentGroup = ProtocolGroups.FirstOrDefault(
                        g => g.Mappings.Contains(mapping)
                    );
                    if (parentGroup != null)
                    {
                        parentGroup.Mappings.Remove(mapping);
                        if (SelectedMapping == mapping)
                        {
                            SelectedMapping = null;
                        }
                    }
                }
            });

            // 4. 上移指定映射
            MoveMappingUpCommand = ReactiveCommand.Create<MappingDto>(mapping =>
            {
                if (mapping != null)
                {
                    var parentGroup = ProtocolGroups.FirstOrDefault(
                        g => g.Mappings.Contains(mapping)
                    );
                    if (parentGroup != null)
                    {
                        int index = parentGroup.Mappings.IndexOf(mapping);
                        if (index > 0)
                        {
                            parentGroup.Mappings.Move(index, index - 1);
                        }
                    }
                }
            });

            // 5. 下移指定映射
            MoveMappingDownCommand = ReactiveCommand.Create<MappingDto>(mapping =>
            {
                if (mapping != null)
                {
                    var parentGroup = ProtocolGroups.FirstOrDefault(
                        g => g.Mappings.Contains(mapping)
                    );
                    if (parentGroup != null)
                    {
                        int index = parentGroup.Mappings.IndexOf(mapping);
                        if (index < parentGroup.Mappings.Count - 1)
                        {
                            parentGroup.Mappings.Move(index, index + 1);
                        }
                    }
                }
            });

            // 6. 编辑指定映射
            EditMappingCommand = ReactiveCommand.Create<MappingDto>(mapping =>
            {
                if (mapping != null)
                {
                    // 找到所属组并选中
                    var parentGroup = ProtocolGroups.FirstOrDefault(
                        g => g.Mappings.Contains(mapping)
                    );
                    if (parentGroup != null)
                    {
                        SelectedProtocolGroup = parentGroup;
                        SelectedMapping = mapping;

                        // 显示编辑面板，隐藏协议组配置面板
                        IsEditPanelVisible = true;
                        IsGroupPanelVisible = false;
                    }
                }
            });

            // 7. 编辑协议组
            EditProtocolGroupCommand = ReactiveCommand.Create<ProtocolGroup>(group =>
            {
                if (group != null)
                {
                    SelectedProtocolGroup = group;
                    IsGroupPanelVisible = true;
                    IsEditPanelVisible = false;
                }
            });

            // 8. 清空协议组所有映射
            ClearMappingsCommand = ReactiveCommand.Create<ProtocolGroup>(group =>
            {
                if (group != null && group.Mappings.Count > 0)
                {
                    group.Mappings.Clear();
                    if (SelectedMapping != null && SelectedProtocolGroup == group)
                    {
                        SelectedMapping = null;
                    }
                }
            });

            // 9. 映射键去重
            DeduplicateMappingsCommand = ReactiveCommand.Create<ProtocolGroup>(group =>
            {
                if (group != null && group.Mappings.Count > 0)
                {
                    // 根据SourceKey去重，保留第一个
                    var uniqueMappings = group.Mappings
                        .GroupBy(m => m.SourceKey)
                        .Select(g => g.First())
                        .ToList();

                    if (uniqueMappings.Count < group.Mappings.Count)
                    {
                        group.Mappings.Clear();
                        foreach (var mapping in uniqueMappings)
                        {
                            group.Mappings.Add(mapping);
                        }

                        // 如果当前选中的映射被删除了，清除选择
                        if (SelectedMapping != null && !uniqueMappings.Contains(SelectedMapping))
                        {
                            SelectedMapping = null;
                        }
                    }
                }
            });

            // 10. 批量范围映射命令
            var canAddRangeMapping = this.WhenAnyValue(x => x.SelectedProtocolGroup)
                .Select(x => x != null);
            AddRangeMappingCommand = ReactiveCommand.CreateFromTask(
                async () =>
                {
                    if (SelectedProtocolGroup != null)
                    {
                        await ShowRangeMappingDialog();
                    }
                },
                canAddRangeMapping
            );

            // 11. 刷新COM口命令
            RefreshComPortsCommand = ReactiveCommand.Create(() =>
            {
                RefreshAvailablePorts();
            });

            // 自动加载配置
            LoadConfig();
        }

        private async System.Threading.Tasks.Task ShowRangeMappingDialog()
        {
            var dialog = new Views.RangeMappingDialog
            {
                DataContext = new RangeMappingDialogViewModel(
                    SelectedProtocolGroup?.ProtocolType ?? "NetIO"
                )
            };

            var viewModel = (RangeMappingDialogViewModel)dialog.DataContext;

            // 获取当前事件转发配置窗口
            var ownerWindow = Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.Windows.OfType<Views.EventForwardConfigWindow>().FirstOrDefault()
                : null;

            // 订阅确认命令
            bool confirmed = false;
            viewModel.ConfirmCommand.Subscribe(_ =>
            {
                confirmed = true;
                dialog.Close();
            });

            viewModel.CancelCommand.Subscribe(_ =>
            {
                confirmed = false;
                dialog.Close();
            });

            if (ownerWindow != null)
            {
                await dialog.ShowDialog(ownerWindow);
            }
            else
            {
                // 如果找不到事件转发窗口，使用主窗口
                var mainWindow = (
                    Avalonia.Application.Current?.ApplicationLifetime
                    as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime
                )?.MainWindow;
                await dialog.ShowDialog(
                    mainWindow ?? throw new InvalidOperationException("无法获取窗口")
                );
            }

            // 如果用户确认，生成范围映射
            if (confirmed && SelectedProtocolGroup != null)
            {
                GenerateRangeMappings(viewModel);
            }
        }

        private void GenerateRangeMappings(RangeMappingDialogViewModel config)
        {
            if (SelectedProtocolGroup == null)
                return;

            // 计算映射数量
            int sourceCount = config.SourceEndChannel - config.SourceStartChannel + 1;
            int targetCount = config.TargetEndChannel - config.TargetStartChannel + 1;

            // 使用较小的数量作为生成数量
            int count = Math.Min(sourceCount, targetCount);

            for (int i = 0; i < count; i++)
            {
                int sourceChannel = config.SourceStartChannel + i;
                int targetChannel = config.TargetStartChannel + i;

                var mapping = new MappingDto
                {
                    SourceKey = $"{config.SourceKeyPrefix}{sourceChannel:00}",
                    TargetKey = $"{config.TargetKeyPrefix}{targetChannel:00}",
                    IsEnabled = config.IsEnabled,
                    Description = string.IsNullOrWhiteSpace(config.Description)
                        ? $"范围映射: {sourceChannel} -> {targetChannel}"
                        : config.Description,
                    Type = config.MappingType == "模拟量" ? "Analog" : "Digital"
                };

                SelectedProtocolGroup.Mappings.Add(mapping);
            }

            // 展开协议组以显示新添加的映射
            SelectedProtocolGroup.IsExpanded = true;
        }

        private void SaveConfig()
        {
            var configDto = new EventForwardConfigDto
            {
                ProtocolGroups = _protocolGroupsSource.Items.ToDtoList()
            };

            var directory = Path.GetDirectoryName(ConfigFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var serializer = new XmlSerializer(typeof(EventForwardConfigDto));
            using (var writer = new StreamWriter(ConfigFilePath))
            {
                serializer.Serialize(writer, configDto);
            }
        }

        private void LoadConfig()
        {
            if (!File.Exists(ConfigFilePath))
            {
                return;
            }

            try
            {
                var serializer = new XmlSerializer(typeof(EventForwardConfigDto));
                using (var reader = new StreamReader(ConfigFilePath))
                {
                    var configDto = (EventForwardConfigDto?)serializer.Deserialize(reader);
                    if (configDto?.ProtocolGroups == null)
                        return;

                    _protocolGroupsSource.Clear();
                    foreach (var group in configDto.ProtocolGroups.ToModelList())
                    {
                        _protocolGroupsSource.Add(group);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load config: {ex.Message}");
            }
        }

        private void LoadAvailableDevices()
        {
            try
            {
                // 从IORoot获取设备列表
                if (IORoot.Instance?.Devices != null)
                {
                    var devices = IORoot.Instance.Devices
                        .Where(d => d.Type != "Standard")
                        .GroupBy(d => d.Name)
                        .Select(g => g.First().Name)
                        .ToList();

                    AvailableDevices.Clear();
                    foreach (var device in devices)
                    {
                        AvailableDevices.Add(device);
                    }
                }

                // 如果没有设备，添加默认提示
                if (AvailableDevices.Count == 0)
                {
                    AvailableDevices.Add("默认设备");
                }
            }
            catch
            {
                // 如果获取失败，添加默认设备
                if (AvailableDevices.Count == 0)
                {
                    AvailableDevices.Add("默认设备");
                }
            }
        }

        /// <summary>
        /// 刷新系统可用的COM口列表
        /// </summary>
        private void RefreshAvailablePorts()
        {
            try
            {
                AvailablePorts.Clear();

                // 获取系统中所有可用的串口
                string[] portNames = SerialPort.GetPortNames();

                if (portNames.Length > 0)
                {
                    // 排序串口名称（COM1, COM2, COM3...）
                    var sortedPorts = portNames
                        .OrderBy(p =>
                        {
                            // 尝试提取数字部分进行数值排序
                            if (p.StartsWith("COM") && int.TryParse(p.Substring(3), out int num))
                                return num;
                            return int.MaxValue;
                        })
                        .ToList();

                    foreach (var port in sortedPorts)
                    {
                        AvailablePorts.Add(port);
                    }
                }
                else
                {
                    // 如果没有检测到串口，添加提示信息
                    AvailablePorts.Add("未检测到串口");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to refresh COM ports: {ex.Message}");
                // 出错时添加默认COM口
                if (AvailablePorts.Count == 0)
                {
                    AvailablePorts.Add("COM1");
                    AvailablePorts.Add("COM3");
                }
            }
        }
    }
}
