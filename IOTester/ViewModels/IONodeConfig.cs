using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System;
using System.IO;
using System.Windows.Input;
using System.Xml;
using System.Xml.Serialization;
using DNHper;
using IOToolkit;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Avalonia.Controls;
using System.Reactive;
using System.Diagnostics;
using System.Reactive.Subjects;
using DynamicData;
using DynamicData.Binding;
using IOTester.Models;
using IOTester.Services;

namespace IOTester.ViewModels;

// 定义XML节点对应的类结构
public class IORoot : SingletonConfig<IORoot>
{
    [XmlElement("Device")]
    public List<Device> Devices { get; set; } = new List<Device>();

    public Device FirstOrCreate(string deviceName)
    {
        var _device = Devices.Where(_ => _.Name == deviceName).FirstOrDefault();
        if (_device == null)
        {
            _device = new Device { Name = deviceName };
            this.Devices.Add(_device);
        }
        return _device;
    }

    public void AfterDeserialization()
    {
        Devices.ForEach(_device => _device.AfterDeserialization());
    }

    public void Update()
    {
        Devices.ForEach(_device => _device.Update());
    }
}

public class Device : ViewModelBase
{
    private string name = string.Empty;

    [XmlAttribute("Name")]
    public string Name
    {
        get { return name; }
        set { this.RaiseAndSetIfChanged(ref name, value); }
    }

    private bool isValid = false;

    [XmlIgnore]
    public bool IsValid
    {
        get => isValid;
        set { this.RaiseAndSetIfChanged(ref isValid, value); }
    }

    /// <summary>
    /// 获取设备的友好显示名称
    /// 格式: DisplayName-Index 或 DllName-Index
    /// </summary>
    [XmlIgnore]
    public string Title
    {
        get
        {
            try
            {
                var schema = Services.DeviceSchemaService.Instance.LoadSchema(DllName);
                if (schema != null)
                {
                    var displayName = !string.IsNullOrEmpty(schema.DisplayName)
                        ? schema.DisplayName
                        : DllName;
                    return $"{displayName}-{Index}";
                }
            }
            catch
            {
                // 如果加载schema失败，使用默认格式
            }
            return $"{DllName}-{Index}";
        }
    }

    private string _configSummaryText = string.Empty;

    /// <summary>
    /// 配置摘要文本，用于在设备界面顶部显示
    /// </summary>
    [XmlIgnore]
    public string ConfigSummaryText
    {
        get => _configSummaryText;
        set => this.RaiseAndSetIfChanged(ref _configSummaryText, value);
    }

    /// <summary>
    /// 更新配置摘要文本
    /// </summary>
    public void UpdateConfigSummary()
    {
        try
        {
            var summaryParts = new List<string>();
            var schema = Services.DeviceSchemaService.Instance.LoadSchema(DllName);
            if (schema?.Sections != null)
            {
                var iniService = Services.IniConfigService.Instance;
                var deviceSection = iniService.ReadSection(DllName, $"device_{Index}");

                foreach (var section in schema.Sections)
                {
                    foreach (var field in section.Fields)
                    {
                        if (field.ShowInSummary)
                        {
                            var key = field.Key;
                            string? value = null;

                            // 从对应的section读取值
                            if (!string.IsNullOrEmpty(section.IniSection))
                            {
                                var sectionData = iniService.ReadSection(
                                    DllName,
                                    section.IniSection
                                );
                                sectionData?.TryGetValue(key, out value);
                            }
                            else
                            {
                                deviceSection?.TryGetValue(key, out value);
                            }

                            if (!string.IsNullOrEmpty(value))
                            {
                                // 如果有选项，显示选项的label
                                var displayValue = value;
                                if (field.Options != null)
                                {
                                    var option = field.Options.FirstOrDefault(
                                        o =>
                                            o.Value.Equals(
                                                value,
                                                StringComparison.OrdinalIgnoreCase
                                            )
                                    );
                                    if (option != null)
                                    {
                                        displayValue = option.Label;
                                    }
                                }
                                summaryParts.Add($"{field.Label}: {displayValue}");
                            }
                        }
                    }
                }
            }
            ConfigSummaryText = string.Join("  |  ", summaryParts);
        }
        catch
        {
            ConfigSummaryText = string.Empty;
        }
    }

    private readonly SourceList<Key> diKeySource = new SourceList<Key>();

    [XmlIgnore]
    public ReadOnlyObservableCollection<Key> DIKeys { get; }

    private readonly SourceList<Key> doKeySource = new SourceList<Key>();

    [XmlIgnore]
    public ReadOnlyObservableCollection<Key> DOKeys { get; }

    private readonly SourceList<Key> adKeySource = new SourceList<Key>();

    [XmlIgnore]
    public ReadOnlyObservableCollection<Key> ADKeys { get; }

    private int selectedDICountIndex = 1;

    [XmlIgnore]
    public int SelectedDICountIndex
    {
        get => selectedDICountIndex;
        set { this.RaiseAndSetIfChanged(ref selectedDICountIndex, value); }
    }

    private int selectedDOCountIndex = 1;

    [XmlIgnore]
    public int SelectedDOCountIndex
    {
        get => selectedDOCountIndex;
        set { this.RaiseAndSetIfChanged(ref selectedDOCountIndex, value); }
    }

    private bool hasConfig = false;

    [XmlIgnore]
    public bool HasConfig
    {
        get => hasConfig;
        set { this.RaiseAndSetIfChanged(ref hasConfig, value); }
    }

    private int selectedADCountIndex = 0;

    [XmlIgnore]
    public int SelectedADCountIndex
    {
        get => selectedADCountIndex;
        set { this.RaiseAndSetIfChanged(ref selectedADCountIndex, value); }
    }

    private int offsetDIIndex = 0;

    [XmlIgnore]
    public int OffsetDIIndex
    {
        get => offsetDIIndex;
        set { this.RaiseAndSetIfChanged(ref offsetDIIndex, value); }
    }

    private int offsetDIMaxIndex = 0;

    [XmlIgnore]
    public int OffsetDIMaxIndex
    {
        get => offsetDIMaxIndex;
        set { this.RaiseAndSetIfChanged(ref offsetDIMaxIndex, value); }
    }

    private int offsetADIndex = 0;

    [XmlIgnore]
    public int OffsetADIndex
    {
        get => offsetADIndex;
        set { this.RaiseAndSetIfChanged(ref offsetADIndex, value); }
    }

