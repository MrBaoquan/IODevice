using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using IOStudio.ViewModels;
using ReactiveUI;

namespace IOStudio.ViewModels.Timeline
{
    /// <summary>
    /// AddTrackDialog 的 ViewModel — 管理设备/输出选择、标签、颜色、值类型等属性,
    /// 并通过 <see cref="ConfirmCommand"/> / <see cref="CancelCommand"/> 产生对话框结果。
    /// </summary>
    public class AddTrackDialogViewModel : ViewModelBase
    {
        // ── 颜色面板预设 ──
        public static readonly string[] ColorPalette =
        {
            "#4FC3F7",
            "#81C784",
            "#FFB74D",
            "#E57373",
            "#BA68C8",
            "#4DD0E1",
            "#F06292",
            "#AED581",
            "#7986CB",
            "#FFD54F"
        };

        // ── 输出类型选项 ──
        public static readonly string[] OutputTypeOptions = { "oaction", "oaxis" };
        public static readonly string[] OutputTypeDisplayNames = { "OAction 输出动作", "OAxis 输出通道" };

        // ── 值类型选项 ──
        public static readonly string[] ValueTypeOptions = { "float", "bool" };
        public static readonly string[] ValueTypeDisplayNames =
        {
            "Float 连续值 (0.0 ~ 1.0)",
            "Bool 开关值 (0 / 1)"
        };

        // ── 内部数据 ──
        private readonly List<DeviceSchemaInfo> _devices;

        // ── 属性 (ReactiveUI) ──
        private string? _selectedDeviceName;
        private int _selectedOutputTypeIndex;
        private string? _selectedOutputDisplay;
        private string _label = "";
        private string _selectedColor = "#4FC3F7";
        private int _selectedValueTypeIndex;

        /// <summary>设备名称列表 (ComboBox ItemsSource)。</summary>
        public ObservableCollection<string> DeviceNames { get; } = new();

        /// <summary>当前输出列表 (根据设备+输出类型过滤)。</summary>
        public ObservableCollection<string> OutputItems { get; } = new();

