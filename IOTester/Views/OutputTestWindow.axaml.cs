using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using IOTester.ViewModels;
using System;

namespace IOTester.Views
{
    public partial class OutputTestWindow : Window
    {
        private DispatcherTimer? _updateTimer;
        private OutputTestViewModel? _viewModel;

        // 用于检测双击
        private DateTime _lastClickTime = DateTime.MinValue;
        private TestableKey? _lastClickedKey = null;
        private const int DoubleClickThreshold = 300; // 毫秒

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

            // 启动定时更新
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _updateTimer.Tick += (s, e) => _viewModel?.UpdateValues();
            _updateTimer.Start();
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            _updateTimer?.Stop();
            _viewModel?.StopAllTests();
        }

        /// <summary>
        /// 通道点击处理 - 自行检测单击/双击
        /// 单击：切换选中状态
        /// 双击：切换开关状态（同时也会切换选中状态，因为包含了第一次单击）
        /// </summary>
        private void OnChannelPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && border.DataContext is TestableKey key)
            {
                var point = e.GetCurrentPoint(border);
                if (!point.Properties.IsLeftButtonPressed)
                    return;

                var now = DateTime.Now;
                var timeSinceLastClick = (now - _lastClickTime).TotalMilliseconds;

                if (_lastClickedKey == key && timeSinceLastClick < DoubleClickThreshold)
                {
                    // 双击检测成功：先撤销上次单击的选中切换，再切换开关状态
                    key.IsSelected = !key.IsSelected; // 撤销上次单击的效果
                    _viewModel?.ToggleKeyCommand.Execute(key).Subscribe();

                    // 重置，防止连续触发
                    _lastClickedKey = null;
                    _lastClickTime = DateTime.MinValue;
                }
                else
                {
                    // 首次点击：切换选中状态
                    key.IsSelected = !key.IsSelected;

                    // 记录本次点击，用于检测后续的双击
                    _lastClickedKey = key;
                    _lastClickTime = now;
                }

                e.Handled = true;
            }
        }

        /// <summary>
        /// 禁用系统双击事件（我们自己处理）
        /// </summary>
        private void OnChannelDoubleTapped(object? sender, TappedEventArgs e)
        {
            // 不做任何处理，由 OnChannelPointerPressed 统一处理
            e.Handled = true;
        }

        /// <summary>
        /// CheckBox 点击 - 直接处理选中，不走双击逻辑
        /// </summary>
        private void OnCheckBoxPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // 阻止冒泡，让 CheckBox 自己处理
            e.Handled = true;

            // 重置双击检测，避免干扰
            _lastClickedKey = null;
            _lastClickTime = DateTime.MinValue;
        }
    }
}
