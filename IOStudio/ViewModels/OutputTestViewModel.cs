using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using DynamicData;
using DynamicData.Binding;
using IOToolkit;
using ReactiveUI;

namespace IOStudio.ViewModels;

/// <summary>
/// 输出测试模式
/// </summary>
public enum OutputTestMode
{
    Analog, // 模拟量输出
    Pulse, // 脉冲测试
    Sequence // 跑马灯测试
}

/// <summary>
/// 跑马灯方向
/// </summary>
public enum SequenceDirection
{
    Forward, // 正向 0→N
    Backward, // 反向 N→0
    PingPong // 往返
}

/// <summary>
/// 可测试的通道项
/// </summary>
public class TestableKey : ReactiveObject
{
    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    private string _displayName = string.Empty;
    public string DisplayName
    {
        get => _displayName;
        set => this.RaiseAndSetIfChanged(ref _displayName, value);
    }

    private float _value = 0f;
    public float Value
    {
        get => _value;
        set => this.RaiseAndSetIfChanged(ref _value, value);
    }

    private bool _isSelected = false;
    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    private bool _isHighlighted = false;
    public bool IsHighlighted
    {
        get => _isHighlighted;
        set => this.RaiseAndSetIfChanged(ref _isHighlighted, value);
    }

    private bool _isActive = false;
    public bool IsActive
    {
        get => _isActive;
        set => this.RaiseAndSetIfChanged(ref _isActive, value);
    }

    /// <summary>行内数值输出的目标值（0~100%）。</summary>
    private double _targetPercent = 0.0;
    public double TargetPercent
    {
        get => _targetPercent;
        set => this.RaiseAndSetIfChanged(ref _targetPercent, value);
    }

    /// <summary>是否模拟量通道（OAxis_ 前缀为连续量，其余视为开关量）。</summary>
    public bool IsAnalog => Name.StartsWith("OAxis_", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 关联的原始 Key 对象（用于获取实时值）
    /// </summary>
    public Key? SourceKey { get; set; }

    /// <summary>通道标记色（示波器波形/通道面板圆点共用）。</summary>
    private IBrush _colorBrush = Brushes.Gray;
    public IBrush ColorBrush
    {
        get => _colorBrush;
        set => this.RaiseAndSetIfChanged(ref _colorBrush, value);
    }
}

/// <summary>
/// 通道分组（OAxis_xx 索引通道 / OAction 命名通道）
/// </summary>
public class ChannelGroup
{
    public ChannelGroup(string name)
    {
        Name = name;
        Items = new ObservableCollection<TestableKey>();
    }

    public string Name { get; }
    public ObservableCollection<TestableKey> Items { get; }
}

/// <summary>
/// 输出测试 ViewModel
/// </summary>
public class OutputTestViewModel : ViewModelBase
{
    private readonly Device _device;
    private IODevice? _ioDevice;
    private CancellationTokenSource? _sequenceCts;
    private CancellationTokenSource? _pulseCts;

    // 高级调试工作台服务
    private readonly ScopeSampler _scopeSampler;
    private readonly SignalGenerator _generator;
    private readonly OutputRecorder _recorder = new();
    private CancellationTokenSource? _genCts;
    private CancellationTokenSource? _armCts;
    private bool _disposed;

    #region 属性

    private string _title = "输出测试";
    public string Title
    {
        get => _title;
        set => this.RaiseAndSetIfChanged(ref _title, value);
    }

    private string _dataSourceType = "DO 通道";
    public string DataSourceType
    {
        get => _dataSourceType;
        set => this.RaiseAndSetIfChanged(ref _dataSourceType, value);
    }

    private OutputTestMode _testMode = OutputTestMode.Analog;
    public OutputTestMode TestMode
    {
        get => _testMode;
        set => this.RaiseAndSetIfChanged(ref _testMode, value);
    }

    // 模拟量参数
    private float _analogValue = 0.5f;
    public float AnalogValue
    {
        get => _analogValue;
        set => this.RaiseAndSetIfChanged(ref _analogValue, value);
    }

    // 批量设值（0~100%，作用于选中通道）
    private double _batchValuePercent = 0.0;
    public double BatchValuePercent
    {
        get => _batchValuePercent;
        set => this.RaiseAndSetIfChanged(ref _batchValuePercent, value);
    }

    private float _analogMin = 0f;
    public float AnalogMin
    {
        get => _analogMin;
        set => this.RaiseAndSetIfChanged(ref _analogMin, value);
    }

    private float _analogMax = 1f;
    public float AnalogMax
    {
        get => _analogMax;
        set => this.RaiseAndSetIfChanged(ref _analogMax, value);
    }

    // 脉冲参数
    private int _pulseDuration = 500;
    public int PulseDuration
    {
        get => _pulseDuration;
        set => this.RaiseAndSetIfChanged(ref _pulseDuration, value);
    }

    private bool _isContinuousPulse = false;
    public bool IsContinuousPulse
    {
        get => _isContinuousPulse;
        set => this.RaiseAndSetIfChanged(ref _isContinuousPulse, value);
    }

    // 跑马灯参数
    private int _sequenceInterval = 200;
    public int SequenceInterval
    {
        get => _sequenceInterval;
        set => this.RaiseAndSetIfChanged(ref _sequenceInterval, value);
    }

    private int _sequenceDirectionIndex = 0;
    public int SequenceDirectionIndex
    {
        get => _sequenceDirectionIndex;
        set
        {
            this.RaiseAndSetIfChanged(ref _sequenceDirectionIndex, value);
            SequenceDirection = value switch
            {
                0 => SequenceDirection.Forward,
                1 => SequenceDirection.Backward,
                2 => SequenceDirection.PingPong,
                _ => SequenceDirection.Forward
            };
        }
    }

