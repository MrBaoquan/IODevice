using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using IOTester.Controls;
using IOTester.ViewModels;
using ReactiveUI;
using System;
using System.Diagnostics;
using System.Reactive.Linq;

namespace IOTester.Views
{
    public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
    {
        public MainWindow()
        {
            this.WhenActivated(disposeables =>{});
            InitializeComponent();

            //this.ref_TabControl.DataContextChanged += (e, sender) =>
            //{
            //    Debug.WriteLine(sender.GetType());
            //};
            // AvaloniaXamlLoader.Load(this);

            // this.FindControl<IONodeView>("ionode").ViewModel = new IONodeViewModel { Title = "¸É!!!!" };
        }
    }
}