using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System;
using System.IO;
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
        set
        {
            this.RaiseAndSetIfChanged(ref isValid, value);
        }
    }


    [XmlIgnore]
    public string Title => $"{DllName}-{Index}";

    private readonly SourceList<Key> diKeySource = new SourceList<Key>();
    [XmlIgnore]
    public ReadOnlyObservableCollection<Key> DIKeys { get; }

    private readonly SourceList<Key> doKeySource = new SourceList<Key>();
    [XmlIgnore]
    public ReadOnlyObservableCollection<Key> DOKeys { get; }

    private readonly SourceList<Key> adKeySource = new SourceList<Key>();
    [XmlIgnore]
    public ReadOnlyObservableCollection<Key> ADKeys { get;  }

    private int selectedDICountIndex = 1;
    [XmlIgnore]
    public int SelectedDICountIndex
    {
        get => selectedDICountIndex;
        set
        {
            this.RaiseAndSetIfChanged(ref selectedDICountIndex, value);
        }
    }

    private int selectedDOCountIndex = 1;
    [XmlIgnore]
    public int SelectedDOCountIndex
    {
        get => selectedDOCountIndex;
        set
        {
            this.RaiseAndSetIfChanged(ref selectedDOCountIndex, value);
        }
    }

    private bool hasConfig = false;
    [XmlIgnore]
    public bool HasConfig { 
        get => hasConfig; 
        set 
        {
            this.RaiseAndSetIfChanged(ref hasConfig, value);
        } 
    }

    private int selectedADCountIndex = 0;
    [XmlIgnore]
    public int SelectedADCountIndex
    {
        get => selectedADCountIndex;
        set
        {
            this.RaiseAndSetIfChanged(ref selectedADCountIndex, value);
        }
    }

    private int offsetDIIndex = 0;
    [XmlIgnore]
    public int OffsetDIIndex
    {
        get => offsetDIIndex;
        set
        {
            this.RaiseAndSetIfChanged(ref offsetDIIndex, value);
        }
    }


    private int offsetDIMaxIndex = 0;
    [XmlIgnore]
    public int OffsetDIMaxIndex
    {
        get => offsetDIMaxIndex;
        set
        {
            this.RaiseAndSetIfChanged(ref offsetDIMaxIndex, value);
        }
    }

    private int offsetADIndex = 0;
    [XmlIgnore]
    public int OffsetADIndex
    {
        get=> offsetADIndex;
        set
        {
            this.RaiseAndSetIfChanged(ref offsetADIndex, value);
        }
    }

    private int offsetADMaxIndex = 0;
    [XmlIgnore]
    public int OffsetADMaxIndex
    {
        get=>offsetADMaxIndex;
        set
        {
            this.RaiseAndSetIfChanged(ref offsetADMaxIndex, value);
        }
    }

    private int offsetDOIndex = 0;
    [XmlIgnore]
    public int OffsetDOIndex
    {
        get => offsetDOIndex;
        set
        {
            this.RaiseAndSetIfChanged(ref offsetDOIndex, value);
        }
    }
    private int offsetDOMaxIndex = 247;
    [XmlAttribute]
    public int OffsetDOMaxIndex
    {
        get => offsetDOMaxIndex;
        set
        {
            this.RaiseAndSetIfChanged(ref offsetDOMaxIndex, value);
        }
    }

    [XmlIgnore]
    public string TestText => string.Join("-", Actions.Select(_ => _.Name));

    [XmlAttribute("Type")]
    public string Type { get; set; } = "External";

    [XmlAttribute("DllName")]
    public string DllName { get; set; } = "";

    [XmlAttribute("Index")]
    public int Index { get; set; }

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


    List<int> channels = new List<int> {8, 16, 32, 64, 128, 256 };

    [XmlIgnore]
    public ReactiveCommand<Unit,Unit> EditConfigCommand { get; }

    [XmlIgnore]
    public string AppRoot => AppDomain.CurrentDomain.BaseDirectory;

    private bool diExpand = true;
    [XmlAttribute]
    public bool DIExpand
    {
        get => diExpand;
        set=>this.RaiseAndSetIfChanged(ref diExpand, value);
    }

    private bool adExpand = true;
    [XmlAttribute] public bool ADExpand
    {
        get=> adExpand;
        set=>this.RaiseAndSetIfChanged(ref adExpand, value);
    }

    private bool doExpand = true;
    [XmlAttribute]
    public bool DOExpand
    {
        get => doExpand;
        set=>this.RaiseAndSetIfChanged(ref doExpand, value);
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
        get=>toggleAllDOText;
        set{
            this.RaiseAndSetIfChanged(ref  toggleAllDOText, value);
        }
    }

    [XmlIgnore]
    public ReactiveCommand<Unit,Unit> ToggleAllDOCommand { get; }

    private string columnLayout = "*,2,*";
    public string ColumnLayout
    {
        get => columnLayout;
        set
        {
            this.RaiseAndSetIfChanged(ref columnLayout, value);
        }
    }

    private bool userIOFullscreen = false;
    [XmlIgnore]
    public bool UserIOFullscreen
    {
        get => userIOFullscreen;
        set => this.RaiseAndSetIfChanged(ref  userIOFullscreen, value);
    }

    private bool standardIOFullscreen = false;
    [XmlIgnore]
    public bool StandardIOFullscreen
    {
        get=> standardIOFullscreen;
        set=> this.RaiseAndSetIfChanged(ref standardIOFullscreen, value);
    }

    private GridLength customWidth = new GridLength(1, GridUnitType.Star);

    public GridLength CustomWidth
    {
        get => customWidth;
        set => this.RaiseAndSetIfChanged(ref customWidth, value);
    }

    private GridLength standardWidth = new GridLength(1, GridUnitType.Star);
    public GridLength StandardWidth
    {
        get=> standardWidth;
        set { this.RaiseAndSetIfChanged(ref standardWidth, value); }
    }

    public Device()
    {
        actionSource
            .Connect()
            .AutoRefresh()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var readonlyActionList)
            .Subscribe();
        ActionList = readonlyActionList;

        oactionSource.Connect()
            .AutoRefresh()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var readOnlyOActionList)
            .Subscribe();
        OActionList = readOnlyOActionList;

        axisSource.Connect()
            .AutoRefresh()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var readonlyAxisList)
            .Subscribe();
        AxisList = readonlyAxisList;


        diKeySource.Connect()
          .AutoRefresh()
          .ObserveOn(RxApp.MainThreadScheduler)
          .Bind(out var readonlyDIKeyList)
          .Subscribe();
        DIKeys = readonlyDIKeyList;

        doKeySource.Connect()
            .AutoRefresh()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var readonlyDOKeyList)
            .Subscribe();
        DOKeys = readonlyDOKeyList;


        adKeySource.Connect()
            .AutoRefresh()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var readonlyADKeyList)
            .Subscribe();
        ADKeys = readonlyADKeyList;

        this.WhenAnyValue(x => x.SelectedDICountIndex, x=>x.OffsetDIIndex)
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

                var _newDIKeys = Enumerable.Range(_offset, channels[_idx]).Select(_id => new Key { Name = $"Button_{_id:D2}" });
                diKeySource.Edit(_source =>
                {
                    if (_source == null) return;
                    _source.Clear();
                    _source.AddRange(_newDIKeys);
                });
            });
        SelectedDICountIndex = 1;

        this.WhenAnyValue(x => x.SelectedADCountIndex, x=>x.OffsetADIndex)
            .Subscribe(item =>
            {
                var (_idx, _offset) = item;
                var _adOffsetMax = 256 - channels[_idx];
                OffsetADMaxIndex = _adOffsetMax;
                if (_offset > _adOffsetMax) {
                    _offset = _adOffsetMax;
                    OffsetADIndex = _adOffsetMax;
                }

                var _newADKeys = Enumerable.Range(_offset, channels[_idx]).Select(_id => new Key { Name = $"Axis_{_id:D2}" });
                adKeySource.Edit(_source =>
                {
                    if (_source == null) return;
                    _source.Clear();
                    _source.AddRange(_newADKeys);
                });
            
            });
        SelectedADCountIndex = 0;

        
        this.WhenAnyValue(x => x.SelectedDOCountIndex, x=>x.OffsetDOIndex).Subscribe(item =>
        {
            var (_idx, _offset) = item;

            var _doOffsetMax = 256 - channels[_idx];
            OffsetDOMaxIndex = _doOffsetMax;
            if(_offset > _doOffsetMax)
            {
                _offset = _doOffsetMax;
                OffsetDOIndex = _doOffsetMax;
            }

            var _newDOKeys = Enumerable.Range(_offset, channels[_idx]).Select(_id => new Key { Name = $"OAxis_{_id:D2}" });
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
            var _configPath = new List<string> { "config.ini", "config.xml" }.Select(_ => Path.Combine(_configDir, _)).Where(_ => File.Exists(_)).FirstOrDefault();
            if (string.IsNullOrEmpty(_configPath)) return;
            EditorLauncher.OpenWithPreferredEditor(_configPath);
        });

        ToggleAllDOCommand = ReactiveCommand.Create(() =>
        {
            AllOn = !AllOn;
            ToggleAllDOText = AllOn ? "全关" : "全开";
            Enumerable.Range(OffsetDOIndex, channels[SelectedDOCountIndex]).ForEach(_idx =>
            {
                IOToolkit.Key _oKey = $"OAxis_{_idx:D2}";
                this.devcie.SetDO(_oKey, AllOn ? 1 : 0);
            });
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
        }else if (UserIOFullscreen)
        {
            StandardWidth = new GridLength(0);
            CustomWidth = new GridLength(1,GridUnitType.Star);
        }
        else
        {
            Random random = new Random();
            StandardWidth = new GridLength(1+random.Next(1, 4) / 1000f, GridUnitType.Star);
            CustomWidth = new GridLength(1+random.Next(1, 4) / 1000f, GridUnitType.Star);
        }
    }

    IDisposable doKeyHandler = null;
    private void rebindDOEvents()
    {
        doKeyHandler?.Dispose();
        doKeyHandler = DOKeys.Select(_key => _key.OnToggleDO)
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

        Axes.ForEach(_axis => _axis.AfterDeserialization());
        Actions.ForEach(_action => _action.AfterDeserialization());
        OActions.ForEach(_oaction => _oaction.AfterDeserialization());

        TriggerUIUpdate();

        OActions.Select(_ => _.OnToggleDO).Merge().Subscribe(_params =>
        {
            var (OAction, Target) = _params;
            this.devcie.SetDO(OAction, Target ? 1 : 0);
            Debug.WriteLine($"{this.devcie.ID} SetDO {OAction} {Target}");
        });

        OActions
            .Select(_ => _.Keys).SelectMany(x => x)
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
            if (_source == null) return;
            _source.Clear();
            _source.AddRange(Actions);
        });

        oactionSource.Edit(_source =>
        {
            if (_source == null) return;
            _source.Clear();
            _source.AddRange(OActions);
        });

        axisSource.Edit(_source =>
        {
            if (_source == null) return;
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

    public void Update()
    {
        if(this.devcie == null)
        {
            this.devcie = IODeviceController.GetIODevice(Name);
        }
        IsValid = this.devcie.IsValid();
        if (IsValid == false) return;

        Actions.ForEach(_action =>
        {
            _action.Keys.ForEach(_key =>
            {
                //Debug.WriteLine(Name + " " + _key.Name);
                _key.Value = this.devcie.GetKey(_key.Name) ? 1 : 0;
                // Debug.WriteLine($"{this.devcie.ID} di {_key.Name} is:{_key.Value}");
            });
        });


        DIKeys.ToList().ForEach(_key =>
        {
            _key.Value = this.devcie.GetKey(_key.Name) ? 1 : 0;
        });

        ADKeys.ToList().ForEach(_key =>
        {
            _key.Value = this.devcie.GetAxisKey(_key.Name);
        });

        DOKeys.ToList().ForEach(_key =>
        {
            IOToolkit.Key doKey = _key.Name;
            _key.Value = this.devcie.GetDO(doKey);
        });

        Axes.ForEach(_axis =>
        {
            _axis.Value = this.devcie.GetAxis(_axis.Name);
            _axis.Keys.ForEach(_key =>
            {
                _key.Value = this.devcie.GetAxisKey(_key.Name);
            });
        });

        OActions.ForEach(_oaction =>
        {
            _oaction.Value = this.devcie.GetDO(_oaction.Name);
            // Debug.WriteLine(_oaction.Value);
            _oaction.Keys.ForEach(_key =>
            {
                IOToolkit.Key _ioKey = _key.Name;
                _key.Value = this.devcie.GetDO(_ioKey);
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
    public float Value
    {
        get => _value;
        set => this.RaiseAndSetIfChanged(ref _value, value);
    }

    private readonly ObservableAsPropertyHelper<bool> active;
    
    [XmlIgnore]
    public bool Active=>active.Value;
    public ReactiveCommand<Unit, bool> ToggleDOCommand { get; }

    public Subject<(string Name, float Value)> OnToggleDO = new Subject<(string Name, float Value)>();

    public Key()
    {
        ToggleDOCommand = ReactiveCommand.Create(() =>
        {
            var _targetValue = this.Value == 0 ? 1f * Scale : 0f;
            _targetValue = this.Invert == "True" ? 0:_targetValue;

            OnToggleDO.OnNext((Name, _targetValue));
            return true;
        });
        active = this.WhenAnyValue(x => x.Value,x=>x.Invert)
            .Select(x=>x.Item1!=0&&x.Item2=="False" || x.Item1==0 && x.Item2=="True")
            .ToProperty(this, x => x.Active);
    }

    [XmlAttribute("PreOffset"), DefaultValueAttribute(0)]
    public float PreOffset { get; set; } = 1;

    [XmlAttribute("PreScale"), DefaultValueAttribute(1)]
    public float PreScale { get; set; } = 1;

    [XmlAttribute("Min"), DefaultValueAttribute(int.MinValue)]
    public int Min { get; set; } = int.MinValue;

    [XmlAttribute("Max"), DefaultValueAttribute(int.MaxValue)]
    public int Max { get; set; } = int.MaxValue;

    [XmlAttribute("DeadZone"), DefaultValueAttribute(0)]
    public float DeadZone { get; set; } = 0;

    [XmlAttribute("Sensitivity"), DefaultValueAttribute(1)]
    public float Sensitivity { get; set; } = 1;

    [XmlAttribute("Exponent"), DefaultValueAttribute(1)]
    public float Exponent { get; set; } = 1;

    [XmlAttribute("Invert"), DefaultValueAttribute("False")]
    public string Invert { get; set; } = "False";

    [XmlAttribute("InvertEvent"), DefaultValueAttribute(false)]
    public string InvertEvent { get; set; } = "False";

    [XmlAttribute("Scale"), DefaultValueAttribute(1)]
    public float Scale { get; set; } = 1;
}

public class IONodeBase : ReactiveObject
{
    private string name;
    [XmlAttribute("Name")]
    public string Name
    {
        get => name;
        set
        {
            this.RaiseAndSetIfChanged(ref name, value);
        }
    }

    private string label;
    [XmlAttribute("Label")]
    public string Label
    {
        get => label;
        set
        {
            this.RaiseAndSetIfChanged(ref label, value);
        }
    }

    [XmlIgnore]
    public string DeviceLabel
    {
        get
        {
            return this.label ?? this.Name;
        }
    }

    [XmlElement("Key")]
    public List<Key> Keys { get; set; } = new List<Key>();

    public ObservableCollection<Key> KeyList { get; set; } = new ObservableCollection<Key>();

    public bool ShowKeyValue=>this is Axis || this is OAction;
    public bool ShowBtn => this is OAction;
    public bool ShowNodeValue => this is Axis;

    private ObservableAsPropertyHelper<bool> active;

    private float _value = 0f;
    public float Value
    {
        get => _value;
        set => this.RaiseAndSetIfChanged(ref _value, value);
    }

    [XmlIgnore]
    public bool Active => active.Value;

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
    public ReactiveCommand<Unit, bool> ToggleDOCommand { get; }
    public Subject<(string OAction, bool Target)> OnToggleDO { get; set; } = new Subject<(string OAction, bool Target)>();
    public IONodeBase()
    {
        // 页面设计模拟数据
        //Keys = Enumerable.Range(0, 2).Select(_ => new Key { Name = "B" }).ToList();

        var _normalKeys = Keys.Where(_ => _.InvertEvent == "False");
        active = _normalKeys
            .Select(_ => _.WhenAnyValue(key => key.Active)).Merge()
          .Select(_active => _normalKeys.Any(key => key.Active))
          .StartWith(_normalKeys.Any(key => key.Active)).ToProperty(this, vm => vm.Active);

        ToggleDOCommand = ReactiveCommand.Create(() =>
        {
            var _target = this.Value == 1 ? 0 : 1;
            //if (Active) {
            //    _target = 0;
            //}
            OnToggleDO.OnNext((Name,_target == 1));
            // this.Keys.ForEach(_key => _key.Value = _target);
            // this.Value = _target;
            return true;
        });

    }

    public void AfterDeserialization()
    {
        KeyList = new ObservableCollection<Key>(Keys);
        var _normalKeys = Keys.Where(_ => _.InvertEvent == "False");
        active = _normalKeys
            .Select(_ => _.WhenAnyValue(key => key.Active)).Merge()
          .Select(_active => _normalKeys.Any(key => key.Active))
          .StartWith(_normalKeys.Any(key => key.Active)).ToProperty(this, vm => vm.Active);
    }
}

public class Properties : IONodeBase { }

public class Action : IONodeBase { }

public class Axis : IONodeBase { }

public class OAction : IONodeBase { }