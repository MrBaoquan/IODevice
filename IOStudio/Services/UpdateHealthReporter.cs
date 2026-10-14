using System;
using System.IO;

namespace IOStudio.Services
{
    /// <summary>
    /// 自更新健康上报（HiMind 软件分发）。
    ///
    /// <para>背景：<c>HiMind.Distribution.Windows.WindowsUpdateInstaller</c> 启动独立 Updater 时，
    /// 会固定传入 <c>--himind-update-health-file &lt;path&gt;</c> 与 <c>--health-timeout-seconds 30</c>。
    /// Updater（<c>HiMind.Distribution.Updater</c>）替换完文件、拉起新版本后，会等待宿主写入该文件；
    /// 若超时仍未写回，即判定“新版本未成功启动”，会终止新进程并回滚到旧版本。</para>
    ///
    /// <para>因此宿主必须在 UI / 消息循环就绪后主动写回健康文件。
    /// 本类只做两件事：从命令行解析目标路径、在该路径写入一行状态。
    /// 不涉及凭据，写失败不抛出 —— 写不出时 Updater 会按超时回滚，属预期安全行为。</para>
    /// </summary>
    public static class UpdateHealthReporter
    {
        /// <summary>Updater 传递给宿主进程的健康文件参数名（与 HiMind.Distribution 保持一致）。</summary>
        public const string HealthFileArgument = "--himind-update-health-file";

        /// <summary>
        /// 从命令行参数解析 Updater 期望的健康文件路径。
        /// 未处于自更新场景时返回 <c>null</c>。
        /// </summary>
        public static string? ResolveHealthFile(string[]? args)
        {
            if (args is null)
                return null;

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], HealthFileArgument, StringComparison.OrdinalIgnoreCase))
                {
                    var path = args[i + 1];
                    return string.IsNullOrWhiteSpace(path) ? null : path;
                }
            }

            return null;
        }

        /// <summary>
        /// 写入健康文件，通知 Updater 新版本已成功启动。
        /// </summary>
        /// <returns>写入成功返回 <c>true</c>；未请求或写入失败返回 <c>false</c>（不抛出）。</returns>
        public static bool Report(string? healthFilePath)
        {
            if (string.IsNullOrWhiteSpace(healthFilePath))
                return false;

            try
            {
                var directory = Path.GetDirectoryName(healthFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(
                    healthFilePath!,
                    $"ok pid={Environment.ProcessId} utc={DateTime.UtcNow:O}"
                );
                return true;
            }
            catch
            {
                // 写不出时交由 Updater 超时回滚，不向上抛出。
                return false;
            }
        }
    }
}
