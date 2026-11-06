using IOToolkit.WebBridge.Configuration;
using IOToolkit.WebBridge.Hosting;
using IOToolkit.WebBridge.Runtime;

var options = BridgeOptions.Parse(args, AppContext.BaseDirectory);
var logsDirectory = Path.Combine(options.ProjectRoot, "logs");
Directory.CreateDirectory(logsDirectory);
var logPath = Path.Combine(logsDirectory, $"bridge-{DateTime.Now:yyyyMMdd}.log");
var logGate = new object();
void Log(string message, Exception? exception = null)
{
    var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {(exception is null ? "INFO" : "ERROR")} {message}";
    if (exception is not null)
        line += Environment.NewLine + exception;
    lock (logGate)
        File.AppendAllText(logPath, line + Environment.NewLine);
}

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

BridgeConfig config;
try
{
    config = BridgeConfig.Load(options.BindingsPath);
}
catch (Exception ex)
{
    Log("Failed to load bridge bindings.", ex);
    Console.Error.WriteLine(ex.Message);
    return 2;
}

Log($"Starting WebBridge project={options.ProjectRoot} mock={options.Mock} device={config.Device} port={options.Port}");
Log(options.AllowedOrigins.Count == 0
    ? "Browser origins: loopback only. Pass --allow-origin to allow another host."
    : $"Browser origins: loopback plus {string.Join(", ", options.AllowedOrigins)}");
ProjectRuntimeLock? projectLock = null;
if (!options.Mock)
{
    try
    {
        projectLock = ProjectRuntimeLock.Acquire(options.ProjectRoot);
    }
    catch (Exception ex)
    {
        Log("WebBridge hardware lock acquisition failed.", ex);
        Console.Error.WriteLine(ex.Message);
        return 3;
    }
}
using var messageWindow = new HiddenMessageWindow();
await using var runtime = new DeviceRuntime(options, config, Log);
await using var server = new BridgeServer(options, config, runtime, Log);

try
{
    if (!options.Mock)
    {
        // Log off / shutdown / restart does not give the process a chance to unwind, so the safe
        // stop has to run inside the session-ending callback itself.
        messageWindow.SessionEnding = () => SafeStopForSessionEnd(runtime, Log);
        messageWindow.Start(TimeSpan.FromSeconds(10));
        Log("Hidden Windows message window started.");
    }

    await runtime.StartAsync(shutdown.Token);
    await server.StartAsync(shutdown.Token);
    Log($"WebBridge ready at {server.BaseAddress}");
    Console.WriteLine($"{{\"ok\":true,\"endpoint\":\"{server.BaseAddress}\",\"mock\":{options.Mock.ToString().ToLowerInvariant()},\"device\":\"{config.Device}\"}}");
    await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token);
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
}
catch (Exception ex)
{
    Log("WebBridge terminated with an error.", ex);
    Console.Error.WriteLine(ex);
    return 1;
}
finally
{
    try { await runtime.SafeStopAsync(CancellationToken.None); } catch (Exception ex) { Log("Safe stop during shutdown failed.", ex); }
    Log("WebBridge stopped.");
}
return 0;

// Runs on the message thread while Windows is ending the session. The OS gives us roughly five
// seconds before it terminates the process, so this must not wait on the runtime loop for long.
static void SafeStopForSessionEnd(DeviceRuntime runtime, Action<string, Exception?> log)
{
    log("Windows session is ending; running safe stop.", null);
    try
    {
        var completed = runtime.SafeStopAsync(CancellationToken.None).Wait(TimeSpan.FromSeconds(3));
        log(completed ? "Safe stop completed before session end." : "Safe stop did not complete before session end.", null);
    }
    catch (Exception ex)
    {
        log("Safe stop during session end failed.", ex);
    }
}




