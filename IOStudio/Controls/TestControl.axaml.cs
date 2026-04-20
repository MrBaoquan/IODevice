using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ReactiveUI.Avalonia;
using IOStudio.ViewModels;
using ReactiveUI;

namespace IOStudio.Controls;

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
