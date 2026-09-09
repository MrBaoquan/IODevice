using HiMind.Distribution;
using HiMind.Distribution.Windows;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IOStudio.Services
{
    /// <summary>
    /// HiMind 软件分发更新服务：负责检查更新、下载产物与启动独立安装器。
    /// </summary>
    public interface ISoftwareUpdateService
    {
        /// <summary>是否已完成 distribution.json 配置。</summary>
        bool IsConfigured { get; }

        /// <summary>当前应用版本（取自程序集 InformationalVersion）。</summary>
        string CurrentVersion { get; }

        /// <summary>向组织分发服务查询可用更新；无更新或未配置时返回 null。</summary>
        Task<SoftwareUpdateManifest?> CheckForUpdateAsync(
            CancellationToken cancellationToken = default
        );

        /// <summary>下载更新包并校验大小与 SHA-256。</summary>
        Task<DownloadedUpdate> DownloadAsync(
            SoftwareUpdateManifest manifest,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default
        );

        /// <summary>启动独立 Updater 安装（替换应用目录文件，失败自动回滚）。</summary>
        bool LaunchInstaller(DownloadedUpdate update);
    }

    /// <inheritdoc cref="ISoftwareUpdateService"/>
    public sealed class SoftwareUpdateService : ISoftwareUpdateService
    {
        private readonly DashboardDistributionClient _client;
        private readonly WindowsUpdateInstaller _installer;

        public SoftwareUpdateService()
        {
            var options = DistributionOptions.Load();
            _client = new DashboardDistributionClient(options);
            _installer = new WindowsUpdateInstaller();
        }

        public bool IsConfigured => _client.IsConfigured;

        public string CurrentVersion => DistributionOptions.GetCurrentVersion();

        public Task<SoftwareUpdateManifest?> CheckForUpdateAsync(
            CancellationToken cancellationToken = default
        ) => _client.CheckForUpdateAsync(cancellationToken);

        public Task<DownloadedUpdate> DownloadAsync(
            SoftwareUpdateManifest manifest,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default
        ) => _client.DownloadAsync(manifest, progress, cancellationToken);

        public bool LaunchInstaller(DownloadedUpdate update) => _installer.Launch(update);
    }
}
