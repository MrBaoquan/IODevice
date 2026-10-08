using Avalonia.Controls;
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

            // 关窗必须释放监听端口/取消接收循环, 否则端口会被窗口"占着"到进程退出
            Closed += (_, _) => ViewModel?.Dispose();
        }
    }
}
