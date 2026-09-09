using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive;
using IOStudio.Models.Motion;
using IOStudio.ViewModels;
using ReactiveUI;

// 全局命名空间中存在 class Action : IONodeBase，遮蔽了 System.Action
using SysAction = System.Action;

namespace IOStudio.ViewModels.Timeline
{
    /// <summary>
    /// 检查器面板 ViewModel — 始终跟随播放头时间 + 选中轨道显示属性。
    /// DaVinci Resolve Inspector 风格: 面板内容只与播放头位置有关, 不与关键帧选中状态绑定。
    /// 支持轨道、动作实例、多选关键帧与独立事件等检查器模式。
    /// </summary>
    public class KeyframePropertyViewModel : ViewModelBase
    {
        // ── 显示模式 ──
        public enum DisplayMode
        {
            /// <summary>无轨道选中</summary>
            None,

            /// <summary>轨道检查器: 显示播放头位置的插值值/插值类型</summary>
            TrackInspector,

            /// <summary>跨轨动作实例摘要</summary>
            ActionInstance,

            /// <summary>多选关键帧: 显示批量操作</summary>
            MultiSelect,

            /// <summary>独立事件属性</summary>
            StandaloneEvent
        }

        private DisplayMode _mode = DisplayMode.None;
        private bool _isSyncing; // ShowTrackState 期间抑制编辑事件
        private bool _isAutoKeyMode;
        private bool _hasKeyframeAtPlayhead;
        private double _timeMs;
        private float _value;
        private string _interpolation = "linear";
        private float? _tangentIn;
        private float? _tangentOut;
        private float? _cp1x;
        private float? _cp2x;
        private int _clipIndex = -1;
        private int _keyframeIndex = -1;
        private string _valueType = "float";
        private string _eventName = "";
        private string? _eventData;
        private string _panelTitle = "检查器";
        private string _indexLabel = "";
        private string _eventBadge = "";
        private bool _eventNameDuplicate;
        private TimelineEvent? _displayedEvent;
        private string _trackName = "";
        private string _trackColor = "#4FC3F7";
        private int _totalKfCount;
        private string _intervalInterpolation = "";
        private TimelineEvent? _playheadEvent;
        private string _playheadEventName = "";
        private string? _playheadEventData;
        private string _playheadEventDataType = "string";
        private string _eventDataType = "string";
        private string _eventTimeText = "";
        private string _eventDataError = "";
        private string _actionName = "";
        private string _actionCategory = "";
        private string _actionTimeRangeText = "";
        private string _actionParametersText = "";
        private string _actionBindingText = "";
        private bool _actionEditLocked;

        // ── 多选统计 ──
        private int _multiSelectCount;
        private double _multiMinTime;
        private double _multiMaxTime;
        private float _multiMinValue;
        private float _multiMaxValue;
        private float _multiAvgValue;

        // ── 属性 ──

        public DisplayMode Mode
        {
            get => _mode;
            set => this.RaiseAndSetIfChanged(ref _mode, value);
        }

        public bool HasSelection => _mode != DisplayMode.None;
        public bool IsTrackInspector => _mode == DisplayMode.TrackInspector;
        public bool IsActionInstance => _mode == DisplayMode.ActionInstance;
        public bool IsMultiSelect => _mode == DisplayMode.MultiSelect;
        public bool IsStandaloneEvent => _mode == DisplayMode.StandaloneEvent;
        public bool IsFloatMode => _valueType == "float" && _mode == DisplayMode.TrackInspector;
        public bool IsBoolMode => _valueType == "bool" && _mode == DisplayMode.TrackInspector;
        public bool ShowInterpolation =>
            _mode == DisplayMode.TrackInspector && _hasKeyframeAtPlayhead && _valueType != "bool";
        public bool ShowEventBinding => _mode == DisplayMode.StandaloneEvent;
        public bool ShowValueArea =>
            _mode == DisplayMode.TrackInspector || _mode == DisplayMode.MultiSelect;

