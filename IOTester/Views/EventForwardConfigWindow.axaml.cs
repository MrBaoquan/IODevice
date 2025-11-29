using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using IOTester.ViewModels;
using ReactiveUI;

namespace IOTester.Views
{
    public partial class EventForwardConfigWindow : ReactiveWindow<EventForwardConfigViewModel>
    {
        public EventForwardConfigWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
