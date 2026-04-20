using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ReactiveUI.Avalonia;
using IOStudio.ViewModels;
using ReactiveUI;

namespace IOStudio.Controls;

public partial class IODeviceView : ReactiveUserControl<Device>
{
    public IODeviceView()
    {
        InitializeComponent();

        this.WhenActivated(disposeables =>
        {
            //var _flyout = this.Resources["MySharedFlyout"] as Flyout;

            //_flyout.ShowAt(this.DORepeater.Children[1]);
        });
    }
}
