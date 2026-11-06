using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using IOToolkit.WebBridge.Configuration;

namespace IOToolkit.WebBridge.Runtime;

public sealed record SnapshotFrame(
    long Seq,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, double> Values,
    bool Connected,
    bool Mock);

public sealed record RuntimeEvent(string Name, object? Payload, DateTimeOffset Timestamp);

public sealed class DeviceRuntime : IAsyncDisposable
{
    private readonly BridgeOptions _options;
    private readonly BridgeConfig _config;
    private readonly Action<string, Exception?>? _log;
    private readonly Channel<RuntimeCommand> _commands = Channel.CreateUnbounded<RuntimeCommand>();
    private readonly ConcurrentDictionary<string, double> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _snapshotGate = new();
    private IODevice? _device;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private long _seq;
    private SnapshotFrame _snapshot = new(0, DateTimeOffset.Now, new Dictionary<string, double>(), false, false);

    public DeviceRuntime(BridgeOptions options, BridgeConfig config, Action<string, Exception?>? log = null)
    {
        _options = options;
        _config = config;
        _log = log;
    }

    public BridgeConfig Config => _config;
    public BridgeOptions Options => _options;
    public SnapshotFrame Snapshot => Volatile.Read(ref _snapshot);
    public bool IsMock => _options.Mock;

