using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using IOTester.Services;
using IOTester.ViewModels;
using System.Linq;

namespace IOTester.Views
{
    public partial class DevicePropertiesWindow : Window
    {
        public bool IsConfirmed { get; private set; }

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

            if (cancelButton != null)
                cancelButton.Click += CancelButton_Click;

            if (saveButton != null)
                saveButton.Click += SaveButton_Click;

            if (addPropertyKeyButton != null)
                addPropertyKeyButton.Click += AddPropertyKeyButton_Click;

            if (clearPropertyKeysButton != null)
                clearPropertyKeysButton.Click += ClearPropertyKeysButton_Click;

            // Handle edit and delete button clicks from DataTemplate
            this.AddHandler(Button.ClickEvent, OnEditPropertyKeyClick, handledEventsToo: true);
            this.AddHandler(Button.ClickEvent, OnDeletePropertyKeyClick, handledEventsToo: true);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public void SetDevice(Device device)
        {
            DataContext = device;
            if (device != null)
            {
                Title = $"设备配置 - {device.Name}";
            }
        }

        private async void OnEditPropertyKeyClick(object? sender, RoutedEventArgs e)
        {
            if (e.Source is Button button && button.Name == "EditPropertyKeyButton")
            {
                if (button.DataContext is Key key)
                {
                    var device = DataContext as Device;
                    var originalKeyName = key.Name;
                    var dialog = new PropertyKeyEditDialog();
                    dialog.SetKey(key, device?.Name);
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

                        // 立即同步到 IODevice（PropertyKeyEditDialog 中已经调用过 SetPKProps）
                        // 这里只需要保存配置文件
                        IORoot.Instance.Save();
                    }
                }
                e.Handled = true;
            }
        }

        private void OnDeletePropertyKeyClick(object? sender, RoutedEventArgs e)
        {
            if (e.Source is Button button && button.Name == "DeletePropertyKeyButton")
            {
                if (button.DataContext is Key key)
                {
                    var device = DataContext as Device;
                    if (device?.Properties != null)
                    {
                        device.Properties.KeyList.Remove(key);
                        device.Properties.Keys.Remove(key);
                        IORoot.Instance.Save();
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
            var newKey = new Key
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
            dialog.SetKey(newKey, device.Name, isNewKey: true, existingKeyNames: existingKeyNames);

            await dialog.ShowDialog(this);
            if (dialog.IsConfirmed)
            {
                // 验证已在对话框中完成，这里只需添加
                device.Properties.KeyList.Add(newKey);
                device.Properties.Keys.Add(newKey);

                // 立即同步到 IODevice（PropertyKeyEditDialog 中已经调用过 SetPKProps）
                // 这里只需要保存配置文件
                IORoot.Instance.Save();
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
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            Close();
        }

        private void SaveButton_Click(object? sender, RoutedEventArgs e)
        {
            IsConfirmed = true;
            IORoot.Instance?.Save();
            Close();
        }
    }
}
