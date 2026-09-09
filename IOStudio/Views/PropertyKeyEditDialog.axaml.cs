using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using IOToolkit;
using IOStudio.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ViewKey = IOStudio.ViewModels.Key;

namespace IOStudio.Views
{
    public partial class PropertyKeyEditDialog : Window
    {
        // decimal 可表示的最大/最小安全值 (用于 UI 控件)
        private const float SafeMinValue = -1e10f;
        private const float SafeMaxValue = 1e10f;

        public ViewKey? Key { get; private set; }
        public bool IsConfirmed { get; private set; }
        public bool DeleteRequested { get; private set; }
        private bool _isNewKey;
        private string? _deviceName;
        private string? _originalKeyName; // 编辑时用于排除自身
        private IEnumerable<string?>? _existingKeyNames; // 现有键名列表，用于重复检查

        // 原始值用于检测变更
        private float _originalOffset;
        private float _originalScale;
        private float _originalMin;
        private float _originalMax;
        private float _originalDeadZone;
        private float _originalSensitivity;
        private float _originalExponent;
        private bool _originalInvert;
        private bool _originalInvertEvent;

        // 控件引用
        private TextBlock? _keyNameText;
        private TextBlock? _inputValueText;
        private TextBlock? _processedValueText;
        private NumericUpDown? _offsetNumeric;
        private NumericUpDown? _scaleNumeric;
        private NumericUpDown? _minNumeric;
        private NumericUpDown? _maxNumeric;
        private NumericUpDown? _deadZoneNumeric;
        private NumericUpDown? _sensitivityNumeric;
        private NumericUpDown? _exponentNumeric;
        private CheckBox? _invertCheckBox;
        private CheckBox? _invertEventCheckBox;
        private CheckBox? _minUnlimitedCheckBox;
        private CheckBox? _maxUnlimitedCheckBox;
        private TextBox? _keyNameTextBox;

        // 初始化标志，用于防止初始化过程中多次调用 ApplyCurrentConfig
        private bool _isInitializing = true;

        // 定时器，用于实时更新原始值和处理值
        private DispatcherTimer? _updateTimer;

        // FLT_MAX 阈值，用于判断是否为"无限制"
        private const float UnlimitedThreshold = 1e10f;

        // 校准相关字段
        private float? _calibrationRawMin = null;
        private float? _calibrationRawMax = null;
        private bool _isCalibrationExpanded = false;
        private float _currentRawValueForCalibration = 0f;

        // 校准相关控件引用
        private StackPanel? _calibrationContent;
        private TextBlock? _calibrationToggleIcon;
        private TextBlock? _calibrationRawValueText;
        private TextBlock? _capturedMinValueText;
        private TextBlock? _capturedMaxValueText;
        private NumericUpDown? _targetMinNumeric;
        private NumericUpDown? _targetMaxNumeric;
        private Border? _calibrationResultPanel;
        private TextBlock? _calculatedOffsetText;
        private TextBlock? _calculatedScaleText;
        private Button? _applyCalibrationButton;

        /// <summary>
        /// 安全地将 float 转换为 decimal，避免溢出
        /// </summary>
        private static decimal SafeFloatToDecimal(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0m;
            if (value > (float)decimal.MaxValue || value < (float)decimal.MinValue)
                return value > 0 ? (decimal)SafeMaxValue : (decimal)SafeMinValue;
            try
            {
                return (decimal)value;
            }
            catch (OverflowException)
            {
                return value > 0 ? (decimal)SafeMaxValue : (decimal)SafeMinValue;
            }
        }

        /// <summary>
        /// 判断 Min/Max 值是否为"无限制"（接近 FLT_MAX）
        /// </summary>
        private static bool IsUnlimitedValue(float value, bool isMin)
        {
            if (isMin)
                return value < -UnlimitedThreshold;
            else
                return value > UnlimitedThreshold;
        }

        public PropertyKeyEditDialog()
        {
            InitializeComponent();
        }