    public event Action<SnapshotFrame>? SnapshotChanged;
    public event Action<RuntimeEvent>? EventRaised;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_loopTask is not null)
            return;

        if (!_options.Mock)
        {
            if (!File.Exists(_options.HardwareConfigPath))
                throw new FileNotFoundException("Hardware IODevice.xml was not found.", _options.HardwareConfigPath);

            var hardwareRoot = Path.GetDirectoryName(_options.HardwareConfigPath)!;
            var runtimeRootResult = IOSettings.SetIORuntimeRoot(AppContext.BaseDirectory);
            var pluginConfigResult = IOSettings.SetPluginConfigRoot(hardwareRoot);
            var logResult = IOSettings.SetIOLogDir(Path.Combine(_options.ProjectRoot, "logs"));
            var configResult = IOSettings.SetIOConfigPath(_options.HardwareConfigPath);
            _log?.Invoke($"IOSettings runtimeRoot={runtimeRootResult} pluginConfig={pluginConfigResult} log={logResult} config={configResult} hardware={_options.HardwareConfigPath}", null);

            var loadResult = IODeviceController.Load();
            _log?.Invoke($"IODeviceController.Load result={loadResult}", null);
            _device = IODeviceController.GetIODevice(_config.Device);
            if (_device is null || !_device.IsValid())
                throw new InvalidOperationException($"IODevice device '{_config.Device}' is unavailable.");

            BindInputs();
        }

        foreach (var channel in _config.Channels.Where(x => x.Read))
            _values[channel.Id] = ReadChannel(channel);

        PublishSnapshot(connected: true, force: true);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = Task.Run(() => RunLoopAsync(_cts.Token), CancellationToken.None);
        await Task.CompletedTask;
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromMilliseconds(_config.PollIntervalMs);
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                while (_commands.Reader.TryRead(out var command))
                    ExecuteCommand(command);

                if (!_options.Mock)
                    IODeviceController.Update();

                var changed = false;
                foreach (var channel in _config.Channels.Where(x => x.Read))
                {
                    var value = ReadChannel(channel);
                    if (!_values.TryGetValue(channel.Id, out var previous) || Math.Abs(previous - value) > 0.000001)
                    {
                        _values[channel.Id] = value;
                        changed = true;
                        EventRaised?.Invoke(new RuntimeEvent(
                            $"channel.{channel.Id}.changed",
                            new { channel = channel.Id, value },
                            DateTimeOffset.Now));
                    }
                }

                if (changed)
                    PublishSnapshot(connected: true);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _log?.Invoke("Device runtime loop failed.", ex);
            PublishSnapshot(connected: false, force: true);
            while (_commands.Reader.TryRead(out var command))
                command.Completion.TrySetException(ex);
        }
    }

    private void ExecuteCommand(RuntimeCommand command)
    {
        try
        {
            if (command.Kind == RuntimeCommandKind.SafeStop)
            {
                ExecuteSafeStop();
                command.Completion.TrySetResult(0);
                return;
            }

            var channel = FindChannel(command.ChannelId!);
            ValidateWritable(channel, command.Value!.Value);
            var writeResult = WriteChannel(channel, command.Value.Value);
            var flushResult = _options.Mock ? 1 : _device!.DOImmediate();
            if (!_options.Mock)
            {
                _log?.Invoke($"Readback action={SafeReadAction(channel.Key)} key={SafeReadKey(channel.Key)}", null);
            }
            _log?.Invoke($"Write {channel.Id} type={channel.Type} key={channel.Key} value={command.Value.Value} result={writeResult} flush={flushResult}", null);
            if (writeResult <= 0)
                throw new InvalidOperationException($"Native write for '{channel.Id}' failed with code {writeResult}.");

            if (channel.Read)
            {
                _values[channel.Id] = command.Value.Value;
                PublishSnapshot(connected: true);
            }
            command.Completion.TrySetResult(writeResult);
        }
        catch (Exception ex)
        {
            command.Completion.TrySetException(ex);
        }
    }

    public Task SetAsync(string channelId, double value, CancellationToken cancellationToken = default)
    {
        var channel = FindChannel(channelId);
        ValidateWritable(channel, value);

        if (_options.Mock)
        {
            _values[channel.Id] = value;
            PublishSnapshot(connected: true);
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_commands.Writer.TryWrite(new RuntimeCommand(RuntimeCommandKind.Set, channel.Id, value, completion)))
            return Task.FromException(new InvalidOperationException("Bridge runtime is not accepting commands."));
        return WaitForCompletion(completion.Task, cancellationToken);
    }

    public async Task RunActionAsync(string actionId, CancellationToken cancellationToken = default)
    {
        var action = _config.Actions.FirstOrDefault(x => string.Equals(x.Id, actionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Action '{actionId}' was not found.");

        await SetAsync(action.Channel, action.Value, cancellationToken);
        if (action.PulseMs > 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(action.PulseMs);
                    await SetAsync(action.Channel, 0);
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"Pulse reset for action '{action.Id}' failed.", ex);
                }
            });
        }
    }

    public Task SafeStopAsync(CancellationToken cancellationToken = default)
    {
        if (_loopTask is null)
            return Task.CompletedTask;
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_commands.Writer.TryWrite(new RuntimeCommand(RuntimeCommandKind.SafeStop, null, null, completion)))
            return Task.FromException(new InvalidOperationException("Bridge runtime is not accepting commands."));
        return WaitForCompletion(completion.Task, cancellationToken);
    }

    public Task TriggerMockInputAsync(string channelId, string eventName)
    {
        if (!_options.Mock)
            return Task.FromException(new InvalidOperationException("Mock input is only available in mock mode."));
        var channel = FindChannel(channelId);
        if (channel.Type != "DI")
            return Task.FromException(new InvalidOperationException($"Channel '{channelId}' is not a DI input."));

        var normalized = eventName.Trim().ToLowerInvariant();
        if (normalized is not ("pressed" or "released"))
            return Task.FromException(new InvalidOperationException("Mock event must be pressed or released."));

        _values[channel.Id] = normalized == "pressed" ? 1 : 0;
        EventRaised?.Invoke(new RuntimeEvent(
            $"input.{channel.Id}.{normalized}",
            new { channel = channel.Id, value = _values[channel.Id] },
            DateTimeOffset.Now));
        PublishSnapshot(connected: true);
        return Task.CompletedTask;
    }

    private void ExecuteSafeStop()
    {
        if (_options.Mock)
        {
            foreach (var channel in _config.Channels.Where(x => x.Write && x.Read))
                _values[channel.Id] = channel.ClampSafeValue();
            PublishSnapshot(connected: true, force: true);
            return;
        }

        IODeviceController.EnterSafeState();
        foreach (var channel in _config.Channels.Where(x => x.Write))
        {
            try
            {
                var safeValue = channel.ClampSafeValue();
                if (Math.Abs(safeValue - channel.SafeValue) > 0.000001)
                    _log?.Invoke(
                        $"Safe value {channel.SafeValue} for '{channel.Id}' is outside [{channel.Min}, {channel.Max}]; using {safeValue}.",
                        null);
                WriteChannel(channel, safeValue);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Safe value write failed for '{channel.Id}'.", ex);
            }
        }
        _device!.DOImmediate();
        foreach (var channel in _config.Channels.Where(x => x.Read))
            _values[channel.Id] = ReadChannel(channel);
        PublishSnapshot(connected: true, force: true);
    }
    private void BindInputs()
    {
        foreach (var channel in _config.Channels.Where(x => x.Type == "DI"))
        {
            foreach (var eventName in NormalizeEvents(channel))
            {
                var inputEvent = eventName switch
                {
                    "pressed" => InputEvent.IE_Pressed,
                    "released" => InputEvent.IE_Released,
                    "repeat" => InputEvent.IE_Repeat,
                    "double-click" => InputEvent.IE_DoubleClick,
                    _ => throw new InvalidDataException($"Unsupported input event '{eventName}'.")
                };
                _device!.BindKey(channel.Key, inputEvent, () =>
                    EventRaised?.Invoke(new RuntimeEvent(
                        $"input.{channel.Id}.{eventName}",
                        new { channel = channel.Id, eventName },
                        DateTimeOffset.Now)));
            }
        }
    }

    private static IEnumerable<string> NormalizeEvents(BridgeChannel channel)
    {
        if (channel.Events.Count == 0)
            return ["pressed", "released"];
        return channel.Events.Select(x => x.Trim().ToLowerInvariant()).Distinct();
    }

    private double ReadChannel(BridgeChannel channel)
    {
        if (_options.Mock)
            return _values.TryGetValue(channel.Id, out var value) ? value : 0;

        return channel.Type switch
        {
            "DI" => _device!.GetKey((Key)channel.Key) ? 1 : 0,
            "AD" => _device!.GetRawKeyValue((Key)channel.Key),
            "DO" or "OAXIS" => _device!.GetDO((Key)channel.Key),
            "OACTION" => _device!.GetDO(channel.Key),
            _ => 0
        };
    }

    private int WriteChannel(BridgeChannel channel, double value)
    {
        if (_options.Mock)
            return 1;

        return channel.Type switch
        {
            "DO" or "OAXIS" => _device!.SetDO((Key)channel.Key, (float)value),
            "OACTION" => _device!.SetDO(channel.Key, (float)value),
            _ => throw new InvalidOperationException($"Channel '{channel.Id}' is read-only.")
        };
    }

    private string SafeReadAction(string key)
    {
        try { return _device?.GetDO(key).ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"; }
        catch (Exception ex) { return "error:" + ex.GetType().Name; }
    }

    private string SafeReadKey(string key)
    {
        try { return _device?.GetDO((Key)key).ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"; }
        catch (Exception ex) { return "error:" + ex.GetType().Name; }
    }
    private BridgeChannel FindChannel(string channelId) =>
        _config.Channels.FirstOrDefault(x => string.Equals(x.Id, channelId, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"Channel '{channelId}' was not found.");

    private static void ValidateWritable(BridgeChannel channel, double value)
    {
        if (!channel.Write)
            throw new InvalidOperationException($"Channel '{channel.Id}' is read-only.");
        if (channel.Min is not null && value < channel.Min)
            throw new ArgumentOutOfRangeException(nameof(value), $"Value {value} is below minimum {channel.Min} for '{channel.Id}'.");
        if (channel.Max is not null && value > channel.Max)
            throw new ArgumentOutOfRangeException(nameof(value), $"Value {value} is above maximum {channel.Max} for '{channel.Id}'.");
    }

    private void PublishSnapshot(bool connected, bool force = false)
    {
        var values = new Dictionary<string, double>(_values, StringComparer.OrdinalIgnoreCase);
        SnapshotFrame next;
        lock (_snapshotGate)
        {
            if (!force && _snapshot.Values.Count == values.Count && values.All(x => _snapshot.Values.TryGetValue(x.Key, out var old) && Math.Abs(old - x.Value) < 0.000001))
                return;
            next = new SnapshotFrame(Interlocked.Increment(ref _seq), DateTimeOffset.Now, values, connected, _options.Mock);
            Volatile.Write(ref _snapshot, next);
        }
        SnapshotChanged?.Invoke(next);
    }

    private static async Task WaitForCompletion(Task<int> completionTask, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var completed = await Task.WhenAny(completionTask, Task.Delay(Timeout.Infinite, timeout.Token));
        if (completed != completionTask)
            throw new TimeoutException("Bridge command timed out.");
        await completionTask;
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _commands.Writer.TryComplete();
        if (_loopTask is not null)
        {
            try { await _loopTask; } catch { }
        }
        if (!_options.Mock)
        {
            try { IODeviceController.ClearAllBingings(); } catch { }
            try { IODeviceController.Unload(); } catch { }
        }
        _cts?.Dispose();
        _device = null;
    }

    private enum RuntimeCommandKind
    {
        Set,
        SafeStop
    }

    private sealed record RuntimeCommand(
        RuntimeCommandKind Kind,
        string? ChannelId,
        double? Value,
        TaskCompletionSource<int> Completion);
}