        public string ActionName
        {
            get => _actionName;
            private set => this.RaiseAndSetIfChanged(ref _actionName, value);
        }

        public string ActionCategory
        {
            get => _actionCategory;
            private set => this.RaiseAndSetIfChanged(ref _actionCategory, value);
        }

        public string ActionTimeRangeText
        {
            get => _actionTimeRangeText;
            private set => this.RaiseAndSetIfChanged(ref _actionTimeRangeText, value);
        }

        public string ActionParametersText
        {
            get => _actionParametersText;
            private set => this.RaiseAndSetIfChanged(ref _actionParametersText, value);
        }

        public string ActionBindingText
        {
            get => _actionBindingText;
            private set => this.RaiseAndSetIfChanged(ref _actionBindingText, value);
        }

        /// <summary>当前动作是否包含锁定轨道；锁定时动作级写操作在检查器中禁用。</summary>
        public bool ActionEditLocked
        {
            get => _actionEditLocked;
            private set => this.RaiseAndSetIfChanged(ref _actionEditLocked, value);
        }

        /// <summary>值是否可编辑: 在关键帧上 或 AutoKey 开启</summary>
        public bool IsValueEditable =>
            _mode == DisplayMode.TrackInspector && (_hasKeyframeAtPlayhead || _isAutoKeyMode);

        /// <summary>显示编辑提示 (不可编辑时)</summary>
        public bool ShowEditHint =>
            _mode == DisplayMode.TrackInspector && !(_hasKeyframeAtPlayhead || _isAutoKeyMode);

        /// <summary>播放头位置有事件时显示</summary>
        public bool ShowPlayheadEvent =>
            _mode == DisplayMode.TrackInspector && _playheadEvent != null;

        /// <summary>自动关键帧模式: 编辑值时自动创建关键帧</summary>
        public bool IsAutoKeyMode
        {
            get => _isAutoKeyMode;
            set
            {
                this.RaiseAndSetIfChanged(ref _isAutoKeyMode, value);
                this.RaisePropertyChanged(nameof(IsValueEditable));
                this.RaisePropertyChanged(nameof(ShowEditHint));
            }
        }

        /// <summary>当前播放头位置是否有关键帧 (用于 ◆ 指示器)</summary>
        public bool HasKeyframeAtPlayhead
        {
            get => _hasKeyframeAtPlayhead;
            set
            {
                this.RaiseAndSetIfChanged(ref _hasKeyframeAtPlayhead, value);
                this.RaisePropertyChanged(nameof(ShowInterpolation));
            }
        }

        public double TimeMs
        {
            get => _timeMs;
            set => this.RaiseAndSetIfChanged(ref _timeMs, value);
        }

        public string TimeDisplayText
        {
            get
            {
                if (_mode == DisplayMode.MultiSelect)
                    return $"{FormatTimeMs(_multiMinTime)} ~ {FormatTimeMs(_multiMaxTime)}  (跨度: {FormatTimeMs(_multiMaxTime - _multiMinTime)})";
                return FormatTimeMs(_timeMs);
            }
        }

        public float Value
        {
            get => _value;
            set => this.RaiseAndSetIfChanged(ref _value, value);
        }

        public string ValueDisplayText
        {
            get
            {
                if (_mode == DisplayMode.MultiSelect)
                    return $"值: {_multiMinValue:F3} ~ {_multiMaxValue:F3}  平均: {_multiAvgValue:F3}";
                return _value.ToString("F3", CultureInfo.InvariantCulture);
            }
        }

        public bool BoolValue
        {
            get => _value >= 0.5f;
            set
            {
                Value = value ? 1f : 0f;
                this.RaisePropertyChanged(nameof(BoolValue));
                this.RaisePropertyChanged(nameof(BoolValueLabel));
            }
        }

        public string BoolValueLabel => _value >= 0.5f ? "1.0" : "0.0";

        public string Interpolation
        {
            get => _interpolation;
            set => this.RaiseAndSetIfChanged(ref _interpolation, value);
        }