    private int offsetADMaxIndex = 0;

    [XmlIgnore]
    public int OffsetADMaxIndex
    {
        get => offsetADMaxIndex;
        set { this.RaiseAndSetIfChanged(ref offsetADMaxIndex, value); }
    }

    private int offsetDOIndex = 0;

    [XmlIgnore]
    public int OffsetDOIndex
    {
        get => offsetDOIndex;
        set { this.RaiseAndSetIfChanged(ref offsetDOIndex, value); }
    }
    private int offsetDOMaxIndex = 247;

    [XmlIgnore]
    public int OffsetDOMaxIndex
    {
        get => offsetDOMaxIndex;
        set { this.RaiseAndSetIfChanged(ref offsetDOMaxIndex, value); }
    }

    [XmlIgnore]
    public string TestText => string.Join("-", Actions.Select(_ => _.Name));

    private string _type = "External";

    [XmlAttribute("Type")]
    public string Type
    {
        get => _type;
        set => this.RaiseAndSetIfChanged(ref _type, value);
    }

    private string _dllName = "";

    [XmlAttribute("DllName")]
    public string DllName
    {
        get => _dllName;
        set => this.RaiseAndSetIfChanged(ref _dllName, value);
    }

    private int _index;

    [XmlAttribute("Index")]
    public int Index
    {
        get => _index;
        set => this.RaiseAndSetIfChanged(ref _index, value);
    }

