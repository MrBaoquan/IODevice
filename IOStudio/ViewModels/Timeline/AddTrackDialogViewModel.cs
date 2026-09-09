using System;
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

        // ── Idle 相位模式选项 ──
        public static readonly string[] IdlePhaseModeOptions = { "continuous", "restart" };
        public static readonly string[] IdlePhaseModeDisplayNames =
        {
            "连续 (跨空窗连贯同步)",
            "重置 (每个空窗从头播放)"
        };

        // Idle 插值选项 (行内下拉)
        public static readonly string[] IdleInterpolationOptions =
        {
            "bezier",
            "linear",
            "step",
            "ease_in_out"
        };
        public static readonly string[] IdleInterpolationDisplayNames =
        {
            "贝塞尔",
            "线性",
            "阶梯",
            "缓入缓出"
        };

        // ── 内部数据 ──
        private readonly List<DeviceSchemaInfo> _devices;

        // ── Idle 循环配置 ──
        private bool _idleEnabled;
        private double _idlePeriodMs = 1600.0;
        private double _idleBlendMs = 300.0;
        private int _idlePhaseModeIndex;
        private double _idlePhaseOffsetMs;
        private string? _idleGroupId;
        private readonly ObservableCollection<IdleKeyframeItem> _idleKeyframes = new();

        // ── 属性 (ReactiveUI) ──
        private string? _selectedDeviceName;
        private int _selectedOutputTypeIndex;
        private string? _selectedOutputDisplay;
        private string _label = "";
        private string _selectedColor = "#4FC3F7";
        private int _selectedValueTypeIndex;
        private bool _isEditMode;
        private string _dialogTitle = "添加轨道";
        private string _confirmButtonText = "✓ 确定添加";

        /// <summary>是否为编辑模式 (true=编辑已有轨道, false=新建)</summary>
        public bool IsEditMode
        {
            get => _isEditMode;
            private set => this.RaiseAndSetIfChanged(ref _isEditMode, value);
        }

        /// <summary>对话框标题文本 (根据模式自动切换)</summary>
        public string DialogTitle
        {
            get => _dialogTitle;
            private set => this.RaiseAndSetIfChanged(ref _dialogTitle, value);
        }

        /// <summary>确认按钮文本 (根据模式自动切换)</summary>
        public string ConfirmButtonText
        {
            get => _confirmButtonText;
            private set => this.RaiseAndSetIfChanged(ref _confirmButtonText, value);
        }

        /// <summary>设备名称列表 (ComboBox ItemsSource)。</summary>
        public ObservableCollection<string> DeviceNames { get; } = new();

        /// <summary>当前输出列表 (根据设备+输出类型过滤)。</summary>
        public ObservableCollection<string> OutputItems { get; } = new();

        public bool HasOutputItems => OutputItems.Count > 0;

        public string OutputEmptyMessage =>
            string.IsNullOrEmpty(SelectedDeviceName)
                ? "请先选择目标设备"
                : $"设备“{SelectedDeviceName}”没有可用的 {SelectedOutputTypeDisplayName}，请切换设备或输出类型";

        public string SelectedOutputTypeDisplayName =>
            SelectedOutputType == "oaxis" ? "OAxis 输出通道" : "OAction 输出动作";

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

        // ── Idle 循环配置属性 ──

        /// <summary>是否启用 Idle 循环 (空窗期填充待机循环动作)。</summary>
        public bool IdleEnabled
        {
            get => _idleEnabled;
            set => this.RaiseAndSetIfChanged(ref _idleEnabled, value);
        }

        /// <summary>Idle 循环周期 (毫秒)。</summary>
        public double IdlePeriodMs
        {
            get => _idlePeriodMs;
            set
            {
                this.RaiseAndSetIfChanged(ref _idlePeriodMs, value);
                this.RaisePropertyChanged(nameof(IdlePeriodSeconds));
            }
        }

        /// <summary>周期 (秒, 显示友好)。</summary>
        public double IdlePeriodSeconds => IdlePeriodMs / 1000.0;

        /// <summary>与相邻 clip 的交叉淡化窗口 (毫秒)。</summary>
        public double IdleBlendMs
        {
            get => _idleBlendMs;
            set => this.RaiseAndSetIfChanged(ref _idleBlendMs, value);
        }

        /// <summary>Idle 相位模式索引 (0=continuous, 1=restart)。</summary>
        public int IdlePhaseModeIndex
        {
            get => _idlePhaseModeIndex;
            set => this.RaiseAndSetIfChanged(ref _idlePhaseModeIndex, value);
        }

        /// <summary>Idle 相位偏移 (毫秒) — 同组多轨错相编排。</summary>
        public double IdlePhaseOffsetMs
        {
            get => _idlePhaseOffsetMs;
            set => this.RaiseAndSetIfChanged(ref _idlePhaseOffsetMs, value);
        }

        /// <summary>Idle 节奏组标识 — 同组多轨 continuous 下共享全局时钟同步 (空=独立)。</summary>
        public string? IdleGroupId
        {
            get => _idleGroupId;
            set => this.RaiseAndSetIfChanged(ref _idleGroupId, value);
        }

        /// <summary>Idle 单周期关键帧列表 (行编辑模型)。</summary>
        public ObservableCollection<IdleKeyframeItem> IdleKeyframes => _idleKeyframes;

        // ── 命令 ──

        /// <summary>确认添加 — 构建 <see cref="AddTrackResult"/> 并关闭对话框。</summary>
        public ReactiveCommand<Unit, AddTrackResult?> ConfirmCommand { get; }

        /// <summary>取消 — 返回 null 结果并关闭对话框。</summary>
        public ReactiveCommand<Unit, AddTrackResult?> CancelCommand { get; }

        /// <summary>添加 idle 关键帧行。</summary>
        public ReactiveCommand<Unit, Unit> AddIdleKeyframeCommand { get; }

        /// <summary>移除 idle 关键帧行。</summary>
        public ReactiveCommand<IdleKeyframeItem, Unit> RemoveIdleKeyframeCommand { get; }

        /// <summary>应用待机模板 (breathing/sway/micro) 一键填充 idle 关键帧。</summary>
        public ReactiveCommand<string, Unit> ApplyIdleTemplateCommand { get; }

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

            // Idle 关键帧行编辑命令
            AddIdleKeyframeCommand = ReactiveCommand.Create(() =>
            {
                double t =
                    IdleKeyframes.Count > 0 ? IdleKeyframes[^1].TimeMs + IdlePeriodMs / 4 : 0;
                IdleKeyframes.Add(
                    new IdleKeyframeItem
                    {
                        TimeMs = Math.Min(t, IdlePeriodMs),
                        Value = 0.5f,
                        InterpolationIndex = 0
                    }
                );
                return Unit.Default;
            });

            RemoveIdleKeyframeCommand = ReactiveCommand.Create<IdleKeyframeItem, Unit>(item =>
            {
                IdleKeyframes.Remove(item);
                return Unit.Default;
            });

            // 待机模板一键填充 (呼吸/摇摆/微幅)
            ApplyIdleTemplateCommand = ReactiveCommand.Create<string, Unit>(template =>
            {
                ApplyIdleTemplate(template);
                return Unit.Default;
            });

            // 默认选择第一个真正提供当前输出类型的设备，避免打开对话框即进入不可提交状态。
            var firstUsable =
                _devices.FirstOrDefault(d => d.OActions.Count > 0)?.DeviceName
                ?? _devices.FirstOrDefault()?.DeviceName;
            if (firstUsable != null)
                SelectedDeviceName = firstUsable;
        }

        /// <summary>按模板填充 idle 关键帧 (覆盖现有)。</summary>
        private void ApplyIdleTemplate(string template)
        {
            _idleKeyframes.Clear();
            double period = IdlePeriodMs > 100 ? IdlePeriodMs : 1600.0;
            // 关键帧 (timeMs 相对周期, value 归一化, interp=bezier)
            (double t, float v)[] pts = template switch
            {
                // 呼吸: 中位 → 缓起缓落 (类似正弦)
                "breathing"
                    => new (double, float)[]
                    {
                        (0, 0.5f),
                        (period * 0.25, 0.62f),
                        (period * 0.55, 0.55f),
                        (period * 0.8, 0.68f),
                        (period, 0.5f)
                    },
                // 摇摆: 高 ✓ 低往还 (类似三角/缓动)
                "sway"
                    => new (double, float)[]
                    {
                        (0, 0.5f),
                        (period * 0.2, 0.75f),
                        (period * 0.5, 0.45f),
                        (period * 0.8, 0.7f),
                        (period, 0.5f)
                    },
                // 微幅: 贴近中性位的小扰动 (减少体感干扰)
                "micro"
                    => new (double, float)[]
                    {
                        (0, 0.52f),
                        (period * 0.3, 0.5f),
                        (period * 0.6, 0.54f),
                        (period, 0.52f)
                    },
                _ => new (double, float)[] { (0, 0.5f), (period, 0.5f) },
            };

            foreach (var (t, v) in pts)
            {
                _idleKeyframes.Add(
                    new IdleKeyframeItem
                    {
                        TimeMs = t,
                        Value = v,
                        InterpolationIndex = 0
                    }
                );
            }
        }

        /// <summary>
        /// 从已有 <see cref="MotionTrack"/> 加载数据, 切换到编辑模式。
        /// </summary>
        public void LoadFromTrack(MotionTrack track)
        {
            IsEditMode = true;
            DialogTitle = "编辑轨道属性";
            ConfirmButtonText = "✓ 保存修改";

            // 选中对应设备
            if (DeviceNames.Contains(track.DeviceName))
            {
                SelectedDeviceName = track.DeviceName;
            }

            // 切换输出类型
            SelectedOutputTypeIndex = track.OutputType == "oaxis" ? 1 : 0;

            // 选中对应输出项 (按 DisplayName 匹配)
            var device = _devices.FirstOrDefault(d => d.DeviceName == track.DeviceName);
            if (device != null)
            {
                if (track.OutputType == "oaxis")
                {
                    var ch = device.OAxisChannels.FirstOrDefault(
                        c => c.ChannelName == track.OAxisChannel
                    );
                    if (ch != null && OutputItems.Contains(ch.DisplayName))
                        SelectedOutputDisplay = ch.DisplayName;
                }
                else
                {
                    var oa = device.OActions.FirstOrDefault(
                        o => o.OActionName == track.OActionName
                    );
                    if (oa != null && OutputItems.Contains(oa.DisplayName))
                        SelectedOutputDisplay = oa.DisplayName;
                }
            }

            Label = track.Label;
            SelectedColor = track.Color;
            SelectedValueTypeIndex = track.ValueType == "bool" ? 1 : 0;

            // Idle 循环配置
            _idleKeyframes.Clear();
            if (track.IdleLoop is { } idle)
            {
                IdleEnabled = idle.Enabled;
                IdlePeriodMs = idle.PeriodMs;
                IdleBlendMs = idle.BlendMs;
                IdlePhaseModeIndex = idle.PhaseMode == "restart" ? 1 : 0;
                IdlePhaseOffsetMs = idle.PhaseOffsetMs;
                IdleGroupId = idle.GroupId;
                foreach (var kf in idle.Keyframes)
                {
                    int interpIdx = Math.Max(
                        0,
                        Array.IndexOf(IdleInterpolationOptions, kf.Interpolation ?? "bezier")
                    );
                    _idleKeyframes.Add(
                        new IdleKeyframeItem
                        {
                            TimeMs = kf.TimeMs,
                            Value = kf.Value,
                            InterpolationIndex = interpIdx
                        }
                    );
                }
            }
            else
            {
                IdleEnabled = false;
                IdlePeriodMs = 1600.0;
                IdleBlendMs = 300.0;
                IdlePhaseModeIndex = 0;
                IdlePhaseOffsetMs = 0;
                IdleGroupId = null;
                // 默认给一个简单的呼吸循环, 用户可在此基础上编辑
                _idleKeyframes.Add(
                    new IdleKeyframeItem
                    {
                        TimeMs = 0,
                        Value = 0.5f,
                        InterpolationIndex = 0
                    }
                );
                _idleKeyframes.Add(
                    new IdleKeyframeItem
                    {
                        TimeMs = 800,
                        Value = 0.6f,
                        InterpolationIndex = 0
                    }
                );
                _idleKeyframes.Add(
                    new IdleKeyframeItem
                    {
                        TimeMs = 1600,
                        Value = 0.5f,
                        InterpolationIndex = 0
                    }
                );
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
            this.RaisePropertyChanged(nameof(HasOutputItems));
            this.RaisePropertyChanged(nameof(OutputEmptyMessage));
            this.RaisePropertyChanged(nameof(SelectedOutputTypeDisplayName));
        }

        /// <summary>从当前选择构建 <see cref="AddTrackResult"/>。</summary>
        private AddTrackResult BuildResult()
        {
            var device = _devices.FirstOrDefault(d => d.DeviceName == SelectedDeviceName);

            IdleLoop? idle = null;
            if (IdleEnabled)
            {
                var kfs = _idleKeyframes
                    .OrderBy(k => k.TimeMs)
                    .Select(
                        k =>
                            new MotionKeyframe
                            {
                                TimeMs = Math.Clamp(k.TimeMs, 0, IdlePeriodMs),
                                Value = Math.Clamp(k.Value, 0f, 1f),
                                Interpolation = IdleInterpolationOptions[
                                    Math.Clamp(
                                        k.InterpolationIndex,
                                        0,
                                        IdleInterpolationOptions.Length - 1
                                    )
                                ]
                            }
                    )
                    .ToList();
                idle = new IdleLoop
                {
                    Enabled = true,
                    PeriodMs = Math.Max(100, IdlePeriodMs),
                    BlendMs = Math.Max(0, IdleBlendMs),
                    PhaseMode = IdlePhaseModeIndex == 1 ? "restart" : "continuous",
                    PhaseOffsetMs = IdlePhaseOffsetMs,
                    GroupId = string.IsNullOrWhiteSpace(IdleGroupId) ? null : IdleGroupId.Trim(),
                    Keyframes = kfs
                };
            }

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
                    ValueType = SelectedValueType,
                    IdleLoop = idle
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
                    ValueType = SelectedValueType,
                    IdleLoop = idle
                };
            }
        }
    }

    /// <summary>Idle 关键帧行编辑模型 (对话框表格行)。</summary>
    public class IdleKeyframeItem : ReactiveUI.ReactiveObject
    {
        private double _timeMs;
        private float _value;
        private int _interpolationIndex;

        /// <summary>时间偏移 (相对循环起始, 0 ~ PeriodMs)。</summary>
        public double TimeMs
        {
            get => _timeMs;
            set => this.RaiseAndSetIfChanged(ref _timeMs, value);
        }

        /// <summary>归一化值 (0~1)。</summary>
        public float Value
        {
            get => _value;
            set => this.RaiseAndSetIfChanged(ref _value, value);
        }

        /// <summary>插值类型索引 (对应 AddTrackDialogViewModel.IdleInterpolationOptions)。</summary>
        public int InterpolationIndex
        {
            get => _interpolationIndex;
            set => this.RaiseAndSetIfChanged(ref _interpolationIndex, value);
        }
    }
}
