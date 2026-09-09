using Avalonia.Controls;
using ReactiveUI.Avalonia;
using IOStudio.Services;
using IOStudio.ViewModels;

namespace IOStudio.Views
{
    /// <summary>
    /// 接口调试窗口：查看/下发设备插件通道（Plugin Channel）字节流。
    /// 典型用途：调试 NETIO 设备的 <c>netio.udp.in</c> / <c>netio.udp.out</c> 通道。
    /// </summary>
    public partial class InterfaceDebugWindow : ReactiveWindow<InterfaceDebugViewModel>
    {
        public InterfaceDebugWindow()
        {
            InitializeComponent();
            DataContext = ViewModel = new InterfaceDebugViewModel();

            // 窗口关闭时自动取消所有订阅，避免 DLL 回调持有已销毁对象
            Closed += (_, _) => PluginChannelDebugService.Instance.UnsubscribeAll();
        }
    }
}