    /// <summary>
    /// 是否为 External 类型设备
    /// </summary>
    [XmlIgnore]
    public bool IsExternalType =>
        Type?.Equals("External", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// 可选的设备类型列表
    /// </summary>
    [XmlIgnore]
    public static List<string> AvailableTypes => new() { "Joystick", "External" };

    /// <summary>
    /// 可选的 DllName 列表（External 类型设备）
    /// </summary>
    [XmlIgnore]
    public static List<string> AvailableDllNames => new() { "MODBUS", "SNAP7", "IOHUB" };

    /// <summary>
    /// 可选的设备索引列表 (0-8)
    /// </summary>
    [XmlIgnore]
    public static List<int> AvailableIndexes => new() { 0, 1, 2, 3, 4, 5, 6, 7, 8 };

    [XmlElement("Properties")]
    public Properties Properties { get; set; } = new Properties();

    [XmlElement("Action")]
    public List<Action> Actions { get; set; } = new List<Action>();

    private readonly SourceList<Action> actionSource = new SourceList<Action>();

    [XmlIgnore]
    public ReadOnlyObservableCollection<Action> ActionList { get; }

    private readonly SourceList<OAction> oactionSource = new SourceList<OAction>();

    [XmlIgnore]
    public ReadOnlyObservableCollection<OAction> OActionList { get; }

    private readonly SourceList<Axis> axisSource = new SourceList<Axis>();

    [XmlIgnore]
    public ReadOnlyObservableCollection<Axis> AxisList { get; }

    [XmlElement("Axis")]
    public List<Axis> Axes { get; set; } = new List<Axis>();

    [XmlElement("OAction")]
    public List<OAction> OActions { get; set; } = new List<OAction>();

    public Action FirstOrCreateAction(string actionName)
    {
        var _action = Actions.Where(_ => _.Name == actionName).FirstOrDefault();
        if (_action == null)
        {
            _action = new Action { Name = actionName };
            Actions.Add(_action);
        }
        return _action;
    }

    public OAction FirstOrCreateOAction(string actionName)
    {
        var _action = OActions.Where(_ => _.Name == actionName).FirstOrDefault();
        if (_action == null)
        {
            _action = new OAction { Name = actionName };
            OActions.Add(_action);
        }
        return _action;
    }

    public Axis FirstOrCreateAxis(string axisName)
    {
        var _axis = Axes.Where(_ => _.Name == axisName).FirstOrDefault();
        if (_axis == null)
        {
            _axis = new Axis { Name = axisName };
            Axes.Add(_axis);
        }
        return _axis;
    }

    List<int> channels = new List<int> { 8, 16, 32, 64, 128, 256 };

    [XmlIgnore]
    public ReactiveCommand<Unit, Unit> EditConfigCommand { get; }

    [XmlIgnore]
    public string AppRoot => AppDomain.CurrentDomain.BaseDirectory;

    private bool diExpand = true;

    [XmlIgnore]
    public bool DIExpand
    {
        get => diExpand;
        set => this.RaiseAndSetIfChanged(ref diExpand, value);
    }

    private bool adExpand = true;

    [XmlIgnore]
    public bool ADExpand
    {
        get => adExpand;
        set => this.RaiseAndSetIfChanged(ref adExpand, value);
    }

    private bool doExpand = true;

    [XmlIgnore]
    public bool DOExpand
    {
        get => doExpand;
        set => this.RaiseAndSetIfChanged(ref doExpand, value);
    }

    private bool allOn = false;

    [XmlIgnore]
    public bool AllOn
    {
        get => allOn;
        set { this.RaiseAndSetIfChanged(ref allOn, value); }
    }
    private string toggleAllDOText = "全开";

    [XmlIgnore]
    public string ToggleAllDOText
    {
        get => toggleAllDOText;
        set { this.RaiseAndSetIfChanged(ref toggleAllDOText, value); }
    }

    [XmlIgnore]
    public ReactiveCommand<Unit, Unit> ToggleAllDOCommand { get; }

    /// <summary>
    /// 打开 DO 输出测试窗口
    /// </summary>
    [XmlIgnore]
    public ICommand OpenDOTestCommand { get; }

    /// <summary>
    /// 打开 OAction 输出测试窗口
    /// </summary>
    [XmlIgnore]
    public ICommand OpenOActionTestCommand { get; }

    private string columnLayout = "*,2,*";

    [XmlIgnore]
    public string ColumnLayout
    {
        get => columnLayout;
        set { this.RaiseAndSetIfChanged(ref columnLayout, value); }
    }

    private bool userIOFullscreen = false;

    [XmlIgnore]
    public bool UserIOFullscreen
    {
        get => userIOFullscreen;
        set => this.RaiseAndSetIfChanged(ref userIOFullscreen, value);
    }

    private bool standardIOFullscreen = false;

    [XmlIgnore]
    public bool StandardIOFullscreen
    {
        get => standardIOFullscreen;
        set => this.RaiseAndSetIfChanged(ref standardIOFullscreen, value);
    }

    private GridLength customWidth = new GridLength(1, GridUnitType.Star);

    [XmlIgnore]
    public GridLength CustomWidth
    {
        get => customWidth;
        set => this.RaiseAndSetIfChanged(ref customWidth, value);
    }

    private GridLength standardWidth = new GridLength(1, GridUnitType.Star);

    [XmlIgnore]
    public GridLength StandardWidth
    {
        get => standardWidth;
        set { this.RaiseAndSetIfChanged(ref standardWidth, value); }
    }

    private bool _isEditMode = false;

    [XmlIgnore]
    public bool IsEditMode
    {
        get => _isEditMode;
        set => this.RaiseAndSetIfChanged(ref _isEditMode, value);
    }

    private bool _isActionExpanded = true;

    [XmlIgnore]
    public bool IsActionExpanded
    {
        get => _isActionExpanded;
        set => this.RaiseAndSetIfChanged(ref _isActionExpanded, value);
    }

    private bool _isAxisExpanded = true;

    [XmlIgnore]
    public bool IsAxisExpanded
    {
        get => _isAxisExpanded;
        set => this.RaiseAndSetIfChanged(ref _isAxisExpanded, value);
    }

    private bool _isOActionExpanded = true;

    [XmlIgnore]
    public bool IsOActionExpanded
    {
        get => _isOActionExpanded;
        set => this.RaiseAndSetIfChanged(ref _isOActionExpanded, value);
    }

    // 添加/清空 Action 命令
    [XmlIgnore]
    public ICommand AddActionCommand =>
        ReactiveCommand.CreateFromTask(async () =>
        {
            var newAction = new Action
            {
                Name = $"Action_{Actions.Count:D2}",
                Label = $"新Action_{Actions.Count + 1}",
                Keys = new List<Key>
                {
                    new Key { Name = $"Button_{0:D2}", InvertEvent = "False" }
                }
            };
            newAction.AfterDeserialization(); // 初始化 KeyList
            newAction.DeleteNodeCommand = DeleteActionCommand; // 设置删除命令
            newAction.IsEditMode = this.IsEditMode; // 继承编辑模式状态
            newAction.ShowRecordBtn = true; // 在编辑窗口中始终显示录制按钮

            // 弹出编辑窗口确认
            var editWindow = new Views.IONodeEditWindow();
            editWindow.SetNode(newAction, this, true);
            await editWindow.ShowDialog(
                Avalonia.Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null
            );

            // 只有确认后才添加到列表
            if (editWindow.IsConfirmed)
            {
                Actions.Add(newAction);
                actionSource.Add(newAction);
                IORoot.Instance.Save();
            }
        });

    [XmlIgnore]
    public ICommand ClearActionCommand =>
        ReactiveCommand.Create(() =>
        {
            Actions.Clear();
            actionSource.Clear();
        });

    [XmlIgnore]
    public ReactiveCommand<Action, Unit> DeleteActionCommand =>
        ReactiveCommand.Create<Action>(action =>
        {
            if (action != null)
            {
                Actions.Remove(action);
                actionSource.Remove(action);
                IORoot.Instance.Save();
            }
        });

    // 添加/清空 Axis 命令
    [XmlIgnore]
    public ICommand AddAxisCommand =>
        ReactiveCommand.CreateFromTask(async () =>
        {
            var newAxis = new Axis
            {
                Name = $"Axis_{Axes.Count:D2}",
                Label = $"新Axis_{Axes.Count + 1}",
                Keys = new List<Key>
                {
                    new Key { Name = $"Axis_{0:D2}", InvertEvent = "False" }
                }
            };
            newAxis.AfterDeserialization(); // 初始化 KeyList
            newAxis.DeleteNodeCommand = DeleteAxisCommand; // 设置删除命令
            newAxis.IsEditMode = this.IsEditMode; // 继承编辑模式状态

            // 弹出编辑窗口确认
            var editWindow = new Views.IONodeEditWindow();
            editWindow.SetNode(newAxis, this, true);
            await editWindow.ShowDialog(
                Avalonia.Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null
            );

            // 只有确认后才添加到列表
            if (editWindow.IsConfirmed)
            {
                Axes.Add(newAxis);
                axisSource.Add(newAxis);
                IORoot.Instance.Save();
            }
        });

    [XmlIgnore]
    public ICommand ClearAxisCommand =>
        ReactiveCommand.Create(() =>
        {
            Axes.Clear();
            axisSource.Clear();
        });

    [XmlIgnore]
    public ReactiveCommand<Axis, Unit> DeleteAxisCommand =>
        ReactiveCommand.Create<Axis>(axis =>
        {
            if (axis != null)
            {
                Axes.Remove(axis);
                axisSource.Remove(axis);
                IORoot.Instance.Save();
            }
        });

    // 添加/清空 OAction 命令
    [XmlIgnore]
    public ICommand AddOActionCommand =>
        ReactiveCommand.CreateFromTask(async () =>
        {
            var newOAction = new OAction
            {
                Name = $"OAction_{OActions.Count:D2}",
                Label = $"新OAction_{OActions.Count + 1}",
                Keys = new List<Key>
                {
                    new Key { Name = $"OAxis_{0:D2}", InvertEvent = "False" }
                }
            };
            newOAction.AfterDeserialization(); // 初始化 KeyList
            newOAction.DeleteNodeCommand = DeleteOActionCommand; // 设置删除命令
            newOAction.IsEditMode = this.IsEditMode; // 继承编辑模式状态

            // 弹出编辑窗口确认
            var editWindow = new Views.IONodeEditWindow();
            editWindow.SetNode(newOAction, this, true);
            await editWindow.ShowDialog(
                Avalonia.Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null
            );

            // 只有确认后才添加到列表
            if (editWindow.IsConfirmed)
            {
                OActions.Add(newOAction);
                oactionSource.Add(newOAction);
                IORoot.Instance.Save();
            }
        });

    [XmlIgnore]
    public ICommand ClearOActionCommand =>
        ReactiveCommand.Create(() =>
        {
            OActions.Clear();
            oactionSource.Clear();
        });

    [XmlIgnore]
    public ReactiveCommand<OAction, Unit> DeleteOActionCommand =>
        ReactiveCommand.Create<OAction>(oaction =>
        {
            if (oaction != null)
            {
                OActions.Remove(oaction);
                oactionSource.Remove(oaction);
                IORoot.Instance.Save();
            }
        });

    [XmlIgnore]
    public ICommand EditDevicePropertiesCommand =>
        ReactiveCommand.CreateFromTask(async () =>
        {
            var window = new Views.DevicePropertiesWindow();
            window.SetDevice(this);
            // Get the main window from application lifetime
            var mainWindow = Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
            if (mainWindow != null)
            {
                await window.ShowDialog(mainWindow);
            }
        });

    public Device()
    {
        actionSource
            .Connect()
            // .AutoRefresh() 移除：只在集合变化时刷新，不监听每个属性变化
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var readonlyActionList)
            .Subscribe();
        ActionList = readonlyActionList;

        oactionSource
            .Connect()
            // .AutoRefresh() 移除：只在集合变化时刷新，不监听每个属性变化
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var readOnlyOActionList)
            .Subscribe();
        OActionList = readOnlyOActionList;

