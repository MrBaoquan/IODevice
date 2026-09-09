using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using IOStudio.ViewModels.Timeline;

namespace IOStudio.Controls.Timeline
{
    public partial class KeyframePropertyPanel : UserControl
    {
        private bool _suppressSliderChange;
        private bool _suppressComboChange;

        public KeyframePropertyPanel()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private KeyframePropertyViewModel? Vm => DataContext as KeyframePropertyViewModel;

        public event Action? DuplicateActionRequested;
        public event Action? MoveActionRequested;
        public event Action? SaveActionAsPresetRequested;
        public event Action? DeleteActionRequested;

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            var vm = Vm;
            if (vm == null)
                return;
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(KeyframePropertyViewModel.Value))
                    SyncSliderFromVm();
                if (args.PropertyName == nameof(KeyframePropertyViewModel.Interpolation))
                    SyncComboFromVm();
                if (args.PropertyName == nameof(KeyframePropertyViewModel.EventDataType))
                    SyncDataTypeComboFromVm();
                if (args.PropertyName == nameof(KeyframePropertyViewModel.PlayheadEventDataType))
                    SyncPlayheadDataTypeComboFromVm();
            };
            SyncSliderFromVm();
            SyncComboFromVm();
            SyncDataTypeComboFromVm();
            SyncPlayheadDataTypeComboFromVm();
        }

        // ── 面板级键盘处理: Space 不应被按钮消费 ──

        private void OnPanelKeyDown(object? sender, KeyEventArgs e)
        {
            // 如果当前焦点不在 TextBox 上, 让 Space/Enter 等穿透到父窗口
            if (
                e.Key == Key.Space
                && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not TextBox
            )
            {
                e.Handled = false; // 确保 Space 可以冒泡到 Window.OnKeyDown
            }
        }

        // ── 关键帧导航 ──

        private void OnPrevKeyframeClick(object? sender, RoutedEventArgs e)
        {
            Vm?.RaiseNavigatePrev();
        }

        private void OnNextKeyframeClick(object? sender, RoutedEventArgs e)
        {
            Vm?.RaiseNavigateNext();
        }

        private void OnToggleKeyframeClick(object? sender, RoutedEventArgs e)
        {
            if (Vm is { } vm)
                vm.RaiseToggleKeyframe(vm.TimeMs);
        }

        private void OnDuplicateActionClick(object? sender, RoutedEventArgs e) =>
            DuplicateActionRequested?.Invoke();

        private void OnMoveActionClick(object? sender, RoutedEventArgs e) =>
            MoveActionRequested?.Invoke();

        private void OnSaveActionAsPresetClick(object? sender, RoutedEventArgs e) =>
            SaveActionAsPresetRequested?.Invoke();

        private void OnDeleteActionClick(object? sender, RoutedEventArgs e) =>
            DeleteActionRequested?.Invoke();

        // ── 值编辑 (Float) ──

        private void OnValueSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_suppressSliderChange || Vm is null)
                return;
            float val = (float)e.NewValue;
            Vm.CommitValue(val);
        }

        private void OnValueLostFocus(object? sender, RoutedEventArgs e)
        {
            CommitValueFromTextBox();
        }

        private void OnValueKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitValueFromTextBox();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                SyncSliderFromVm();
                (sender as TextBox)?.SelectAll();
                e.Handled = true;
            }
        }

        private void CommitValueFromTextBox()
        {
            if (Vm is null)
                return;
            var tb = this.FindControl<TextBox>("ValueInput");
            if (tb is null)
                return;
            if (
                float.TryParse(
                    tb.Text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float val
                )
            )
            {
                Vm.CommitValue(val);
            }
        }

        private void OnValueDecClick(object? sender, RoutedEventArgs e)
        {
            Vm?.CommitValue((Vm?.Value ?? 0.5f) - 0.01f);
        }

        private void OnValueIncClick(object? sender, RoutedEventArgs e)
        {
            Vm?.CommitValue((Vm?.Value ?? 0.5f) + 0.01f);
        }

        private void OnValueDecFineClick(object? sender, RoutedEventArgs e)
        {
            Vm?.CommitValue((Vm?.Value ?? 0.5f) - 0.001f);
        }

        private void OnValueIncFineClick(object? sender, RoutedEventArgs e)
        {
            Vm?.CommitValue((Vm?.Value ?? 0.5f) + 0.001f);
        }

        private void OnResetValueClick(object? sender, RoutedEventArgs e)
        {
            Vm?.CommitValue(0.5f);
        }

        // ── 值编辑 (Bool) ──

        private void OnBoolToggleChanged(object? sender, RoutedEventArgs e)
        {
            if (Vm is null || sender is not ToggleSwitch ts)
                return;
            Vm.CommitValue(ts.IsChecked == true ? 1f : 0f);
        }

        // ── 插值 ──

        private void OnInterpolationChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressComboChange || Vm is null)
                return;
            var combo = sender as ComboBox;
            if (combo?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                Vm.CommitInterpolationCommand.Execute(tag).Subscribe();
            }
        }

        private void OnResetInterpolationClick(object? sender, RoutedEventArgs e)
        {
            Vm?.CommitInterpolationCommand.Execute("linear").Subscribe();
        }

        // ── 事件 (独立事件) ──

        private void OnEventLostFocus(object? sender, RoutedEventArgs e)
        {
            Vm?.CommitEvent();
        }

        private void OnEventKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Vm?.CommitEvent();
                e.Handled = true;
            }
        }

        private void OnEventDataTypeChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem item)
            {
                string dataType = (item.Tag as string) ?? item.Content?.ToString() ?? "string";
                if (Vm != null)
                {
                    Vm.EventDataType = dataType;
                    Vm.CommitEvent();
                }
            }
        }

        // ── 播放头事件编辑 ──

        private void OnPlayheadEventDataTypeChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem item)
            {
                string dataType = (item.Tag as string) ?? item.Content?.ToString() ?? "string";
                if (Vm != null)
                {
                    Vm.PlayheadEventDataType = dataType;
                    Vm.CommitPlayheadEvent();
                }
            }
        }

        private void OnPlayheadEventLostFocus(object? sender, RoutedEventArgs e)
        {
            Vm?.CommitPlayheadEvent();
        }

        private void OnPlayheadEventKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Vm?.CommitPlayheadEvent();
                e.Handled = true;
            }
        }

        // ── 输入焦点选中 ──

        private void OnInputGotFocus(object? sender, GotFocusEventArgs e)
        {
            if (sender is TextBox tb)
                tb.SelectAll();
        }

        // ── 同步辅助 ──

        private void SyncSliderFromVm()
        {
            var slider = this.FindControl<Slider>("ValueSlider");
            if (slider is null || Vm is null)
                return;
            _suppressSliderChange = true;
            slider.Value = Vm.Value;
            _suppressSliderChange = false;
        }

        private void SyncComboFromVm()
        {
            var combo = this.FindControl<ComboBox>("InterpolationCombo");
            if (combo is null || Vm is null)
                return;
            _suppressComboChange = true;
            string target = Vm.Interpolation;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem item && item.Tag is string tag && tag == target)
                {
                    combo.SelectedIndex = i;
                    break;
                }
            }
            _suppressComboChange = false;
        }

        private void SyncDataTypeComboFromVm()
        {
            var combo = this.FindControl<ComboBox>("EventDataTypeCombo");
            if (combo is null || Vm is null)
                return;
            string target = Vm.EventDataType ?? "string";
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem item && item.Content?.ToString() == target)
                {
                    combo.SelectedIndex = i;
                    break;
                }
            }
        }

        private void SyncPlayheadDataTypeComboFromVm()
        {
            var combo = this.FindControl<ComboBox>("PlayheadEventDataTypeCombo");
            if (combo is null || Vm is null)
                return;
            string target = Vm.PlayheadEventDataType ?? "string";
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem item && item.Content?.ToString() == target)
                {
                    combo.SelectedIndex = i;
                    break;
                }
            }
        }
    }
}
