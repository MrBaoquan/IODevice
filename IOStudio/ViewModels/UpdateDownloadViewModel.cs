using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reactive;
using ReactiveUI;

namespace IOStudio.ViewModels
{
    /// <summary>
    /// 「正在下载更新」窗口的状态。
    ///
    /// <para><b>为什么需要它</b>: 更新包 150MB 级, 而原实现把进度 <c>Console.WriteLine</c> 掉了 ——
    /// 本应用是 <c>WinExe</c>, <b>没有控制台</b>, 于是点完"是"之后整个下载过程界面上一片空白,
    /// 用户只能以为程序卡死。这里把进度、速度、剩余时间与取消都变成可见可操作的。</para>
    ///
    /// <para>速度按<b>滑动窗口</b>算 (默认最近 3 秒), 不用全局平均 —— 全局平均会让"中途变快"
    /// 迟迟反应不过来, 而用户对速度的感知就是即时的。时间源可注入, 便于单测。</para>
    /// </summary>
    public class UpdateDownloadViewModel : ViewModelBase
    {
        /// <summary>算速率用的采样窗口 (秒)。</summary>
        private const double RateWindowSeconds = 3.0;

        /// <summary>两次实际刷新之间的最小间隔 (毫秒) —— 避免每个字节回调都触发一次 UI 重排。</summary>
        private const double MinRefreshIntervalMs = 120.0;

        private readonly Func<double> _nowMs;
        private readonly List<(double AtMs, long Bytes)> _samples = new();

        private long _received;
        private long _total;
        private double _lastRefreshMs = double.NegativeInfinity;
        private double _rateBytesPerSec;

        public UpdateDownloadViewModel(Func<double>? nowMs = null)
        {
            _nowMs = nowMs ?? (() => Environment.TickCount64);
            CancelCommand = ReactiveCommand.Create(RequestCancel);
        }

        /// <summary>请求取消下载 (窗口把它接到 CancellationTokenSource)。</summary>
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }

        /// <summary>用户是否已请求取消 —— 窗口据此取消 CTS。</summary>
        public bool CancelRequested { get; private set; }

        // ── 绑定属性 ──

        private string _headerText = "正在下载更新";
        public string HeaderText
        {
            get => _headerText;
            private set => this.RaiseAndSetIfChanged(ref _headerText, value);
        }

        private string _versionText = "";
        public string VersionText
        {
            get => _versionText;
            private set => this.RaiseAndSetIfChanged(ref _versionText, value);
        }

        private double _progressPercent;
        public double ProgressPercent
        {
            get => _progressPercent;
            private set => this.RaiseAndSetIfChanged(ref _progressPercent, value);
        }

        private string _progressText = "";
        public string ProgressText
        {
            get => _progressText;
            private set => this.RaiseAndSetIfChanged(ref _progressText, value);
        }

        private string _rateText = "";
        public string RateText
        {
            get => _rateText;
            private set => this.RaiseAndSetIfChanged(ref _rateText, value);
        }

        /// <summary>总量未知 (服务端未给 Content-Length) 时进度条走不确定态。</summary>
        private bool _isIndeterminate;
        public bool IsIndeterminate
        {
            get => _isIndeterminate;
            private set => this.RaiseAndSetIfChanged(ref _isIndeterminate, value);
        }

        private bool _canCancel = true;
        public bool CanCancel
        {
            get => _canCancel;
            private set => this.RaiseAndSetIfChanged(ref _canCancel, value);
        }

        private string _footerText = "下载完成后将自动关闭本应用并安装新版本。";
        /// <summary>底部说明 —— 随状态变化, 结束后不能还留着"即将安装"这种已经不成立的话。</summary>
        public string FooterText
        {
            get => _footerText;
            private set => this.RaiseAndSetIfChanged(ref _footerText, value);
        }

        private string? _errorText;
        /// <summary>非空 = 失败, 窗口显示它并允许关闭。</summary>
        public string? ErrorText
        {
            get => _errorText;
            private set
            {
                this.RaiseAndSetIfChanged(ref _errorText, value);
                this.RaisePropertyChanged(nameof(HasError));
            }
        }

        public bool HasError => !string.IsNullOrEmpty(_errorText);

        // ── 状态迁移 ──

