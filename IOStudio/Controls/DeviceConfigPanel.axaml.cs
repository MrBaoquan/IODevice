using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using IOStudio.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IOStudio.Controls
{
    public partial class DeviceConfigPanel : UserControl
    {
        private string? _dllName;
        private int _currentDeviceIndex = 0;
        private DeviceSchema? _schema;
        private Dictionary<string, string> _currentValues = new();
        private Dictionary<string, string> _defaultValues = new();
        private Dictionary<string, Control> _fieldControls = new();
        private Dictionary<string, string> _modifiedValues = new();

        // 跟踪每个字段属于哪个INI section
        private Dictionary<string, string> _fieldToIniSection = new();

        // 独立INI section的原始值（用于保存时对比）
        private Dictionary<string, Dictionary<string, string>> _sectionValues = new();

        private Button? _syncSchemaButton;
        private Button? _refreshButton;
        private Button? _openConfigFileButton;
        private Button? _saveAsPresetButton;
        private Button? _resetButton;
        private StackPanel? _configContainer;
        private Border? _emptyState;
        private StackPanel? _presetPanel;
        private ComboBox? _presetComboBox;
        private Button? _applyPresetButton;

        // 当前使用的预设名称（保存到INI的_preset key）
        private string? _currentPresetName;

        // 预设隐藏的section名称列表
        private HashSet<string> _presetHiddenSections = new();

        // 跟踪section控件，用于预设隐藏
        private Dictionary<string, Control> _sectionControls = new();

        // 缓存当前设备section的键，用于判断是否使用默认值（避免重复读取INI）
        private HashSet<string> _deviceSectionKeys = new();

        public event EventHandler? ConfigChanged;

        public DeviceConfigPanel()
        {
            InitializeComponent();
            InitializeControls();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void InitializeControls()
        {
            _syncSchemaButton = this.FindControl<Button>("SyncSchemaButton");
            _refreshButton = this.FindControl<Button>("RefreshButton");
            _openConfigFileButton = this.FindControl<Button>("OpenConfigFileButton");
            _saveAsPresetButton = this.FindControl<Button>("SaveAsPresetButton");
            _resetButton = this.FindControl<Button>("ResetButton");
            _configContainer = this.FindControl<StackPanel>("ConfigContainer");
            _emptyState = this.FindControl<Border>("EmptyState");
            _presetPanel = this.FindControl<StackPanel>("PresetPanel");
            _presetComboBox = this.FindControl<ComboBox>("PresetComboBox");
            _applyPresetButton = this.FindControl<Button>("ApplyPresetButton");

            if (_syncSchemaButton != null)
            {
                _syncSchemaButton.Click += OnSyncSchemaClick;
            }

            if (_refreshButton != null)
            {
                _refreshButton.Click += OnRefreshClick;
            }

            if (_openConfigFileButton != null)
            {
                _openConfigFileButton.Click += OnOpenConfigFileClick;
            }

            if (_saveAsPresetButton != null)
            {
                _saveAsPresetButton.Click += OnSaveAsPresetClick;
            }

            if (_resetButton != null)
            {
                _resetButton.Click += OnResetClick;
            }

            if (_presetComboBox != null)
            {
                _presetComboBox.SelectionChanged += OnPresetSelectionChanged;
            }

            if (_applyPresetButton != null)
            {
                _applyPresetButton.Click += OnApplyPresetClick;
            }
        }

        /// <summary>
        /// 加载设备配置
        /// </summary>
        /// <param name="dllName">DLL名称（如MODBUS、SNAP7等）</param>
        /// <param name="deviceIndex">初始设备索引</param>
        public void LoadConfig(string dllName, int deviceIndex = 0)
        {
            _dllName = dllName;
            _currentDeviceIndex = deviceIndex;

            // 加载Schema
            _schema = DeviceSchemaService.Instance.LoadSchema(dllName);

            // 读取默认配置
            _defaultValues = IniConfigService.Instance.ReadSection(dllName, "default");

            // 读取当前配置（合并default和device_N）
            _currentValues = IniConfigService.Instance.ReadConfig(dllName, deviceIndex);

            // 缓存设备section的键，用于后续判断是否使用默认值
            _deviceSectionKeys.Clear();
            if (deviceIndex > 0)
            {
                var deviceSection = IniConfigService.Instance.ReadSection(
                    dllName,
                    $"device_{deviceIndex}"
                );
                foreach (var key in deviceSection.Keys)
                {
                    _deviceSectionKeys.Add(key);
                }
            }

            // 清空跟踪数据
            _modifiedValues.Clear();
            _fieldToIniSection.Clear();
            _sectionValues.Clear();
            _presetHiddenSections.Clear();
            _sectionControls.Clear();

            // 加载独立INI section的数据
            if (_schema?.Sections != null)
            {
                foreach (var section in _schema.Sections)
                {
                    if (!string.IsNullOrEmpty(section.IniSection))
                    {
                        var sectionData = IniConfigService.Instance.ReadSection(
                            dllName,
                            section.IniSection
                        );
                        _sectionValues[section.IniSection] = sectionData;

                        // 将独立section的值也合并到_currentValues，加上前缀避免冲突
                        foreach (var kvp in sectionData)
                        {
                            var prefixedKey = $"{section.IniSection}:{kvp.Key}";
                            _currentValues[prefixedKey] = kvp.Value;
                            _fieldToIniSection[prefixedKey] = section.IniSection;
                        }
                    }
                }
            }

            // 读取当前使用的预设标记
            _currentPresetName =
                _currentValues.TryGetValue("_preset", out var presetName)
                && !string.IsNullOrEmpty(presetName)
                    ? presetName
                    : null;

            // 加载预设列表
            LoadPresets();

            // 生成UI
            BuildConfigUI();

            // 更新预设指示器显示
            UpdatePresetIndicator();
        }

        /// <summary>
        /// 获取已修改的配置值
        /// </summary>
        public Dictionary<string, string> GetModifiedValues()
        {
            return new Dictionary<string, string>(_modifiedValues);
        }

        /// <summary>
        /// 获取所有当前配置值
        /// </summary>
        public Dictionary<string, string> GetAllValues()
        {
            var result = new Dictionary<string, string>(_currentValues);
            foreach (var kvp in _modifiedValues)
            {
                if (kvp.Value == "\0DELETED\0")
                {
                    // 移除已删除的项
                    result.Remove(kvp.Key);
                }
                else
                {
                    result[kvp.Key] = kvp.Value;
                }
            }
            return result;
        }

        /// <summary>
        /// 获取已删除的键列表
        /// </summary>
        public List<string> GetDeletedKeys()
        {
            return _modifiedValues
                .Where(kvp => kvp.Value == "\0DELETED\0")
                .Select(kvp => kvp.Key)
                .ToList();
        }

        /// <summary>
        /// 获取当前使用的预设名称
        /// </summary>
        public string? GetCurrentPresetName()
        {
            return _currentPresetName;
        }

        /// <summary>
        /// 获取配置摘要信息（用于在设备基本信息区域显示重要配置）
        /// </summary>
        /// <returns>配置摘要列表，每项包含标签和值</returns>
        public List<ConfigSummaryItem> GetConfigSummary()
        {
            var summary = new List<ConfigSummaryItem>();
            var allValues = GetAllValues();

            // 添加预设名称（如果有）
            if (!string.IsNullOrEmpty(_currentPresetName))
            {
                summary.Add(new ConfigSummaryItem("预设", _currentPresetName, "#3b82f6"));
            }

            // 从Schema中提取标记为summary的重要字段（只显示当前可见的字段）
            if (_schema?.Sections != null)
            {
                foreach (var section in _schema.Sections)
                {
                    // 检查section的visibleWhen条件
                    if (!CheckVisibility(section.VisibleWhen))
                        continue;

                    foreach (var field in section.Fields)
                    {
                        if (field.ShowInSummary)
                        {
                            // 检查字段的visibleWhen条件
                            if (!CheckVisibility(field.VisibleWhen))
                                continue;

                            var key = string.IsNullOrEmpty(section.IniSection)
                                ? field.Key
                                : $"{section.IniSection}:{field.Key}";

                            if (
                                allValues.TryGetValue(key, out var value)
                                && !string.IsNullOrEmpty(value)
                            )
                            {
                                // 如果有选项，显示选项的label而不是value
                                var displayValue = value;
                                if (field.Options != null)
                                {
                                    var option = field.Options.FirstOrDefault(
                                        o =>
                                            o.Value.Equals(
                                                value,
                                                StringComparison.OrdinalIgnoreCase
                                            )
                                    );
                                    if (option != null)
                                    {
                                        displayValue = option.Label;
                                    }
                                }

                                summary.Add(
                                    new ConfigSummaryItem(
                                        field.Label,
                                        displayValue,
                                        field.SummaryColor
                                    )
                                );
                            }
                        }
                    }
                }
            }

            return summary;
        }

        /// <summary>
        /// 保存配置到INI文件
        /// </summary>
        public void SaveConfig()
        {
            if (string.IsNullOrEmpty(_dllName))
                return;

            var allValues = GetAllValues();
            var deletedKeys = GetDeletedKeys();

            // 分离独立section的数据
            var mainValues = new Dictionary<string, string>();
            var mainDeletedKeys = new List<string>();
            var sectionData = new Dictionary<string, Dictionary<string, string>>();
            var sectionDeletedKeys = new Dictionary<string, List<string>>();

            foreach (var kvp in allValues)
            {
                if (kvp.Key.Contains(':'))
                {
                    // 独立section的数据
                    var parts = kvp.Key.Split(':', 2);
                    var sectionName = parts[0];
                    var actualKey = parts[1];

                    if (!sectionData.ContainsKey(sectionName))
                        sectionData[sectionName] = new Dictionary<string, string>();

                    sectionData[sectionName][actualKey] = kvp.Value;
                }
                else
                {
                    mainValues[kvp.Key] = kvp.Value;
                }
            }

            foreach (var key in deletedKeys)
            {
                if (key.Contains(':'))
                {
                    var parts = key.Split(':', 2);
                    var sectionName = parts[0];
                    var actualKey = parts[1];

                    if (!sectionDeletedKeys.ContainsKey(sectionName))
                        sectionDeletedKeys[sectionName] = new List<string>();

                    sectionDeletedKeys[sectionName].Add(actualKey);
                }
                else
                {
                    mainDeletedKeys.Add(key);
                }
            }

            // 保存主配置（default/device_N）
            IniConfigService.Instance.SaveConfig(
                _dllName,
                _currentDeviceIndex,
                mainValues,
                _defaultValues,
                mainDeletedKeys
            );

            // 保存独立section的数据
            foreach (var section in sectionData)
            {
                var deleted = sectionDeletedKeys.TryGetValue(section.Key, out var d) ? d : null;
                IniConfigService.Instance.SaveSection(
                    _dllName,
                    section.Key,
                    section.Value,
                    deleted
                );
            }

            // 处理只有删除没有新增的section
            foreach (var section in sectionDeletedKeys)
            {
                if (!sectionData.ContainsKey(section.Key))
                {
                    IniConfigService.Instance.SaveSection(
                        _dllName,
                        section.Key,
                        new Dictionary<string, string>(),
                        section.Value
                    );
                }
            }

            // 保存后清空修改标记
            _modifiedValues.Clear();

            // 刷新UI以更新"默认值"标记
            LoadConfig(_dllName, _currentDeviceIndex);
        }

        /// <summary>
        /// 检查是否有未保存的修改
        /// </summary>
        public bool HasUnsavedChanges()
        {
            return _modifiedValues.Count > 0;
        }

        private void OnSyncSchemaClick(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_dllName))
                return;

            _schema = DeviceSchemaService.Instance.SyncFromIni(_dllName);
            BuildConfigUI();
        }

        private void OnRefreshClick(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_dllName))
                return;

            LoadConfig(_dllName, _currentDeviceIndex);
        }

        private async void OnResetClick(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_dllName) || _schema == null)
                return;

            // 弹出确认对话框
            var dialog = new Window
            {
                Title = "重置配置",
                Width = 400,
                Height = 180,
                WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            var confirmed = false;

            var confirmButton = new Button
            {
                Content = "确认重置",
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 8, 0),
                Padding = new Thickness(16, 8, 16, 8),
                Background = new SolidColorBrush(Color.Parse("#f59e0b")),
                Foreground = Brushes.White
            };

            var cancelButton = new Button
            {
                Content = "取消",
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0),
                Padding = new Thickness(16, 8, 16, 8)
            };

            confirmButton.Click += (s, args) =>
            {
                confirmed = true;
                dialog.Close();
            };

            cancelButton.Click += (s, args) =>
            {
                dialog.Close();
            };

            var panel = new StackPanel
            {
                Margin = new Thickness(24),
                Children =
                {
                    new TextBlock
                    {
                        Text = "确定要重置为默认配置吗？",
                        FontWeight = FontWeight.SemiBold,
                        FontSize = 14,
                        Margin = new Thickness(0, 0, 0, 8)
                    },
                    new TextBlock
                    {
                        Text = "此操作将清除当前的配置修改，恢复为Schema中定义的默认值。",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(Color.Parse("#6b7280"))
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { confirmButton, cancelButton }
                    }
                }
            };

            dialog.Content = panel;

            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            if (parentWindow != null)
            {
                await dialog.ShowDialog(parentWindow);
            }

            if (!confirmed)
                return;

            // 执行重置
            ResetToDefaults();
        }

        /// <summary>
        /// 重置配置为Schema中定义的默认值
        /// </summary>
        private void ResetToDefaults()
        {
            if (_schema == null)
                return;

            // 使用Schema默认值重置
            var defaultValues = new Dictionary<string, string>();

            foreach (var section in _schema.Sections)
            {
                var prefix = string.IsNullOrEmpty(section.IniSection)
                    ? ""
                    : $"{section.IniSection}:";

                foreach (var field in section.Fields)
                {
                    if (!string.IsNullOrEmpty(field.Default))
                    {
                        var key = prefix + field.Key;
                        defaultValues[key] = field.Default;
                    }
                }
            }

            // 应用默认值到所有字段
            foreach (var kvp in defaultValues)
            {
                _modifiedValues[kvp.Key] = kvp.Value;

                if (_fieldControls.TryGetValue(kvp.Key, out var control))
                {
                    UpdateControlValue(control, kvp.Value);
                }
            }

            // 清除预设标记
            _currentPresetName = null;
            _modifiedValues["_preset"] = "";
            UpdatePresetIndicator();

            // 触发可见性更新
            UpdateAllFieldVisibilities();

            // 通知配置已变更
            ConfigChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnOpenConfigFileClick(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_dllName))
                return;

            var configPath = IniConfigService.Instance.GetConfigPath(_dllName);
            if (System.IO.File.Exists(configPath))
            {
                try
                {
                    // 使用系统默认程序打开配置文件
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = configPath,
                            UseShellExecute = true
                        }
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"打开配置文件失败: {ex.Message}");
                }
            }
        }

        private async void OnSaveAsPresetClick(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_dllName) || _schema == null)
                return;

            // 获取当前所有配置值
            var currentValues = GetAllValues();
            if (currentValues.Count == 0)
                return;

            // 弹出对话框输入预设名称
            var dialog = new Window
            {
                Title = "保存为预设",
                Width = 400,
                Height = 320,
                MinHeight = 280,
                WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
                CanResize = true
            };

            var nameTextBox = new TextBox
            {
                Watermark = "输入预设名称",
                Margin = new Thickness(0, 0, 0, 8)
            };

            var descTextBox = new TextBox
            {
                Watermark = "预设描述（可选）",
                Margin = new Thickness(0, 0, 0, 8)
            };

            var groupTextBox = new TextBox { Watermark = "分组（如 PLC、通用）", Text = "自定义" };

            var saveButton = new Button
            {
                Content = "保存",
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0),
                Padding = new Thickness(24, 8, 24, 8),
                Background = new SolidColorBrush(Color.Parse("#3b82f6")),
                Foreground = Brushes.White
            };

            var panel = new StackPanel
            {
                Margin = new Thickness(24),
                Children =
                {
                    new TextBlock
                    {
                        Text = "预设名称",
                        FontWeight = FontWeight.SemiBold,
                        Margin = new Thickness(0, 0, 0, 4)
                    },
                    nameTextBox,
                    new TextBlock
                    {
                        Text = "描述",
                        FontWeight = FontWeight.SemiBold,
                        Margin = new Thickness(0, 8, 0, 4)
                    },
                    descTextBox,
                    new TextBlock
                    {
                        Text = "分组",
                        FontWeight = FontWeight.SemiBold,
                        Margin = new Thickness(0, 8, 0, 4)
                    },
                    groupTextBox,
                    saveButton
                }
            };

            dialog.Content = panel;

            var presetName = "";
            var presetDesc = "";
            var presetGroup = "";

            saveButton.Click += (s, args) =>
            {
                presetName = nameTextBox.Text?.Trim() ?? "";
                presetDesc = descTextBox.Text?.Trim() ?? "";
                presetGroup = groupTextBox.Text?.Trim() ?? "自定义";

                if (!string.IsNullOrEmpty(presetName))
                {
                    dialog.Close();
                }
            };

            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            if (parentWindow != null)
            {
                await dialog.ShowDialog(parentWindow);
            }

            if (string.IsNullOrEmpty(presetName))
                return;

            // 创建新预设
            var newPreset = new PresetConfig
            {
                Name = presetName,
                Description = string.IsNullOrEmpty(presetDesc) ? null : presetDesc,
                Group = presetGroup,
                Values = currentValues
            };

            // 添加到 Schema
            if (_schema.Presets == null)
            {
                _schema.Presets = new List<PresetConfig>();
            }

            // 检查是否已存在同名预设
            var existingIndex = _schema.Presets.FindIndex(p => p.Name == presetName);
            if (existingIndex >= 0)
            {
                _schema.Presets[existingIndex] = newPreset;
            }
            else
            {
                _schema.Presets.Add(newPreset);
            }

            // 保存 Schema
            DeviceSchemaService.Instance.SaveSchema(_dllName, _schema);

            // 刷新预设列表
            LoadPresets();
        }

        private void OnPresetSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_applyPresetButton != null)
            {
                _applyPresetButton.IsEnabled = _presetComboBox?.SelectedItem != null;
            }
        }

        private void OnApplyPresetClick(object? sender, RoutedEventArgs e)
        {
            if (_presetComboBox?.SelectedItem is not ComboBoxItem item)
                return;

            if (item.Tag is not PresetConfig preset)
                return;

            // 应用预设的所有值
            ApplyPreset(preset);
        }

        /// <summary>
        /// 应用预设配置
        /// </summary>
        private void ApplyPreset(PresetConfig preset)
        {
            // 处理预设隐藏的sections
            _presetHiddenSections.Clear();
            if (preset.HideSections != null)
            {
                foreach (var sectionName in preset.HideSections)
                {
                    _presetHiddenSections.Add(sectionName);

                    // 隐藏section控件
                    if (_sectionControls.TryGetValue(sectionName, out var sectionControl))
                    {
                        sectionControl.IsVisible = false;
                    }

                    // 清除该section中所有字段的值（标记为删除）
                    ClearSectionFields(sectionName);
                }
            }

            // 显示非隐藏的sections
            foreach (var kvp in _sectionControls)
            {
                if (!_presetHiddenSections.Contains(kvp.Key))
                {
                    kvp.Value.IsVisible = true;
                }
            }

            foreach (var kvp in preset.Values)
            {
                // 更新内部值
                _modifiedValues[kvp.Key] = kvp.Value;

                // 更新UI控件的值
                if (_fieldControls.TryGetValue(kvp.Key, out var control))
                {
                    UpdateControlValue(control, kvp.Value);
                }
            }

            // 保存预设名称标记
            _currentPresetName = preset.Name;
            _modifiedValues["_preset"] = preset.Name;
            UpdatePresetIndicator();

            // 触发可见性更新
            UpdateAllFieldVisibilities();

            // 通知配置已变更
            ConfigChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 清除指定section中所有字段的值（标记为删除）
        /// </summary>
        private void ClearSectionFields(string sectionName)
        {
            if (_schema?.Sections == null)
                return;

            var section = _schema.Sections.FirstOrDefault(s => s.Name == sectionName);
            if (section == null)
                return;

            foreach (var field in section.Fields)
            {
                // 标记为删除
                _modifiedValues[field.Key] = "\0DELETED\0";

                // 清空控件值
                if (_fieldControls.TryGetValue(field.Key, out var control))
                {
                    ClearControlValue(control, field);
                }
            }
        }

        /// <summary>
        /// 清空控件的值
        /// </summary>
        private void ClearControlValue(Control control, SchemaField field)
        {
            switch (control)
            {
                case TextBox textBox:
                    textBox.Text = "";
                    break;

                case NumericUpDown numericUpDown:
                    numericUpDown.Value = numericUpDown.Minimum;
                    break;

                case ComboBox comboBox:
                    comboBox.SelectedIndex = -1;
                    break;

                case CheckBox checkBox:
                    checkBox.IsChecked = false;
                    break;
            }
        }

        /// <summary>
        /// 更新预设下拉框选中项
        /// </summary>
        private void UpdatePresetIndicator()
        {
            if (_presetComboBox == null || string.IsNullOrEmpty(_currentPresetName))
            {
                if (_presetComboBox != null)
                    _presetComboBox.SelectedIndex = -1;
                return;
            }

            // 查找并选中当前预设
            for (int i = 0; i < _presetComboBox.Items.Count; i++)
            {
                if (
                    _presetComboBox.Items[i] is ComboBoxItem item
                    && item.Tag is PresetConfig preset
                    && preset.Name == _currentPresetName
                )
                {
                    _presetComboBox.SelectedIndex = i;
                    break;
                }
            }
        }

        /// <summary>
        /// 更新控件的值
        /// </summary>
        private void UpdateControlValue(Control control, string value)
        {
            switch (control)
            {
                case TextBox textBox:
                    textBox.Text = value;
                    break;

                case NumericUpDown numericUpDown:
                    if (double.TryParse(value, out double numValue))
                    {
                        numericUpDown.Value = (decimal)numValue;
                    }
                    break;

                case ComboBox comboBox:
                    for (int i = 0; i < comboBox.Items.Count; i++)
                    {
                        if (
                            comboBox.Items[i] is ComboBoxItem cbItem
                            && cbItem.Tag is string tagValue
                            && tagValue.Equals(value, StringComparison.OrdinalIgnoreCase)
                        )
                        {
                            comboBox.SelectedIndex = i;
                            break;
                        }
                    }
                    break;

                case CheckBox checkBox:
                    checkBox.IsChecked =
                        value.Equals("true", StringComparison.OrdinalIgnoreCase)
                        || value.Equals("1", StringComparison.OrdinalIgnoreCase);
                    break;
            }
        }

        /// <summary>
        /// 加载预设到下拉框
        /// </summary>
        private void LoadPresets()
        {
            if (_presetComboBox == null || _presetPanel == null || _schema == null)
                return;

            _presetComboBox.Items.Clear();

            if (_schema.Presets == null || _schema.Presets.Count == 0)
            {
                _presetPanel.IsVisible = false;
                return;
            }

            _presetPanel.IsVisible = true;

            // 按分组组织预设
            var groups = _schema.Presets
                .GroupBy(p => p.Group ?? "其他")
                .OrderBy(g => g.Key == "通用" ? 0 : (g.Key == "其他" ? 2 : 1))
                .ThenBy(g => g.Key);

            foreach (var group in groups)
            {
                // 添加分组标题（不可选）
                var groupHeader = new ComboBoxItem
                {
                    Content = $"── {group.Key} ──",
                    IsEnabled = false,
                    Foreground = new SolidColorBrush(Color.Parse("#9ca3af")),
                    FontWeight = FontWeight.SemiBold
                };
                _presetComboBox.Items.Add(groupHeader);

                // 添加该分组下的预设
                foreach (var preset in group)
                {
                    var presetItem = new ComboBoxItem { Content = preset.Name, Tag = preset };

                    // 设置ToolTip
                    if (!string.IsNullOrEmpty(preset.Description))
                    {
                        ToolTip.SetTip(presetItem, preset.Description);
                    }

                    _presetComboBox.Items.Add(presetItem);
                }
            }

            if (_applyPresetButton != null)
            {
                _applyPresetButton.IsEnabled = false;
            }
        }

        private void BuildConfigUI()
        {
            if (_configContainer == null || _schema == null)
                return;

            // 清空现有内容（保留EmptyState）
            var emptyState = _emptyState;
            _configContainer.Children.Clear();
            _fieldControls.Clear();

            if (_schema.Sections.Count == 0)
            {
                if (emptyState != null)
                {
                    emptyState.IsVisible = true;
                    _configContainer.Children.Add(emptyState);
                }
                return;
            }

            // 隐藏空状态
            if (emptyState != null)
            {
                emptyState.IsVisible = false;
            }

            // 生成每个Section
            foreach (var section in _schema.Sections)
            {
                // 检查section的visibleWhen条件
                if (!CheckVisibility(section.VisibleWhen))
                    continue;

                var sectionControl = CreateSectionControl(section);
                _sectionControls[section.Name] = sectionControl;
                _configContainer.Children.Add(sectionControl);
            }
        }

        private Control CreateSectionControl(SchemaSection section)
        {
            var fieldsPanel = new StackPanel { Spacing = 12 };

            foreach (var field in section.Fields)
            {
                // 检查field的visibleWhen条件
                var fieldControl = CreateFieldControl(field, section.IniSection);
                fieldsPanel.Children.Add(fieldControl);
            }

            Control result;
            if (section.Collapsed)
            {
                // 使用Expander
                var expander = new Expander
                {
                    Header = section.Title,
                    IsExpanded = false,
                    Content = fieldsPanel,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                };
                result = expander;
            }
            else
            {
                // 使用普通Border
                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.Parse("#e5e7eb")),
                    BorderThickness = new Thickness(1),
                    Background = Brushes.White,
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var contentPanel = new StackPanel { Spacing = 12 };

                // 添加标题
                var titleBlock = new TextBlock
                {
                    Text = section.Title,
                    FontSize = 14,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(Color.Parse("#1f2937")),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                contentPanel.Children.Add(titleBlock);
                contentPanel.Children.Add(fieldsPanel);

                border.Child = contentPanel;
                result = border;
            }

            return result;
        }

        private Control CreateFieldControl(SchemaField field, string? iniSection = null)
        {
            var container = new StackPanel { Spacing = 4 };
            container.Name = $"Field_{field.Key}";

            // 标签行（包含label和默认值指示器）
            var labelRow = new StackPanel { Orientation = Orientation.Horizontal };

            var label = new TextBlock
            {
                Text = field.Label + (field.Required ? " *" : ""),
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.Parse("#4b5563"))
            };
            labelRow.Children.Add(label);

            // 检查是否使用默认值（使用缓存的键，避免重复读取INI）
            var currentValue = GetFieldValue(field.Key);
            var isUsingDefault =
                !_modifiedValues.ContainsKey(field.Key)
                && _currentDeviceIndex > 0
                && !_deviceSectionKeys.Contains(field.Key);

            if (isUsingDefault)
            {
                var defaultIndicator = new TextBlock
                {
                    Text = "(默认)",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.Parse("#3b82f6")),
                    FontStyle = FontStyle.Italic,
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                labelRow.Children.Add(defaultIndicator);
            }

            container.Children.Add(labelRow);

            // 如果当前值为空，使用Schema默认值
            var displayValue = currentValue;
            if (string.IsNullOrEmpty(displayValue) && !string.IsNullOrEmpty(field.Default))
            {
                displayValue = field.Default;
            }

            // 创建输入控件
            Control inputControl = field.Type switch
            {
                "select" => CreateSelectControl(field, displayValue),
                "number" => CreateNumberControl(field, displayValue),
                "boolean" => CreateBooleanControl(field, displayValue),
                "hex" => CreateHexControl(field, displayValue),
                "array" => CreateArrayControl(field, iniSection),
                _ => CreateTextControl(field, displayValue)
            };

            _fieldControls[field.Key] = inputControl;
            container.Children.Add(inputControl);

            // 添加描述
            if (!string.IsNullOrEmpty(field.Description))
            {
                var desc = new TextBlock
                {
                    Text = field.Description,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.Parse("#9ca3af")),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                container.Children.Add(desc);
            }

            // 设置visibleWhen绑定
            if (field.VisibleWhen != null && field.VisibleWhen.Count > 0)
            {
                UpdateFieldVisibility(container, field.VisibleWhen);
            }

            return container;
        }

        private Control CreateTextControl(SchemaField field, string currentValue)
        {
            var textBox = new TextBox
            {
                Text = currentValue,
                Watermark = field.Placeholder ?? "",
                Height = 32,
                FontSize = 13
            };

            textBox.TextChanged += (s, e) =>
            {
                OnFieldValueChanged(field.Key, textBox.Text ?? "");
            };

            return textBox;
        }

        private Control CreateNumberControl(SchemaField field, string currentValue)
        {
            var numericUpDown = new NumericUpDown
            {
                Height = 32,
                FontSize = 13,
                Increment = 1
            };

            if (field.Min.HasValue)
                numericUpDown.Minimum = (decimal)field.Min.Value;
            if (field.Max.HasValue)
                numericUpDown.Maximum = (decimal)field.Max.Value;

            if (double.TryParse(currentValue, out double value))
            {
                numericUpDown.Value = (decimal)value;
            }

            numericUpDown.ValueChanged += (s, e) =>
            {
                OnFieldValueChanged(field.Key, numericUpDown.Value?.ToString() ?? "");
            };

            return numericUpDown;
        }

        private Control CreateSelectControl(SchemaField field, string currentValue)
        {
            var comboBox = new ComboBox
            {
                Height = 32,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            int selectedIndex = -1;
            if (field.Options != null)
            {
                for (int i = 0; i < field.Options.Count; i++)
                {
                    var option = field.Options[i];
                    comboBox.Items.Add(
                        new ComboBoxItem { Content = option.Label, Tag = option.Value }
                    );

                    if (option.Value.Equals(currentValue, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = i;
                    }
                }
            }

            if (selectedIndex >= 0)
            {
                comboBox.SelectedIndex = selectedIndex;
            }

            comboBox.SelectionChanged += (s, e) =>
            {
                if (comboBox.SelectedItem is ComboBoxItem item && item.Tag is string value)
                {
                    OnFieldValueChanged(field.Key, value);
                    // 触发visibleWhen更新
                    UpdateAllFieldVisibilities();
                }
            };

            return comboBox;
        }

        private Control CreateBooleanControl(SchemaField field, string currentValue)
        {
            var checkBox = new CheckBox
            {
                Content = field.Label,
                IsChecked =
                    currentValue.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || currentValue.Equals("1", StringComparison.OrdinalIgnoreCase)
            };

            checkBox.IsCheckedChanged += (s, e) =>
            {
                OnFieldValueChanged(field.Key, checkBox.IsChecked == true ? "true" : "false");
            };

            return checkBox;
        }

        private Control CreateHexControl(SchemaField field, string currentValue)
        {
            var textBox = new TextBox
            {
                Text = currentValue,
                Watermark = field.Placeholder ?? "0x0000",
                Height = 32,
                FontSize = 13,
                FontFamily = new FontFamily("Consolas")
            };

            textBox.TextChanged += (s, e) =>
            {
                OnFieldValueChanged(field.Key, textBox.Text ?? "");
            };

            return textBox;
        }

        private Control CreateArrayControl(SchemaField field, string? iniSection = null)
        {
            // 判断是否使用独立INI section模式
            // 独立section模式：键是纯数字（如 0, 1, 2），在_currentValues中带前缀（如 InputMapping:0）
            var useIniSectionPrefix = !string.IsNullOrEmpty(iniSection);

            // baseKey用于在_currentValues中查找
            // displayBaseKey用于显示和保存到INI（独立section时为空）
            string baseKey;
            string displayBaseKey;

            if (useIniSectionPrefix)
            {
                // 独立section模式：键格式为 "SectionName:数字"
                baseKey = $"{iniSection}:";
                displayBaseKey = "";
            }
            else if (!string.IsNullOrEmpty(field.ArrayKeyPrefix))
            {
                // 有前缀模式：键格式为 "前缀_数字"
                baseKey = field.ArrayKeyPrefix + "_";
                displayBaseKey = baseKey;
            }
            else
            {
                // 默认模式
                baseKey = field.Key + "_";
                displayBaseKey = baseKey;
            }

            // 查找所有匹配的条目
            var existingEntries = new List<(string Key, string Value, string IndexStr)>();
            foreach (var kvp in _currentValues)
            {
                if (kvp.Key.StartsWith(baseKey) && kvp.Key.Length > baseKey.Length)
                {
                    var suffix = kvp.Key.Substring(baseKey.Length);
                    if (int.TryParse(suffix, out _))
                    {
                        existingEntries.Add((kvp.Key, kvp.Value, suffix));
                    }
                }
            }

            // 按数字排序
            existingEntries = existingEntries
                .OrderBy(e => int.TryParse(e.IndexStr, out var num) ? num : 0)
                .ToList();

            // 创建主容器
            var mainPanel = new StackPanel { Spacing = 4 };

            // 条目列表容器
            var itemsPanel = new StackPanel { Spacing = 4 };
            mainPanel.Children.Add(itemsPanel);

            // 添加现有条目
            foreach (var entry in existingEntries)
            {
                var itemRow = CreateArrayItemRow(
                    field,
                    entry.Key,
                    entry.Value,
                    entry.IndexStr,
                    itemsPanel,
                    baseKey,
                    displayBaseKey,
                    iniSection
                );
                itemsPanel.Children.Add(itemRow);
            }

            // 添加按钮行
            var addButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "+",
                            FontWeight = FontWeight.Bold,
                            FontSize = 14,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = "添加",
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                        }
                    }
                },
                Padding = new Thickness(8, 4),
                Margin = new Thickness(0, 4, 0, 0)
            };

            addButton.Click += (s, e) =>
            {
                // 找到下一个可用的索引（从已有最大值+1开始）
                var maxIndex = -1;
                foreach (var key in _currentValues.Keys.Concat(_modifiedValues.Keys))
                {
                    if (key.StartsWith(baseKey))
                    {
                        var suffix = key.Substring(baseKey.Length);
                        if (int.TryParse(suffix, out var idx) && idx > maxIndex)
                        {
                            maxIndex = idx;
                        }
                    }
                }
                var nextIndex = maxIndex + 1;

                var newKey = $"{baseKey}{nextIndex}";
                var itemRow = CreateArrayItemRow(
                    field,
                    newKey,
                    "",
                    nextIndex.ToString(),
                    itemsPanel,
                    baseKey,
                    displayBaseKey,
                    iniSection
                );
                itemsPanel.Children.Add(itemRow);

                // 触发值变更以标记为已修改
                OnFieldValueChanged(newKey, "");
            };

            mainPanel.Children.Add(addButton);

            return mainPanel;
        }

        private Grid CreateArrayItemRow(
            SchemaField field,
            string itemKey,
            string itemValue,
            string indexStr,
            StackPanel itemsPanel,
            string baseKey,
            string displayBaseKey,
            string? iniSection
        )
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                Tag = itemKey
            };

            // 可编辑的索引输入框 - 使用Border包裹以实现更好的居中效果
            var indexBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.Parse("#d1d5db")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Width = 40,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ClipToBounds = true
            };

            var indexTextBox = new TextBox
            {
                Text = indexStr,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Color.Parse("#374151")),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4, 0),
                TextAlignment = TextAlignment.Center,
                MaxWidth = 38,
                MinWidth = 38
            };
            indexBorder.Child = indexTextBox;
            Grid.SetColumn(indexBorder, 0);

            var valueTextBox = new TextBox
            {
                Text = itemValue,
                Watermark = field.Placeholder ?? field.ArrayItemFormat ?? "",
                Height = 32,
                FontSize = 13,
                FontFamily = new FontFamily("Consolas"),
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(valueTextBox, 1);

            // 注册到_fieldControls以便保存时获取值
            _fieldControls[itemKey] = valueTextBox;

            // 保存当前key的引用，用于跟踪key变化
            var currentKeyHolder = new { Key = itemKey };

            // 索引变化时更新key
            indexTextBox.LostFocus += (s, e) =>
            {
                var newIndexStr = indexTextBox.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(newIndexStr) || !int.TryParse(newIndexStr, out _))
                {
                    // 恢复原索引
                    indexTextBox.Text = (grid.Tag as string)?.Substring(baseKey.Length) ?? "0";
                    return;
                }

                var oldKey = grid.Tag as string ?? "";
                var newKey = $"{baseKey}{newIndexStr}";

                if (oldKey == newKey)
                    return;

                // 检查新key是否已存在
                if (_currentValues.ContainsKey(newKey) || _modifiedValues.ContainsKey(newKey))
                {
                    // key已存在，恢复原索引
                    indexTextBox.Text = oldKey.Substring(baseKey.Length);
                    return;
                }

                // 更新控件映射
                _fieldControls.Remove(oldKey);
                _fieldControls[newKey] = valueTextBox;

                // 标记旧key为删除
                if (_currentValues.ContainsKey(oldKey))
                {
                    _modifiedValues[oldKey] = "\0DELETED\0";
                }
                else
                {
                    _modifiedValues.Remove(oldKey);
                }

                // 添加新key
                _modifiedValues[newKey] = valueTextBox.Text ?? "";

                // 更新grid的Tag
                grid.Tag = newKey;
            };

            valueTextBox.TextChanged += (s, e) =>
            {
                var currentKey = grid.Tag as string ?? itemKey;
                OnFieldValueChanged(currentKey, valueTextBox.Text ?? "");
            };

            var deleteButton = new Button
            {
                Content = new PathIcon
                {
                    Data = StreamGeometry.Parse(
                        "M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z"
                    ),
                    Width = 12,
                    Height = 12,
                    Foreground = new SolidColorBrush(Color.Parse("#9ca3af"))
                },
                Background = new SolidColorBrush(Color.Parse("#f3f4f6")),
                Padding = new Thickness(8),
                Width = 32,
                Height = 32,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };

            // 悬停效果
            deleteButton.PointerEntered += (s, e) =>
            {
                deleteButton.Background = new SolidColorBrush(Color.Parse("#fee2e2"));
                if (deleteButton.Content is PathIcon icon)
                    icon.Foreground = new SolidColorBrush(Color.Parse("#ef4444"));
            };
            deleteButton.PointerExited += (s, e) =>
            {
                deleteButton.Background = new SolidColorBrush(Color.Parse("#f3f4f6"));
                if (deleteButton.Content is PathIcon icon)
                    icon.Foreground = new SolidColorBrush(Color.Parse("#9ca3af"));
            };

            ToolTip.SetTip(deleteButton, "删除此项");
            Grid.SetColumn(deleteButton, 2);

            deleteButton.Click += (s, e) =>
            {
                var currentKey = grid.Tag as string ?? itemKey;

                // 从UI移除
                itemsPanel.Children.Remove(grid);

                // 从控件映射移除
                _fieldControls.Remove(currentKey);

                // 标记为删除（使用特殊标记）
                _modifiedValues[currentKey] = "\0DELETED\0";
            };

            grid.Children.Add(indexBorder);
            grid.Children.Add(valueTextBox);
            grid.Children.Add(deleteButton);

            return grid;
        }

        private string GetFieldValue(string key)
        {
            // 优先返回已修改的值
            if (_modifiedValues.TryGetValue(key, out var modified))
                return modified;

            // 然后返回当前配置值
            if (_currentValues.TryGetValue(key, out var current))
                return current;

            return "";
        }

        private void OnFieldValueChanged(string key, string newValue)
        {
            var originalValue = _currentValues.TryGetValue(key, out var v) ? v : "";

            if (newValue == originalValue)
            {
                _modifiedValues.Remove(key);
            }
            else
            {
                _modifiedValues[key] = newValue;
            }

            ConfigChanged?.Invoke(this, EventArgs.Empty);
        }

        private bool CheckVisibility(Dictionary<string, string>? visibleWhen)
        {
            if (visibleWhen == null || visibleWhen.Count == 0)
                return true;

            foreach (var condition in visibleWhen)
            {
                var currentValue = GetFieldValue(condition.Key);
                if (!currentValue.Equals(condition.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private void UpdateFieldVisibility(
            Control container,
            Dictionary<string, string> visibleWhen
        )
        {
            container.IsVisible = CheckVisibility(visibleWhen);
        }

        private void UpdateAllFieldVisibilities()
        {
            if (_configContainer == null || _schema == null)
                return;

            // 遍历所有section，更新section和field的可见性
            foreach (var section in _schema.Sections)
            {
                // 检查section是否被预设隐藏
                if (_presetHiddenSections.Contains(section.Name))
                {
                    if (_sectionControls.TryGetValue(section.Name, out var sectionControl))
                    {
                        sectionControl.IsVisible = false;
                    }
                    continue;
                }

                // 检查section的visibleWhen条件
                bool sectionVisible = CheckVisibility(section.VisibleWhen);
                if (_sectionControls.TryGetValue(section.Name, out var ctrl))
                {
                    ctrl.IsVisible = sectionVisible;
                }

                // 更新field的可见性
                foreach (var field in section.Fields)
                {
                    if (field.VisibleWhen != null && field.VisibleWhen.Count > 0)
                    {
                        var fieldContainer = FindFieldContainer(field.Key);
                        if (fieldContainer != null)
                        {
                            fieldContainer.IsVisible = CheckVisibility(field.VisibleWhen);
                        }
                    }
                }
            }
        }

        private Control? FindFieldContainer(string fieldKey)
        {
            return FindControlByName(_configContainer, $"Field_{fieldKey}");
        }

        private Control? FindControlByName(Control? parent, string name)
        {
            if (parent == null)
                return null;

            if (parent.Name == name)
                return parent;

            if (parent is Panel panel)
            {
                foreach (var child in panel.Children)
                {
                    var found = FindControlByName(child as Control, name);
                    if (found != null)
                        return found;
                }
            }
            else if (parent is Decorator decorator && decorator.Child is Control child)
            {
                return FindControlByName(child, name);
            }
            else if (
                parent is ContentControl contentControl && contentControl.Content is Control content
            )
            {
                return FindControlByName(content, name);
            }
            else if (parent is Expander expander && expander.Content is Control expanderContent)
            {
                return FindControlByName(expanderContent, name);
            }

            return null;
        }
    }
}
