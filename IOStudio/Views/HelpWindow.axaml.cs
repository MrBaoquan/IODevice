using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using IOStudio.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace IOStudio.Views
{
    public partial class HelpWindow : Window
    {
        private TextBox? _keySearchTextBox;
        private WrapPanel? _axisKeysPanel;
        private WrapPanel? _buttonKeysPanel;
        private WrapPanel? _oAxisKeysPanel;
        private WrapPanel? _letterKeysPanel;
        private WrapPanel? _numberKeysPanel;
        private WrapPanel? _functionKeysPanel;
        private WrapPanel? _controlKeysPanel;
        private WrapPanel? _numPadKeysPanel;
        private WrapPanel? _otherKeysPanel;
        private WrapPanel? _mouseKeysPanel;
        private WrapPanel? _joystickAxisKeysPanel;
        private WrapPanel? _joystickVelocityKeysPanel;
        private WrapPanel? _joystickSliderKeysPanel;
        private WrapPanel? _dllNamesPanel;
        private TextBlock? _noDllText;

        // 所有键名面板及其键名列表
        private readonly Dictionary<WrapPanel, List<string>> _keyPanelData = new();

        public HelpWindow()
        {
            InitializeComponent();
            InitializeControls();
            PopulateKeyNames();
            PopulateDllNames();
        }

        private void InitializeControls()
        {
            var closeButton = this.FindControl<Button>("CloseButton");
            if (closeButton != null)
                closeButton.Click += (s, e) => Close();

            var refreshDllButton = this.FindControl<Button>("RefreshDllButton");
            if (refreshDllButton != null)
                refreshDllButton.Click += (s, e) => PopulateDllNames();

            _keySearchTextBox = this.FindControl<TextBox>("KeySearchTextBox");
            if (_keySearchTextBox != null)
                _keySearchTextBox.TextChanged += OnKeySearchTextChanged;

            _axisKeysPanel = this.FindControl<WrapPanel>("AxisKeysPanel");
            _buttonKeysPanel = this.FindControl<WrapPanel>("ButtonKeysPanel");
            _oAxisKeysPanel = this.FindControl<WrapPanel>("OAxisKeysPanel");
            _letterKeysPanel = this.FindControl<WrapPanel>("LetterKeysPanel");
            _numberKeysPanel = this.FindControl<WrapPanel>("NumberKeysPanel");
            _functionKeysPanel = this.FindControl<WrapPanel>("FunctionKeysPanel");
            _controlKeysPanel = this.FindControl<WrapPanel>("ControlKeysPanel");
            _numPadKeysPanel = this.FindControl<WrapPanel>("NumPadKeysPanel");
            _otherKeysPanel = this.FindControl<WrapPanel>("OtherKeysPanel");
            _mouseKeysPanel = this.FindControl<WrapPanel>("MouseKeysPanel");
            _joystickAxisKeysPanel = this.FindControl<WrapPanel>("JoystickAxisKeysPanel");
            _joystickVelocityKeysPanel = this.FindControl<WrapPanel>("JoystickVelocityKeysPanel");
            _joystickSliderKeysPanel = this.FindControl<WrapPanel>("JoystickSliderKeysPanel");
            _dllNamesPanel = this.FindControl<WrapPanel>("DllNamesPanel");
            _noDllText = this.FindControl<TextBlock>("NoDllText");
        }

        private void OnKeySearchTextChanged(object? sender, TextChangedEventArgs e)
        {
            var searchText = _keySearchTextBox?.Text?.Trim() ?? "";
            FilterKeyNames(searchText);
        }

        private void FilterKeyNames(string searchText)
        {
            foreach (var kvp in _keyPanelData)
            {
                var panel = kvp.Key;
                var allKeys = kvp.Value;

                panel.Children.Clear();

                var filteredKeys = string.IsNullOrEmpty(searchText)
                    ? allKeys
                    : allKeys
                        .Where(k => k.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                foreach (var keyName in filteredKeys)
                {
                    panel.Children.Add(CreateKeyButton(keyName));
                }
            }
        }

        private void PopulateKeyNames()
        {
            // 动态键名 (显示前8个作为示例)
            var axisKeys = Enumerable.Range(0, 8).Select(i => $"Axis_{i:D2}").ToList();
            var buttonKeys = Enumerable.Range(0, 8).Select(i => $"Button_{i:D2}").ToList();
            var oAxisKeys = Enumerable.Range(0, 8).Select(i => $"OAxis_{i:D2}").ToList();

            // 字母键
            var letterKeys = new List<string>
            {
                "A",
                "B",
                "C",
                "D",
                "E",
                "F",
                "G",
                "H",
                "I",
                "J",
                "K",
                "L",
                "M",
                "N",
                "O",
                "P",
                "Q",
                "R",
                "S",
                "T",
                "U",
                "V",
                "W",
                "X",
                "Y",
                "Z"
            };

            // 数字键
            var numberKeys = new List<string>
            {
                "Zero",
                "One",
                "Two",
                "Three",
                "Four",
                "Five",
                "Six",
                "Seven",
                "Eight",
                "Nine"
            };

            // 功能键
            var functionKeys = new List<string>
            {
                "F1",
                "F2",
                "F3",
                "F4",
                "F5",
                "F6",
                "F7",
                "F8",
                "F9",
                "F10",
                "F11",
                "F12"
            };

            // 控制键
            var controlKeys = new List<string>
            {
                "LeftShift",
                "RightShift",
                "LeftControl",
                "RightControl",
                "LeftAlt",
                "RightAlt",
                "LeftCommand",
                "RightCommand",
                "CapsLock",
                "NumLock",
                "ScrollLock"
            };

            // 小键盘
            var numPadKeys = new List<string>
            {
                "NumPadZero",
                "NumPadOne",
                "NumPadTwo",
                "NumPadThree",
                "NumPadFour",
                "NumPadFive",
                "NumPadSix",
                "NumPadSeven",
                "NumPadEight",
                "NumPadNine",
                "Multiply",
                "Add",
                "Subtract",
                "Decimal",
                "Divide"
            };

            // 其他键
            var otherKeys = new List<string>
            {
                "BackSpace",
                "Tab",
                "Enter",
                "Pause",
                "Escape",
                "SpaceBar",
                "PageUp",
                "PageDown",
                "End",
                "Home",
                "Left",
                "Up",
                "Right",
                "Down",
                "Insert",
                "Delete",
                "AnyKey",
                "Invalid"
            };

            // 鼠标
            var mouseKeys = new List<string>
            {
                "MouseX",
                "MouseY",
                "MouseScrollUp",
                "MouseScrollDown",
                "MouseWheelAxis",
                "LeftMouseButton",
                "RightMouseButton",
                "MiddleMouseButton",
                "ThumbMouseButton",
                "ThumbMouseButton2"
            };

            // 摇杆轴向
            var joystickAxisKeys = new List<string>
            {
                "JS_X",
                "JS_Y",
                "JS_Z",
                "JS_Rx",
                "JS_Ry",
                "JS_Rz"
            };

            // 摇杆速度/加速度/力
            var joystickVelocityKeys = new List<string>
            {
                "JS_VX",
                "JS_VY",
                "JS_VZ",
                "JS_VRx",
                "JS_VRy",
                "JS_VRz",
                "JS_AX",
                "JS_AY",
                "JS_AZ",
                "JS_ARx",
                "JS_ARy",
                "JS_ARz",
                "JS_FX",
                "JS_FY",
                "JS_FZ",
                "JS_FRx",
                "JS_FRy",
                "JS_FRz"
            };

            // 摇杆滑块/POV
            var joystickSliderKeys = new List<string>
            {
                "JS_Slider_00",
                "JS_Slider_01",
                "JS_VSlider_00",
                "JS_VSlider_01",
                "JS_ASlider_00",
                "JS_ASlider_01",
                "JS_FSlider_00",
                "JS_FSlider_01",
                "JS_POV_00",
                "JS_POV_01",
                "JS_POV_02",
                "JS_POV_03"
            };

            // 填充面板
            PopulatePanel(_axisKeysPanel, axisKeys);
            PopulatePanel(_buttonKeysPanel, buttonKeys);
            PopulatePanel(_oAxisKeysPanel, oAxisKeys);
            PopulatePanel(_letterKeysPanel, letterKeys);
            PopulatePanel(_numberKeysPanel, numberKeys);
            PopulatePanel(_functionKeysPanel, functionKeys);
            PopulatePanel(_controlKeysPanel, controlKeys);
            PopulatePanel(_numPadKeysPanel, numPadKeys);
            PopulatePanel(_otherKeysPanel, otherKeys);
            PopulatePanel(_mouseKeysPanel, mouseKeys);
            PopulatePanel(_joystickAxisKeysPanel, joystickAxisKeys);
            PopulatePanel(_joystickVelocityKeysPanel, joystickVelocityKeys);
            PopulatePanel(_joystickSliderKeysPanel, joystickSliderKeys);
        }

        private void PopulatePanel(WrapPanel? panel, List<string> keyNames)
        {
            if (panel == null)
                return;

            _keyPanelData[panel] = keyNames;

            foreach (var keyName in keyNames)
            {
                panel.Children.Add(CreateKeyButton(keyName));
            }
        }

        private Border CreateKeyButton(string keyName)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#f3f4f6")),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4),
                Margin = new Thickness(2),
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            var textBlock = new TextBlock
            {
                Text = keyName,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#374151"))
            };

            border.Child = textBlock;

            // 悬停效果
            border.PointerEntered += (s, e) =>
            {
                border.Background = new SolidColorBrush(Color.Parse("#dbeafe"));
            };
            border.PointerExited += (s, e) =>
            {
                border.Background = new SolidColorBrush(Color.Parse("#f3f4f6"));
            };

            // 点击复制
            border.PointerPressed += async (s, e) =>
            {
                try
                {
                    var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                    if (clipboard != null)
                    {
                        await clipboard.SetTextAsync(keyName);

                        // 显示复制成功反馈
                        var originalText = textBlock.Text;
                        textBlock.Text = "✓ 已复制";
                        textBlock.Foreground = new SolidColorBrush(Color.Parse("#16a34a"));

                        await System.Threading.Tasks.Task.Delay(800);

                        textBlock.Text = originalText;
                        textBlock.Foreground = new SolidColorBrush(Color.Parse("#374151"));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Copy to clipboard failed: {ex.Message}");
                }
            };

            return border;
        }

        private void PopulateDllNames()
        {
            if (_dllNamesPanel == null)
                return;

            _dllNamesPanel.Children.Clear();

            var dllNames = GetAvailableDllNames();

            if (dllNames.Count == 0)
            {
                if (_noDllText != null)
                    _noDllText.IsVisible = true;
                return;
            }

            if (_noDllText != null)
                _noDllText.IsVisible = false;

            foreach (var dllName in dllNames)
            {
                _dllNamesPanel.Children.Add(CreateDllButton(dllName));
            }
        }

        private List<string> GetAvailableDllNames()
        {
            var dllNames = new List<string>();

            try
            {
                // 查找可能的 ExternalLibraries 文件夹路径
                var possiblePaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExternalLibraries"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "ExternalLibraries"),
                    Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "..",
                        "..",
                        "ExternalLibraries"
                    ),
                    Path.Combine(Environment.CurrentDirectory, "ExternalLibraries"),
                    // 针对开发环境
                    Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "..",
                        "..",
                        "..",
                        "..",
                        "Binaries",
                        "Win64",
                        "ExternalLibraries"
                    ),
                    @"O:\DevModules\IODevice\IODevice\Binaries\Win64\ExternalLibraries"
                };

                foreach (var path in possiblePaths)
                {
                    if (Directory.Exists(path))
                    {
                        var pattern = new Regex(@"IOUI-Win64-(.+)\.dll$", RegexOptions.IgnoreCase);
                        var files = Directory.GetFiles(path, "IOUI-Win64-*.dll");

                        foreach (var file in files)
                        {
                            var fileName = Path.GetFileName(file);
                            var match = pattern.Match(fileName);
                            if (match.Success)
                            {
                                dllNames.Add(match.Groups[1].Value);
                            }
                        }

                        if (dllNames.Count > 0)
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetAvailableDllNames failed: {ex.Message}");
            }

            return dllNames.Distinct().OrderBy(x => x).ToList();
        }

        private Border CreateDllButton(string dllName)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#fef3c7")),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8),
                Margin = new Thickness(4),
                Cursor = new Cursor(StandardCursorType.Hand),
                BorderBrush = new SolidColorBrush(Color.Parse("#fcd34d")),
                BorderThickness = new Thickness(1)
            };

            var textBlock = new TextBlock
            {
                Text = dllName,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse("#92400e"))
            };

            border.Child = textBlock;

            // 悬停效果
            border.PointerEntered += (s, e) =>
            {
                border.Background = new SolidColorBrush(Color.Parse("#fde68a"));
            };
            border.PointerExited += (s, e) =>
            {
                border.Background = new SolidColorBrush(Color.Parse("#fef3c7"));
            };

            // 点击复制
            border.PointerPressed += async (s, e) =>
            {
                try
                {
                    var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                    if (clipboard != null)
                    {
                        await clipboard.SetTextAsync(dllName);

                        // 显示复制成功反馈
                        var originalText = textBlock.Text;
                        textBlock.Text = "✓ 已复制";
                        textBlock.Foreground = new SolidColorBrush(Color.Parse("#16a34a"));

                        await System.Threading.Tasks.Task.Delay(800);

                        textBlock.Text = originalText;
                        textBlock.Foreground = new SolidColorBrush(Color.Parse("#92400e"));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Copy to clipboard failed: {ex.Message}");
                }
            };

            return border;
        }
    }
}
