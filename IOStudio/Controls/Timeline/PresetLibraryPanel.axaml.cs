using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using IOStudio.ViewModels.Timeline;

namespace IOStudio.Controls.Timeline
{
    /// <summary>
    /// 效果预设库面板 — 浏览/搜索/应用/保存预设。
    /// </summary>
    public partial class PresetLibraryPanel : UserControl
    {
        private Button? _selectedPresetRow;
        private Point? _presetDragStart;
        private EffectPreset? _dragPreset;
        private bool _presetDragStarted;
        private bool _suppressNextClick;

        private PresetLibraryPanelViewModel? ViewModel =>
            DataContext as PresetLibraryPanelViewModel;

        /// <summary>应用预设请求 (由 TimelineEditorViewModel 监听)</summary>
        public event Action<EffectPreset, PresetApplyOptions>? PresetApplyRequested;

        /// <summary>保存预设请求 (由 TimelineEditorViewModel 处理)</summary>
        public event EventHandler? SavePresetRequested;

        public event Action<EffectPreset>? EditPresetRequested;

        /// <summary>关闭面板请求</summary>
        public event Action? CloseRequested;

        public PresetLibraryPanel()
        {
            InitializeComponent();
            AddHandler(
                PointerPressedEvent,
                OnPresetCardPointerPressed,
                RoutingStrategies.Tunnel,
                handledEventsToo: true
            );
            AddHandler(
                PointerMovedEvent,
                OnPresetCardPointerMoved,
                RoutingStrategies.Tunnel,
                handledEventsToo: true
            );
            AddHandler(
                PointerReleasedEvent,
                OnPresetCardPointerReleased,
                RoutingStrategies.Tunnel,
                handledEventsToo: true
            );
            AddHandler(
                KeyDownEvent,
                OnParameterDialogKeyDown,
                RoutingStrategies.Tunnel,
                handledEventsToo: true
            );
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (ViewModel != null)
            {
                ViewModel.PresetApplyRequested += OnViewModelApplyRequested;
                ViewModel.SavePresetRequested += OnViewModelSaveRequested;
                ViewModel.EditPresetRequested += OnViewModelEditRequested;
            }
        }

        private void OnPresetCardClick(object? sender, RoutedEventArgs e)
        {
            if (_suppressNextClick)
            {
                _suppressNextClick = false;
                e.Handled = true;
                return;
            }
            if (sender is Button button && button.Tag is EffectPreset preset)
                SelectPreset(button, preset);
        }

        // ── 卡片右键菜单管理操作 (转发到 ViewModel) ──

        private EffectPreset? ResolveContextPreset(object? sender)
        {
            // ContextMenu 内 MenuItem 的 DataContext 继承自放置目标 (Button → 预设)
            if (sender is MenuItem mi)
            {
                if (mi.DataContext is EffectPreset p)
                    return p;
                if (mi.Tag is EffectPreset tp)
                    return tp;
            }
            return ViewModel?.SelectedPreset;
        }

        private void OnToggleFavoriteClick(object? sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
                ViewModel.ToggleFavoriteCommand.Execute(ResolveContextPreset(sender));
        }

        private void OnRenamePresetClick(object? sender, RoutedEventArgs e)
        {
            var preset = ResolveContextPreset(sender);
            if (preset == null || ViewModel?.IsBuiltInPreset(preset) == true)
                return;
            ViewModel?.RequestRenamePreset(preset);
        }

        private void OnChangeCategoryClick(object? sender, RoutedEventArgs e)
        {
            var preset = ResolveContextPreset(sender);
            if (preset == null || ViewModel?.IsBuiltInPreset(preset) == true)
                return;
            ViewModel?.RequestChangeCategory(preset);
        }

        private async void OnExportPresetClick(object? sender, RoutedEventArgs e)
        {
            var preset = ResolveContextPreset(sender);
            if (preset == null || ViewModel == null)
                return;
            var json = ViewModel.Library.ExportJson(preset);
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard != null)
            {
                await top.Clipboard.SetTextAsync(json);
                ViewModel.OperationStatus = $"已复制“{preset.Name}” JSON 到剪贴板";
            }
            else
            {
                ViewModel.OperationStatus = "剪贴板不可用";
            }
        }

        private void OnDeletePresetClick(object? sender, RoutedEventArgs e)
        {
            var preset = ResolveContextPreset(sender);
            if (preset == null || ViewModel?.IsBuiltInPreset(preset) == true)
                return;
            ViewModel?.RequestDeletePreset(preset);
        }

