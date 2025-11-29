using Avalonia.Controls;
using DynamicData;
using IOTester.Models;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using IOToolkit;
using System.Xml.Serialization;

namespace IOTester.ViewModels
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

        private ProtocolGroup? selectedProtocolGroup;
        public ProtocolGroup? SelectedProtocolGroup
        {
            get => selectedProtocolGroup;
            set
            {
                this.RaiseAndSetIfChanged(ref selectedProtocolGroup, value);

                if (value != null)
                {
                    // 选中协议组时，自动显示协议组配置面板
                    IsGroupPanelVisible = true;

                    // 切换协议组时，清除映射选择（这将自动隐藏映射配置面板）
                    SelectedMapping = null;
                }
                else
                {
                    // 未选中协议组时，隐藏协议组配置面板
                    IsGroupPanelVisible = false;
                }
            }
        }

        // 当前选中的映射
        private ActionKeyMapping? selectedMapping;
        public ActionKeyMapping? SelectedMapping
        {
            get => selectedMapping;
            set
            {
                this.RaiseAndSetIfChanged(ref selectedMapping, value);

                if (value != null)
                {
                    // 选中映射时，自动显示映射配置面板
                    IsEditPanelVisible = true;
                    // 同时隐藏协议组配置面板，避免重叠或混淆
                    IsGroupPanelVisible = false;
                }
                else
                {
                    // 未选中映射时，隐藏映射配置面板
                    IsEditPanelVisible = false;
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
            new ObservableCollection<string> { "NetIO", "Modbus-RTU" };

        // 可用的串口列表
        public ObservableCollection<string> AvailablePorts { get; } =
            new ObservableCollection<string>
            {
                "COM1",
                "COM2",
                "COM3",
                "COM4",
                "COM5",
                "COM6",
                "COM7",
                "COM8"
            };

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
        public ObservableCollection<string> AvailableStopBits { get; } =
            new ObservableCollection<string> { "None", "One", "Two", "OnePointFive" };

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
        public ReactiveCommand<ActionKeyMapping, Unit> DeleteSpecificMappingCommand { get; }
        public ReactiveCommand<ActionKeyMapping, Unit> MoveMappingUpCommand { get; }
        public ReactiveCommand<ActionKeyMapping, Unit> MoveMappingDownCommand { get; }
        public ReactiveCommand<ActionKeyMapping, Unit> EditMappingCommand { get; }
        public ReactiveCommand<ProtocolGroup, Unit> EditProtocolGroupCommand { get; }

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
                        var newMapping = new ActionKeyMapping
                        {
                            SourceDevice = AvailableDevices.FirstOrDefault() ?? "",
                            SourceKey = "Button_01",
                            TargetKey = "Button_01",
                            IsEnabled = true,
                            Description = "新建映射"
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
                SelectedMapping = null;
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
                    Description = "协议组描述"
                };
                _protocolGroupsSource.Add(newGroup);
                SelectedProtocolGroup = newGroup;
            });

            // 选择协议组命令（从UI触发）
            SelectProtocolGroupCommand = ReactiveCommand.Create<ProtocolGroup>(group =>
            {
                if (group != null)
                {
                    SelectedProtocolGroup = group;
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
                    var newMapping = new ActionKeyMapping
                    {
                        SourceDevice = AvailableDevices.FirstOrDefault() ?? "",
                        SourceKey = "Button_01",
                        TargetKey = "Button_01",
                        IsEnabled = true,
                        Description = "新建映射"
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
            DeleteSpecificMappingCommand = ReactiveCommand.Create<ActionKeyMapping>(mapping =>
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
            MoveMappingUpCommand = ReactiveCommand.Create<ActionKeyMapping>(mapping =>
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
            MoveMappingDownCommand = ReactiveCommand.Create<ActionKeyMapping>(mapping =>
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
            EditMappingCommand = ReactiveCommand.Create<ActionKeyMapping>(mapping =>
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

            // 自动加载配置
            LoadConfig();
        }

        private void SaveConfig()
        {
            var configDto = new EventForwardConfigDto
            {
                ProtocolGroups = _protocolGroupsSource.Items
                    .Select(
                        g =>
                            new ProtocolGroupDto
                            {
                                Id = g.Id,
                                GroupName = g.GroupName,
                                ProtocolType = g.ProtocolType,
                                TargetIP = g.TargetIP,
                                TargetPort = g.TargetPort,
                                SerialPort = g.SerialPort,
                                BaudRate = g.BaudRate,
                                DataBits = g.DataBits,
                                Parity = g.Parity,
                                StopBits = g.StopBits,
                                Description = g.Description,
                                IsExpanded = g.IsExpanded,
                                Mappings = g.Mappings
                                    .Select(
                                        m =>
                                            new MappingDto
                                            {
                                                SourceDevice = m.SourceDevice,
                                                SourceKey = m.SourceKey,
                                                TargetKey = m.TargetKey,
                                                IsEnabled = m.IsEnabled,
                                                Description = m.Description
                                            }
                                    )
                                    .ToList()
                            }
                    )
                    .ToList()
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
                    foreach (var groupDto in configDto.ProtocolGroups)
                    {
                        var group = new ProtocolGroup
                        {
                            Id = groupDto.Id,
                            GroupName = groupDto.GroupName,
                            ProtocolType = groupDto.ProtocolType,
                            TargetIP = groupDto.TargetIP,
                            TargetPort = groupDto.TargetPort,
                            SerialPort = groupDto.SerialPort,
                            BaudRate = groupDto.BaudRate,
                            DataBits = groupDto.DataBits,
                            Parity = groupDto.Parity,
                            StopBits = groupDto.StopBits,
                            Description = groupDto.Description,
                            IsExpanded = groupDto.IsExpanded
                        };

                        foreach (var mappingDto in groupDto.Mappings)
                        {
                            var mapping = new ActionKeyMapping
                            {
                                SourceDevice = mappingDto.SourceDevice,
                                SourceKey = mappingDto.SourceKey,
                                TargetKey = mappingDto.TargetKey,
                                IsEnabled = mappingDto.IsEnabled,
                                Description = mappingDto.Description
                            };
                            group.Mappings.Add(mapping);
                        }

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
    }
}
