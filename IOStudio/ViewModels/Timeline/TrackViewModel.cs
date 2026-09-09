using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using IOStudio.Models.Motion;
using ReactiveUI;

namespace IOStudio.ViewModels.Timeline
{
    /// <summary>
    /// 轨道 ViewModel — 包装 MotionTrack 模型, 提供 UI 绑定
    /// </summary>
    public class TrackViewModel : ViewModelBase
    {
        public MotionTrack Track { get; }

        /// <summary>
        /// 将 ClipViewModel 集合转换为 List&lt;MotionClip&gt; 供 TrackClipControl 使用
        /// </summary>
        public static readonly IValueConverter ClipsConverter = new FuncValueConverter<
            ObservableCollection<ClipViewModel>,
            List<MotionClip>?
        >(clips => clips?.Select(c => c.Clip).ToList());

        public TrackViewModel(MotionTrack track)
        {
            Track = track;
            _isLocked = track.Locked;

            // 从模型同步片段 ViewModel
            foreach (var clip in track.Clips)
            {
                Clips.Add(new ClipViewModel(clip));
            }
        }

        public string Id => Track.Id;

        public string DeviceName
        {
            get => Track.DeviceName;
            set
            {
                Track.DeviceName = value;
                this.RaisePropertyChanged();
            }
        }

        public string OActionName
        {
            get => Track.OActionName;
            set
            {
                Track.OActionName = value;
                this.RaisePropertyChanged();
            }
        }

        public string Label
        {
            get => Track.Label;
            set
            {
                Track.Label = value;
                this.RaisePropertyChanged();
            }
        }

        /// <summary>显示标题: "Label (DeviceName / OActionName)" 或 "Label (DeviceName / OAxis_XX)"</summary>
        public string DisplayTitle
        {
            get
            {
                string outputDesc =
                    Track.OutputType == "oaxis" && !string.IsNullOrEmpty(Track.OAxisChannel)
                        ? Track.OAxisChannel
                        : OActionName;
                return string.IsNullOrEmpty(Label)
                    ? $"{DeviceName} / {outputDesc}"
                    : $"{Label} ({DeviceName} / {outputDesc})";
            }
        }

        public string Color
        {
            get => Track.Color;
            set
            {
                Track.Color = value;
                this.RaisePropertyChanged();
            }
        }

        /// <summary>值类型: "float" 连续值 / "bool" 开关值</summary>
        public string ValueType
        {
            get => Track.ValueType;
            set
            {
                Track.ValueType = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(ValueTypeDisplay));
            }
        }

        /// <summary>值类型显示文本</summary>
        public string ValueTypeDisplay => ValueType == "bool" ? "Bool 开关" : "Float 连续";

        /// <summary>中性/空闲值 (null=按类型默认)。用于无动作覆盖时的输出与曲线渲染。</summary>
        public float? NeutralValue
        {
            get => Track.NeutralValue;
            set
            {
                Track.NeutralValue = value;
                this.RaisePropertyChanged();
            }
        }