        // 保留切线属性供事件传递 (编辑通过 CurveEditor 进行, 不在 Inspector 显示)
        public float? TangentIn
        {
            get => _tangentIn;
            set => this.RaiseAndSetIfChanged(ref _tangentIn, value);
        }

        public float? TangentOut
        {
            get => _tangentOut;
            set => this.RaiseAndSetIfChanged(ref _tangentOut, value);
        }

        public float? Cp1x
        {
            get => _cp1x;
            set => this.RaiseAndSetIfChanged(ref _cp1x, value);
        }

        public float? Cp2x
        {
            get => _cp2x;
            set => this.RaiseAndSetIfChanged(ref _cp2x, value);
        }

        public int ClipIndex
        {
            get => _clipIndex;
            set => this.RaiseAndSetIfChanged(ref _clipIndex, value);
        }

        public int KeyframeIndex
        {
            get => _keyframeIndex;
            set => this.RaiseAndSetIfChanged(ref _keyframeIndex, value);
        }

        public string ValueType
        {
            get => _valueType;
            set
            {
                this.RaiseAndSetIfChanged(ref _valueType, value);
                this.RaisePropertyChanged(nameof(IsFloatMode));
                this.RaisePropertyChanged(nameof(IsBoolMode));
            }
        }

        public string EventName
        {
            get => _eventName;
            set => this.RaiseAndSetIfChanged(ref _eventName, value);
        }

        public string? EventData
        {
            get => _eventData;
            set => this.RaiseAndSetIfChanged(ref _eventData, value);
        }

        public string PanelTitle
        {
            get => _panelTitle;
            set => this.RaiseAndSetIfChanged(ref _panelTitle, value);
        }

        /// <summary>当前选中的轨道名称 (显示用)</summary>
        public string TrackName
        {
            get => _trackName;
            set => this.RaiseAndSetIfChanged(ref _trackName, value);
        }

        /// <summary>轨道颜色 (hex, 与轨道头一致)</summary>
        public string TrackColor
        {
            get => _trackColor;
            set => this.RaiseAndSetIfChanged(ref _trackColor, value);
        }

        /// <summary>当前轨道关键帧总数</summary>
        public int TotalKfCount
        {
            get => _totalKfCount;
            set => this.RaiseAndSetIfChanged(ref _totalKfCount, value);
        }

        /// <summary>当前区间插值类型 (播放头不在关键帧上时)</summary>
        public string IntervalInterpolation
        {
            get => _intervalInterpolation;
            set => this.RaiseAndSetIfChanged(ref _intervalInterpolation, value);
        }

        /// <summary>播放头位置的事件 (轨道检查器模式下联动显示)</summary>
        public TimelineEvent? PlayheadEvent
        {
            get => _playheadEvent;
            set
            {
                this.RaiseAndSetIfChanged(ref _playheadEvent, value);
                this.RaisePropertyChanged(nameof(ShowPlayheadEvent));
            }
        }

        /// <summary>播放头事件名称 (双向绑定编辑)</summary>
        public string PlayheadEventName
        {
            get => _playheadEventName;
            set => this.RaiseAndSetIfChanged(ref _playheadEventName, value);
        }

        /// <summary>播放头事件参数 (双向绑定编辑)</summary>
        public string? PlayheadEventData
        {
            get => _playheadEventData;
            set => this.RaiseAndSetIfChanged(ref _playheadEventData, value);
        }

        /// <summary>播放头事件数据类型 (none / string / json / number)</summary>
        public string PlayheadEventDataType
        {
            get => _playheadEventDataType;
            set
            {
                this.RaiseAndSetIfChanged(ref _playheadEventDataType, value);
                this.RaisePropertyChanged(nameof(ShowPlayheadEventDataInput));
                this.RaisePropertyChanged(nameof(PlayheadEventDataWatermark));
            }
        }

        /// <summary>播放头事件: 是否显示参数输入框</summary>
        public bool ShowPlayheadEventDataInput => _playheadEventDataType != "none";

