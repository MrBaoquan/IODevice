using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using IOStudio.ViewModels;
using ReactiveUI.Avalonia;

namespace IOStudio.Views
{
    /// <summary>
    /// 协议测试台窗口。
    ///
    /// <para>用途: 用 UI 观察 TCP/UDP/WebSocket/串口 收发到的每一帧, 并可直接运行
    /// 内置场景 (与 CLI <c>--protocol-test</c> 共用同一套执行器, 结论一致)。
    /// 适合"CLI 跑门禁、人在 UI 侧核对收到的报文"这种分工。</para>
    /// </summary>
    public partial class ProtocolTestWindow : ReactiveWindow<ProtocolTestViewModel>
    {
        public ProtocolTestWindow()
        {
            InitializeComponent();
            DataContext = ViewModel = new ProtocolTestViewModel();

            // 文件选择器由窗口注入 —— ViewModel 不依赖 UI, 才能脱离桌面做确定性测试
            ViewModel.FilePicker = PickFileAsync;

            // 关窗必须释放监听端口/取消接收循环, 否则端口会被窗口"占着"到进程退出
            Closed += (_, _) => ViewModel?.Dispose();
        }

        private async System.Threading.Tasks.Task<string?> PickFileAsync()
        {
            var files = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "选择要发送的文件",
                    AllowMultiple = false,
                }
            );
            var first = files?.FirstOrDefault();
            return first?.TryGetLocalPath();
        }
    }
}
