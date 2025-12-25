using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ReactiveUI.Avalonia;
using IOTester.ViewModels;
using ReactiveUI;

namespace IOTester.Controls;

public partial class TestControl : ReactiveUserControl<TestViewModel>
{
    public TestControl()
    {
        this.WhenActivated(disposables =>
        {
            //this.Bind(ViewModel, vm => vm.Title, view => view.textTitle.Text).DisposeWith(disposables);
        });

        InitializeComponent();
    }
}
