using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using IOTester.ViewModels;

namespace IOTester.Controls;

public partial class IODeviceView : ReactiveUserControl<Device>
{
    public IODeviceView()
    {
        InitializeComponent();
    }
}