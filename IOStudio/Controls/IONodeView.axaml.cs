using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ReactiveUI.Avalonia;
using IOStudio.ViewModels;
using IOStudio.Views;
using IOStudio.Services;
using ReactiveUI;
using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace IOStudio.Controls;

public partial class IONodeView : ReactiveUserControl<IONodeBase>
{
    public IONodeView()
        : base()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            // 订阅 OnEditKey 事件，显示编辑对话框
            if (this.ViewModel != null)
            {
                disposables.Add(
                    this.ViewModel.OnEditKey
                        .ObserveOn(RxApp.MainThreadScheduler)
                        .Subscribe(
                            async (Key key) =>
                            {
                                key.IsEditing = true; // 设置为编辑状态
                                bool isNewKey = key.IsNewKey;

                                var dialog = new KeyEditDialog();
                                dialog.SetKey(key, this.ViewModel);

                                var window = TopLevel.GetTopLevel(this) as Window;
                                if (window != null)
                                {
                                    await dialog.ShowDialog(window);

                                    // 如果是新添加的Key且用户取消了，则移除该Key
                                    if (isNewKey && !dialog.IsConfirmed && this.ViewModel != null)
                                    {
                                        this.ViewModel.Keys.Remove(key);
                                        this.ViewModel.KeyList.Remove(key);
                                    }
                                    else if (dialog.IsConfirmed)
                                    {
                                        // 确认后清除新Key标记
                                        key.IsNewKey = false;
                                        // 保存配置到文件
                                        IORoot.Instance.Save();
                                    }
                                }

                                key.IsEditing = false; // 关闭对话框后恢复状态
                            }
                        )
                );
            }

            // 添加编辑节点按钮点击事件
            var editNodeButton = this.FindControl<Button>("EditNodeButton");
            if (editNodeButton != null)
            {
                editNodeButton.Click += EditNodeButton_Click;
            }

            // 添加录制按钮点击事件
            var recordKeyButton = this.FindControl<Button>("RecordKeyButton");
            if (recordKeyButton != null)
            {
                recordKeyButton.Click += RecordKeyButton_Click;
            }
        });
    }

    private void RecordKeyButton_Click(object? sender, RoutedEventArgs e)
    {
        if (this.ViewModel is IOStudio.ViewModels.Action action)
        {
            // 查找包含当前节点的设备
            Device? parentDevice = FindParentDevice(this.ViewModel);

            if (parentDevice != null)
            {
                // 切换录制状态
                action.IsRecording = !action.IsRecording;

                if (action.IsRecording)
                {
                    RecordingManager.Instance.StartRecording(parentDevice, action);
                }
                else
                {
                    RecordingManager.Instance.StopRecording();
                }
            }
        }
    }

    private Device? FindParentDevice(IONodeBase node)
    {
        foreach (var device in IORoot.Instance.Devices)
        {
            if (
                (node is IOStudio.ViewModels.Action && device.ActionList.Any(a => a == node))
                || (node is Axis && device.AxisList.Any(ax => ax == node))
                || (node is OAction && device.OActionList.Any(o => o == node))
            )
            {
                return device;
            }
        }
        return null;
    }

    private async void EditNodeButton_Click(object? sender, RoutedEventArgs e)
    {
        if (this.ViewModel != null && this.ViewModel.IsEditMode)
        {
            // 查找包含当前节点的设备
            Device? parentDevice = FindParentDevice(this.ViewModel);

            var editWindow = new IONodeEditWindow();
            editWindow.SetNode(this.ViewModel, parentDevice);

            var window = TopLevel.GetTopLevel(this) as Window;
            if (window != null)
            {
                await editWindow.ShowDialog(window);
            }
        }
    }

    // P1: 双击节点标题行 → 打开编辑窗口 (与 Key 双击编辑深度对齐)
    private async void OnNodeHeaderDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (this.ViewModel != null && this.ViewModel.IsEditMode)
        {
            Device? parentDevice = FindParentDevice(this.ViewModel);
            var editWindow = new IONodeEditWindow();
            editWindow.SetNode(this.ViewModel, parentDevice);

            var window = TopLevel.GetTopLevel(this) as Window;
            if (window != null)
            {
                await editWindow.ShowDialog(window);
            }
            e.Handled = true;
        }
    }
}
