using System.Text;

namespace IOToolkit.WebBridge.Hosting;

public sealed class ProjectRuntimeLock : IDisposable
{
    private readonly FileStream _stream;

    private ProjectRuntimeLock(FileStream stream) => _stream = stream;

    public static ProjectRuntimeLock Acquire(string projectRoot)
    {
        var hardwareRoot = Path.Combine(projectRoot, "hardware");
        Directory.CreateDirectory(hardwareRoot);
        var lockPath = Path.Combine(hardwareRoot, ".runtime.lock");
        try
        {
            var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var payload = Encoding.UTF8.GetBytes($"IOToolkit.WebBridge pid={Environment.ProcessId} started={DateTimeOffset.Now:O}{Environment.NewLine}");
            stream.SetLength(0);
            stream.Write(payload, 0, payload.Length);
            stream.Flush(true);
            return new ProjectRuntimeLock(stream);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"hardware 配置正在被 IOStudio 或其他 WebBridge 占用。锁文件: {lockPath}",
                ex);
        }
    }

    public void Dispose() => _stream.Dispose();
}
