using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ReactiveUI.Avalonia;
using IOStudio.Controls;
using IOStudio.ViewModels;
using ReactiveUI;
using System;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace IOStudio.Views
{
    public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
    {
        public MainWindow()
        {
            this.WhenActivated(disposeables =>
            {
                disposeables.Add(
                    ViewModel.OnTabChangedCommand.Subscribe(_ =>
                    {
                        this.InvalidateMeasure();
                        this.InvalidateArrange();
                    })
                );
            });
            InitializeComponent();

            // 设置帮助菜单点击事件
            var helpMenuItem = this.FindControl<MenuItem>("HelpReferenceMenuItem");
            if (helpMenuItem != null)
            {
                helpMenuItem.Click += HelpReferenceMenuItem_Click;
            }

            //this.ref_TabControl.DataContextChanged += (e, sender) =>
            //{
            //    Debug.WriteLine(sender.GetType());
            //};
            // AvaloniaXamlLoader.Load(this);

            // this.FindControl<IONodeView>("ionode").ViewModel = new IONodeViewModel { Title = "��!!!!" };
        }

        private void HelpReferenceMenuItem_Click(
            object? sender,
            Avalonia.Interactivity.RoutedEventArgs e
        )
        {
            var helpWindow = new HelpWindow();
            helpWindow.ShowDialog(this);
        }
    }
}