        /// <summary>打开窗口时写入版本信息。</summary>
        public void SetManifest(
            string version,
            string currentVersion,
            string? releaseName,
            long? expectedSize
        )
        {
            VersionText =
                $"版本 {version}（当前 {currentVersion}）"
                + (string.IsNullOrWhiteSpace(releaseName) ? "" : $" · {releaseName}");
            _total = expectedSize ?? 0;
            ProgressPercent = 0;
            ProgressText = _total > 0 ? $"0 / {FormatBytes(_total)}" : "0";
            IsIndeterminate = _total <= 0;
        }

        /// <summary>下载进度回调 (可由任意线程调用)。</summary>
        /// <param name="total">总量; null/≤0 表示服务端未给出 —— 此时进度条走不确定态。</param>
        public void ReportProgress(long received, long? total)
        {
            _received = received;
            if (total is > 0)
                _total = total.Value;

            double now = _nowMs();
            bool first = _samples.Count == 0;
            if (!first && now - _lastRefreshMs < MinRefreshIntervalMs)
                return;
            _lastRefreshMs = now;

            _samples.Add((now, received));
            // 丢掉窗口之外的采样, 让速率反映"最近几秒"
            double cutoff = now - RateWindowSeconds * 1000.0;
            while (_samples.Count > 2 && _samples[0].AtMs < cutoff)
                _samples.RemoveAt(0);

            if (_samples.Count >= 2)
            {
                var (t0, b0) = _samples[0];
                var (t1, b1) = _samples[^1];
                double seconds = (t1 - t0) / 1000.0;
                _rateBytesPerSec = seconds > 0.05 ? (b1 - b0) / seconds : 0;
            }

            IsIndeterminate = _total <= 0;
            ProgressPercent = _total > 0 ? Math.Clamp(received * 100.0 / _total, 0, 100) : 0;
            ProgressText =
                _total > 0
                    ? $"{FormatBytes(received)} / {FormatBytes(_total)}"
                    : FormatBytes(received);
            RateText = DescribeRate(_received, _total);
        }

        /// <summary>下载完成, 进入校验/启动安装 (此时已无法取消)。</summary>
        public void MarkInstalling()
        {
            CanCancel = false;
            HeaderText = "正在安装更新";
            FooterText = "正在替换程序文件，本应用即将退出。";
            ProgressPercent = 100;
            RateText = "";
            ProgressText = _total > 0 ? $"{FormatBytes(_total)} / {FormatBytes(_total)}" : "下载完成";
        }

        public void MarkFailed(string message)
        {
            CanCancel = false;
            HeaderText = "更新失败";
            ErrorText = message;
            RateText = "";
            // 失败后"即将自动安装"已经不成立, 留着会误导
            FooterText = "";
        }

        public void MarkCanceled()
        {
            CanCancel = false;
            HeaderText = "已取消更新";
            RateText = "";
            FooterText = "下载已中止，未对现有安装做任何改动。";
        }

        /// <summary>取消按钮的落点。</summary>
        public void RequestCancel()
        {
            if (!CanCancel)
                return;
            CancelRequested = true;
        }

        // ── 格式化 (internal 供单测) ──

        private string DescribeRate(long received, long total)
        {
            if (_rateBytesPerSec <= 0)
                return "";

            string rate = $"{FormatBytes((long)_rateBytesPerSec)}/s";
            if (total <= received)
                return rate;

            double remainSec = (total - received) / _rateBytesPerSec;
            return $"{rate} · {DescribeEta(remainSec)}";
        }

        internal static string DescribeEta(double seconds)
        {
            if (seconds < 0 || double.IsInfinity(seconds) || double.IsNaN(seconds))
                return "";
            // 超过 1 分钟就没必要装精确 —— 速率本身在抖, 假精度反而让人盯着不放
            if (seconds < 60)
                return $"剩余 {Math.Max(1, (int)Math.Ceiling(seconds))} 秒";
            return $"剩余 {Math.Max(1, (int)Math.Round(seconds / 60))} 分钟";
        }

        internal static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            double kb = bytes / 1024.0;
            if (kb < 1024)
                return kb.ToString("0.#", CultureInfo.InvariantCulture) + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024)
                return mb.ToString("0.#", CultureInfo.InvariantCulture) + " MB";
            return (mb / 1024.0).ToString("0.##", CultureInfo.InvariantCulture) + " GB";
        }
    }
}