        /// <summary>播放头事件: 参数输入框占位提示</summary>
        public string PlayheadEventDataWatermark =>
            _playheadEventDataType switch
            {
                "json" => "JSON 格式",
                "number" => "数值",
                _ => "文本"
            };

        /// <summary>事件数据类型 (none / string / json / number)</summary>
        public string EventDataType
        {
            get => _eventDataType;
            set
            {
                this.RaiseAndSetIfChanged(ref _eventDataType, value);
                this.RaisePropertyChanged(nameof(ShowEventDataInput));
                this.RaisePropertyChanged(nameof(EventDataWatermark));
                ValidateEventData();
            }
        }

        /// <summary>事件数据格式校验错误 (为空时表示无错误)</summary>
        public string EventDataError
        {
            get => _eventDataError;
            set => this.RaiseAndSetIfChanged(ref _eventDataError, value);
        }

        /// <summary>是否显示参数输入框 (none 类型时隐藏)</summary>
        public bool ShowEventDataInput => _eventDataType != "none";

        /// <summary>参数输入框水印文字 (随类型变化)</summary>
        public string EventDataWatermark =>
            _eventDataType switch
            {
                "number" => "例: 3.14",
                "json" => "例: {\"key\": \"value\"}",
                "string" => "例: hello world",
                _ => ""
            };

        /// <summary>校验事件数据格式</summary>
        public bool ValidateEventData()
        {
            string? data = EventData;
            if (
                string.IsNullOrWhiteSpace(data)
                || _eventDataType == "none"
                || _eventDataType == "string"
            )
            {
                EventDataError = "";
                return true;
            }

            if (_eventDataType == "number")
            {
                if (
                    !double.TryParse(
                        data.Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out _
                    )
                )
                {
                    EventDataError = "⚠ 请输入有效数字";
                    return false;
                }
                EventDataError = "";
                return true;
            }

            if (_eventDataType == "json")
            {
                try
                {
                    System.Text.Json.JsonDocument.Parse(data.Trim());
                    EventDataError = "";
                    return true;
                }
                catch
                {
                    EventDataError = "⚠ JSON 格式无效";
                    return false;
                }
            }

            EventDataError = "";
            return true;
        }

        /// <summary>事件时间文本 (双向绑定编辑, 格式 MM:SS.fff)</summary>
        public string EventTimeText
        {
            get => _eventTimeText;
            set => this.RaiseAndSetIfChanged(ref _eventTimeText, value);
        }

        /// <summary>独立事件模式下时间可编辑</summary>
        public bool IsEventTimeEditable => _mode == DisplayMode.StandaloneEvent;

        public string IndexLabel
        {
            get => _indexLabel;
            set => this.RaiseAndSetIfChanged(ref _indexLabel, value);
        }

        public string EventBadge
        {
            get => _eventBadge;
            set => this.RaiseAndSetIfChanged(ref _eventBadge, value);
        }

        public bool EventNameDuplicate
        {
            get => _eventNameDuplicate;
            set => this.RaiseAndSetIfChanged(ref _eventNameDuplicate, value);
        }

        public TimelineEvent? DisplayedEvent
        {
            get => _displayedEvent;
            set => this.RaiseAndSetIfChanged(ref _displayedEvent, value);
        }

        public int MultiSelectCount
        {
            get => _multiSelectCount;
            set => this.RaiseAndSetIfChanged(ref _multiSelectCount, value);
        }

        public bool IsTimeReadOnly => true; // 时间始终由播放头驱动, 不可手动编辑

        // ── 事件 (供父 VM 订阅) ──

        /// <summary>关键帧属性编辑: (timeMs, value, interpolation, tangentIn, tangentOut, cp1x, cp2x)</summary>
        public event Action<
            double,
            float,
            string,
            float?,
            float?,
            float?,
            float?
        >? KeyframePropertyEdited;

        /// <summary>关键帧事件属性编辑: (eventName, eventData)</summary>
        public event Action<string, string?>? EventPropertyEdited;

        /// <summary>独立事件属性编辑: (event, timeMs, eventName, eventData)</summary>
        public event Action<TimelineEvent, double, string, string?>? StandaloneEventEdited;

