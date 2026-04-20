using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    /// <summary>
    /// 关联的原始 Key 对象（用于获取实时值）
    /// </summary>
    public Key? SourceKey { get; set; }
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
    }

    private void LoadKeys(IEnumerable<Key> keys)
    {
        var testableKeys = keys.Select(
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

        _testKeysSource.Edit(list =>
        {
            list.Clear();
            list.AddRange(testableKeys);
        });
    }

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
    }

    #region 命令实现

    private void ExecuteSendAnalog()
    {
        var device = GetIODevice();
        if (device == null)
            return;

        foreach (var key in TestKeys.Where(k => k.IsSelected))
        {
            IOToolkit.Key ioKey = key.Name;
            device.SetDO(ioKey, AnalogValue);
        }
    }

    private void ExecuteSendAnalogToAll()
    {
        var device = GetIODevice();
        if (device == null)
            return;

        foreach (var key in TestKeys)
        {
            IOToolkit.Key ioKey = key.Name;
            device.SetDO(ioKey, AnalogValue);
        }
    }

    private void ExecuteSendPulse()
    {
        _ = SendPulseAsync();
    }

    private async Task SendPulseAsync()
    {
        var device = GetIODevice();
        if (device == null)
            return;

        var selectedKeys = TestKeys.Where(k => k.IsSelected).ToList();
        foreach (var key in selectedKeys)
        {
            key.IsHighlighted = true;
            IOToolkit.Key ioKey = key.Name;
            device.SetDO(ioKey, 1f);
        }

        await Task.Delay(PulseDuration);

        foreach (var key in selectedKeys)
        {
            key.IsHighlighted = false;
            IOToolkit.Key ioKey = key.Name;
            device.SetDO(ioKey, 0f);
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
                    IOToolkit.Key ioKey = key.Name;
                    device.SetDO(ioKey, 1f);
                }

                await Task.Delay(PulseDuration, ct);

                // 关闭
                foreach (var key in selectedKeys)
                {
                    key.IsHighlighted = false;
                    IOToolkit.Key ioKey = key.Name;
                    device.SetDO(ioKey, 0f);
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
        var device = GetIODevice();
        if (device == null)
            return;

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
                    IOToolkit.Key ioKey = key.Name;
                    device.SetDO(ioKey, 0f);
                }

                // 获取当前要点亮的通道
                var currentKey = selectedKeys[currentIndex];
                currentKey.IsHighlighted = true;
                CurrentSequenceKey = currentKey.DisplayName;
                IOToolkit.Key currentIoKey = currentKey.Name;
                device.SetDO(currentIoKey, 1f);

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
            var device2 = GetIODevice();
            if (device2 != null)
            {
                foreach (var key in TestKeys)
                {
                    key.IsHighlighted = false;
                    IOToolkit.Key ioKey = key.Name;
                    device2.SetDO(ioKey, 0f);
                }
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
        var device = GetIODevice();
        if (device == null)
            return;

        foreach (var key in TestKeys)
        {
            IOToolkit.Key ioKey = key.Name;
            device.SetDO(ioKey, 1f);
        }
    }

    private void ExecuteAllOff()
    {
        var device = GetIODevice();
        if (device == null)
            return;

        foreach (var key in TestKeys)
        {
            IOToolkit.Key ioKey = key.Name;
            device.SetDO(ioKey, 0f);
        }
    }

    private void ExecuteToggleKey(TestableKey key)
    {
        var device = GetIODevice();
        if (device == null)
            return;

        IOToolkit.Key ioKey = key.Name;
        var newValue = key.Value > 0.5f ? 0f : 1f;
        device.SetDO(ioKey, newValue);
    }

    private void ExecuteToggleSelection(TestableKey key)
    {
        key.IsSelected = !key.IsSelected;
    }

    #endregion

    /// <summary>
    /// 停止所有测试
    /// </summary>
    public void StopAllTests()
    {
        _sequenceCts?.Cancel();
        _pulseCts?.Cancel();
    }
}
