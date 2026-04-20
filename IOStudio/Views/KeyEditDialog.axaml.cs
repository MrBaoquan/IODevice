using Avalonia.Controls;
using Avalonia.Interactivity;
using IOStudio.Services;
using IOStudio.ViewModels;
using System.Collections.Generic;
using System.Linq;

namespace IOStudio.Views
{
    public partial class KeyEditDialog : Window
    {
        public Key? Key { get; private set; }
        public bool IsConfirmed { get; private set; }
        private IONodeBase? _parentNode;
        private string? _originalKeyName;
        private IEnumerable<string?>? _existingKeyNames;

        public KeyEditDialog()
        {
            InitializeComponent();

            var okButton = this.FindControl<Button>("OkButton");
            var cancelButton = this.FindControl<Button>("CancelButton");

            if (okButton != null)
                okButton.Click += OkButton_Click;

            if (cancelButton != null)
                cancelButton.Click += CancelButton_Click;
        }

        public void SetKey(
            Key key,
            IONodeBase? parentNode = null,
            bool isNewKey = false,
            IEnumerable<string?>? existingKeyNames = null
        )
        {
            Key = key;
            _parentNode = parentNode;
            _originalKeyName = isNewKey ? null : key.Name;
            _existingKeyNames = existingKeyNames;

            var keyNameTextBox = this.FindControl<TextBox>("KeyNameTextBox");
            var scaleNumericUpDown = this.FindControl<NumericUpDown>("ScaleNumericUpDown");
            var invertEventCheckBox = this.FindControl<CheckBox>("InvertEventCheckBox");
            var scalePanel = this.FindControl<StackPanel>("ScalePanel");
            var invertEventPanel = this.FindControl<StackPanel>("InvertEventPanel");

            if (keyNameTextBox != null && key != null)
                keyNameTextBox.Text = key.Name;

            if (scaleNumericUpDown != null)
                scaleNumericUpDown.Value = (decimal?)(key?.Scale ?? 1.0);

            if (invertEventCheckBox != null)
                invertEventCheckBox.IsChecked = key?.InvertEvent == "True";

            // 根据节点类型显示/隐藏字段
            if (scalePanel != null && invertEventPanel != null)
            {
                var showScale = parentNode is Axis || parentNode is OAction;
                var showInvertEvent = parentNode is OAction; // InvertEvent仅在OAction中可用

                scalePanel.IsVisible = showScale;
                invertEventPanel.IsVisible = showInvertEvent;
            }
        }

        private async void OkButton_Click(object? sender, RoutedEventArgs e)
        {
            if (Key != null)
            {
                var keyNameTextBox = this.FindControl<TextBox>("KeyNameTextBox");
                var scaleNumericUpDown = this.FindControl<NumericUpDown>("ScaleNumericUpDown");
                var invertEventCheckBox = this.FindControl<CheckBox>("InvertEventCheckBox");

                if (keyNameTextBox != null && !string.IsNullOrWhiteSpace(keyNameTextBox.Text))
                {
                    var keyName = keyNameTextBox.Text;

                    // 验证键名
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

                        var panel = new StackPanel
                        {
                            Margin = new Avalonia.Thickness(20),
                            Spacing = 16
                        };
                        panel.Children.Add(
                            new TextBlock
                            {
                                Text = validationError,
                                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                                FontSize = 13
                            }
                        );

                        var okBtn = new Button
                        {
                            Content = "确定",
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            Padding = new Avalonia.Thickness(24, 8)
                        };
                        okBtn.Click += (s, args) => messageBox.Close();
                        panel.Children.Add(okBtn);

                        messageBox.Content = panel;
                        await messageBox.ShowDialog(this);
                        return;
                    }

                    Key.Name = keyName;

                    if (
                        scaleNumericUpDown != null
                        && (_parentNode is Axis || _parentNode is OAction)
                    )
                    {
                        Key.Scale = (float)scaleNumericUpDown.Value;
                    }

                    if (
                        invertEventCheckBox != null
                        && (_parentNode is IOStudio.ViewModels.Action || _parentNode is OAction)
                    )
                    {
                        Key.InvertEvent = invertEventCheckBox.IsChecked == true ? "True" : "False";
                    }

                    IsConfirmed = true;
                    Close(true);
                }
            }
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            Close(false);
        }
    }
}
