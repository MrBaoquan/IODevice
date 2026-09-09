using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using IOStudio.ViewModels;
using System;

namespace IOStudio.Views
{
    public partial class OutputTestWindow : Window
    {
        private DispatcherTimer? _updateTimer;
        private OutputTestViewModel? _viewModel;

        public OutputTestWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
            this.Closed += OnWindowClosed;
        }

        public void SetViewModel(OutputTestViewModel viewModel)
        {
            _viewModel = viewModel;
            DataContext = viewModel;

            // 启动定时更新：通道实时值 + 示波器帧推送
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _updateTimer.Tick += (s, e) => Tick();
            _updateTimer.Start();
        }

        private void Tick()
        {
            if (_viewModel == null)
                return;

            _viewModel.UpdateValues();

            // 示波器：非暂停时刷新帧并推送
            if (!_viewModel.IsScopePaused)
            {
                _viewModel.RefreshScopeFrame();
                ScopePlot.SetFrame(_viewModel.ScopeFrame);
            }
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            _updateTimer?.Stop();
            _viewModel?.StopAllTests();
            _viewModel?.Dispose();
        }

        /// <summary>
        /// 通道行单击 = 切换选中（参与输出目标/录波/示波器）。
        /// 输出开关由独立 ON/OFF 按钮负责，二者不再叠加。
        /// </summary>
        private void OnChannelPointerPressed(object? sender, PointerPressedEventArgs e)
        {
                    if (sender is Border border && border.DataContext is TestableKey key)
            {
                        var point = e.GetCurrentPoint(border);
                if (!point.Properties.IsLeftButtonPressed)
                    return;

                _viewModel?.ToggleSelectionCommand.Execute(key).Subscribe();
                e.Handled = true;
            }
        }

        /// <summary>
        /// CheckBox / ON-OFF 按钮点击 - 阻止冒泡，避免触发行单击选中逻辑
        /// </summary>
        private void OnCheckBoxPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            e.Handled = true;
        }
    }
}