    private SequenceDirection _sequenceDirection = SequenceDirection.Forward;
    public SequenceDirection SequenceDirection
    {
        get => _sequenceDirection;
        set => this.RaiseAndSetIfChanged(ref _sequenceDirection, value);
    }

    private bool _isLooping = true;
    public bool IsLooping
    {
        get => _isLooping;
        set => this.RaiseAndSetIfChanged(ref _isLooping, value);
    }

    private bool _isSequenceRunning = false;
    public bool IsSequenceRunning
    {
        get => _isSequenceRunning;
        set => this.RaiseAndSetIfChanged(ref _isSequenceRunning, value);
    }

    private bool _isPulseRunning = false;
    public bool IsPulseRunning
    {
        get => _isPulseRunning;
        set => this.RaiseAndSetIfChanged(ref _isPulseRunning, value);
    }

    private string _currentSequenceKey = string.Empty;
    public string CurrentSequenceKey
    {
        get => _currentSequenceKey;
        set => this.RaiseAndSetIfChanged(ref _currentSequenceKey, value);
    }

    private int _selectedCount = 0;
    public int SelectedCount
    {
        get => _selectedCount;
        set => this.RaiseAndSetIfChanged(ref _selectedCount, value);
    }

    #endregion

    #region 调试模式

    private int _selectedModeIndex = 0;
    /// <summary>0=Scope 示波器 / 1=Generator 信号发生器 / 2=Pattern 跑马灯 / 3=Recorder 录波。</summary>
    public int SelectedModeIndex
    {
        get => _selectedModeIndex;
        set => this.RaiseAndSetIfChanged(ref _selectedModeIndex, value);
    }

    #endregion

    #region 安全护栏（L4）

    private bool _isArmed = false;
    /// <summary>ARM 已使能（安全解锁）。</summary>
    public bool IsArmed
    {
        get => _isArmed;
        set => this.RaiseAndSetIfChanged(ref _isArmed, value);
    }

    private bool _isArming = false;
    /// <summary>ARM 倒计时进行中。</summary>
    public bool IsArming
    {
        get => _isArming;
        set => this.RaiseAndSetIfChanged(ref _isArming, value);
    }

    private string _armCountdownText = "ARM 使能";
    public string ArmCountdownText
    {
        get => _armCountdownText;
        set => this.RaiseAndSetIfChanged(ref _armCountdownText, value);
    }

    private bool _isEstopActive = false;
    /// <summary>急停已触发（所有输出被强制置零并禁止写入）。</summary>
    public bool IsEstopActive
    {
        get => _isEstopActive;
        set => this.RaiseAndSetIfChanged(ref _isEstopActive, value);
    }

    private double _outputLimitPercent = 100.0;
    /// <summary>输出限幅 %（0~100），所有写入按此比例钳制。</summary>
    public double OutputLimitPercent
    {
        get => _outputLimitPercent;
        set => this.RaiseAndSetIfChanged(ref _outputLimitPercent, Math.Clamp(value, 0, 100));
    }

    private bool _isOutputEnabled = false;
    /// <summary>输出使能 = ARM 且未急停。所有实际写入前必须校验。</summary>
    public bool IsOutputEnabled
    {
        get => _isOutputEnabled;
        set => this.RaiseAndSetIfChanged(ref _isOutputEnabled, value);
    }

    #endregion

    #region 通道面板（L1）

    private string _channelFilter = string.Empty;
    public string ChannelFilter
    {
        get => _channelFilter;
        set
        {
            if (string.Equals(_channelFilter, value))
                return;
            this.RaiseAndSetIfChanged(ref _channelFilter, value);
            RebuildChannelGroups();
        }
    }

    /// <summary>分组后的通道列表（按 OAxis_ 索引 / OAction 命名分组）。</summary>
    public ObservableCollection<ChannelGroup> ChannelGroups { get; } = new();

    #endregion

    #region Scope 示波器

    /// <summary>时基选项 ms。</summary>
    public static IReadOnlyList<double> TimeBaseOptions { get; } = new[] { 10.0, 20.0, 50.0, 100.0, 200.0, 500.0, 1000.0, 2000.0, 5000.0 };
    /// <summary>采样率选项 Hz。</summary>
    public static IReadOnlyList<int> SampleRateOptions { get; } = new[] { 10, 50, 100, 200, 500, 1000 };

    private int _timeBaseIndex = 2; // 50ms → 全屏 500ms
    public int TimeBaseIndex
    {
        get => _timeBaseIndex;
        set
        {
            if (_timeBaseIndex == value)
                return;
            this.RaiseAndSetIfChanged(ref _timeBaseIndex, value);
            this.RaisePropertyChanged(nameof(ScopeTimeBaseMs));
        }
    }

    private int _sampleRateIndex = 1; // 50Hz
    public int SampleRateIndex
    {
        get => _sampleRateIndex;
        set => this.RaiseAndSetIfChanged(ref _sampleRateIndex, value);
    }

    private int _triggerModeIndex = 0; // 0=自由 1=上升沿 2=下降沿
    public int TriggerModeIndex
    {
        get => _triggerModeIndex;
        set => this.RaiseAndSetIfChanged(ref _triggerModeIndex, value);
    }

    private bool _isScopePaused = false;
    public bool IsScopePaused
    {
        get => _isScopePaused;
        set => this.RaiseAndSetIfChanged(ref _isScopePaused, value);
    }