        /// <summary>请求删除独立事件</summary>
        public event Action<TimelineEvent>? DeleteEventRequested;

        /// <summary>播放头事件属性编辑: (event, eventName, eventData, dataType)</summary>
        public event Action<TimelineEvent, string, string?, string>? PlayheadEventEdited;

        /// <summary>多选批量插值变更: (interpolation)</summary>
        public event Action<string>? BatchInterpolationChanged;

        /// <summary>请求在播放头位置切换关键帧 (点击 ◆)</summary>
        public event Action<double>? ToggleKeyframeRequested;

        /// <summary>AutoKey 创建关键帧并赋值: (timeMs, desiredValue)</summary>
        public event Action<double, float>? AutoKeyCreateRequested;

        /// <summary>请求跳转到前一个关键帧</summary>
        public event SysAction? NavigatePrevKeyframeRequested;

        /// <summary>请求跳转到后一个关键帧</summary>
        public event SysAction? NavigateNextKeyframeRequested;

        /// <summary>触发切换关键帧请求</summary>
        public void RaiseToggleKeyframe(double timeMs) => ToggleKeyframeRequested?.Invoke(timeMs);

        /// <summary>触发跳转到前一个关键帧</summary>
        public void RaiseNavigatePrev() => NavigatePrevKeyframeRequested?.Invoke();

        /// <summary>触发跳转到后一个关键帧</summary>
        public void RaiseNavigateNext() => NavigateNextKeyframeRequested?.Invoke();

        /// <summary>事件名唯一性校验委托</summary>
        public Func<string, TimelineEvent?, bool>? ValidateEventName { get; set; }

        /// <summary>
        /// 选中状态版本号 — 每次显示更新时递增。
        /// 用于 LostFocus 守卫。
        /// </summary>
        public int SelectionVersion { get; private set; }

        // ── 命令 ──

        public ReactiveCommand<Unit, Unit> DeleteCommand { get; }
        public ReactiveCommand<string, Unit> CommitInterpolationCommand { get; }

        public KeyframePropertyViewModel()
        {
            DeleteCommand = ReactiveCommand.Create(() =>
            {
                if (_displayedEvent is not null)
                    DeleteEventRequested?.Invoke(_displayedEvent);
            });

            CommitInterpolationCommand = ReactiveCommand.Create<string>(interp =>
            {
                if (_mode == DisplayMode.MultiSelect)
                    BatchInterpolationChanged?.Invoke(interp);
                else if (_mode == DisplayMode.TrackInspector && _hasKeyframeAtPlayhead)
                    RaiseKeyframeEdited(null, null, interp, null, null);
            });
        }

        // ── 显示更新方法 (供父 ViewModel 调用) ──

        /// <summary>
        /// 显示轨道检查器状态 — 核心方法。
        /// 始终由播放头位置驱动, 显示当前时间点的插值值/插值类型。
        /// 如果播放头恰好在关键帧上, 可以编辑该关键帧的属性。
        /// </summary>
        public void ShowTrackState(
            double timeMs,
            float interpolatedValue,
            string valueType,
            string trackName,
            bool hasKfAtPlayhead,
            string interpolation = "linear",
            float? tangentIn = null,
            float? tangentOut = null,
            float? cp1x = null,
            float? cp2x = null,
            int clipIdx = -1,
            int kfIdx = -1,
            string trackColor = "#4FC3F7",
            int totalKfCount = 0,
            string intervalInterpolation = "",
            TrackViewModel? trackVm = null
        )
        {
            _isSyncing = true;
            SelectionVersion++;
            DisplayedEvent = null;
            Mode = DisplayMode.TrackInspector;
            ValueType = valueType;
            TrackName = trackName;
            TrackColor = trackColor;
            TotalKfCount = totalKfCount;
            IntervalInterpolation = intervalInterpolation;
            TimeMs = timeMs;
            Value = interpolatedValue;
            HasKeyframeAtPlayhead = hasKfAtPlayhead;
            Interpolation = interpolation;
            TangentIn = tangentIn;
            TangentOut = tangentOut;
            Cp1x = cp1x;
            Cp2x = cp2x;
            ClipIndex = clipIdx;
            KeyframeIndex = kfIdx;
            _isSyncing = false;
            PanelTitle = "检查器";
            IndexLabel = hasKfAtPlayhead
                ? $"KF {kfIdx + 1}/{totalKfCount}"
                : (totalKfCount > 0 ? $"共 {totalKfCount} KF" : "无关键帧");
            EventBadge = "";
            EventNameDuplicate = false;
            TrackVm = trackVm;
            RaiseModeProperties();
        }

