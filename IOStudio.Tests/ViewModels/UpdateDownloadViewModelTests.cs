using Xunit;
using IOStudio.ViewModels;

namespace IOStudio.Tests.ViewModels
{
    /// <summary>
    /// 更新下载窗口的状态与格式化。
    ///
    /// <para><b>守的是什么</b>: 原实现把下载进度写进 <c>Console</c>, 而本应用是 <c>WinExe</c>
    /// 没有控制台 —— 150MB 下载全程界面空白, 用户只能以为卡死。这个 ViewModel 就是
    /// 把那段不可见的进度变成可见的进度条/速度/剩余时间, 所以它的派生显示必须可测。</para>
    /// </summary>
    public class UpdateDownloadViewModelTests
    {
        /// <summary>可注入的假时钟 —— 速率按滑动窗口算, 不注入就没法稳定断言。</summary>
        private sealed class FakeClock
        {
            public double NowMs { get; set; }
            public void Advance(double ms) => NowMs += ms;
        }

        [Theory]
        [InlineData(0, "0 B")]
        [InlineData(512, "512 B")]
        [InlineData(2048, "2 KB")]
        [InlineData(1536 * 1024, "1.5 MB")]
        [InlineData(150607601, "143.6 MB")]
        public void FormatBytes_IsHumanReadable(long bytes, string expected)
        {
            Assert.Equal(expected, UpdateDownloadViewModel.FormatBytes(bytes));
        }

        [Theory]
        [InlineData(5, "剩余 5 秒")]
        [InlineData(59.2, "剩余 60 秒")]
        [InlineData(120, "剩余 2 分钟")]
        [InlineData(3600, "剩余 60 分钟")]
        public void DescribeEta_ShiftsToMinutesPastOneMinute(double seconds, string expected)
        {
            Assert.Equal(expected, UpdateDownloadViewModel.DescribeEta(seconds));
        }

        [Fact]
        public void ReportProgress_TracksPercentAndText()
        {
            var vm = new UpdateDownloadViewModel();
            vm.SetManifest("1.0.12", "1.0.11", "releases", 1000);

            vm.ReportProgress(250, 1000);

            Assert.Equal(25.0, vm.ProgressPercent, 3);
            Assert.Equal("250 B / 1000 B", vm.ProgressText);
            Assert.False(vm.IsIndeterminate);
        }

        [Fact]
        public void ReportProgress_UnknownTotal_IsIndeterminate()
        {
            // 服务端没给总量时必须走不确定态 —— 否则会显示一个假的 0% 一直不动
            var vm = new UpdateDownloadViewModel();
            vm.SetManifest("1.0.12", "1.0.11", null, null);

            vm.ReportProgress(4096, null);

            Assert.True(vm.IsIndeterminate);
            Assert.Equal(0, vm.ProgressPercent);
            Assert.Equal("4 KB", vm.ProgressText);
        }

        [Fact]
        public void ReportProgress_ClampsBeyondTotal()
        {
            // 总量略低估时不能出现 103% 这种数字
            var vm = new UpdateDownloadViewModel();
            vm.SetManifest("1.0.12", "1.0.11", null, 1000);

            vm.ReportProgress(1030, 1000);

            Assert.Equal(100.0, vm.ProgressPercent, 3);
        }

        [Fact]
        public void Rate_IsComputedAndShowsRemainingTime()
        {
            var clock = new FakeClock();
            var vm = new UpdateDownloadViewModel(() => clock.NowMs);
            vm.SetManifest("1.0.12", "1.0.11", null, 10_000);

            vm.ReportProgress(0, 10_000);
            clock.Advance(1000);
            vm.ReportProgress(5000, 10_000);   // 1 秒内 5000 B/s

            // 剩 5000 B / 5000 B/s = 1 秒
            Assert.Contains("剩余 1 秒", vm.RateText);
            Assert.Contains("/s", vm.RateText);
        }

        [Fact]
        public void Rate_IsThrottledToAvoidUiChurn()
        {
            // 每个进度回调都刷新会让 UI 被字节级回调淹没 (150MB 会有上万次)
            var clock = new FakeClock();
            var vm = new UpdateDownloadViewModel(() => clock.NowMs);
            vm.SetManifest("1.0.12", "1.0.11", null, 1_000_000);

            vm.ReportProgress(0, 1_000_000);
            clock.Advance(1000);
            vm.ReportProgress(10_000, 1_000_000);
            string afterFirst = vm.ProgressText;

            // 同一时刻的第二个回调应被节流, 文本不变
            vm.ReportProgress(999_999, 1_000_000);
            Assert.Equal(afterFirst, vm.ProgressText);
        }

        [Fact]
        public void MarkInstalling_ClearsRateAndDisablesCancel()
        {
            var vm = new UpdateDownloadViewModel();
            vm.SetManifest("1.0.12", "1.0.11", null, 1000);
            vm.ReportProgress(1000, 1000);

            vm.MarkInstalling();

            Assert.False(vm.CanCancel);
            Assert.Equal("", vm.RateText);
            Assert.Equal("正在安装更新", vm.HeaderText);
            Assert.Equal(100.0, vm.ProgressPercent, 3);
        }

        [Fact]
        public void MarkFailed_SurfacesTheReason()
        {
            var vm = new UpdateDownloadViewModel();

            vm.MarkFailed("下载失败：连接被重置");

            Assert.True(vm.HasError);
            Assert.Contains("连接被重置", vm.ErrorText);
            Assert.False(vm.CanCancel);   // 已结束, 只剩"关闭"
        }

        [Fact]
        public void Footer_StopsPromisingAnInstallThatWontHappen()
        {
            // 取消/失败后底部不能再留着"即将自动关闭并安装" —— 那句已经不成立了
            var canceled = new UpdateDownloadViewModel();
            canceled.MarkCanceled();
            Assert.DoesNotContain("安装新版本", canceled.FooterText);
            Assert.Contains("未对现有安装做任何改动", canceled.FooterText);

            var failed = new UpdateDownloadViewModel();
            failed.MarkFailed("下载失败：连接被重置");
            Assert.DoesNotContain("安装新版本", failed.FooterText);

            // 进入安装时反而要说清"应用即将退出"
            var installing = new UpdateDownloadViewModel();
            installing.MarkInstalling();
            Assert.Contains("退出", installing.FooterText);
        }

        [Fact]
        public void RequestCancel_SetsFlagOnlyWhileCancelable()
        {
            var vm = new UpdateDownloadViewModel();
            Assert.True(vm.CanCancel);

            vm.RequestCancel();
            Assert.True(vm.CancelRequested);

            // 已进入安装阶段就不可取消了
            var vm2 = new UpdateDownloadViewModel();
            vm2.MarkInstalling();
            vm2.RequestCancel();
            Assert.False(vm2.CancelRequested);
        }
    }
}
