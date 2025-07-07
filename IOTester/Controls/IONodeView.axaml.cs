using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using IOTester.ViewModels;
using ReactiveUI;

namespace IOTester.Controls;

public partial class IONodeView : ReactiveUserControl<IONodeBase>
{
    public IONodeView():base()
    {
        this.WhenActivated(disposables => 
        {
            //this.Bind(ViewModel, vm => vm.Title, view => view.textTitle.Text).DisposeWith(disposables);
        });

        InitializeComponent();

    }

    private void Binding(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
    }

    private void Binding_1(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
    }
}