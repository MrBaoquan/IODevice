using Avalonia.Controls;
using Avalonia.Interactivity;
using IOStudio.ViewModels;
using IOStudio.Services;
using IOToolkit;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace IOStudio.Views
{
    public partial class IONodeEditWindow : Window
    {
        private IONodeBase? _originalNode; // 原始节点引用
        private IONodeBase? _nodeToEdit; // 编辑用的副本
        private Device? _parentDevice;
        public bool IsConfirmed { get; private set; }

        public IONodeEditWindow()
        {
            InitializeComponent();
            SetupEventHandlers();
        }

        private void SetupEventHandlers()
        {
            var saveButton = this.FindControl<Button>("SaveButton");
            var cancelButton = this.FindControl<Button>("CancelButton");
            var addKeyButton = this.FindControl<Button>("AddKeyButton");
            var clearKeysButton = this.FindControl<Button>("ClearKeysButton");
            var deleteNodeButton = this.FindControl<Button>("DeleteNodeButton");
            var recordKeyButton = this.FindControl<Button>("RecordKeyButton");

            // 输入时清除名称错误提示
            var nodeNameBox = this.FindControl<TextBox>("NodeNameTextBox");
            if (nodeNameBox != null)
            {
                nodeNameBox.TextChanged += (_, _) =>
                {
                    var err = this.FindControl<TextBlock>("NodeNameErrorText");
                    if (err != null)
                        err.IsVisible = false;
                };
            }

            if (saveButton != null)
                saveButton.Click += SaveButton_Click;

            if (cancelButton != null)
                cancelButton.Click += CancelButton_Click;

            if (addKeyButton != null)
                addKeyButton.Click += AddKeyButton_Click;

            if (clearKeysButton != null)
                clearKeysButton.Click += ClearKeysButton_Click;

            if (deleteNodeButton != null)
                deleteNodeButton.Click += DeleteNodeButton_Click;

            if (recordKeyButton != null)
                recordKeyButton.Click += RecordKeyButton_Click;

            // 处理ItemsControl中的删除按钮点击
            this.AddHandler(Button.ClickEvent, OnDeleteKeyButtonClick, handledEventsToo: true);
        }

        private async void OnDeleteKeyButtonClick(object? sender, RoutedEventArgs e)
        {
            if (
                e.Source is Button button
                && button.Name == "DeleteKeyButton"
                && button.DataContext is ViewModels.Key key
            )
            {
                if (_nodeToEdit != null)
                {
                    var result = await ShowConfirmDialog("确认删除", $"确定要删除按键 '{key.Name}' 吗？");
                    if (result)
                    {
                        _nodeToEdit.Keys.Remove(key);
                        _nodeToEdit.KeyList.Remove(key);
                        _nodeToEdit.RebuildActiveSubscription();
                    }
                }
                e.Handled = true;
            }
        }

        private void DeleteNodeButton_Click(object? sender, RoutedEventArgs e)
        {
            // 删除节点时使用原始节点引用
            if (
                _originalNode?.DeleteNodeCommand != null
                && _originalNode.DeleteNodeCommand.CanExecute(_originalNode)
            )
            {
                // 确认已在 DeleteNodeCommand (统一走 IDialogService) 内完成, 此处直接触发
                _originalNode.DeleteNodeCommand.Execute(_originalNode);
                Close();
            }
        }

        private void RecordKeyButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_nodeToEdit is IOStudio.ViewModels.Action action && _parentDevice != null)
            {
                // 切换录制状态
                action.IsRecording = !action.IsRecording;

                if (action.IsRecording)
                {
                    RecordingManager.Instance.StartRecording(_parentDevice, action);
                }
                else
                {
                    RecordingManager.Instance.StopRecording();
                }
            }
        }

        public void SetNode(IONodeBase node, Device? device = null, bool isNew = false)
        {
            _originalNode = node;
            _parentDevice = device;

            // 创建副本用于编辑，避免实时同步到主窗口
            if (isNew)
            {
                // 新建节点直接使用原对象
                _nodeToEdit = node;
            }
            else
            {
                // 编辑现有节点使用副本
                _nodeToEdit = node.Clone();
            }

            DataContext = _nodeToEdit;

            // 设置窗口标题
            var nodeType = node switch
            {
                IOStudio.ViewModels.Action => "Action 输入",
                Axis => "Axis 输入",
                OAction => "OAction 输出",
                _ => "节点"
            };
            var action = isNew ? "新增" : "编辑";
            Title = $"{action}节点 - {nodeType}";
        }

        private async void AddKeyButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_nodeToEdit == null)
                return;

            // 获取现有键名列表
            var existingKeyNames = _nodeToEdit.Keys.Select(k => k.Name);

            var dialog = new KeyEditDialog();

            // 根据节点类型创建默认 Key
            var newKey = new ViewModels.Key
            {
                Name = GetDefaultKeyName(),
                Scale = 1.0f,
                InvertEvent = "False"
            };

            dialog.SetKey(newKey, _nodeToEdit, isNewKey: true, existingKeyNames: existingKeyNames);

            var result = await dialog.ShowDialog<bool>(this);

            if (result && newKey != null && !string.IsNullOrWhiteSpace(newKey.Name))
            {
                // 验证已在对话框中完成
                _nodeToEdit.Keys.Add(newKey);
                _nodeToEdit.KeyList.Add(newKey);
                _nodeToEdit.RebuildActiveSubscription();
            }
        }

        private string GetDefaultKeyName()
        {
            if (_nodeToEdit == null)
                return "Axis_00";

            var prefix = _nodeToEdit switch
            {
                IOStudio.ViewModels.Action => "Button",
                Axis => "Axis",
                OAction => "OAxis",
                _ => "Button"
            };

            var existingKeyNames = _nodeToEdit.Keys.Select(k => k.Name);
            return KeyNameValidator.GenerateUniqueKeyName(prefix, existingKeyNames);
        }

        private async void ClearKeysButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_nodeToEdit == null)
                return;

            var result = await ShowConfirmDialog("确认清空", $"确定要清空节点 '{_nodeToEdit.Name}' 的所有按键吗？");
            if (result)
            {
                _nodeToEdit.Keys.Clear();
                _nodeToEdit.KeyList.Clear();
                _nodeToEdit.RebuildActiveSubscription();
            }
        }

        private void SaveButton_Click(object? sender, RoutedEventArgs e)
        {
            var nodeNameTextBox = this.FindControl<TextBox>("NodeNameTextBox");
            var nodeLabelTextBox = this.FindControl<TextBox>("NodeLabelTextBox");
            var nameErrorText = this.FindControl<TextBlock>("NodeNameErrorText");

            if (nodeNameTextBox != null && _nodeToEdit != null && _originalNode != null)
            {
                // P0-1: 空名/重名校验 — 内联错误提示 + 聚焦, 不再静默返回
                var name = nodeNameTextBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(name))
                {
                    ShowNameError("节点名称不能为空", nodeNameTextBox, nameErrorText);
                    return;
                }

                // 重名校验: 与同设备/同类型下其它节点比较 (排除自身)
                var duplicate = FindDuplicateName(name);
                if (duplicate != null)
                {
                    ShowNameError(
                        $"节点名称 \"{name}\" 已存在（{duplicate}），请换一个名称",
                        nodeNameTextBox,
                        nameErrorText
                    );
                    return;
                }

                // 更新副本的名称和标签
                _nodeToEdit.Name = name;
                _nodeToEdit.Label = nodeLabelTextBox?.Text ?? "";

                // 将副本的更改同步回原始节点
                _originalNode.CopyFrom(_nodeToEdit);

                // 同步 Key 属性到 IODevice
                SyncKeyPropsToIODevice();

                // 保存配置
                IORoot.Instance.Save();

                IsConfirmed = true;
                Close();
            }
        }

        /// <summary>校验失败: 显示内联错误并聚焦名称输入框。</summary>
        private void ShowNameError(string message, TextBox nameBox, TextBlock? errorText)
        {
            if (errorText != null)
            {
                errorText.Text = $"⚠ {message}";
                errorText.IsVisible = true;
            }
            nameBox.Focus();
            nameBox.SelectAll();
        }

        /// <summary>重名校验: 查找同设备同类型下已存在的同名节点 (排除自身), 返回其显示名或 null。</summary>
        private string? FindDuplicateName(string name)
        {
            if (_parentDevice == null || _originalNode == null)
                return null;

            // 与同设备内其它节点比较
            foreach (var n in _parentDevice.ActionList)
                if (
                    !ReferenceEquals(n, _originalNode)
                    && string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase)
                )
                    return $"{n.Name} (Action)";
            foreach (var n in _parentDevice.AxisList)
                if (
                    !ReferenceEquals(n, _originalNode)
                    && string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase)
                )
                    return $"{n.Name} (Axis)";
            foreach (var n in _parentDevice.OActionList)
                if (
                    !ReferenceEquals(n, _originalNode)
                    && string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase)
                )
                    return $"{n.Name} (OAction)";
            return null;
        }

        /// <summary>
        /// 将 Key 的 Scale 和 InvertEvent 属性同步到 IODevice
        /// </summary>
        private void SyncKeyPropsToIODevice()
        {
            if (_parentDevice == null || _originalNode == null)
                return;

            try
            {
                var ioDevice = IODeviceController.GetIODevice(_parentDevice.Name);
                if (!ioDevice.IsValid())
                    return;

                var nodeName = _originalNode.Name;

                foreach (var key in _originalNode.Keys)
                {
                    if (_originalNode is Axis)
                    {
                        // Axis: 设置 Scale
                        ioDevice.SetAKProps(nodeName, key.Name, key.Scale);
                    }
                    else if (_originalNode is OAction)
                    {
                        // OAction: 设置 Scale 和 InvertEvent
                        ioDevice.SetOKProps(nodeName, key.Name, key.Scale, key.InvertEventBool);
                    }
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SyncKeyPropsToIODevice error: {ex.Message}");
            }
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            Close();
        }

        private async Task<bool> ShowConfirmDialog(string title, string message)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 400,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            var result = false;
            var mainPanel = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 30,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            // 消息文本
            var messageText = new TextBlock
            {
                Text = message,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                FontSize = 14,
                TextAlignment = Avalonia.Media.TextAlignment.Center
            };
            mainPanel.Children.Add(messageText);

            // 按钮区 - 居中
            var buttonPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Spacing = 12
            };

            var cancelButton = new Button
            {
                Content = "取消",
                Width = 100,
                Height = 36,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            cancelButton.Click += (s, e) =>
            {
                dialog.Close();
            };

            var confirmButton = new Button
            {
                Content = "确定",
                Width = 100,
                Height = 36,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Background = new Avalonia.Media.SolidColorBrush(
                    Avalonia.Media.Color.Parse("#dc2626")
                ),
                Foreground = Avalonia.Media.Brushes.White
            };
            confirmButton.Click += (s, e) =>
            {
                result = true;
                dialog.Close();
            };

            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(confirmButton);
            mainPanel.Children.Add(buttonPanel);

            dialog.Content = mainPanel;

            await dialog.ShowDialog(this);
            return result;
        }
    }
}