    private double _verticalScalePercent = 100.0;
    public double VerticalScalePercent
    {
        get => _verticalScalePercent;
        set => this.RaiseAndSetIfChanged(ref _verticalScalePercent, Math.Clamp(value, 20, 500));
    }

    private double _verticalOffsetPercent = 0.0;
    public double VerticalOffsetPercent
    {
        get => _verticalOffsetPercent;
        set => this.RaiseAndSetIfChanged(ref _verticalOffsetPercent, Math.Clamp(value, -100, 100));
    }

    private ScopeFrame? _scopeFrame;
    public ScopeFrame? ScopeFrame
    {
        get => _scopeFrame;
        set => this.RaiseAndSetIfChanged(ref _scopeFrame, value);
    }

    /// <summary>示波器当前时基 ms（由 TimeBaseIndex 映射）。</summary>
    public double ScopeTimeBaseMs => TimeBaseOptions[TimeBaseIndex];

    #endregion

    #region Generator 信号发生器

    public static IReadOnlyList<string> WaveTypeNames { get; } =
        new[] { "正弦 Sine", "方波 Square", "三角 Triangle", "锯齿 Ramp", "阶跃 Step", "PWM", "扫频 Sweep" };

    private int _genWaveTypeIndex = 0;
    public int GenWaveTypeIndex
    {
        get => _genWaveTypeIndex;
        set => this.RaiseAndSetIfChanged(ref _genWaveTypeIndex, value);
    }

    private double _genFrequency = 1.0;
    public double GenFrequency
    {
        get => _genFrequency;
        set => this.RaiseAndSetIfChanged(ref _genFrequency, Math.Clamp(value, 0.1, 100));
    }

    private double _genAmplitude = 100.0;
    public double GenAmplitude
    {
        get => _genAmplitude;
        set => this.RaiseAndSetIfChanged(ref _genAmplitude, Math.Clamp(value, 0, 100));
    }

    private double _genOffset = 50.0;
    public double GenOffset
    {
        get => _genOffset;
        set => this.RaiseAndSetIfChanged(ref _genOffset, Math.Clamp(value, 0, 100));
    }

    private double _genDutyCycle = 50.0;
    public double GenDutyCycle
    {
        get => _genDutyCycle;
        set => this.RaiseAndSetIfChanged(ref _genDutyCycle, Math.Clamp(value, 1, 99));
    }

    private double _genPhase = 0.0;
    public double GenPhase
    {
        get => _genPhase;
        set => this.RaiseAndSetIfChanged(ref _genPhase, Math.Clamp(value, 0, 360));
    }

    private double _genDurationMs = 1000.0;
    public double GenDurationMs
    {
        get => _genDurationMs;
        set => this.RaiseAndSetIfChanged(ref _genDurationMs, Math.Clamp(value, 10, 600000));
    }

    private bool _isGenLoop = true;
    public bool IsGenLoop
    {
        get => _isGenLoop;
        set => this.RaiseAndSetIfChanged(ref _isGenLoop, value);
    }

    private bool _isGenRunning = false;
    public bool IsGenRunning
    {
        get => _isGenRunning;
        set => this.RaiseAndSetIfChanged(ref _isGenRunning, value);
    }

    private string _genStatusText = "就绪";
    public string GenStatusText
    {
        get => _genStatusText;
        set => this.RaiseAndSetIfChanged(ref _genStatusText, value);
    }

    #endregion

    #region Recorder 录波

    private bool _isRecording = false;
    public bool IsRecording
    {
        get => _isRecording;
        set => this.RaiseAndSetIfChanged(ref _isRecording, value);
    }

    private int _recordFrameCount = 0;
    public int RecordFrameCount
    {
        get => _recordFrameCount;
        set => this.RaiseAndSetIfChanged(ref _recordFrameCount, value);
    }

    private string _recordFilePath = string.Empty;
    public string RecordFilePath
    {
        get => _recordFilePath;
        set => this.RaiseAndSetIfChanged(ref _recordFilePath, value);
    }

    private string _recordStatusText = "未录制";
    public string RecordStatusText
    {
        get => _recordStatusText;
        set => this.RaiseAndSetIfChanged(ref _recordStatusText, value);
    }

    #endregion

    #region 通道列表

    private readonly SourceList<TestableKey> _testKeysSource = new();
    public ReadOnlyObservableCollection<TestableKey> TestKeys { get; }

    #endregion

    #region 命令

    public ReactiveCommand<Unit, Unit> SendAnalogCommand { get; }
    public ReactiveCommand<Unit, Unit> SendAnalogToAllCommand { get; }
    public ReactiveCommand<Unit, Unit> SendPulseCommand { get; }
    public ReactiveCommand<Unit, Unit> StartContinuousPulseCommand { get; }
    public ReactiveCommand<Unit, Unit> StopContinuousPulseCommand { get; }
    public ReactiveCommand<Unit, Unit> StartSequenceCommand { get; }
    public ReactiveCommand<Unit, Unit> StopSequenceCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectAllCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectNoneCommand { get; }
    public ReactiveCommand<Unit, Unit> InvertSelectionCommand { get; }
    public ReactiveCommand<Unit, Unit> AllOnCommand { get; }
    public ReactiveCommand<Unit, Unit> AllOffCommand { get; }
    public ReactiveCommand<TestableKey, Unit> ToggleKeyCommand { get; }
    public ReactiveCommand<TestableKey, Unit> ToggleSelectionCommand { get; }
    // 通道级调试表
    public ReactiveCommand<TestableKey, Unit> SetKeyOnCommand { get; }
    public ReactiveCommand<TestableKey, Unit> SetKeyOffCommand { get; }
    public ReactiveCommand<TestableKey, Unit> SendKeyValueCommand { get; }
    public ReactiveCommand<Unit, Unit> BatchOnCommand { get; }
    public ReactiveCommand<Unit, Unit> BatchOffCommand { get; }
    public ReactiveCommand<Unit, Unit> BatchSetValueCommand { get; }