        /// <summary>Idle 循环 (gap 填充) — 无 clip 覆盖时的待机循环动作 (null=无 idle)。</summary>
        public IdleLoop? IdleLoop
        {
            get => Track.IdleLoop;
            set
            {
                Track.IdleLoop = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(HasIdleLoop));
            }
        }

        /// <summary>是否已配置 idle 循环 (用于 UI 开关态)</summary>
        public bool HasIdleLoop => Track.IdleLoop != null && Track.IdleLoop.Enabled;

        /// <summary>
        /// Overlay 覆盖关键帧 (空窗自由数值点, 绝对时间) — 优先级高于 idle, 用于在待机循环上打点。
        /// </summary>
        public List<MotionKeyframe>? OverrideKeyframes
        {
            get => Track.OverrideKeyframes;
            set
            {
                Track.OverrideKeyframes = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(HasOverrideKeyframes));
            }
        }

        /// <summary>是否已配置 overlay 覆盖关键帧</summary>
        public bool HasOverrideKeyframes =>
            Track.OverrideKeyframes != null && Track.OverrideKeyframes.Count > 0;

        /// <summary>轨道逻辑角色标签 (自由字符串, 来自 Track.Role)</summary>
        public string Role => Track.Role;

        /// <summary>是否有 Role (用于显示/隐藏徽章)</summary>
        public bool HasRole => !string.IsNullOrEmpty(Track.Role);

        /// <summary>Role 显示文本 (截断为 8 字符)</summary>
        public string RoleDisplay
        {
            get
            {
                string r = Track.Role ?? "";
                return r.Length > 8 ? r[..8] + "…" : r;
            }
        }

        private bool _isMuted;
        public bool IsMuted
        {
            get => Track.Muted;
            set
            {
                Track.Muted = value;
                this.RaiseAndSetIfChanged(ref _isMuted, value);
                this.RaisePropertyChanged(nameof(TrackOpacity));
            }
        }

        private bool _isSolo;
        public bool IsSolo
        {
            get => Track.Solo;
            set
            {
                Track.Solo = value;
                this.RaiseAndSetIfChanged(ref _isSolo, value);
            }
        }

        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => Track.Enabled;
            set
            {
                Track.Enabled = value;
                this.RaiseAndSetIfChanged(ref _isEnabled, value);
                this.RaisePropertyChanged(nameof(TrackOpacity));
            }
        }

        /// <summary>轨道不透明度 — 禁用/静音/非Solo 时变淡</summary>
        public double TrackOpacity => IsEnabled && !IsMuted ? 1.0 : 0.4;

        /// <summary>刷新外观 (Solo 状态变化时需全部轨道刷新)</summary>
        public void RaiseDimmingChanged()
        {
            this.RaisePropertyChanged(nameof(TrackOpacity));
        }

        private bool _isLocked;
        public bool IsLocked
        {
            get => Track.Locked;
            set
            {
                Track.Locked = value;
                this.RaiseAndSetIfChanged(ref _isLocked, value);
                this.RaisePropertyChanged(nameof(LockActionLabel));
            }
        }

        /// <summary>锁定按钮的动作语义，随当前状态切换以保持提示和无障碍名称一致。</summary>
        public string LockActionLabel => IsLocked ? "解锁轨道" : "锁定轨道";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }

        /// <summary>所属分组名称 (空字符串表示未分组)</summary>
        public string Group
        {
            get => Track.Group;
            set
            {
                Track.Group = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(HasGroup));
            }
        }

        /// <summary>是否属于某个分组</summary>
        public bool HasGroup => !string.IsNullOrEmpty(Group);

        /// <summary>在曲线编辑器中是否可见 (UX-B1 焦点模式)</summary>
        public bool ShowInCurve
        {
            get => Track.ShowInCurve;
            set
            {
                if (Track.ShowInCurve == value)
                    return;
                Track.ShowInCurve = value;
                this.RaisePropertyChanged();
            }
        }

        /// <summary>轨道在时间轴中是否可见 (用于分组折叠)</summary>
        private bool _isVisibleInTimeline = true;
        public bool IsVisibleInTimeline
        {
            get => _isVisibleInTimeline;
            set => this.RaiseAndSetIfChanged(ref _isVisibleInTimeline, value);
        }

        /// <summary>标记: 这是轨道 (用于 DataTemplate 区分分组头)</summary>
        public bool IsGroupHeader => false;

        /// <summary>
        /// 独立轨道高度 (像素), 0 表示使用全局默认高度
        /// 支持通过右键菜单或拖拽调整每条轨道的独立高度
        /// </summary>
        private double _individualTrackHeight;
        public double IndividualTrackHeight
        {
            get => _individualTrackHeight;
            set => this.RaiseAndSetIfChanged(ref _individualTrackHeight, Math.Max(0, value));
        }

        /// <summary>是否使用独立高度 (非全局)</summary>
        public bool HasIndividualHeight => _individualTrackHeight > 0;

        /// <summary>重置为全局默认高度</summary>
        public void ResetToGlobalHeight()
        {
            IndividualTrackHeight = 0;
            this.RaisePropertyChanged(nameof(HasIndividualHeight));
            this.RaisePropertyChanged(nameof(IsCompactDisplay));
        }

        /// <summary>设置独立高度</summary>
        public void SetIndividualHeight(double height)
        {
            IndividualTrackHeight = Math.Max(24, Math.Min(200, height));
            this.RaisePropertyChanged(nameof(HasIndividualHeight));
            this.RaisePropertyChanged(nameof(IsCompactDisplay));
        }

        /// <summary>
        /// 紧凑显示模式 — 当前轨道高度 < 36px 时仅显示轨道名, 隐藏副标题和按钮
        /// 由实际渲染高度决定 (独立高度优先, 否则由全局高度决定)
        /// </summary>
        public bool IsCompactDisplay { get; private set; }

        /// <summary>是否显示副标题行 (需要较大轨道高度 ≥ 52px)</summary>
        private bool _showSubtitle;
        public bool ShowSubtitle
        {
            get => _showSubtitle;
            private set => this.RaiseAndSetIfChanged(ref _showSubtitle, value);
        }

        /// <summary>由 TimelineEditorViewModel 在全局高度或独立高度变化时调用</summary>
        public void UpdateCompactDisplay(double globalHeight)
        {
            double effectiveH = HasIndividualHeight ? IndividualTrackHeight : globalHeight;
            bool compact = effectiveH < 36;
            if (compact != IsCompactDisplay)
            {
                IsCompactDisplay = compact;
                this.RaisePropertyChanged(nameof(IsCompactDisplay));
            }
            ShowSubtitle = effectiveH >= 52;
        }

        /// <summary>播放时的实时输出值 (由 DeviceDispatcher.ValueDispatched 同步)</summary>
        private float _liveValue = 0f;
        public float LiveValue
        {
            get => _liveValue;
            set
            {
                this.RaiseAndSetIfChanged(ref _liveValue, value);
                this.RaisePropertyChanged(nameof(LiveValueDisplay));
            }
        }

        /// <summary>实时值显示文本 (如 "0.75" 或 "ON"/"OFF")</summary>
        public string LiveValueDisplay =>
            ValueType == "bool" ? (LiveValue >= 0.5f ? "ON" : "OFF") : LiveValue.ToString("F2");

        public ObservableCollection<ClipViewModel> Clips { get; } = new();

        /// <summary>
        /// 通知 Clips 属性变化 (触发 FuncValueConverter 重新转换, 使 TrackClipControl 重绘)
        /// </summary>
        public void RaiseClipsChanged()
        {
            this.RaisePropertyChanged(nameof(Clips));
        }

        /// <summary>UX-A2: 轨道属性对话框提交后通知所有派生属性刷新</summary>
        public void RaiseAllBindingsChanged()
        {
            this.RaisePropertyChanged(nameof(DisplayTitle));
            this.RaisePropertyChanged(nameof(DeviceName));
            this.RaisePropertyChanged(nameof(OActionName));
            this.RaisePropertyChanged(nameof(Label));
            this.RaisePropertyChanged(nameof(Color));
            this.RaisePropertyChanged(nameof(ValueType));
            this.RaisePropertyChanged(nameof(ValueTypeDisplay));
            this.RaisePropertyChanged(nameof(LiveValueDisplay));
        }
    }
}