        private void OnPresetCardPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var button = FindPresetButton(e.Source);
            if (button?.Tag is not EffectPreset preset)
                return;
            if (!e.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
                return;

            _presetDragStart = e.GetPosition(button);
            _dragPreset = preset;
            _presetDragStarted = false;
            _suppressNextClick = false;
            e.Pointer.Capture(button);
        }

        private async void OnPresetCardPointerMoved(object? sender, PointerEventArgs e)
        {
            var button = FindPresetButton(e.Source);
            if (
                button == null
                || _dragPreset == null
                || !_presetDragStart.HasValue
                || _presetDragStarted
                || !e.GetCurrentPoint(button).Properties.IsLeftButtonPressed
            )
                return;

            var pos = e.GetPosition(button);
            var delta = pos - _presetDragStart.Value;
            if (Math.Abs(delta.X) < 5 && Math.Abs(delta.Y) < 5)
                return;

            _presetDragStarted = true;
            _suppressNextClick = true;
            SelectPreset(button, _dragPreset);

            var item = new DataTransferItem();
            item.Set(PresetDragFormats.PresetId, _dragPreset.Id);
            item.Set(
                PresetDragFormats.DurationMs,
                _dragPreset.DurationMs.ToString(CultureInfo.InvariantCulture)
            );
            var data = new DataTransfer();
            data.Add(item);
            e.Pointer.Capture(null);
            try
            {
                await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
            }
            finally
            {
                _presetDragStart = null;
                _dragPreset = null;
                _presetDragStarted = false;
                _suppressNextClick = false;
            }
        }

        private void OnPresetCardPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            // 仅在确实发起拖拽时释放指针捕获；普通点击/点击按钮时不要释放,
            // 否则隧道路由先于子按钮执行 Capture(null) 会抑制子按钮的 Click 事件。
            if (_presetDragStarted)
            {
                _presetDragStarted = false;
                e.Pointer.Capture(null);
            }
            _presetDragStart = null;
            _dragPreset = null;
        }

        private static Button? FindPresetButton(object? source)
        {
            var button = source as Button ?? (source as Visual)?.FindAncestorOfType<Button>();
            return button?.Classes.Contains("preset-card") == true ? button : null;
        }

        private void OnCloseParameterDialogClick(object? sender, RoutedEventArgs e) =>
            CloseParameterDialog();

        private void OnParameterDialogBackdropPressed(object? sender, PointerPressedEventArgs e)
        {
            CloseParameterDialog();
            e.Handled = true;
        }

        private void OnParameterDialogKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || ViewModel?.ShowParameterDialog != true)
                return;
            CloseParameterDialog();
            e.Handled = true;
        }

        private void CloseParameterDialog()
        {
            if (ViewModel != null)
                ViewModel.ShowParameterDialog = false;
        }

        /// <summary>单击仅选择，双击进入参数确认，保持连续浏览不被打断。</summary>
        private void OnPresetCardDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (sender is Button button && button.Tag is EffectPreset preset)
            {
                SelectPreset(button, preset);
                ViewModel?.SelectAndApplyPreset(preset);
                e.Handled = true;
            }
        }

        private void SelectPreset(Button button, EffectPreset preset)
        {
            if (_selectedPresetRow != null && !ReferenceEquals(_selectedPresetRow, button))
                _selectedPresetRow.Classes.Remove("selected");
            button.Classes.Add("selected");
            _selectedPresetRow = button;
            if (ViewModel != null)
                ViewModel.SelectedPreset = preset;
        }

        /// <summary>关闭面板</summary>
        private void OnClosePanelClick(object? sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
                ViewModel.IsPanelVisible = false;
            CloseRequested?.Invoke();
        }

        /// <summary>从 ViewModel 转发应用预设事件</summary>
        private void OnViewModelApplyRequested(EffectPreset preset, PresetApplyOptions options)
        {
            PresetApplyRequested?.Invoke(preset, options);
        }

        /// <summary>从 ViewModel 转发保存预设事件</summary>
        private void OnViewModelSaveRequested(object? sender, EventArgs e)
        {
            SavePresetRequested?.Invoke(this, e);
        }

        private void OnViewModelEditRequested(EffectPreset preset)
        {
            EditPresetRequested?.Invoke(preset);
        }
    }
}