    // 安全护栏
    public ReactiveCommand<Unit, Unit> ArmCommand { get; }
    public ReactiveCommand<Unit, Unit> EstopCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetEstopCommand { get; }
    // Generator
    public ReactiveCommand<Unit, Unit> StartGenCommand { get; }
    public ReactiveCommand<Unit, Unit> StopGenCommand { get; }
    // Recorder
    public ReactiveCommand<Unit, Unit> RecordCommand { get; }
    public ReactiveCommand<Unit, Unit> StopRecordCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveRecordCommand { get; }
    public ReactiveCommand<Unit, Unit> LoadRecordCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearRecordCommand { get; }

    #endregion

    public OutputTestViewModel(Device device, IEnumerable<Key> keys, string dataSourceType)
    {
        _device = device;
        DataSourceType = dataSourceType;
        Title = $"输出测试 - {device.Title} - {dataSourceType}";

        // 初始化通道列表
        _testKeysSource
            .Connect()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var testKeys)
            .Subscribe();
        TestKeys = testKeys;

        // 加载通道
        LoadKeys(keys);

        // 监听选中数量变化
        _testKeysSource
            .Connect()
            .AutoRefresh(x => x.IsSelected)
            .Subscribe(_ => UpdateSelectedCount());

        // 创建命令
        var hasSelection = this.WhenAnyValue(x => x.SelectedCount).Select(c => c > 0);

        SendAnalogCommand = ReactiveCommand.Create(ExecuteSendAnalog, hasSelection);
        SendAnalogToAllCommand = ReactiveCommand.Create(ExecuteSendAnalogToAll);
        SendPulseCommand = ReactiveCommand.Create(ExecuteSendPulse, hasSelection);

        var canStartPulse = this.WhenAnyValue(x => x.IsPulseRunning).Select(r => !r);
        var canStopPulse = this.WhenAnyValue(x => x.IsPulseRunning);
        StartContinuousPulseCommand = ReactiveCommand.Create(
            ExecuteStartContinuousPulse,
            canStartPulse.CombineLatest(hasSelection, (a, b) => a && b)
        );
        StopContinuousPulseCommand = ReactiveCommand.Create(
            ExecuteStopContinuousPulse,
            canStopPulse
        );

        var canStartSequence = this.WhenAnyValue(x => x.IsSequenceRunning).Select(r => !r);
        var canStopSequence = this.WhenAnyValue(x => x.IsSequenceRunning);
        StartSequenceCommand = ReactiveCommand.Create(
            ExecuteStartSequence,
            canStartSequence.CombineLatest(hasSelection, (a, b) => a && b)
        );
        StopSequenceCommand = ReactiveCommand.Create(ExecuteStopSequence, canStopSequence);

        SelectAllCommand = ReactiveCommand.Create(ExecuteSelectAll);
        SelectNoneCommand = ReactiveCommand.Create(ExecuteSelectNone);
        InvertSelectionCommand = ReactiveCommand.Create(ExecuteInvertSelection);
        AllOnCommand = ReactiveCommand.Create(ExecuteAllOn);
        AllOffCommand = ReactiveCommand.Create(ExecuteAllOff);
        ToggleKeyCommand = ReactiveCommand.Create<TestableKey>(ExecuteToggleKey);
        ToggleSelectionCommand = ReactiveCommand.Create<TestableKey>(ExecuteToggleSelection);
        SetKeyOnCommand = ReactiveCommand.Create<TestableKey>(ExecuteSetKeyOn);
        SetKeyOffCommand = ReactiveCommand.Create<TestableKey>(ExecuteSetKeyOff);
        SendKeyValueCommand = ReactiveCommand.Create<TestableKey>(ExecuteSendKeyValue);
        BatchOnCommand = ReactiveCommand.Create(ExecuteBatchOn, hasSelection);
        BatchOffCommand = ReactiveCommand.Create(ExecuteBatchOff, hasSelection);
        BatchSetValueCommand = ReactiveCommand.Create(ExecuteBatchSetValue, hasSelection);

        // —— 高级调试工作台初始化 ——
        _scopeSampler = new ScopeSampler(GetIODevice);
        _generator = new SignalGenerator(GetIODevice);
        _generator.AttachPreview(_scopeSampler);

        // 选中通道变化 → 重建示波器通道/预览
        _testKeysSource
            .Connect()
            .AutoRefresh(x => x.IsSelected)
            .Subscribe(_ => RebuildScopeChannels());

        // 输出使能 = ARM && !急停
        this.WhenAnyValue(x => x.IsArmed, x => x.IsEstopActive)
            .Subscribe(t => IsOutputEnabled = t.Item1 && !t.Item2);

        // 采样率/暂停 → 采样器
        this.WhenAnyValue(x => x.SampleRateIndex)
            .Subscribe(_ => _scopeSampler.SetSampleRateHz(GetSampleRateHz()));

        // 时基/触发变化 → 刷新当前帧
        this.WhenAnyValue(x => x.TimeBaseIndex, x => x.TriggerModeIndex)
            .Subscribe(_ => RefreshScopeFrame());

