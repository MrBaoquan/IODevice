using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Xunit;
using IOStudio.ViewModels;
using IOStudio.Views;

namespace IOStudio.Tests.E2E
{
    /// <summary>
    /// 更新下载窗口的真实布局与绑定 (Avalonia Headless)。
    ///
    /// <para><b>为什么要这层</b>: 这个窗口存在的唯一理由是"把下载进度变可见"。
    /// 编译器能验证 XAML 结构, <b>但验证不了绑定是否真的连上</b> —— 属性名打错、
    /// VM 没设 DataContext, 都会得到一条永远 0% 的进度条, 而构建仍然是绿的。
    /// 这里实例化真窗口并检查进度条的值确实跟着 ViewModel 走。</para>
    ///
    /// <para>并发: 与其它 headless 用例同集合串行 (见 <c>AvaloniaHeadlessCollection</c>)。</para>
    /// </summary>
    [Collection("AvaloniaHeadless")]
    public class UpdateDownloadWindowTests : IDisposable
    {
        private readonly HeadlessUnitTestSession _session;

        public UpdateDownloadWindowTests()
        {
            _session = HeadlessUnitTestSession.StartNew(typeof(HeadlessApp));
        }

        public void Dispose() => _session.Dispose();

        [Fact]
        public async Task Window_LaysOut_And_GetsRealSize()
        {
            var size = await _session.Dispatch(
                () =>
                {
                    var window = new UpdateDownloadWindow
                    {
                        DataContext = new UpdateDownloadViewModel(),
                    };
                    window.Show();
                    var result = (window.Bounds.Width, window.Bounds.Height);
                    window.Close();
                    return result;
                },
                CancellationToken.None
            );

            Assert.True(size.Width > 0 && size.Height > 0, $"窗口未参与布局: {size}");
        }

        [Fact]
        public async Task ProgressBar_FollowsTheViewModel()
        {
            // 这条就是本窗口的核心价值: 进度必须真的显示出来
            var observed = await _session.Dispatch(
                () =>
                {
                    var vm = new UpdateDownloadViewModel();
                    vm.SetManifest("1.0.12", "1.0.11", "releases", 1000);
                    var window = new UpdateDownloadWindow { DataContext = vm };
                    window.Show();

                    vm.ReportProgress(420, 1000);

                    var bar = window.FindControl<ProgressBar>("Progress");
                    var value = bar?.Value ?? -1;
                    var indeterminate = bar?.IsIndeterminate ?? true;
                    window.Close();
                    return (value, indeterminate);
                },
                CancellationToken.None
            );

            Assert.Equal(42.0, observed.value, 3);
            Assert.False(observed.indeterminate);
        }

        [Fact]
        public async Task CancelButton_IsBoundToTheViewModelState()
        {
            // 下载中可取消; 进入安装阶段后必须只剩"关闭" —— 否则用户会以为还能中止
            var states = await _session.Dispatch(
                () =>
                {
                    var vm = new UpdateDownloadViewModel();
                    var window = new UpdateDownloadWindow { DataContext = vm };
                    window.Show();
                    var cancel = window.FindControl<Button>("CancelButton");

                    bool duringDownload = cancel?.IsEnabled ?? false;

                    vm.MarkInstalling();
                    bool afterInstalling = cancel?.IsEnabled ?? true;

                    window.Close();
                    return (duringDownload, afterInstalling);
                },
                CancellationToken.None
            );

            Assert.True(states.duringDownload, "下载中「取消」应当可用");
            Assert.False(states.afterInstalling, "进入安装后「取消」应当不可用");
        }

        [Fact]
        public async Task ErrorBanner_IsHiddenUntilThereIsAnError()
        {
            var visible = await _session.Dispatch(
                () =>
                {
                    var vm = new UpdateDownloadViewModel();
                    var window = new UpdateDownloadWindow { DataContext = vm };
                    window.Show();

                    bool before = AnyBoundErrorVisible(window);
                    vm.MarkFailed("下载失败：连接被重置");
                    bool after = AnyBoundErrorVisible(window);

                    window.Close();
                    return (before, after);
                },
                CancellationToken.None
            );

            Assert.False(visible.before, "没有错误时不该显示红色告示");
            Assert.True(visible.after, "失败原因必须显示出来");
        }

        /// <summary>错误告示是一个含 TextBlock 的 Border —— 找"内容非空且可见"的那个。</summary>
        private static bool AnyBoundErrorVisible(Window window)
        {
            foreach (var border in Enumerate<Border>(window))
            {
                if (border.IsVisible
                    && border.Child is TextBlock tb
                    && !string.IsNullOrEmpty(tb.Text))
                {
                    return true;
                }
            }
            return false;
        }

        private static System.Collections.Generic.IEnumerable<T> Enumerate<T>(Visual root)
            where T : Visual =>
            root.GetVisualDescendants().OfType<T>();
    }
}