        private TrackViewModel? _trackVm;

        /// <summary>当前检查器对应的轨道 (用于待机循环等轨道级操作)。</summary>
        public TrackViewModel? TrackVm
        {
            get => _trackVm;
            private set
            {
                this.RaiseAndSetIfChanged(ref _trackVm, value);
                this.RaisePropertyChanged(nameof(HasIdleLoop));
                this.RaisePropertyChanged(nameof(IdleStatusText));
            }
        }

        /// <summary>当前轨道是否已启用待机循环。</summary>
        public bool HasIdleLoop => TrackVm?.IdleLoop is { Enabled: true };

        /// <summary>待机循环状态文本。</summary>
        public string IdleStatusText =>
            TrackVm?.IdleLoop is { } idle
                ? (
                    idle.Enabled
                        ? $"待机已启用 · 周期 {idle.PeriodMs / 1000.0:F1}s · {idle.Keyframes?.Count ?? 0} 关键帧"
                        : "待机已停用"
                )
                : "未配置待机循环";

        /// <summary>显示多选模式摘要。</summary>
        public void ShowMultiSelection(
            int count,
            double minTime,
            double maxTime,
            float minValue,
            float maxValue,
            float avgValue
        )
        {
            SelectionVersion++;
            DisplayedEvent = null;
            Mode = DisplayMode.MultiSelect;
            MultiSelectCount = count;
            _multiMinTime = minTime;
            _multiMaxTime = maxTime;
            _multiMinValue = minValue;
            _multiMaxValue = maxValue;
            _multiAvgValue = avgValue;
            PanelTitle = "多选模式";
            IndexLabel = $"已选中 {count} 个关键帧";
            Interpolation = ""; // 混合状态
            RaiseModeProperties();
        }

        /// <summary>显示一个跨轨动作实例的整体摘要。</summary>
        public void ShowActionInstance(
            ActionInstance instance,
            EffectPreset? preset,
            bool actionEditLocked = false
        )
        {
            SelectionVersion++;
            DisplayedEvent = null;
            Mode = DisplayMode.ActionInstance;
            PanelTitle = "动作";
            ActionName = string.IsNullOrWhiteSpace(instance.Name) ? "未命名动作" : instance.Name;
            ActionCategory = preset?.Category ?? "项目动作";
            ActionTimeRangeText =
                $"{FormatTimeMs(instance.StartMs)} - {FormatTimeMs(instance.StartMs + instance.DurationMs)}";
            ActionParametersText = $"强度 {instance.Intensity:F2}   速度 {instance.PlaybackRate:F2}x";
            int channelCount = instance.RoleTrackIds?.Count ?? 0;
            ActionBindingText = $"{channelCount} 通道   修订 {instance.DefinitionRevision}";
            IndexLabel = $"{instance.DurationMs / 1000.0:F1}s";
            ActionEditLocked = actionEditLocked;
            RaiseModeProperties();
        }

        /// <summary>显示独立事件属性。</summary>
        public void ShowEvent(TimelineEvent evt)
        {
            SelectionVersion++;
            DisplayedEvent = evt;
            Mode = DisplayMode.StandaloneEvent;
            TimeMs = evt.TimeMs;
            EventTimeText = FormatTimeMs(evt.TimeMs);
            EventName = evt.EventName ?? "";
            EventData = evt.EventData;
            EventDataType = evt.DataType ?? "string";
            PanelTitle = "事件属性";
            IndexLabel = "独立事件";
            EventBadge = "事件";
            ActionEditLocked = false;
            EventNameDuplicate = false;
            RaiseModeProperties();
        }