        // 安全护栏命令
        ArmCommand = ReactiveCommand.CreateFromTask(ExecuteArmAsync);
        EstopCommand = ReactiveCommand.Create(ExecuteEstop);
        ResetEstopCommand = ReactiveCommand.Create(ExecuteResetEstop, this.WhenAnyValue(x => x.IsEstopActive));
        // Generator 命令
        var genCanRun = this.WhenAnyValue(x => x.IsGenRunning, x => x.IsArmed, x => x.IsEstopActive)
            .Select(t => !t.Item1 && t.Item2 && !t.Item3);
        StartGenCommand = ReactiveCommand.CreateFromTask(ExecuteStartGenAsync, genCanRun);
        StopGenCommand = ReactiveCommand.Create(ExecuteStopGen, this.WhenAnyValue(x => x.IsGenRunning));
        // Recorder 命令
        RecordCommand = ReactiveCommand.Create(ExecuteRecord, this.WhenAnyValue(x => x.IsRecording, x => x.IsArmed, x => x.IsEstopActive)
            .Select(t => !t.Item1 && t.Item2 && !t.Item3));
        StopRecordCommand = ReactiveCommand.Create(ExecuteStopRecord, this.WhenAnyValue(x => x.IsRecording));
        SaveRecordCommand = ReactiveCommand.Create(ExecuteSaveRecord, this.WhenAnyValue(x => x.RecordFrameCount).Select(c => c > 0));
        LoadRecordCommand = ReactiveCommand.Create(ExecuteLoadRecord, this.WhenAnyValue(x => x.IsRecording).Select(r => !r));
        ClearRecordCommand = ReactiveCommand.Create(ExecuteClearRecord, this.WhenAnyValue(x => x.RecordFrameCount).Select(c => c > 0));

        // 启动采样
        _scopeSampler.SetSampleRateHz(GetSampleRateHz());
        _scopeSampler.Start();
    }

    private void LoadKeys(IEnumerable<Key> keys)
    {
        var all = keys.ToList();
        var testableKeys = all.Select(
                k =>
                    new TestableKey
                    {
                        Name = k.Name,
                        DisplayName = k.Name,
                        Value = k.Value,
                        SourceKey = k
                    }
            )
            .ToList();

        // 按列表顺序分配调色板颜色
        for (int i = 0; i < testableKeys.Count; i++)
            testableKeys[i].ColorBrush = ChannelPalette[i % ChannelPalette.Length];

        _testKeysSource.Edit(list =>
        {
            list.Clear();
            list.AddRange(testableKeys);
        });

        RebuildChannelGroups();
    }

    /// <summary>通道调色板（8 色，按选中顺序/列表顺序循环）。</summary>
    private static readonly IBrush[] ChannelPalette =
    {
        new SolidColorBrush(Color.Parse("#3978d4")),
        new SolidColorBrush(Color.Parse("#e8543f")),
        new SolidColorBrush(Color.Parse("#2ba640")),
        new SolidColorBrush(Color.Parse("#f5a623")),
        new SolidColorBrush(Color.Parse("#8e5ad6")),
        new SolidColorBrush(Color.Parse("#23b0c8")),
        new SolidColorBrush(Color.Parse("#e86fae")),
        new SolidColorBrush(Color.Parse("#7a8a99"))
    };