        /// <summary>选中的设备名称。</summary>
        public string? SelectedDeviceName
        {
            get => _selectedDeviceName;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedDeviceName, value);
                RefreshOutputList();
            }
        }

        /// <summary>输出类型选择索引 (0=oaction, 1=oaxis)。</summary>
        public int SelectedOutputTypeIndex
        {
            get => _selectedOutputTypeIndex;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedOutputTypeIndex, value);
                RefreshOutputList();
            }
        }

        /// <summary>当前选择的输出类型 tag ("oaction" | "oaxis")。</summary>
        public string SelectedOutputType =>
            _selectedOutputTypeIndex >= 0 && _selectedOutputTypeIndex < OutputTypeOptions.Length
                ? OutputTypeOptions[_selectedOutputTypeIndex]
                : "oaction";

        /// <summary>选中的输出项显示名称。</summary>
        public string? SelectedOutputDisplay
        {
            get => _selectedOutputDisplay;
            set => this.RaiseAndSetIfChanged(ref _selectedOutputDisplay, value);
        }

        /// <summary>自定义标签。</summary>
        public string Label
        {
            get => _label;
            set => this.RaiseAndSetIfChanged(ref _label, value);
        }

        /// <summary>选中的颜色 (Hex)。</summary>
        public string SelectedColor
        {
            get => _selectedColor;
            set => this.RaiseAndSetIfChanged(ref _selectedColor, value);
        }

        /// <summary>值类型选择索引 (0=float, 1=bool)。</summary>
        public int SelectedValueTypeIndex
        {
            get => _selectedValueTypeIndex;
            set => this.RaiseAndSetIfChanged(ref _selectedValueTypeIndex, value);
        }

        /// <summary>当前选择的值类型 tag ("float" | "bool")。</summary>
        public string SelectedValueType =>
            _selectedValueTypeIndex >= 0 && _selectedValueTypeIndex < ValueTypeOptions.Length
                ? ValueTypeOptions[_selectedValueTypeIndex]
                : "float";

        // ── 命令 ──

        /// <summary>确认添加 — 构建 <see cref="AddTrackResult"/> 并关闭对话框。</summary>
        public ReactiveCommand<Unit, AddTrackResult?> ConfirmCommand { get; }

        /// <summary>取消 — 返回 null 结果并关闭对话框。</summary>
        public ReactiveCommand<Unit, AddTrackResult?> CancelCommand { get; }

        /// <summary>对话框结果 — 确认后非 null, 取消后 null。</summary>
        public AddTrackResult? Result { get; private set; }

        // ── 构造 ──

        /// <summary>
        /// 初始化 ViewModel, 使用指定的设备架构服务加载设备列表。
        /// </summary>
        /// <param name="deviceSchemaService">设备架构服务 (解析 IODevice.xml)。</param>
        public AddTrackDialogViewModel(IDeviceSchemaService? deviceSchemaService = null)
        {
            var service = deviceSchemaService ?? new DeviceSchemaService();
            _devices = service.LoadDevices();

            foreach (var d in _devices)
            {
                DeviceNames.Add(d.DeviceName);
            }

            // 确认命令 — 当设备和输出均已选中时才可执行
            var canConfirm = this.WhenAnyValue(
                    x => x.SelectedDeviceName,
                    x => x.SelectedOutputDisplay,
                    (dev, output) => !string.IsNullOrEmpty(dev) && !string.IsNullOrEmpty(output)
                )
                .DistinctUntilChanged();

            ConfirmCommand = ReactiveCommand.Create<AddTrackResult?>(
                () =>
                {
                    Result = BuildResult();
                    return Result;
                },
                canConfirm
            );

            CancelCommand = ReactiveCommand.Create<AddTrackResult?>(() =>
            {
                Result = null;
                return null;
            });

            // 初始选中第一个设备
            if (DeviceNames.Count > 0)
            {
                SelectedDeviceName = DeviceNames[0];
            }
        }

        // ── 私有方法 ──

        /// <summary>根据当前设备+输出类型刷新输出列表。</summary>
        private void RefreshOutputList()
        {
            OutputItems.Clear();
            SelectedOutputDisplay = null;

            if (string.IsNullOrEmpty(SelectedDeviceName))
                return;

            var device = _devices.FirstOrDefault(d => d.DeviceName == SelectedDeviceName);
            if (device is null)
                return;

            if (SelectedOutputType == "oaxis")
            {
                foreach (var ch in device.OAxisChannels)
                {
                    OutputItems.Add(ch.DisplayName);
                }
            }
            else
            {
                foreach (var oa in device.OActions)
                {
                    OutputItems.Add(oa.DisplayName);
                }
            }

            if (OutputItems.Count > 0)
            {
                SelectedOutputDisplay = OutputItems[0];
            }
        }

        /// <summary>从当前选择构建 <see cref="AddTrackResult"/>。</summary>
        private AddTrackResult BuildResult()
        {
            var device = _devices.FirstOrDefault(d => d.DeviceName == SelectedDeviceName);

            if (SelectedOutputType == "oaxis")
            {
                var channel = device?.OAxisChannels.FirstOrDefault(
                    c => c.DisplayName == SelectedOutputDisplay
                );

                return new AddTrackResult
                {
                    DeviceName = SelectedDeviceName ?? "",
                    OActionName = "",
                    OutputType = "oaxis",
                    OAxisChannel = channel?.ChannelName ?? "OAxis_00",
                    Label = string.IsNullOrWhiteSpace(Label) ? channel?.ChannelName ?? "" : Label,
                    Color = SelectedColor,
                    ValueType = SelectedValueType
                };
            }
            else
            {
                var oaction = device?.OActions.FirstOrDefault(
                    o => o.DisplayName == SelectedOutputDisplay
                );

                return new AddTrackResult
                {
                    DeviceName = SelectedDeviceName ?? "",
                    OActionName = oaction?.OActionName ?? SelectedOutputDisplay ?? "",
                    OutputType = "oaction",
                    OAxisChannel = "",
                    Label = string.IsNullOrWhiteSpace(Label) ? oaction?.Label ?? "" : Label,
                    Color = SelectedColor,
                    ValueType = SelectedValueType
                };
            }
        }
    }
}