        /// <summary>清空面板 (无轨道选中)。</summary>
        public void Clear()
        {
            SelectionVersion++;
            DisplayedEvent = null;
            Mode = DisplayMode.None;
            PanelTitle = "检查器";
            IndexLabel = "";
            TrackName = "";
            TrackColor = "#4FC3F7";
            TotalKfCount = 0;
            IntervalInterpolation = "";
            ActionEditLocked = false;
            EventNameDuplicate = false;
            RaiseModeProperties();
        }

        // ── 编辑提交 (供 Panel code-behind 或命令调用) ──

        /// <summary>提交值编辑 — 如果播放头在关键帧上则编辑, 否则 AutoKey 创建。</summary>
        public void CommitValue(float value)
        {
            if (_isSyncing)
                return; // ShowTrackState 期间不触发编辑
            value = Math.Clamp(value, 0f, 1f);
            if (_hasKeyframeAtPlayhead)
            {
                // 播放头在关键帧上: 直接编辑
                RaiseKeyframeEdited(null, value, null, null, null);
            }
            else if (_isAutoKeyMode)
            {
                // AutoKey: 创建关键帧并赋予用户指定的值
                AutoKeyCreateRequested?.Invoke(TimeMs, value);
            }
        }

        /// <summary>提交事件属性编辑。</summary>
        public void CommitEvent()
        {
            string name = EventName.Trim();
            string? data =
                _eventDataType == "none"
                    ? null
                    : (string.IsNullOrWhiteSpace(EventData) ? null : EventData.Trim());

            // 格式校验
            if (!ValidateEventData())
                return;

            if (!string.IsNullOrWhiteSpace(name) && ValidateEventName is not null)
            {
                bool isUnique = ValidateEventName(name, _displayedEvent);
                EventNameDuplicate = !isUnique;
                if (!isUnique)
                    return;
            }
            else
            {
                EventNameDuplicate = false;
            }

            EventBadge = !string.IsNullOrWhiteSpace(name) ? "已绑定" : "";

            if (_displayedEvent is not null)
            {
                // 解析时间输入
                double newTimeMs = TimeMs;
                if (TryParseTimeInput(EventTimeText, out double parsedMs))
                    newTimeMs = parsedMs;

                // 同步 DataType 到模型
                _displayedEvent.DataType = EventDataType ?? "string";

                StandaloneEventEdited?.Invoke(_displayedEvent, newTimeMs, name, data);
            }
            else
            {
                EventPropertyEdited?.Invoke(name, data);
            }
        }

        /// <summary>提交播放头事件编辑。</summary>
        public void CommitPlayheadEvent()
        {
            if (_playheadEvent is null)
                return;
            string name = PlayheadEventName.Trim();
            string? data = string.IsNullOrWhiteSpace(PlayheadEventData)
                ? null
                : PlayheadEventData.Trim();
            string dataType = PlayheadEventDataType ?? "string";
            PlayheadEventEdited?.Invoke(_playheadEvent, name, data, dataType);
        }

        /// <summary>设置播放头位置的事件 (供 RefreshPropertyPanel 调用)。</summary>
        public void SetPlayheadEvent(TimelineEvent? evt)
        {
            PlayheadEvent = evt;
            if (evt != null)
            {
                PlayheadEventName = evt.EventName ?? "";
                PlayheadEventData = evt.EventData;
                PlayheadEventDataType = evt.DataType ?? "string";
            }
            else
            {
                PlayheadEventName = "";
                PlayheadEventData = null;
                PlayheadEventDataType = "string";
            }
        }

        // ── 内部 ──