    /// <summary>
    /// 重建通道分组：OAxis_ 前缀按索引段分组（每 8 个一组），其余归入 OAction 组。
    /// </summary>
    private void RebuildChannelGroups()
    {
        string filter = ChannelFilter?.Trim() ?? string.Empty;

        var axisKeys = TestKeys.Where(k => k.Name.StartsWith("OAxis_", StringComparison.OrdinalIgnoreCase)).ToList();
        var actionKeys = TestKeys.Where(k => !k.Name.StartsWith("OAxis_", StringComparison.OrdinalIgnoreCase)).ToList();

        var groups = new List<ChannelGroup>();
        if (axisKeys.Count > 0)
        {
            // 按数字段分组：OAxis_0~7 → 0-7，OAxis_8~15 → 8-15 …
            var buckets = axisKeys
                .GroupBy(k =>
                {
                    var s = k.Name["OAxis_".Length..];
                    return int.TryParse(s, out var n) ? n / 8 : -1;
                })
                .OrderBy(g => g.Key);
            foreach (var g in buckets)
            {
                int start = g.Key * 8;
                int end = start + g.Count() - 1;
                var grp = new ChannelGroup($"OAxis {start}-{end}");
                foreach (var k in g.OrderBy(k => k.Name))
                    grp.Items.Add(k);
                groups.Add(grp);
            }
        }
        if (actionKeys.Count > 0)
        {
            var grp = new ChannelGroup("OAction");
            foreach (var k in actionKeys.OrderBy(k => k.Name))
                grp.Items.Add(k);
            groups.Add(grp);
        }

        ChannelGroups.Clear();
        foreach (var g in groups)
        {
            if (string.IsNullOrEmpty(filter) || g.Items.Any(k => k.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            {
                if (!string.IsNullOrEmpty(filter))
                {
                    var filtered = new ChannelGroup(g.Name);
                    foreach (var k in g.Items.Where(k => k.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                        filtered.Items.Add(k);
                    ChannelGroups.Add(filtered);
                }
                else
                {
                    ChannelGroups.Add(g);
                }
            }
        }
    }

    /// <summary>当前采样率 Hz（由 SampleRateIndex 映射）。</summary>
    private int GetSampleRateHz() => SampleRateOptions[SampleRateIndex];

    /// <summary>
    /// 按选中通道重建示波器采样通道，并同步预览曲线。
    /// </summary>
    private void RebuildScopeChannels()
    {
        var selected = TestKeys.Where(k => k.IsSelected).ToList();
        _scopeSampler.ReplaceChannels(selected.Select(k => (IOToolkit.Key)k.Name).ToList());
        _scopeSampler.ResetPreview();
    }

    /// <summary>刷新示波器当前帧（非暂停时）。</summary>
    public void RefreshScopeFrame()
    {
        if (IsScopePaused)
            return;
        var frame = _scopeSampler.Capture((ScopeTriggerMode)TriggerModeIndex, ScopeTimeBaseMs);
        if (frame != null)
            {
                var selected = TestKeys.Where(k => k.IsSelected).ToList();
                if (selected.Count > 0)
                    frame.Colors = selected.Select(k => k.ColorBrush).ToList();
                ScopeFrame = frame;
            }
        }

    /// <summary>
    /// 安全写出口：所有输出写入必须经过此方法。
    /// 未使能/急停时拒绝并返回 false；急停时强制置 0 直通。
    /// </summary>
    public bool SafeSetDO(IOToolkit.Key key, float value)
    {
        var device = GetIODevice();
        if (device == null)
            return false;

        if (IsEstopActive)
        {
            device.SetDO(key, 0f);
            return false;
        }

        if (!IsOutputEnabled)
            return false;

        float limit = (float)Math.Clamp(OutputLimitPercent / 100.0, 0, 1);
        device.SetDO(key, Math.Clamp(value, 0f, 1f) * limit);
        return true;
    }

    #region 命令实现

    private void UpdateSelectedCount()
    {
        SelectedCount = TestKeys.Count(k => k.IsSelected);
    }

    private IODevice? GetIODevice()
    {
        if (_ioDevice == null)
        {
            _ioDevice = IODeviceController.GetIODevice(_device.Name);
        }
        return _ioDevice?.IsValid() == true ? _ioDevice : null;
    }

    /// <summary>
    /// 更新通道值（由外部定时调用）
    /// </summary>
    public void UpdateValues()
    {
        var device = GetIODevice();
        if (device == null)
            return;

        foreach (var key in TestKeys)
        {
            IOToolkit.Key ioKey = key.Name;
            var newValue = device.GetDO(ioKey);
            if (Math.Abs(key.Value - newValue) > float.Epsilon)
            {
                key.Value = newValue;
                key.IsActive = newValue > 0.5f;
            }
        }

        // 录制采集：把选中通道值按轮询周期写入录波器
        if (IsRecording)
        {
            var selected = TestKeys.Where(k => k.IsSelected).Select(k => (double)k.Value).ToArray();
            if (selected.Length > 0)
            {
                _recorder.Append(selected);
                RecordFrameCount = _recorder.FrameCount;
            }
        }
    }

    private void ExecuteSendAnalog()
    {
        foreach (var key in TestKeys.Where(k => k.IsSelected))
        {
            SafeSetDO((IOToolkit.Key)key.Name, AnalogValue);
        }
    }

    private void ExecuteSendAnalogToAll()
    {
        foreach (var key in TestKeys)
        {
            SafeSetDO((IOToolkit.Key)key.Name, AnalogValue);
        }
    }

    private void ExecuteSendPulse()
    {
        _ = SendPulseAsync();
    }

    private async Task SendPulseAsync()
    {
        var selectedKeys = TestKeys.Where(k => k.IsSelected).ToList();
        if (!IsOutputEnabled)
            return;

        foreach (var key in selectedKeys)
        {
            key.IsHighlighted = true;
            SafeSetDO((IOToolkit.Key)key.Name, 1f);
        }

        await Task.Delay(PulseDuration);

        foreach (var key in selectedKeys)
        {
            key.IsHighlighted = false;
            SafeSetDO((IOToolkit.Key)key.Name, 0f);
        }
    }

    private void ExecuteStartContinuousPulse()
    {
        _pulseCts?.Cancel();
        _pulseCts = new CancellationTokenSource();
        IsPulseRunning = true;
        _ = ContinuousPulseAsync(_pulseCts.Token);
    }

    private async Task ContinuousPulseAsync(CancellationToken ct)
    {
        var device = GetIODevice();
        if (device == null)
            return;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var selectedKeys = TestKeys.Where(k => k.IsSelected).ToList();

                // 开启
                                foreach (var key in selectedKeys)
                                {
                                    key.IsHighlighted = true;
                                    if (!SafeSetDO((IOToolkit.Key)key.Name, 1f))
                                        break;
                                }

                                await Task.Delay(PulseDuration, ct);

                                // 关闭
                                foreach (var key in selectedKeys)
                                {
                                    key.IsHighlighted = false;
                                    SafeSetDO((IOToolkit.Key)key.Name, 0f);
                                }

                                await Task.Delay(PulseDuration, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        finally
        {
            IsPulseRunning = false;
            foreach (var key in TestKeys)
            {
                key.IsHighlighted = false;
            }
        }
    }

    private void ExecuteStopContinuousPulse()
    {
        _pulseCts?.Cancel();
    }

    private void ExecuteStartSequence()
    {
        _sequenceCts?.Cancel();
        _sequenceCts = new CancellationTokenSource();
        IsSequenceRunning = true;
        _ = SequenceAsync(_sequenceCts.Token);
    }

    private async Task SequenceAsync(CancellationToken ct)
        {
            try
            {
                var selectedKeys = TestKeys.Where(k => k.IsSelected).ToList();
                if (selectedKeys.Count == 0)
                    return;

                int currentIndex = 0;
                int direction = 1; // 1=正向, -1=反向

                while (!ct.IsCancellationRequested)
                {
                    // 关闭上一个
                    foreach (var key in selectedKeys)
                    {
                        key.IsHighlighted = false;
                        SafeSetDO((IOToolkit.Key)key.Name, 0f);
                    }

                    // 获取当前要点亮的通道
                    var currentKey = selectedKeys[currentIndex];
                    currentKey.IsHighlighted = true;
                    CurrentSequenceKey = currentKey.DisplayName;
                    if (!SafeSetDO((IOToolkit.Key)currentKey.Name, 1f))
                        break;

                    await Task.Delay(SequenceInterval, ct);

                // 计算下一个索引
                switch (SequenceDirection)
                {
                    case SequenceDirection.Forward:
                        currentIndex++;
                        if (currentIndex >= selectedKeys.Count)
                        {
                            if (IsLooping)
                                currentIndex = 0;
                            else
                                return;
                        }
                        break;

                    case SequenceDirection.Backward:
                        currentIndex--;
                        if (currentIndex < 0)
                        {
                            if (IsLooping)
                                currentIndex = selectedKeys.Count - 1;
                            else
                                return;
                        }
                        break;

                    case SequenceDirection.PingPong:
                        currentIndex += direction;
                        if (currentIndex >= selectedKeys.Count)
                        {
                            direction = -1;
                            currentIndex = selectedKeys.Count - 2;
                            if (currentIndex < 0)
                                currentIndex = 0;
                        }
                        else if (currentIndex < 0)
                        {
                            if (IsLooping)
                            {
                                direction = 1;
                                currentIndex = 1;
                                if (currentIndex >= selectedKeys.Count)
                                    currentIndex = 0;
                            }
                            else
                            {
                                return;
                            }
                        }
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        finally
        {
            IsSequenceRunning = false;
            CurrentSequenceKey = string.Empty;

            // 关闭所有通道
                        foreach (var key in TestKeys)
                        {
                            key.IsHighlighted = false;
                            SafeSetDO((IOToolkit.Key)key.Name, 0f);
                        }
                    }
                }

    private void ExecuteStopSequence()
    {
        _sequenceCts?.Cancel();
    }

    private void ExecuteSelectAll()
    {
        foreach (var key in TestKeys)
        {
            key.IsSelected = true;
        }
    }

    private void ExecuteSelectNone()
    {
        foreach (var key in TestKeys)
        {
            key.IsSelected = false;
        }
    }

    private void ExecuteInvertSelection()
    {
        foreach (var key in TestKeys)
        {
            key.IsSelected = !key.IsSelected;
        }
    }

    private void ExecuteAllOn()
    {
            foreach (var key in TestKeys)
            {
                SafeSetDO((IOToolkit.Key)key.Name, 1f);
            }
        }

        private void ExecuteAllOff()
        {
            foreach (var key in TestKeys)
            {
                SafeSetDO((IOToolkit.Key)key.Name, 0f);
            }
        }

        private void ExecuteToggleKey(TestableKey key)
        {
            var newValue = key.Value > 0.5f ? 0f : 1f;
            SafeSetDO((IOToolkit.Key)key.Name, newValue);
        }

                /// <summary>单通道置 1（开关量输出）。</summary>
                private void ExecuteSetKeyOn(TestableKey key)
                {
                    SafeSetDO((IOToolkit.Key)key.Name, 1f);
                }

                /// <summary>单通道置 0（开关量输出）。</summary>
                private void ExecuteSetKeyOff(TestableKey key)
                {
                    SafeSetDO((IOToolkit.Key)key.Name, 0f);
                }

                /// <summary>单通道发送行内数值（模拟量输出，0~100% → 0~1）。</summary>
                private void ExecuteSendKeyValue(TestableKey key)
                {
                    SafeSetDO((IOToolkit.Key)key.Name, (float)(key.TargetPercent / 100.0));
                }

                /// <summary>批量：选中通道全部置 1。</summary>
                private void ExecuteBatchOn()
                {
                    foreach (var key in TestKeys.Where(k => k.IsSelected))
                    {
                        SafeSetDO((IOToolkit.Key)key.Name, 1f);
                    }
                }

                /// <summary>批量：选中通道全部置 0。</summary>
                private void ExecuteBatchOff()
                {
                    foreach (var key in TestKeys.Where(k => k.IsSelected))
                    {
                        SafeSetDO((IOToolkit.Key)key.Name, 0f);
                    }
                }

                /// <summary>批量：选中通道统一输出指定数值。</summary>
                private void ExecuteBatchSetValue()
                {
                    var value = (float)(BatchValuePercent / 100.0);
                    foreach (var key in TestKeys.Where(k => k.IsSelected))
                    {
                        SafeSetDO((IOToolkit.Key)key.Name, value);
                    }
                }

        private void ExecuteToggleSelection(TestableKey key)
        {
            key.IsSelected = !key.IsSelected;
        }

        #endregion

        #region 高级调试命令实现

        /// <summary>ARM 使能：3 秒倒计时后解锁输出。</summary>
        private async Task ExecuteArmAsync()
        {
            if (IsArmed || IsArming)
                return;

            IsArming = true;
            try
            {
                var cts = new CancellationTokenSource();
                _armCts?.Cancel();
                _armCts = cts;
                for (int i = 3; i > 0; i--)
                {
                    if (cts.IsCancellationRequested)
                        return;
                    ArmCountdownText = $"ARM {i}s…";
                    try { await Task.Delay(1000, cts.Token); }
                    catch (TaskCanceledException) { return; }
                }
                IsArmed = true;
                ArmCountdownText = "已解锁";
            }
            finally
            {
                IsArming = false;
                _armCts = null;
            }
        }

        /// <summary>急停：所有输出强制归零并锁定写入。</summary>
        private void ExecuteEstop()
        {
            IsEstopActive = true;
            GenStatusText = "急停";
            StopGenerator();
            StopRecording();
            _sequenceCts?.Cancel();
            _pulseCts?.Cancel();

            var device = GetIODevice();
            if (device != null)
            {
                foreach (var key in TestKeys)
                {
                    try { device.SetDO((IOToolkit.Key)key.Name, 0f); }
                    catch { /* 忽略单通道失败 */ }
                }
            }
        }

        private void ExecuteResetEstop()
        {
            if (!IsEstopActive)
                return;
            IsEstopActive = false;
            GenStatusText = "就绪";
        }

        private async Task ExecuteStartGenAsync()
        {
            var targets = TestKeys.Where(k => k.IsSelected).Select(k => (IOToolkit.Key)k.Name).ToList();
            if (targets.Count == 0)
            {
                GenStatusText = "请先选择目标通道";
                return;
            }

            _genCts?.Cancel();
            var cts = new CancellationTokenSource();
            _genCts = cts;
            IsGenRunning = true;

            _generator.WaveType = (GeneratorWaveType)GenWaveTypeIndex;
            _generator.Frequency = GenFrequency;
            _generator.Amplitude = GenAmplitude;
            _generator.Offset = GenOffset;
            _generator.DutyCycle = GenDutyCycle;
            _generator.PhaseDeg = GenPhase;
            _generator.DurationMs = GenDurationMs;
            _generator.CycleLimit = IsGenLoop ? 0 : 1;

            GenStatusText = $"运行中 {WaveTypeNames[GenWaveTypeIndex]} @ {GenFrequency:0.##}Hz";

            try
            {
                await _generator.RunAsync(
                    cts.Token,
                    (key, value) => SafeSetDO(key, value),
                    targets,
                    preview => _scopeSampler.PushPreview(preview));
            }
            catch (OperationCanceledException)
            {
                // 正常停止
            }
            finally
            {
                IsGenRunning = false;
                _genCts = null;
                if (!IsEstopActive)
                {
                    GenStatusText = "已停止";
                    // 停止后复位所有目标通道
                    foreach (var key in targets)
                        SafeSetDO(key, 0f);
                    _scopeSampler.ResetPreview();
                }
            }
        }

        private void ExecuteStopGen()
        {
            StopGenerator();
            GenStatusText = "已停止";
            _scopeSampler.ResetPreview();
        }

        private void StopGenerator()
        {
            _genCts?.Cancel();
            _genCts = null;
            IsGenRunning = false;
        }

        private void ExecuteRecord()
        {
            var names = TestKeys.Where(k => k.IsSelected).Select(k => k.Name).ToList();
            if (names.Count == 0)
            {
                RecordStatusText = "请先选择录制通道";
                return;
            }
            _recorder.Begin(names, 0);
            IsRecording = true;
            RecordFrameCount = 0;
            RecordStatusText = $"录制中 {names.Count} 通道…";
        }

        private void ExecuteStopRecord()
        {
            StopRecording();
            if (RecordFrameCount > 0)
                RecordStatusText = $"已录制 {RecordFrameCount} 帧，可保存 CSV";
        }

        private void StopRecording()
        {
            if (!IsRecording)
                return;
            IsRecording = false;
        }

        private void ExecuteSaveRecord()
        {
            try
            {
                var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "OutputLogs");
                var path = Path.Combine(dir, $"DOLog_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                _recorder.Save(path, 1000.0 / GetSampleRateHz());
                RecordFilePath = path;
                RecordStatusText = $"已保存 {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                RecordStatusText = $"保存失败：{ex.Message}";
            }
        }

        private void ExecuteLoadRecord()
        {
            try
            {
                var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "OutputLogs");
                if (!Directory.Exists(dir))
                {
                    RecordStatusText = "无录波文件";
                    return;
                }
                var files = Directory.GetFiles(dir, "*.csv").OrderByDescending(f => f).ToList();
                if (files.Count == 0)
                {
                    RecordStatusText = "无录波文件";
                    return;
                }
                var (channels, intervalMs, names) = OutputRecorder.Load(files[0]);
                RecordFilePath = files[0];
                RecordStatusText = $"已加载 {Path.GetFileName(files[0])}（{channels.Length} 通道, {intervalMs:0.#}ms/帧）";
            }
            catch (Exception ex)
            {
                RecordStatusText = $"加载失败：{ex.Message}";
            }
        }

        private void ExecuteClearRecord()
        {
            _recorder.Clear();
            RecordFrameCount = 0;
            RecordFilePath = string.Empty;
            RecordStatusText = "未录制";
        }

        #endregion

        /// <summary>
        /// 停止所有测试（窗口关闭或急停时调用）
        /// </summary>
        public void StopAllTests()
        {
            _sequenceCts?.Cancel();
            _pulseCts?.Cancel();
            StopGenerator();
            StopRecording();
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            StopAllTests();
            _scopeSampler?.Dispose();
            _armCts?.Cancel();
            _genCts?.Cancel();
        }
    }