        axisSource
            .Connect()
            // .AutoRefresh() 移除：只在集合变化时刷新，不监听每个属性变化
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var readonlyAxisList)
            .Subscribe();
        AxisList = readonlyAxisList;

        diKeySource
            .Connect()
            // .AutoRefresh() 移除：只在集合变化时刷新，不监听每个属性变化
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var readonlyDIKeyList)
            .Subscribe();
        DIKeys = readonlyDIKeyList;

        doKeySource
            .Connect()
            // .AutoRefresh() 移除：只在集合变化时刷新，不监听每个属性变化
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var readonlyDOKeyList)
            .Subscribe();
        DOKeys = readonlyDOKeyList;

        adKeySource
            .Connect()
            // .AutoRefresh() 移除：只在集合变化时刷新，不监听每个属性变化
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var readonlyADKeyList)
            .Subscribe();
        ADKeys = readonlyADKeyList;

        this.WhenAnyValue(x => x.SelectedDICountIndex, x => x.OffsetDIIndex)
            .Subscribe(item =>
            {
                var (_idx, _offset) = item;
                var _diOffsetMax = 256 - channels[_idx];
                OffsetDIMaxIndex = _diOffsetMax;
                if (_offset > _diOffsetMax)
                {
                    _offset = _diOffsetMax;
                    OffsetDIIndex = _diOffsetMax;
                }

                var _newDIKeys = Enumerable
                    .Range(_offset, channels[_idx])
                    .Select(_id => new Key { Name = $"Button_{_id:D2}" });
                diKeySource.Edit(_source =>
                {
                    if (_source == null)
                        return;
                    _source.Clear();
                    _source.AddRange(_newDIKeys);
                });
            });
        SelectedDICountIndex = 1;

        this.WhenAnyValue(x => x.SelectedADCountIndex, x => x.OffsetADIndex)
            .Subscribe(item =>
            {
                var (_idx, _offset) = item;
                var _adOffsetMax = 256 - channels[_idx];
                OffsetADMaxIndex = _adOffsetMax;
                if (_offset > _adOffsetMax)
                {
                    _offset = _adOffsetMax;
                    OffsetADIndex = _adOffsetMax;
                }

                var _newADKeys = Enumerable
                    .Range(_offset, channels[_idx])
                    .Select(_id => new Key { Name = $"Axis_{_id:D2}" });
                adKeySource.Edit(_source =>
                {
                    if (_source == null)
                        return;
                    _source.Clear();
                    _source.AddRange(_newADKeys);
                });
            });
        SelectedADCountIndex = 0;

        this.WhenAnyValue(x => x.SelectedDOCountIndex, x => x.OffsetDOIndex)
            .Subscribe(item =>
            {
                var (_idx, _offset) = item;

                var _doOffsetMax = 256 - channels[_idx];
                OffsetDOMaxIndex = _doOffsetMax;
                if (_offset > _doOffsetMax)
                {
                    _offset = _doOffsetMax;
                    OffsetDOIndex = _doOffsetMax;
                }

                var _newDOKeys = Enumerable
                    .Range(_offset, channels[_idx])
                    .Select(_id => new Key { Name = $"OAxis_{_id:D2}" });
                doKeySource.Edit(_source =>
                {
                    _source.Clear();
                    _source.AddRange(_newDOKeys);
                });
                rebindDOEvents();
            });
        SelectedDOCountIndex = 1;

        EditConfigCommand = ReactiveCommand.Create(() =>
        {
            var _configDir = Path.Combine(AppRoot, "ExternalLibraries/Config", DllName);
            var _configPath = new List<string> { "config.ini", "config.xml" }
                .Select(_ => Path.Combine(_configDir, _))
                .Where(_ => File.Exists(_))
                .FirstOrDefault();
            if (string.IsNullOrEmpty(_configPath))
                return;
            EditorLauncher.OpenWithPreferredEditor(_configPath);
        });

        ToggleAllDOCommand = ReactiveCommand.Create(() =>
        {
            AllOn = !AllOn;
            ToggleAllDOText = AllOn ? "全关" : "全开";
            Enumerable
                .Range(OffsetDOIndex, channels[SelectedDOCountIndex])
                .ForEach(_idx =>
                {
                    IOToolkit.Key _oKey = $"OAxis_{_idx:D2}";
                    this.devcie.SetDO(_oKey, AllOn ? 1 : 0);
                });
        });

        // 初始化 DO 输出测试命令
        OpenDOTestCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var keys = DOKeys.ToList();
            if (keys.Count == 0)
                return;

            var vm = new OutputTestViewModel(this, keys, "DO 输出测试");
            var window = new Views.OutputTestWindow();
            window.SetViewModel(vm);

            var mainWindow = Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (mainWindow != null)
            {
                await window.ShowDialog(mainWindow);
            }
        });

        // 初始化 OAction 输出测试命令
        OpenOActionTestCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var keys = OActions.SelectMany(o => o.Keys).ToList();
            if (keys.Count == 0)
                return;

            var vm = new OutputTestViewModel(this, keys, "OAction 输出测试");
            var window = new Views.OutputTestWindow();
            window.SetViewModel(vm);

            var mainWindow = Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (mainWindow != null)
            {
                await window.ShowDialog(mainWindow);
            }
        });

        this.WhenAnyValue(x => x.UserIOFullscreen)
            .Subscribe(_fullScreen =>
            {
                if (_fullScreen)
                {
                    StandardIOFullscreen = false;
                }
                refreshLayout();
            });

        this.WhenAnyValue(x => x.StandardIOFullscreen)
            .Subscribe(_fullScreen =>
            {
                if (_fullScreen)
                {
                    UserIOFullscreen = false;
                }
                refreshLayout();
            });
    }

    private void refreshLayout()
    {
        if (StandardIOFullscreen)
        {
            StandardWidth = new GridLength(1, GridUnitType.Star);
            CustomWidth = new GridLength(0);
        }
        else if (UserIOFullscreen)
        {
            StandardWidth = new GridLength(0);
            CustomWidth = new GridLength(1, GridUnitType.Star);
        }
        else
        {
            Random random = new Random();
            StandardWidth = new GridLength(1 + random.Next(1, 4) / 1000f, GridUnitType.Star);
            CustomWidth = new GridLength(1 + random.Next(1, 4) / 1000f, GridUnitType.Star);
        }
    }

    IDisposable doKeyHandler = null;
    private bool _propsInitialized = false; // 标记是否已同步属性到 IODevice

    private void rebindDOEvents()
    {
        doKeyHandler?.Dispose();
        doKeyHandler = DOKeys
            .Select(_key => _key.OnToggleDO)
            .Merge()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_param =>
            {
                var (KeyName, KeyValue) = _param;
                IOToolkit.Key doKey = KeyName;
                this.devcie?.SetDO(doKey, KeyValue);
            });
    }

    IODevice devcie;

    public void AfterDeserialization()
    {
        var _configDir = Path.Combine(AppRoot, "ExternalLibraries/Config", DllName);
        var _configDirExists = Directory.Exists(_configDir);
        this.HasConfig = _configDirExists;

        // 初始化 Properties
        if (Properties != null)
        {
            Properties.AfterDeserialization();

            // 监听 PropertyKeys 的删除事件，恢复 IODevice 默认配置
            if (Properties.KeyList != null)
            {
                Properties.KeyList.CollectionChanged += (sender, e) =>
                {
                    if (
                        e.Action
                            == System.Collections.Specialized.NotifyCollectionChangedAction.Remove
                        && e.OldItems != null
                    )
                    {
                        foreach (Key removedKey in e.OldItems)
                        {
                            try
                            {
                                if (this.devcie != null && this.devcie.IsValid())
                                {
                                    // 恢复为默认值
                                    this.devcie.SetPKProps(
                                        removedKey.Name,
                                        0.0f, // Offset
                                        1.0f, // Scale
                                        -3.40282e+38f, // Min (-FLT_MAX)
                                        3.40282e+38f, // Max (FLT_MAX)
                                        0.0f, // DeadZone
                                        1.0f, // Sensitivity
                                        1.0f, // Exponent
                                        false, // Invert
                                        false // InvertEvent
                                    );
                                    Debug.WriteLine(
                                        $"[PropertyKey Removed] {Name}.{removedKey.Name} 已恢复默认配置"
                                    );
                                }
                            }
                            catch (System.Exception ex)
                            {
                                Debug.WriteLine($"恢复默认配置失败: {ex.Message}");
                            }
                        }
                    }
                };
            }
        }

        Axes.ForEach(_axis =>
        {
            _axis.AfterDeserialization();
            _axis.DeleteNodeCommand = DeleteAxisCommand;
        });
        Actions.ForEach(_action =>
        {
            _action.AfterDeserialization();
            _action.DeleteNodeCommand = DeleteActionCommand;
        });
        OActions.ForEach(_oaction =>
        {
            _oaction.AfterDeserialization();
            _oaction.DeleteNodeCommand = DeleteOActionCommand;
        });

        // 监听 IsEditMode 变化并同步到所有 IONode
        this.WhenAnyValue(x => x.IsEditMode)
            .Subscribe(isEditMode =>
            {
                Actions.ForEach(a => a.IsEditMode = isEditMode);
                Axes.ForEach(a => a.IsEditMode = isEditMode);
                OActions.ForEach(o => o.IsEditMode = isEditMode);
            });

        TriggerUIUpdate();

        // 更新配置摘要
        UpdateConfigSummary();

        OActions
            .Select(_ => _.OnToggleDO)
            .Merge()
            .Subscribe(_params =>
            {
                var (OAction, Target) = _params;
                this.devcie.SetDO(OAction, Target ? 1 : 0);
                Debug.WriteLine($"{this.devcie.ID} SetDO {OAction} {Target}");
            });

        OActions
            .Select(_ => _.Keys)
            .SelectMany(x => x)
            .Select(_key => _key.OnToggleDO)
            .Merge()
            .Subscribe(_params =>
            {
                var (KeyName, KeyValue) = _params;
                IOToolkit.Key _doKey = KeyName;
                this.devcie.SetDO(_doKey, KeyValue);
                Debug.WriteLine($"SetDO {this.devcie.ID} {_doKey} {KeyValue}");
            });
    }

    public void TriggerUIUpdate()
    {
        actionSource.Edit(_source =>
        {
            if (_source == null)
                return;
            _source.Clear();
            _source.AddRange(Actions);
        });

        oactionSource.Edit(_source =>
        {
            if (_source == null)
                return;
            _source.Clear();
            _source.AddRange(OActions);
        });

        axisSource.Edit(_source =>
        {
            if (_source == null)
                return;
            _source.Clear();
            _source.AddRange(Axes);
        });

        diKeySource.Edit(_source =>
        {
            var _keys = _source.ToList();
            _source.Clear();
            _source.AddRange(_keys);
        });

        adKeySource.Edit(_source =>
        {
            var _keys = _source.ToList();
            _source.Clear();
            _source.AddRange(_keys);
        });

        doKeySource.Edit(_source =>
        {
            var _keys = _source.ToList();
            _source.Clear();
            _source.AddRange(_keys);
            rebindDOEvents();
        });
        //DIExpand = !DIExpand;
        //ADExpand = !ADExpand;
        //DOExpand = !DOExpand;
        //Observable.Timer(TimeSpan.FromMilliseconds(50))
        //    .ObserveOn(RxApp.MainThreadScheduler)
        //    .Subscribe(_ =>
        //    {
        //        DIExpand = !DIExpand;
        //        ADExpand = !ADExpand;
        //        DOExpand = !DOExpand;
        //    });
    }

    /// <summary>
    /// 同步所有 OAction 的 Key 属性（Scale, InvertEvent）到 IODevice
    /// </summary>
    private void SyncOActionPropsToIODevice()
    {
        if (this.devcie == null || !this.devcie.IsValid())
            return;

        try
        {
            foreach (var oaction in OActions)
            {
                foreach (var key in oaction.Keys)
                {
                    this.devcie.SetOKProps(oaction.Name, key.Name, key.Scale, key.InvertEventBool);
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.WriteLine($"SyncOActionPropsToIODevice error: {ex.Message}");
        }
    }

    /// <summary>
    /// 同步 Properties 中的所有全局按键配置到 IODevice
    /// </summary>
    private void SyncPropertyKeysToIODevice()
    {
        if (this.devcie == null || !this.devcie.IsValid())
            return;

        if (Properties?.KeyList == null || Properties.KeyList.Count == 0)
            return;

        try
        {
            foreach (var key in Properties.KeyList)
            {
                this.devcie.SetPKProps(
                    key.Name,
                    key.Offset,
                    key.Scale,
                    key.Min,
                    key.Max,
                    key.DeadZone,
                    key.Sensitivity,
                    key.Exponent,
                    key.InvertBool,
                    key.InvertEventBool
                );
                Debug.WriteLine($"[SyncPropertyKeys] {Name}.{key.Name} synced to IODevice");
            }
        }
        catch (System.Exception ex)
        {
            Debug.WriteLine($"SyncPropertyKeysToIODevice error: {ex.Message}");
        }
    }

    public void Update()
    {
        if (this.devcie == null)
        {
            this.devcie = IODeviceController.GetIODevice(Name);
        }
        IsValid = this.devcie.IsValid();
        if (IsValid == false)
            return;

        // 首次设备有效时，同步 OAction 的 Scale 和 InvertEvent 属性到 IODevice
        if (!_propsInitialized)
        {
            _propsInitialized = true;
            SyncOActionPropsToIODevice();
            // SyncPropertyKeysToIODevice(); // 移除：IODevice 会从配置文件自动加载 Properties
        }

        // 只在值变化时更新，减少不必要的属性通知
        Actions.ForEach(_action =>
        {
            _action.Keys.ForEach(_key =>
            {
                var newValue = this.devcie.GetKey(_key.Name) ? 1f : 0f;
                if (Math.Abs(_key.Value - newValue) > float.Epsilon)
                    _key.Value = newValue;
            });
        });

        DIKeys
            .ToList()
            .ForEach(_key =>
            {
                var newValue = this.devcie.GetKey(_key.Name) ? 1f : 0f;
                if (Math.Abs(_key.Value - newValue) > float.Epsilon)
                    _key.Value = newValue;
            });

        ADKeys
            .ToList()
            .ForEach(_key =>
            {
                var newValue = this.devcie.GetRawKeyValue(_key.Name);
                if (Math.Abs(_key.Value - newValue) > float.Epsilon)
                    _key.Value = newValue;
            });

        DOKeys
            .ToList()
            .ForEach(_key =>
            {
                IOToolkit.Key doKey = _key.Name;
                var newValue = this.devcie.GetDO(doKey);
                if (Math.Abs(_key.Value - newValue) > float.Epsilon)
                    _key.Value = newValue;
            });

        Axes.ForEach(_axis =>
        {
            var newAxisValue = this.devcie.GetAxis(_axis.Name);
            if (Math.Abs(_axis.Value - newAxisValue) > float.Epsilon)
                _axis.Value = newAxisValue;

            _axis.Keys.ForEach(_key =>
            {
                var newValue = this.devcie.GetAxisKey(_key.Name);
                if (Math.Abs(_key.Value - newValue) > float.Epsilon)
                    _key.Value = newValue;
            });
        });

        OActions.ForEach(_oaction =>
        {
            var newOActionValue = this.devcie.GetDO(_oaction.Name);
            if (Math.Abs(_oaction.Value - newOActionValue) > float.Epsilon)
                _oaction.Value = newOActionValue;

            _oaction.Keys.ForEach(_key =>
            {
                IOToolkit.Key _ioKey = _key.Name;
                var newValue = this.devcie.GetDO(_ioKey);
                if (Math.Abs(_key.Value - newValue) > float.Epsilon)
                    _key.Value = newValue;
            });
        });
    }
}

public class Key : ReactiveObject
{
    private string name;

    [XmlAttribute("Name"), DefaultValueAttribute("")]
    public string Name
    {
        get => name;
        set { this.RaiseAndSetIfChanged(ref name, value); }
    }

    private float _value = 0f;

    [XmlIgnore]
    public float Value
    {
        get => _value;
        set => this.RaiseAndSetIfChanged(ref _value, value);
    }

    private bool _isEditing = false;

    [XmlIgnore]
    public bool IsEditing
    {
        get => _isEditing;
        set => this.RaiseAndSetIfChanged(ref _isEditing, value);
    }

    private bool _isContextMenuOpen = false;

    [XmlIgnore]
    public bool IsContextMenuOpen
    {
        get => _isContextMenuOpen;
        set => this.RaiseAndSetIfChanged(ref _isContextMenuOpen, value);
    }

    private bool _isNewKey = false;

    [XmlIgnore]
    public bool IsNewKey
    {
        get => _isNewKey;
        set => this.RaiseAndSetIfChanged(ref _isNewKey, value);
    }

    private readonly ObservableAsPropertyHelper<bool> active;

    [XmlIgnore]
    public bool Active => active.Value;

    [XmlIgnore]
    public ReactiveCommand<Unit, bool> ToggleDOCommand { get; }

    [XmlIgnore]
    public Subject<(string Name, float Value)> OnToggleDO =
        new Subject<(string Name, float Value)>();

    public Key()
    {
        ToggleDOCommand = ReactiveCommand.Create(() =>
        {
            // 只发送 0/1 原始值，Scale 和 InvertEvent 由 IODevice 端处理
            var _targetValue = this.Value == 0 ? 1f : 0f;

            OnToggleDO.OnNext((Name, _targetValue));
            return true;
        });
        active = this.WhenAnyValue(x => x.Value, x => x.Invert)
            .Select(x => x.Item1 != 0 && x.Item2 == "False" || x.Item1 == 0 && x.Item2 == "True")
            .ToProperty(this, x => x.Active);
    }

    private float _offset = 0;

    [XmlAttribute("Offset"), DefaultValueAttribute(0f)]
    public float Offset
    {
        get => _offset;
        set => this.RaiseAndSetIfChanged(ref _offset, value);
    }

    private float _scale = 1;

    [XmlAttribute("Scale"), DefaultValueAttribute(1f)]
    public float Scale
    {
        get => _scale;
        set => this.RaiseAndSetIfChanged(ref _scale, value);
    }

    private float _min = -3.40282e+38f; // 默认无限制 (-FLT_MAX)

    [XmlAttribute("Min"), DefaultValueAttribute(-3.40282e+38f)]
    public float Min
    {
        get => _min;
        set => this.RaiseAndSetIfChanged(ref _min, value);
    }

    private float _max = 3.40282e+38f; // 默认无限制 (FLT_MAX)

    [XmlAttribute("Max"), DefaultValueAttribute(3.40282e+38f)]
    public float Max
    {
        get => _max;
        set => this.RaiseAndSetIfChanged(ref _max, value);
    }

    /// <summary>
    /// Min 是否为无限制(对应 C++ 的 -FLT_MAX)
    /// </summary>
    [XmlIgnore]
    public bool IsMinUnlimited
    {
        get => Min <= -3.40282e+38f; // 接近 -FLT_MAX
        set => Min = value ? -3.40282e+38f : 0f;
    }

    /// <summary>
    /// Max 是否为无限制(对应 C++ 的 FLT_MAX)
    /// </summary>
    [XmlIgnore]
    public bool IsMaxUnlimited
    {
        get => Max >= 3.40282e+38f; // 接近 FLT_MAX
        set => Max = value ? 3.40282e+38f : 5f;
    }

    private float _deadZone = 0;

    [XmlAttribute("DeadZone"), DefaultValueAttribute(0f)]
    public float DeadZone
    {
        get => _deadZone;
        set => this.RaiseAndSetIfChanged(ref _deadZone, value);
    }

    private float _sensitivity = 1;

    [XmlAttribute("Sensitivity"), DefaultValueAttribute(1f)]
    public float Sensitivity
    {
        get => _sensitivity;
        set => this.RaiseAndSetIfChanged(ref _sensitivity, value);
    }

    private float _exponent = 1;

    [XmlAttribute("Exponent"), DefaultValueAttribute(1f)]
    public float Exponent
    {
        get => _exponent;
        set => this.RaiseAndSetIfChanged(ref _exponent, value);
    }

    private string _invert = "False";

    [XmlAttribute("Invert"), DefaultValueAttribute("False")]
    public string Invert
    {
        get => _invert;
        set => this.RaiseAndSetIfChanged(ref _invert, value);
    }

    private string _invertEvent = "False";

    [XmlAttribute("InvertEvent"), DefaultValueAttribute("False")]
    public string InvertEvent
    {
        get => _invertEvent;
        set => this.RaiseAndSetIfChanged(ref _invertEvent, value);
    }

    [XmlIgnore]
    public bool InvertEventBool
    {
        get => InvertEvent == "True";
        set => InvertEvent = value ? "True" : "False";
    }

    [XmlIgnore]
    public bool InvertBool
    {
        get => Invert == "True";
        set => Invert = value ? "True" : "False";
    }

    /// <summary>
    /// Scale 不为默认值 1 时返回 true，用于 UI 显示
    /// </summary>
    [XmlIgnore]
    public bool HasCustomScale => Scale != 1;

    /// <summary>
    /// 创建 Key 的副本
    /// </summary>
    public Key Clone()
    {
        return new Key
        {
            Name = this.Name,
            Offset = this.Offset,
            Scale = this.Scale,
            Min = this.Min,
            Max = this.Max,
            DeadZone = this.DeadZone,
            Sensitivity = this.Sensitivity,
            Exponent = this.Exponent,
            Invert = this.Invert,
            InvertEvent = this.InvertEvent
        };
    }
}

public class IONodeBase : ReactiveObject
{
    private string name;

    [XmlAttribute("Name")]
    public string Name
    {
        get => name;
        set { this.RaiseAndSetIfChanged(ref name, value); }
    }

    private string label;

    [XmlAttribute("Label")]
    public string Label
    {
        get => label;
        set { this.RaiseAndSetIfChanged(ref label, value); }
    }

    [XmlIgnore]
    public string DeviceLabel
    {
        get { return string.IsNullOrWhiteSpace(this.label) ? this.Name : this.label; }
    }

    [XmlElement("Key")]
    public List<Key> Keys { get; set; } = new List<Key>();

    [XmlIgnore]
    public ObservableCollection<Key> KeyList { get; set; } = new ObservableCollection<Key>();

    [XmlIgnore]
    public bool ShowKeyValue => this is Axis || this is OAction;

    [XmlIgnore]
    public bool ShowBtn => this is OAction;

    [XmlIgnore]
    public bool ShowNodeValue => this is Axis;

    [XmlIgnore]
    public bool ShowRecordBtn { get; set; }

    [XmlIgnore]
    public string ChannelTypeName =>
        this switch
        {
            IOTester.ViewModels.Action => "开关量输入",
            Axis => "模拟量输入",
            OAction => "输出通道",
            _ => "通道"
        };

    private ObservableAsPropertyHelper<bool> active;
    private IDisposable activeSubscription;

    private float _value = 0f;

    [XmlIgnore]
    public float Value
    {
        get => _value;
        set => this.RaiseAndSetIfChanged(ref _value, value);
    }

    [XmlIgnore]
    public bool Active => active?.Value ?? false;

    public Key FirstOrCreate(string keyName)
    {
        var _key = Keys.Where(_ => _.Name == keyName).FirstOrDefault();
        if (_key == null)
        {
            _key = new Key { Name = keyName };
            Keys.Add(_key);
        }
        return _key;
    }

    private bool _isEditMode = false;

    [XmlIgnore]
    public bool IsEditMode
    {
        get => _isEditMode;
        set => this.RaiseAndSetIfChanged(ref _isEditMode, value);
    }

    private bool _isRecording = false;

    [XmlIgnore]
    public bool IsRecording
    {
        get => _isRecording;
        set => this.RaiseAndSetIfChanged(ref _isRecording, value);
    }

    [XmlIgnore]
    public string RecordingToolTip => IsRecording ? "停止录制" : "录制按键";

    [XmlIgnore]
    public string WindowTitle
    {
        get
        {
            var nodeType = this switch
            {
                IOTester.ViewModels.Action => "Action 输入",
                Axis => "Axis 输入",
                OAction => "OAction 输出",
                _ => "节点"
            };
            return $"编辑节点 - {nodeType}";
        }
    }

    /// <summary>
    /// 创建节点的深拷贝副本（用于编辑时避免实时同步）
    /// </summary>
    public virtual IONodeBase Clone()
    {
        IONodeBase clone = this switch
        {
            IOTester.ViewModels.Action => new IOTester.ViewModels.Action(),
            Axis => new Axis(),
            OAction => new OAction(),
            _ => new Properties()
        };

        clone.Name = this.Name;
        clone.Label = this.Label;
        clone.IsEditMode = this.IsEditMode;
        clone.IsRecording = this.IsRecording;
        clone.DeleteNodeCommand = this.DeleteNodeCommand;

        // 深拷贝 Keys 列表
        foreach (var key in this.Keys)
        {
            var clonedKey = key.Clone();
            clone.Keys.Add(clonedKey);
            clone.KeyList.Add(clonedKey);
        }

        clone.RebuildActiveSubscription();
        return clone;
    }

    /// <summary>
    /// 从另一个节点复制数据（用于保存时同步回原对象）
    /// </summary>
    public virtual void CopyFrom(IONodeBase source)
    {
        this.Name = source.Name;
        this.Label = source.Label;

        // 清空并复制 Keys
        this.Keys.Clear();
        this.KeyList.Clear();

        foreach (var key in source.Keys)
        {
            var clonedKey = key.Clone();
            this.Keys.Add(clonedKey);
            this.KeyList.Add(clonedKey);
        }

        this.RebuildActiveSubscription();
    }

    [XmlIgnore]
    public ReactiveCommand<Unit, bool> ToggleDOCommand { get; }

    [XmlIgnore]
    public ReactiveCommand<Unit, Unit> AddKeyCommand { get; }

    [XmlIgnore]
    public ReactiveCommand<Key, Unit> DeleteKeyCommand { get; }

    [XmlIgnore]
    public ReactiveCommand<Key, Unit> EditKeyCommand { get; }

    [XmlIgnore]
    public ReactiveCommand<Unit, Unit> RecordKeyCommand { get; }

    [XmlIgnore]
    public ICommand? DeleteNodeCommand { get; set; }

    [XmlIgnore]
    public Subject<(string OAction, bool Target)> OnToggleDO { get; set; } =
        new Subject<(string OAction, bool Target)>();

    [XmlIgnore]
    public Subject<Key> OnEditKey { get; set; } = new Subject<Key>();

    public void RebuildActiveSubscription()
    {
        // 释放旧的订阅
        activeSubscription?.Dispose();
        active?.Dispose();

        var _normalKeys = Keys.Where(_ => _.InvertEvent == "False").ToList();

        if (_normalKeys.Any())
        {
            active = _normalKeys
                .Select(_ => _.WhenAnyValue(key => key.Active))
                .Merge()
                .Select(_active => _normalKeys.Any(key => key.Active))
                .StartWith(_normalKeys.Any(key => key.Active))
                .ToProperty(this, vm => vm.Active);
        }
        else
        {
            // 如果没有normal keys，创建一个始终为false的observable
            active = Observable.Return(false).ToProperty(this, vm => vm.Active);
        }
    }

    public IONodeBase()
    {
        // 页面设计模拟数据
        //Keys = Enumerable.Range(0, 2).Select(_ => new Key { Name = "B" }).ToList();

        // 初始化active订阅
        RebuildActiveSubscription();

        ToggleDOCommand = ReactiveCommand.Create(() =>
        {
            var _target = this.Value == 1 ? 0 : 1;
            //if (Active) {
            //    _target = 0;
            //}
            OnToggleDO.OnNext((Name, _target == 1));
            // this.Keys.ForEach(_key => _key.Value = _target);
            // this.Value = _target;
            return true;
        });

        // 添加Key命令（运行时也可用）
        AddKeyCommand = ReactiveCommand.Create(() =>
        {
            // 根据节点类型生成默认键名
            var prefix = this switch
            {
                Action => "Button",
                Axis => "Axis",
                OAction => "OAxis",
                _ => "Axis"
            };
            var existingKeyNames = Keys.Select(k => k.Name);
            var newKey = new Key
            {
                Name = KeyNameValidator.GenerateUniqueKeyName(prefix, existingKeyNames),
                InvertEvent = "False",
                IsNewKey = true // 标记为新增的Key
            };
            Keys.Add(newKey);
            KeyList.Add(newKey);

            // 重建active订阅以包含新Key
            RebuildActiveSubscription();

            // 添加后自动进入编辑状态
            OnEditKey.OnNext(newKey);
        });

        // 删除Key命令（运行时也可用）
        DeleteKeyCommand = ReactiveCommand.Create<Key>(key =>
        {
            if (key != null)
            {
                Keys.Remove(key);
                KeyList.Remove(key);

                // 重建active订阅
                RebuildActiveSubscription();

                // 保存配置到文件
                IORoot.Instance.Save();
            }
        });

        // 编辑Key命令（运行时也可用）
        EditKeyCommand = ReactiveCommand.Create<Key>(key =>
        {
            if (key != null)
            {
                OnEditKey.OnNext(key);
            }
        });

        // 录制Key命令（仅编辑模式且为Action节点时可用）
        RecordKeyCommand = ReactiveCommand.Create(() =>
        {
            IsRecording = !IsRecording;
            this.RaisePropertyChanged(nameof(RecordingToolTip));
        });
    }

    public void AfterDeserialization()
    {
        // 去除重复的 Key（按名称，保留第一个）
        var uniqueKeys = Keys.GroupBy(k => k.Name).Select(g => g.First()).ToList();

        if (uniqueKeys.Count < Keys.Count)
        {
            Keys = uniqueKeys;
        }

        KeyList = new ObservableCollection<Key>(Keys);

        // 重建active订阅
        RebuildActiveSubscription();
    }
}

public class Properties : IONodeBase { }

public class Action : IONodeBase { }

public class Axis : IONodeBase { }

public class OAction : IONodeBase { }