        public void SetKey(
            ViewKey key,
            string? deviceName,
            bool isNewKey = false,
            IEnumerable<string?>? existingKeyNames = null
        )
        {
            _isNewKey = isNewKey;
            _deviceName = deviceName;
            _originalKeyName = isNewKey ? null : key.Name; // 编辑时保存原始名称
            _existingKeyNames = existingKeyNames;
            Key = key;
            DataContext = Key;

            // 保存原始值
            _originalOffset = Key.Offset;
            _originalScale = Key.Scale;
            _originalMin = Key.Min;
            _originalMax = Key.Max;
            _originalDeadZone = Key.DeadZone;
            _originalSensitivity = Key.Sensitivity;
            _originalExponent = Key.Exponent;
            _originalInvert = Key.InvertBool;
            _originalInvertEvent = Key.InvertEventBool;
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            InitializeControls();

            // 初始化完成后调用一次 ApplyCurrentConfig
            _isInitializing = false;
            ApplyCurrentConfig();

            // 启动定时器，实时更新原始值和处理值
            StartUpdateTimer();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            StopUpdateTimer();
        }

        private void StartUpdateTimer()
        {
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50) // 20Hz 刷新率
            };
            _updateTimer.Tick += OnUpdateTimerTick;
            _updateTimer.Start();
        }

        private void StopUpdateTimer()
        {
            if (_updateTimer != null)
            {
                _updateTimer.Stop();
                _updateTimer.Tick -= OnUpdateTimerTick;
                _updateTimer = null;
            }
        }

        private void OnUpdateTimerTick(object? sender, EventArgs e)
        {
            UpdateRawAndProcessedValues();
        }

        /// <summary>
        /// 更新原始值和处理值显示
        /// </summary>
        private void UpdateRawAndProcessedValues()
        {
            if (Key == null)
                return;

            if (_isNewKey)
            {
                // 新建模式：原始值显示为 0，处理值使用模拟计算
                _currentRawValueForCalibration = 0f;
                if (_inputValueText != null)
                {
                    _inputValueText.Text = "0.0000";
                }
                if (_processedValueText != null)
                {
                    float processedValue = SimulateProcessing(0, Key);
                    _processedValueText.Text = processedValue.ToString("F4");
                }
            }
            else
            {
                // 编辑模式：从 IODevice 获取实时数据
                if (!string.IsNullOrEmpty(_deviceName))
                {
                    try
                    {
                        var device = IODeviceController.GetIODevice(_deviceName);

                        // 获取原始值
                        float rawValue = device.GetRawKeyValue(Key.Name);
                        _currentRawValueForCalibration = rawValue;
                        if (_inputValueText != null)
                        {
                            _inputValueText.Text = rawValue.ToString("F4");
                        }

                        // 获取处理后的值
                        float processedValue = device.GetAxisKey(Key.Name);
                        if (_processedValueText != null)
                        {
                            _processedValueText.Text = processedValue.ToString("F4");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"UpdateRawAndProcessedValues failed: {ex.Message}");
                    }
                }
            }

            // 更新校准面板的实时原始值
            if (_calibrationRawValueText != null)
            {
                _calibrationRawValueText.Text = _currentRawValueForCalibration.ToString("F4");
            }
        }

        private void InitializeControls()
        {
            // 查找控件
            _keyNameText = this.FindControl<TextBlock>("KeyNameText");
            _keyNameTextBox = this.FindControl<TextBox>("KeyNameTextBox");
            _inputValueText = this.FindControl<TextBlock>("RawValueText");
            _processedValueText = this.FindControl<TextBlock>("ProcessedValueText");
            _offsetNumeric = this.FindControl<NumericUpDown>("OffsetNumeric");
            _scaleNumeric = this.FindControl<NumericUpDown>("ScaleNumeric");
            _minNumeric = this.FindControl<NumericUpDown>("MinNumeric");
            _maxNumeric = this.FindControl<NumericUpDown>("MaxNumeric");
            _deadZoneNumeric = this.FindControl<NumericUpDown>("DeadZoneNumeric");
            _sensitivityNumeric = this.FindControl<NumericUpDown>("SensitivityNumeric");
            _minUnlimitedCheckBox = this.FindControl<CheckBox>("MinUnlimitedCheckBox");
            _maxUnlimitedCheckBox = this.FindControl<CheckBox>("MaxUnlimitedCheckBox");

            // 绑定按钮事件
            var okButton = this.FindControl<Button>("OkButton");
            var cancelButton = this.FindControl<Button>("CancelButton");
            var resetButton = this.FindControl<Button>("ResetButton");

            if (okButton != null)
                okButton.Click += OnSaveClick;
            if (cancelButton != null)
                cancelButton.Click += OnCancelClick;
            if (resetButton != null)
                resetButton.Click += OnResetClick;
            _exponentNumeric = this.FindControl<NumericUpDown>("ExponentNumeric");
            _invertCheckBox = this.FindControl<CheckBox>("InvertCheckBox");
            _invertEventCheckBox = this.FindControl<CheckBox>("InvertEventCheckBox");

            // 校准相关控件
            _calibrationContent = this.FindControl<StackPanel>("CalibrationContent");
            _calibrationToggleIcon = this.FindControl<TextBlock>("CalibrationToggleIcon");
            _calibrationRawValueText = this.FindControl<TextBlock>("CalibrationRawValueText");
            _capturedMinValueText = this.FindControl<TextBlock>("CapturedMinValueText");
            _capturedMaxValueText = this.FindControl<TextBlock>("CapturedMaxValueText");
            _targetMinNumeric = this.FindControl<NumericUpDown>("TargetMinNumeric");
            _targetMaxNumeric = this.FindControl<NumericUpDown>("TargetMaxNumeric");
            _calibrationResultPanel = this.FindControl<Border>("CalibrationResultPanel");
            _calculatedOffsetText = this.FindControl<TextBlock>("CalculatedOffsetText");
            _calculatedScaleText = this.FindControl<TextBlock>("CalculatedScaleText");
            _applyCalibrationButton = this.FindControl<Button>("ApplyCalibrationButton");

            // 校准按钮事件绑定
            var calibrationToggleButton = this.FindControl<Button>("CalibrationToggleButton");
            var captureMinButton = this.FindControl<Button>("CaptureMinButton");
            var captureMaxButton = this.FindControl<Button>("CaptureMaxButton");
            var calibrationResetButton = this.FindControl<Button>("CalibrationResetButton");

            if (calibrationToggleButton != null)
                calibrationToggleButton.Click += OnCalibrationToggleClick;
            if (captureMinButton != null)
                captureMinButton.Click += OnCaptureMinClick;
            if (captureMaxButton != null)
                captureMaxButton.Click += OnCaptureMaxClick;
            if (calibrationResetButton != null)
                calibrationResetButton.Click += OnCalibrationResetClick;
            if (_applyCalibrationButton != null)
                _applyCalibrationButton.Click += OnApplyCalibrationClick;

            // 目标范围变化时重新计算
            if (_targetMinNumeric != null)
                _targetMinNumeric.ValueChanged += OnTargetRangeChanged;
            if (_targetMaxNumeric != null)
                _targetMaxNumeric.ValueChanged += OnTargetRangeChanged;

            // 设置控件初始值
            if (_keyNameTextBox != null && Key != null)
            {
                _keyNameTextBox.Text = Key.Name;
            }

            if (_offsetNumeric != null)
            {
                _offsetNumeric.Value = SafeFloatToDecimal(_originalOffset);
                _offsetNumeric.ValueChanged += OnConfigChanged;
            }

            if (_scaleNumeric != null)
            {
                _scaleNumeric.Value = SafeFloatToDecimal(_originalScale);
                _scaleNumeric.ValueChanged += OnConfigChanged;
            }

            if (_minNumeric != null)
            {
                bool minIsUnlimited = IsUnlimitedValue(_originalMin, isMin: true);
                if (_minUnlimitedCheckBox != null)
                {
                    _minUnlimitedCheckBox.IsChecked = minIsUnlimited;
                    _minUnlimitedCheckBox.Click += OnMinUnlimitedChanged;
                }
                _minNumeric.IsEnabled = !minIsUnlimited;
                _minNumeric.Value = minIsUnlimited ? -1m : SafeFloatToDecimal(_originalMin);
                _minNumeric.ValueChanged += OnConfigChanged;
            }

            if (_maxNumeric != null)
            {
                bool maxIsUnlimited = IsUnlimitedValue(_originalMax, isMin: false);
                if (_maxUnlimitedCheckBox != null)
                {
                    _maxUnlimitedCheckBox.IsChecked = maxIsUnlimited;
                    _maxUnlimitedCheckBox.Click += OnMaxUnlimitedChanged;
                }
                _maxNumeric.IsEnabled = !maxIsUnlimited;
                _maxNumeric.Value = maxIsUnlimited ? 1m : SafeFloatToDecimal(_originalMax);
                _maxNumeric.ValueChanged += OnConfigChanged;
            }

            if (_deadZoneNumeric != null)
            {
                _deadZoneNumeric.Value = SafeFloatToDecimal(_originalDeadZone);
                _deadZoneNumeric.ValueChanged += OnConfigChanged;
            }

            if (_sensitivityNumeric != null)
            {
                _sensitivityNumeric.Value = SafeFloatToDecimal(_originalSensitivity);
                _sensitivityNumeric.ValueChanged += OnConfigChanged;
            }

            if (_exponentNumeric != null)
            {
                _exponentNumeric.Value = SafeFloatToDecimal(_originalExponent);
                _exponentNumeric.ValueChanged += OnConfigChanged;
            }

            if (_invertCheckBox != null)
            {
                _invertCheckBox.IsChecked = _originalInvert;
                _invertCheckBox.Click += OnCheckBoxClicked;
            }

            if (_invertEventCheckBox != null)
            {
                _invertEventCheckBox.IsChecked = _originalInvertEvent;
                _invertEventCheckBox.Click += OnCheckBoxClicked;
            }

            // 隐藏/显示删除按钮
            var deleteButton = this.FindControl<Button>("DeleteButton");
            if (deleteButton != null)
            {
                deleteButton.IsVisible = !_isNewKey;
            }
        }

        private void OnConfigChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            ApplyCurrentConfig();
        }

        private void OnCheckBoxClicked(object? sender, RoutedEventArgs e)
        {
            ApplyCurrentConfig();
        }

        private void OnMinUnlimitedChanged(object? sender, RoutedEventArgs e)
        {
            if (_minNumeric != null && _minUnlimitedCheckBox != null)
            {
                bool isUnlimited = _minUnlimitedCheckBox.IsChecked ?? false;
                _minNumeric.IsEnabled = !isUnlimited;
                if (isUnlimited)
                {
                    _minNumeric.Value = -1m;
                }
            }
            ApplyCurrentConfig();
        }

        private void OnMaxUnlimitedChanged(object? sender, RoutedEventArgs e)
        {
            if (_maxNumeric != null && _maxUnlimitedCheckBox != null)
            {
                bool isUnlimited = _maxUnlimitedCheckBox.IsChecked ?? false;
                _maxNumeric.IsEnabled = !isUnlimited;
                if (isUnlimited)
                {
                    _maxNumeric.Value = 1m;
                }
            }
            ApplyCurrentConfig();
        }

        private void OnResetClick(object? sender, RoutedEventArgs e)
        {
            // 重置为默认值
            _isInitializing = true;

            if (_offsetNumeric != null)
                _offsetNumeric.Value = 0m;
            if (_scaleNumeric != null)
                _scaleNumeric.Value = 1m;
            if (_deadZoneNumeric != null)
                _deadZoneNumeric.Value = 0m;
            if (_sensitivityNumeric != null)
                _sensitivityNumeric.Value = 1m;
            if (_exponentNumeric != null)
                _exponentNumeric.Value = 1m;
            if (_invertCheckBox != null)
                _invertCheckBox.IsChecked = false;
            if (_invertEventCheckBox != null)
                _invertEventCheckBox.IsChecked = false;

            // 重置 Min/Max 为无限制
            if (_minUnlimitedCheckBox != null)
            {
                _minUnlimitedCheckBox.IsChecked = true;
                if (_minNumeric != null)
                {
                    _minNumeric.IsEnabled = false;
                    _minNumeric.Value = -1m;
                }
            }
            if (_maxUnlimitedCheckBox != null)
            {
                _maxUnlimitedCheckBox.IsChecked = true;
                if (_maxNumeric != null)
                {
                    _maxNumeric.IsEnabled = false;
                    _maxNumeric.Value = 1m;
                }
            }

            _isInitializing = false;
            ApplyCurrentConfig();
        }

        private void ApplyCurrentConfig()
        {
            // 初始化过程中不处理
            if (_isInitializing)
            {
                return;
            }

            var key = Key;
            if (key == null)
                return;

            // 更新 Key 名称
            if (_keyNameTextBox != null && !string.IsNullOrWhiteSpace(_keyNameTextBox.Text))
            {
                key.Name = _keyNameTextBox.Text;
            }

            // 更新Key对象的属性
            if (_offsetNumeric != null)
                key.Offset = (float)(_offsetNumeric.Value ?? 0);
            if (_scaleNumeric != null)
                key.Scale = (float)(_scaleNumeric.Value ?? 1);

            // Min: 如果勾选了无限制，使用 -FLT_MAX
            if (_minNumeric != null)
            {
                bool minUnlimited = _minUnlimitedCheckBox?.IsChecked ?? false;
                key.Min = minUnlimited ? -3.40282e+38f : (float)(_minNumeric.Value ?? -1);
            }
            // Max: 如果勾选了无限制，使用 FLT_MAX
            if (_maxNumeric != null)
            {
                bool maxUnlimited = _maxUnlimitedCheckBox?.IsChecked ?? false;
                key.Max = maxUnlimited ? 3.40282e+38f : (float)(_maxNumeric.Value ?? 1);
            }
            if (_deadZoneNumeric != null)
                key.DeadZone = (float)(_deadZoneNumeric.Value ?? 0);
            if (_sensitivityNumeric != null)
                key.Sensitivity = (float)(_sensitivityNumeric.Value ?? 1);
            if (_exponentNumeric != null)
                key.Exponent = (float)(_exponentNumeric.Value ?? 1);
            if (_invertCheckBox != null)
                key.InvertBool = _invertCheckBox.IsChecked ?? false;
            if (_invertEventCheckBox != null)
                key.InvertEventBool = _invertEventCheckBox.IsChecked ?? false;

            // 调用 IODevice 的 SetPKProps
            if (!string.IsNullOrEmpty(_deviceName))
            {
                try
                {
                    var device = IODeviceController.GetIODevice(_deviceName);
                    int result = device.SetPKProps(
                        key.Name,
                        key.Offset,
                        key.Scale,
                        key.Min,
                        key.Max,
                        key.DeadZone,
                        key.Sensitivity,
                        key.Exponent,
                        key.InvertBool,
                        key.InvertEventBool
                    );

                    Debug.WriteLine($"SetPKProps result: {result} for key: {key.Name}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SetPKProps failed: {ex.Message}");
                }
            }

            // 更新处理后的值显示
            UpdateProcessedValue();
        }

        private void UpdateProcessedValue()
        {
            // 直接调用实时更新方法
            UpdateRawAndProcessedValues();
        }

        /// <summary>
        /// 客户端模拟处理逻辑，与 C++ MassageKeyRawInput 保持一致
        /// 处理流程：RawValue → +Offset → *Scale → DeadZone → Exponent → *Sensitivity → Clamp → Invert → Output
        /// </summary>
        private float SimulateProcessing(float rawValue, ViewKey key)
        {
            float cur = rawValue;

            // Step 1: Apply Offset (偏移)
            cur += key.Offset;

            // Step 2: Apply Scale (缩放)
            cur *= key.Scale;

            // Step 3: Apply DeadZone (死区)
            if (key.DeadZone > 0)
            {
                float absValue = Math.Abs(cur);
                if (absValue <= key.DeadZone)
                {
                    cur = 0;
                }
                else
                {
                    // 将死区外的值重新映射到 0-1 范围
                    // 公式: (|value| - deadZone) / (1 - deadZone) * sign(value)
                    float denominator = 1.0f - key.DeadZone;
                    if (denominator > 0.001f) // 防止除以零
                    {
                        float sign = cur >= 0 ? 1.0f : -1.0f;
                        cur = ((absValue - key.DeadZone) / denominator) * sign;
                    }
                }
            }

            // Step 4: Apply Exponent (指数曲线)
            if (Math.Abs(key.Exponent - 1.0f) > 0.001f)
            {
                // 使用修正后的公式: sign(cur) * pow(|cur|, exponent)
                float sign = cur >= 0 ? 1.0f : -1.0f;
                float absValue = Math.Abs(cur);
                cur = sign * (float)Math.Pow(absValue, key.Exponent);
            }

            // Step 5: Apply Sensitivity (灵敏度)
            cur *= key.Sensitivity;

            // Step 6: Apply Clamp (限制范围)
            cur = Math.Clamp(cur, key.Min, key.Max);

            // Step 7: Apply Invert (反转)
            if (key.InvertBool)
            {
                cur *= -1;
            }

            return cur;
        }

        public void SetInputValue(float value)
        {
            if (_inputValueText != null)
            {
                _inputValueText.Text = value.ToString("F4");
            }
            UpdateProcessedValue();
        }

        private async void OnSaveClick(object? sender, RoutedEventArgs e)
        {
            if (Key == null)
                return;

            // 确保所有值已更新到 Key 对象
            ApplyCurrentConfig();

            // 验证键名
            var keyName = _keyNameTextBox?.Text;
            var validationError = KeyNameValidator.Validate(
                keyName,
                _existingKeyNames,
                _originalKeyName
            );
            if (validationError != null)
            {
                // 显示错误提示
                var messageBox = new Window
                {
                    Title = "键名验证失败",
                    Width = 400,
                    Height = 180,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false
                };

                var panel = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 16 };
                panel.Children.Add(
                    new TextBlock
                    {
                        Text = validationError,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        FontSize = 13
                    }
                );

                var okButton = new Button
                {
                    Content = "确定",
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Padding = new Avalonia.Thickness(24, 8)
                };
                okButton.Click += (s, args) => messageBox.Close();
                panel.Children.Add(okButton);

                messageBox.Content = panel;
                await messageBox.ShowDialog(this);
                return;
            }

            IsConfirmed = true;
            Close(Key);
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e)
        {
            // 恢复原始配置
            RestoreOriginalConfig();
            IsConfirmed = false;
            Close(null);
        }

        private void OnDeleteClick(object? sender, RoutedEventArgs e)
        {
            DeleteRequested = true;
            RestoreOriginalConfig();
            Close(null);
        }

        private void RestoreOriginalConfig()
        {
            if (Key == null || string.IsNullOrEmpty(_deviceName))
                return;

            try
            {
                var device = IODeviceController.GetIODevice(_deviceName);
                int result = device.SetPKProps(
                    Key.Name,
                    _originalOffset,
                    _originalScale,
                    _originalMin,
                    _originalMax,
                    _originalDeadZone,
                    _originalSensitivity,
                    _originalExponent,
                    _originalInvert,
                    _originalInvertEvent
                );

                Debug.WriteLine($"RestoreOriginalConfig result: {result} for key: {Key.Name}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RestoreOriginalConfig failed: {ex.Message}");
            }
        }

        #region 校准相关方法

        /// <summary>
        /// 展开/折叠校准面板
        /// </summary>
        private void OnCalibrationToggleClick(object? sender, RoutedEventArgs e)
        {
            _isCalibrationExpanded = !_isCalibrationExpanded;

            if (_calibrationContent != null)
            {
                _calibrationContent.IsVisible = _isCalibrationExpanded;
            }

            if (_calibrationToggleIcon != null)
            {
                _calibrationToggleIcon.Text = _isCalibrationExpanded ? "▼" : "▶";
            }
        }

        /// <summary>
        /// 采集最小值
        /// </summary>
        private void OnCaptureMinClick(object? sender, RoutedEventArgs e)
        {
            _calibrationRawMin = _currentRawValueForCalibration;

            if (_capturedMinValueText != null)
            {
                _capturedMinValueText.Text = _calibrationRawMin.Value.ToString("F4");
            }

            TryCalculateCalibration();
        }

        /// <summary>
        /// 采集最大值
        /// </summary>
        private void OnCaptureMaxClick(object? sender, RoutedEventArgs e)
        {
            _calibrationRawMax = _currentRawValueForCalibration;

            if (_capturedMaxValueText != null)
            {
                _capturedMaxValueText.Text = _calibrationRawMax.Value.ToString("F4");
            }

            TryCalculateCalibration();
        }

        /// <summary>
        /// 重置校准采集
        /// </summary>
        private void OnCalibrationResetClick(object? sender, RoutedEventArgs e)
        {
            _calibrationRawMin = null;
            _calibrationRawMax = null;

            if (_capturedMinValueText != null)
            {
                _capturedMinValueText.Text = "未采集";
            }

            if (_capturedMaxValueText != null)
            {
                _capturedMaxValueText.Text = "未采集";
            }

            if (_calibrationResultPanel != null)
            {
                _calibrationResultPanel.IsVisible = false;
            }

            if (_applyCalibrationButton != null)
            {
                _applyCalibrationButton.IsEnabled = false;
            }
        }

        /// <summary>
        /// 目标范围变化时重新计算
        /// </summary>
        private void OnTargetRangeChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            TryCalculateCalibration();
        }

        /// <summary>
        /// 尝试计算校准参数
        /// </summary>
        private void TryCalculateCalibration()
        {
            // 必须同时采集了最小值和最大值
            if (_calibrationRawMin == null || _calibrationRawMax == null)
            {
                if (_calibrationResultPanel != null)
                {
                    _calibrationResultPanel.IsVisible = false;
                }
                if (_applyCalibrationButton != null)
                {
                    _applyCalibrationButton.IsEnabled = false;
                }
                return;
            }

            float rawMin = _calibrationRawMin.Value;
            float rawMax = _calibrationRawMax.Value;
            float targetMin = (float)(_targetMinNumeric?.Value ?? -1m);
            float targetMax = (float)(_targetMaxNumeric?.Value ?? 1m);

            // 计算参数
            var (offset, scale) = CalculateCalibrationParams(rawMin, rawMax, targetMin, targetMax);

            // 显示结果
            if (_calculatedOffsetText != null)
            {
                _calculatedOffsetText.Text = offset.ToString("F6");
            }

            if (_calculatedScaleText != null)
            {
                _calculatedScaleText.Text = scale.ToString("F6");
            }

            if (_calibrationResultPanel != null)
            {
                _calibrationResultPanel.IsVisible = true;
            }

            if (_applyCalibrationButton != null)
            {
                _applyCalibrationButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// 计算校准参数（Offset 和 Scale）
        /// 将原始值范围 [rawMin, rawMax] 映射到目标范围 [targetMin, targetMax]
        /// 公式: Output = (RawValue + Offset) × Scale
        /// </summary>
        private static (float Offset, float Scale) CalculateCalibrationParams(
            float rawMin,
            float rawMax,
            float targetMin = -1f,
            float targetMax = 1f
        )
        {
            float range = rawMax - rawMin;
            if (Math.Abs(range) < 0.0001f)
            {
                // 避免除以零
                return (0f, 1f);
            }

            float scale = (targetMax - targetMin) / range;
            float offset = (targetMin / scale) - rawMin;

            return (offset, scale);
        }

        /// <summary>
        /// 应用计算结果到 Offset 和 Scale 控件
        /// </summary>
        private void OnApplyCalibrationClick(object? sender, RoutedEventArgs e)
        {
            if (_calibrationRawMin == null || _calibrationRawMax == null)
                return;

            float rawMin = _calibrationRawMin.Value;
            float rawMax = _calibrationRawMax.Value;
            float targetMin = (float)(_targetMinNumeric?.Value ?? -1m);
            float targetMax = (float)(_targetMaxNumeric?.Value ?? 1m);

            var (offset, scale) = CalculateCalibrationParams(rawMin, rawMax, targetMin, targetMax);

            // 应用到控件
            if (_offsetNumeric != null)
            {
                _offsetNumeric.Value = SafeFloatToDecimal(offset);
            }

            if (_scaleNumeric != null)
            {
                _scaleNumeric.Value = SafeFloatToDecimal(scale);
            }

            // 自动折叠校准面板
            _isCalibrationExpanded = false;
            if (_calibrationContent != null)
            {
                _calibrationContent.IsVisible = false;
            }
            if (_calibrationToggleIcon != null)
            {
                _calibrationToggleIcon.Text = "▶";
            }
        }

        #endregion
    }
}