        private void RaiseKeyframeEdited(
            double? time,
            float? value,
            string? interp,
            float? tangentIn,
            float? tangentOut,
            float? cp1x = null,
            float? cp2x = null
        )
        {
            if (_isSyncing)
                return;

            double finalTime = time ?? TimeMs;
            float finalValue = value ?? Value;
            string finalInterp = interp ?? Interpolation;
            float? finalTIn = tangentIn ?? (finalInterp == "bezier" ? TangentIn : null);
            float? finalTOut = tangentOut ?? (finalInterp == "bezier" ? TangentOut : null);
            float? finalCp1x = cp1x ?? (finalInterp == "bezier" ? Cp1x : null);
            float? finalCp2x = cp2x ?? (finalInterp == "bezier" ? Cp2x : null);

            KeyframePropertyEdited?.Invoke(
                finalTime,
                finalValue,
                finalInterp,
                finalTIn,
                finalTOut,
                finalCp1x,
                finalCp2x
            );
        }

        private void RaiseModeProperties()
        {
            this.RaisePropertyChanged(nameof(HasSelection));
            this.RaisePropertyChanged(nameof(IsTrackInspector));
            this.RaisePropertyChanged(nameof(IsActionInstance));
            this.RaisePropertyChanged(nameof(IsMultiSelect));
            this.RaisePropertyChanged(nameof(IsStandaloneEvent));
            this.RaisePropertyChanged(nameof(IsFloatMode));
            this.RaisePropertyChanged(nameof(IsBoolMode));
            this.RaisePropertyChanged(nameof(ShowInterpolation));
            this.RaisePropertyChanged(nameof(ShowEventBinding));
            this.RaisePropertyChanged(nameof(ShowValueArea));
            this.RaisePropertyChanged(nameof(IsTimeReadOnly));
            this.RaisePropertyChanged(nameof(TimeDisplayText));
            this.RaisePropertyChanged(nameof(ValueDisplayText));
            this.RaisePropertyChanged(nameof(BoolValue));
            this.RaisePropertyChanged(nameof(BoolValueLabel));
            this.RaisePropertyChanged(nameof(HasKeyframeAtPlayhead));
            this.RaisePropertyChanged(nameof(IsValueEditable));
            this.RaisePropertyChanged(nameof(ShowEditHint));
            this.RaisePropertyChanged(nameof(ShowPlayheadEvent));
            this.RaisePropertyChanged(nameof(IsEventTimeEditable));
        }

        // ── 时间格式化 / 解析 ──

        /// <summary>将毫秒格式化为 MM:SS.fff 或 HH:MM:SS.fff</summary>
        public static string FormatTimeMs(double ms)
        {
            double totalSeconds = Math.Abs(ms) / 1000.0;
            int hours = (int)(totalSeconds / 3600);
            int minutes = (int)((totalSeconds % 3600) / 60);
            double seconds = totalSeconds % 60;
            string sign = ms < 0 ? "-" : "";

            if (hours > 0)
                return $"{sign}{hours}:{minutes:D2}:{seconds:00.000}";
            return $"{sign}{minutes:D2}:{seconds:00.000}";
        }

        /// <summary>
        /// 尝试解析时间输入, 支持:
        /// - "MM:SS.fff" / "HH:MM:SS.fff" → 毫秒
        /// - 纯数字 → 按毫秒
        /// </summary>
        public static bool TryParseTimeInput(string? text, out double ms)
        {
            ms = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            text = text.Trim();

            if (text.Contains(':'))
            {
                var parts = text.Split(':');
                if (parts.Length == 2)
                {
                    if (
                        int.TryParse(parts[0], out int min)
                        && double.TryParse(
                            parts[1],
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out double sec
                        )
                    )
                    {
                        ms = (min * 60 + sec) * 1000.0;
                        return true;
                    }
                }
                else if (parts.Length == 3)
                {
                    if (
                        int.TryParse(parts[0], out int hr)
                        && int.TryParse(parts[1], out int min)
                        && double.TryParse(
                            parts[2],
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out double sec
                        )
                    )
                    {
                        ms = (hr * 3600 + min * 60 + sec) * 1000.0;
                        return true;
                    }
                }
                return false;
            }

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out ms);
        }
    }
}
