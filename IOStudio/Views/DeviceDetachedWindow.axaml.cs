using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using IOStudio.ViewModels;

namespace IOStudio.Views
{
    /// <summary>
    /// 设备独立窗口 — 承载与标签页共享的 <c>IODeviceView</c>。
    /// 关闭 (含"返回标签页"按钮) 后由 MainWindowViewModel 恢复设备标签页。
    /// </summary>
    public partial class DeviceDetachedWindow : Window
    {
        public DeviceDetachedWindow()
        {
            InitializeComponent();
        }

        private void OnAttachClick(object? sender, RoutedEventArgs e)
        {
            Close(); // Closed 事件由 ViewModel 监听 → 恢复标签页
        }
    }
}
