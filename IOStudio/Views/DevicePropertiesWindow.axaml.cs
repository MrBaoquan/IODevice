using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using IOStudio.Controls;
using IOStudio.Services;
using IOStudio.ViewModels;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IOStudio.Views
{
    public partial class DevicePropertiesWindow : Window
    {
        public bool IsConfirmed { get; private set; }

        private Device? _device;
        private Device? _sourceDevice;
        private Border? _externalDeviceConfigSection;
        private TextBlock? _dllNameText;
        private DeviceConfigPanel? _deviceConfigPanel;
        private Button? _restartDeviceButton;
        private Button? _docButton;
        private ComboBox? _deviceTypeComboBox;
        private ComboBox? _dllNameComboBox;
        private NumericUpDown? _deviceIndexNumeric;
        private StackPanel? _dllNameSection;
        private StackPanel? _deviceIndexSection;
        private Border? _configSummaryBorder;
        private WrapPanel? _configSummaryPanel;

        public DevicePropertiesWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif

            var cancelButton = this.FindControl<Button>("CancelButton");
            var saveButton = this.FindControl<Button>("SaveButton");
            var addPropertyKeyButton = this.FindControl<Button>("AddPropertyKeyButton");
            var clearPropertyKeysButton = this.FindControl<Button>("ClearPropertyKeysButton");
            _restartDeviceButton = this.FindControl<Button>("RestartDeviceButton");
            _docButton = this.FindControl<Button>("DocButton");
            _externalDeviceConfigSection = this.FindControl<Border>("ExternalDeviceConfigSection");
            _dllNameText = this.FindControl<TextBlock>("DllNameText");
            _deviceConfigPanel = this.FindControl<DeviceConfigPanel>("DeviceConfigPanel");
            _deviceTypeComboBox = this.FindControl<ComboBox>("DeviceTypeComboBox");
            _dllNameComboBox = this.FindControl<ComboBox>("DllNameComboBox");
            _deviceIndexNumeric = this.FindControl<NumericUpDown>("DeviceIndexNumeric");
            _dllNameSection = this.FindControl<StackPanel>("DllNameSection");
            _deviceIndexSection = this.FindControl<StackPanel>("DeviceIndexSection");
            _configSummaryBorder = this.FindControl<Border>("ConfigSummaryBorder");
            _configSummaryPanel = this.FindControl<WrapPanel>("ConfigSummaryPanel");

            if (cancelButton != null)
                cancelButton.Click += CancelButton_Click;

            if (saveButton != null)
                saveButton.Click += SaveButton_Click;

            if (addPropertyKeyButton != null)
                addPropertyKeyButton.Click += AddPropertyKeyButton_Click;

            if (clearPropertyKeysButton != null)
                clearPropertyKeysButton.Click += ClearPropertyKeysButton_Click;

            if (_restartDeviceButton != null)
                _restartDeviceButton.Click += RestartDeviceButton_Click;

            if (_docButton != null)
                _docButton.Click += DocButton_Click;

            // 设备类型变化事件
            if (_deviceTypeComboBox != null)
                _deviceTypeComboBox.SelectionChanged += OnDeviceTypeChanged;

            // DllName 变化事件
            if (_dllNameComboBox != null)
            {
                _dllNameComboBox.SelectionChanged += OnDllNameChanged;
                _dllNameComboBox.DropDownOpened += OnDllNameDropDownOpened;
                _dllNameComboBox.DropDownClosed += OnDllNameDropDownClosed;
            }

            // 设备索引变化事件
            if (_deviceIndexNumeric != null)
                _deviceIndexNumeric.ValueChanged += OnDeviceIndexChanged;

            // 设置 ComboBox 的 ItemsSource
            InitializeComboBoxes();

            // Handle edit and delete button clicks from DataTemplate
            this.AddHandler(Button.ClickEvent, OnEditPropertyKeyClick, handledEventsToo: true);
            this.AddHandler(Button.ClickEvent, OnDeletePropertyKeyClick, handledEventsToo: true);
        }

        private void InitializeComboBoxes()
        {
            if (_deviceTypeComboBox != null)
                _deviceTypeComboBox.ItemsSource = Device.AvailableTypes;

            // 使用 ExternalDeviceService 加载外部设备列表并按分类分组
            if (_dllNameComboBox != null)
            {
                _dllNameComboBox.Items.Clear();

                var devices = ExternalDeviceService.Instance.GetAvailableDevices();

                // 按分类分组
                var groups = devices
                    .GroupBy(d => d.Category ?? "其他")
                    .OrderBy(g => GetCategoryOrder(g.Key))
                    .ThenBy(g => g.Key);

                foreach (var group in groups)
                {
                    // 添加分组标题（不可选）
                    var groupHeader = new ComboBoxItem
                    {
                        Content = $"── {group.Key} ──",
                        IsEnabled = false,
                        Foreground = new SolidColorBrush(Color.Parse("#9ca3af")),
                        FontWeight = FontWeight.SemiBold,
                        FontSize = 12
                    };
                    _dllNameComboBox.Items.Add(groupHeader);

                    // 添加该分组下的设备
                    foreach (var device in group.OrderBy(d => d.DisplayName))
                    {
                        var deviceItem = new ComboBoxItem { Tag = device };

                        // 星标设备：在名称前添加 ★ 符号，并使用蓝色
                        if (device.Starred)
                        {
                            deviceItem.Content = $"★ {device.DisplayName}";
                            deviceItem.Foreground = new SolidColorBrush(Color.Parse("#2563eb"));
                            deviceItem.FontWeight = FontWeight.Medium;
                        }
                        else
                        {
                            deviceItem.Content = device.DisplayName;
                        }

                        _dllNameComboBox.Items.Add(deviceItem);
                    }
                }
            }
        }

        private int GetCategoryOrder(string category)
        {
            // 定义分类显示顺序
            return category switch
            {
                "PLC" => 1,
                "传感器" => 2,
                "执行器" => 3,
                "IO设备" => 4,
                "通讯设备" => 5,
                "输入设备" => 6,
                "调试工具" => 7,
                _ => 99
            };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public void SetDevice(Device device)
        {
            _sourceDevice = device;
            if (_restartDeviceButton != null)
                _restartDeviceButton.IsVisible = IORoot.Instance.Devices.Contains(device);
            _device = new Device
            {
                Name = device.Name,
                Type = device.Type,
                DllName = device.DllName,
                Index = device.Index,
                Properties = new Properties()
            };
            _device.Properties.CopyFrom(device.Properties);
            DataContext = _device;
            if (device != null)
            {
                Title = $"设备配置 - {_device.Name}";

                // 设置 DllNameComboBox 的选中项
                ExternalDeviceInfo? deviceInfo = null;
                if (_dllNameComboBox != null && !string.IsNullOrEmpty(_device.DllName))
                {
                    deviceInfo = ExternalDeviceService.Instance.GetDeviceInfo(_device.DllName);
                    if (deviceInfo != null)
                    {
                        // 遍历找到匹配的 ComboBoxItem
                        foreach (var item in _dllNameComboBox.Items)
                        {
                            if (
                                item is ComboBoxItem comboItem
                                && comboItem.Tag is ExternalDeviceInfo info
                            )
                            {
                                if (info.DllName == device.DllName)
                                {
                                    _dllNameComboBox.SelectedItem = comboItem;
                                    break;
                                }
                            }
                        }
                    }
                }

                // 更新文档按钮状态
                UpdateDocButtonState(deviceInfo);

                // 更新 UI 可见性
                UpdateExternalSectionsVisibility();

                // 如果是 External 类型且有 DllName，加载配置面板
                if (_device.IsExternalType && !string.IsNullOrEmpty(_device.DllName))
                {
                    ShowExternalDeviceConfig(_device.DllName, _device.Index);
                }
                else
                {
                    HideExternalDeviceConfig();
                }
            }
        }

        private void UpdateExternalSectionsVisibility()
        {
            if (_device == null)
                return;

            var isExternal = _device.IsExternalType;

            // DllName 和 Index 选择仅在 External 类型时可见
            if (_dllNameSection != null)
                _dllNameSection.IsVisible = isExternal;

            if (_deviceIndexSection != null)
                _deviceIndexSection.IsVisible = isExternal;
        }

        private void OnDeviceTypeChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_device == null)
                return;

            UpdateExternalSectionsVisibility();

            // 如果切换为 External 类型且有 DllName，显示配置面板
            if (_device.IsExternalType && !string.IsNullOrEmpty(_device.DllName))
            {
                ShowExternalDeviceConfig(_device.DllName, _device.Index);
            }
            else
            {
                HideExternalDeviceConfig();
            }
        }

        private void OnDllNameChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_device == null)
                return;

            // 从选中项获取 DllName
            if (
                _dllNameComboBox?.SelectedItem is ComboBoxItem comboItem
                && comboItem.Tag is ExternalDeviceInfo deviceInfo
            )
            {
                _device.DllName = deviceInfo.DllName;

                // 更新文档按钮状态
                UpdateDocButtonState(deviceInfo);
            }

            // 当 DllName 变化时，重新加载配置面板
            if (_device.IsExternalType && !string.IsNullOrEmpty(_device.DllName))
            {
                ShowExternalDeviceConfig(_device.DllName, _device.Index);
            }
            else
            {
                HideExternalDeviceConfig();
            }
        }

        private void OnDllNameDropDownOpened(object? sender, EventArgs e)
        {
            // 使用简化的星号方案，无需特殊处理
        }

        private void OnDllNameDropDownClosed(object? sender, EventArgs e)
        {
            // 使用简化的星号方案，无需特殊处理
        }

        private void UpdateDocButtonState(ExternalDeviceInfo? deviceInfo)
        {
            if (_docButton == null)
                return;

            _docButton.IsEnabled = deviceInfo?.HasDoc ?? false;
        }

        private void DocButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_device == null || string.IsNullOrEmpty(_device.DllName))
                return;

            var docPath = ExternalDeviceService.Instance.GetDocumentPath(_device.DllName);
            if (!string.IsNullOrEmpty(docPath))
            {
                try
                {
                    // 使用默认浏览器打开 HTML 文件
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = docPath,
                            UseShellExecute = true
                        }
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"打开文档失败: {ex.Message}");
                }
            }
        }

        private void OnDeviceIndexChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_device == null)
                return;

            // 更新设备索引
            if (e.NewValue.HasValue)
            {
                _device.Index = (int)e.NewValue.Value;
            }

            // 当设备索引变化时，重新加载配置面板
            if (_device.IsExternalType && !string.IsNullOrEmpty(_device.DllName))
            {
                ShowExternalDeviceConfig(_device.DllName, _device.Index);
            }
        }

        private void ShowExternalDeviceConfig(string dllName, int deviceIndex)
        {
            if (_externalDeviceConfigSection != null)
            {
                _externalDeviceConfigSection.IsVisible = true;
            }

            if (_dllNameText != null)
            {
                _dllNameText.Text = $"({dllName})";
            }

            if (_deviceConfigPanel != null)
            {
                // 订阅配置变化事件
                _deviceConfigPanel.ConfigChanged -= OnDeviceConfigChanged;
                _deviceConfigPanel.ConfigChanged += OnDeviceConfigChanged;

                _deviceConfigPanel.LoadConfig(dllName, deviceIndex);

                // 更新配置摘要
                UpdateConfigSummary();
            }
        }

        private void HideExternalDeviceConfig()
        {
            if (_externalDeviceConfigSection != null)
            {
                _externalDeviceConfigSection.IsVisible = false;
            }

            // 隐藏配置摘要
            if (_configSummaryBorder != null)
            {
                _configSummaryBorder.IsVisible = false;
            }
        }

        private void OnDeviceConfigChanged(object? sender, EventArgs e)
        {
            // 当配置变化时更新摘要
            UpdateConfigSummary();
        }

        /// <summary>
        /// 更新配置摘要显示
        /// </summary>
        private void UpdateConfigSummary()
        {
            if (
                _configSummaryPanel == null
                || _configSummaryBorder == null
                || _deviceConfigPanel == null
            )
                return;

            _configSummaryPanel.Children.Clear();

            var summary = _deviceConfigPanel.GetConfigSummary();

            if (summary.Count == 0)
            {
                _configSummaryBorder.IsVisible = false;
                return;
            }

            _configSummaryBorder.IsVisible = true;

            foreach (var item in summary)
            {
                var color = item.Color ?? "#64748b";

                var itemBorder = new Border
                {
                    Background = new Avalonia.Media.SolidColorBrush(
                        Avalonia.Media.Color.Parse("#ffffff")
                    ),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 4),
                    Margin = new Thickness(0, 0, 8, 4),
                    BorderBrush = new Avalonia.Media.SolidColorBrush(
                        Avalonia.Media.Color.Parse("#e2e8f0")
                    ),
                    BorderThickness = new Thickness(1)
                };

                var stackPanel = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 4
                };

                stackPanel.Children.Add(
                    new TextBlock
                    {
                        Text = item.Label + ":",
                        FontSize = 12,
                        Foreground = new Avalonia.Media.SolidColorBrush(
                            Avalonia.Media.Color.Parse("#64748b")
                        ),
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    }
                );

                stackPanel.Children.Add(
                    new TextBlock
                    {
                        Text = item.Value,
                        FontSize = 12,
                        FontWeight = Avalonia.Media.FontWeight.SemiBold,
                        Foreground = new Avalonia.Media.SolidColorBrush(
                            Avalonia.Media.Color.Parse(color)
                        ),
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    }
                );

                itemBorder.Child = stackPanel;
                _configSummaryPanel.Children.Add(itemBorder);
            }
        }

        private async void OnEditPropertyKeyClick(object? sender, RoutedEventArgs e)
        {
            if (e.Source is Button button && button.Name == "EditPropertyKeyButton")
            {
                if (button.DataContext is ViewModels.Key key)
                {
                    var device = DataContext as Device;
                    var originalKeyName = key.Name;
                    var dialog = new PropertyKeyEditDialog();
                    dialog.SetKey(key, null);
                    await dialog.ShowDialog(this);
                    if (dialog.IsConfirmed)
                    {
                        // 检查是否改名并且新名称与其他 Key 重复
                        if (
                            key.Name != originalKeyName
                            && device?.Properties.KeyList.Any(k => k != key && k.Name == key.Name)
                                == true
                        )
                        {
                            // 恢复原名称
                            key.Name = originalKeyName;
                            // TODO: 显示错误提示
                            return;
                        }

                    }
                }
                e.Handled = true;
            }
        }

        private void OnDeletePropertyKeyClick(object? sender, RoutedEventArgs e)
        {
            if (e.Source is Button button && button.Name == "DeletePropertyKeyButton")
            {
                if (button.DataContext is ViewModels.Key key)
                {
                    var device = DataContext as Device;
                    if (device?.Properties != null)
                    {
                        device.Properties.KeyList.Remove(key);
                        device.Properties.Keys.Remove(key);
                    }
                }
                e.Handled = true;
            }
        }

        private async void AddPropertyKeyButton_Click(object? sender, RoutedEventArgs e)
        {
            var device = DataContext as Device;
            if (device?.Properties == null)
                return;

            // 获取现有键名列表
            var existingKeyNames = device.Properties.KeyList.Select(k => k.Name);

            // 创建新的Key
            var newKey = new ViewModels.Key
            {
                Name = GetDefaultPropertyKeyName(device),
                Offset = 0.0f,
                Scale = 1.0f,
                Min = -3.40282e+38f, // 默认无限制 (-FLT_MAX)
                Max = 3.40282e+38f, // 默认无限制 (FLT_MAX)
                DeadZone = 0.0f,
                Sensitivity = 1.0f,
                Exponent = 1.0f,
                Invert = "False",
                InvertEvent = "False"
            };

            // 打开编辑对话框
            var dialog = new PropertyKeyEditDialog();
            dialog.SetKey(newKey, null, isNewKey: true, existingKeyNames: existingKeyNames);

            await dialog.ShowDialog(this);
            if (dialog.IsConfirmed)
            {
                // 验证已在对话框中完成，这里只需添加
                device.Properties.KeyList.Add(newKey);
                device.Properties.Keys.Add(newKey);

            }
        }

        private string GetDefaultPropertyKeyName(Device device)
        {
            var existingKeys = device.Properties.KeyList.Select(k => k.Name);
            return KeyNameValidator.GenerateUniqueKeyName("Axis", existingKeys);
        }

        private void ClearPropertyKeysButton_Click(object? sender, RoutedEventArgs e)
        {
            var device = DataContext as Device;
            if (device?.Properties == null)
                return;

            device.Properties.KeyList.Clear();
            device.Properties.Keys.Clear();
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            Close();
        }

        private async void SaveButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_device == null || _sourceDevice == null)
                return;

            if (string.IsNullOrWhiteSpace(_device.Name))
            {
                var box = MessageBoxManager.GetMessageBoxStandard(
                    "无法保存",
                    "设备名称不能为空。",
                    ButtonEnum.Ok,
                    MsBox.Avalonia.Enums.Icon.Warning
                );
                await box.ShowWindowDialogAsync(this);
                return;
            }

            if (_device.IsExternalType && string.IsNullOrWhiteSpace(_device.DllName))
            {
                var box = MessageBoxManager.GetMessageBoxStandard(
                    "无法保存",
                    "外部设备必须选择驱动。",
                    ButtonEnum.Ok,
                    MsBox.Avalonia.Enums.Icon.Warning
                );
                await box.ShowWindowDialogAsync(this);
                return;
            }

            // 保存设备配置面板的修改
            if (_deviceConfigPanel != null && _deviceConfigPanel.HasUnsavedChanges())
            {
                _deviceConfigPanel.SaveConfig();
            }

            _sourceDevice.Name = _device.Name.Trim();
            _sourceDevice.Type = _device.Type;
            _sourceDevice.DllName = _device.DllName;
            _sourceDevice.Index = _device.Index;
            _sourceDevice.Properties.CopyFrom(_device.Properties);
            _sourceDevice.UpdateConfigSummary();

            IsConfirmed = true;
            IORoot.Instance?.Save();

            // 提示用户重启设备
            if (
                _deviceConfigPanel != null
                && _device?.Type?.Equals("External", StringComparison.OrdinalIgnoreCase) == true
            )
            {
                var box = MessageBoxManager.GetMessageBoxStandard(
                    "配置已保存",
                    "外部设备配置已保存。\n修改的配置需要重启设备后才能生效。\n\n是否现在重启设备？",
                    ButtonEnum.YesNo,
                    MsBox.Avalonia.Enums.Icon.Question
                );
                var result = await box.ShowWindowDialogAsync(this);

                if (result == ButtonResult.Yes)
                {
                    RestartDevice();
                }
            }

            Close();
        }

        private async void RestartDeviceButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_deviceConfigPanel != null && _deviceConfigPanel.HasUnsavedChanges())
            {
                var unsavedBox = MessageBoxManager.GetMessageBoxStandard(
                    "存在未保存修改",
                    "请先保存当前配置，再重启设备服务。",
                    ButtonEnum.Ok,
                    MsBox.Avalonia.Enums.Icon.Warning
                );
                await unsavedBox.ShowWindowDialogAsync(this);
                return;
            }

            var box = MessageBoxManager.GetMessageBoxStandard(
                "重启设备",
                "确定要重启所有IO设备吗？\n这将关闭并重新打开所有设备连接。",
                ButtonEnum.YesNo,
                MsBox.Avalonia.Enums.Icon.Question
            );
            var result = await box.ShowWindowDialogAsync(this);

            if (result == ButtonResult.Yes)
            {
                RestartDevice();

                var infoBox = MessageBoxManager.GetMessageBoxStandard(
                    "重启完成",
                    "设备已重启，新配置已生效。",
                    ButtonEnum.Ok,
                    MsBox.Avalonia.Enums.Icon.Info
                );
                await infoBox.ShowWindowDialogAsync(this);
            }
        }

        private void RestartDevice()
        {
            // 调用MainWindowViewModel的静态重启方法
            MainWindowViewModel.RestartDevices();
        }
    }
}
