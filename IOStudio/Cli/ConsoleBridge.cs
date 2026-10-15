using System;
using System.IO;
using System.Runtime.InteropServices;

namespace IOStudio.Cli;

/// <summary>
/// 让 IOStudio 在命令行场景下真正能写 stdout/stderr。
///
/// <para><b>为什么需要它</b>: 本工程是 <c>WinExe</c>（GUI 子系统），进程默认<b>没有控制台</b>，
/// 而 <c>Console.Out</c> 在这种情形下是一个"黑洞"——写入既不报错也不落到任何地方。
/// 于是 <c>--help</c>/<c>--validate</c> 这类命令的输出全部消失，只有 <c>--status-file</c>
/// 的 JSON 还能拿到结果，脚本与 AI 无法把 IOStudio 当命令用。</para>
///
/// <para><b>两条来源要分别处理</b>:</para>
/// <list type="number">
/// <item>被父进程<b>重定向</b>（脚本/AI 走管道）：标准句柄已由父进程给好，直接复用即可。</item>
/// <item>人在终端里手敲：句柄无效，需要 <c>AttachConsole(ATTACH_PARENT_PROCESS)</c> 附着上去。</item>
/// </list>
///
/// <para>正常双击启动 GUI 时不调用本类，因此不会平白多出一个控制台窗口。</para>
/// </summary>
internal static class ConsoleBridge
{
    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;
    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    private static bool _initialized;
    private static bool _hasOutput;

    /// <summary>是否已成功接上输出通道 (false = 调用方应改用 --status-file 兜底)。</summary>
    public static bool HasOutput => _hasOutput;

    /// <summary>
    /// 接上 stdout/stderr。幂等；任何失败都返回 false 而不是抛出——
    /// 拿不到输出不该让命令本身失败（结果仍在 status 文件与退出码里）。
    /// </summary>
    public static bool Ensure()
    {
        if (_initialized)
            return _hasOutput;
        _initialized = true;

        try
        {
            bool outOk = IsValid(GetStdHandle(StdOutputHandle));
            bool errOk = IsValid(GetStdHandle(StdErrorHandle));

            if (!outOk && !errOk)
            {
                // 没有可用句柄 → 附着到父进程的控制台 (交互终端手敲的情形)。
                AttachConsole(AttachParentProcess);
                outOk = IsValid(GetStdHandle(StdOutputHandle));
                errOk = IsValid(GetStdHandle(StdErrorHandle));
            }

            if (outOk)
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            if (errOk)
                Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });

            _hasOutput = outOk || errOk;
        }
        catch
        {
            _hasOutput = false;
        }

        return _hasOutput;
    }

    private static bool IsValid(IntPtr handle) => handle != IntPtr.Zero && handle != new IntPtr(-1);
}
