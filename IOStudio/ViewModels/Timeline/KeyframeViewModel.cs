using IOStudio.Models.Motion;
using ReactiveUI;

namespace IOStudio.ViewModels.Timeline
{
    /// <summary>
    /// 关键帧 ViewModel — 包装 MotionKeyframe 模型
    /// </summary>
    public class KeyframeViewModel : ViewModelBase
    {
        public MotionKeyframe Keyframe { get; }

        public KeyframeViewModel(MotionKeyframe keyframe)
        {
            Keyframe = keyframe;
        }

        public double TimeMs
        {
            get => Keyframe.TimeMs;
            set
            {
                Keyframe.TimeMs = value;
                this.RaisePropertyChanged();
            }
        }

        public float Value
        {
            get => Keyframe.Value;
            set
            {
                Keyframe.Value = value;
                this.RaisePropertyChanged();
            }
        }

        public string Interpolation
        {
            get => Keyframe.Interpolation;
            set
            {
                Keyframe.Interpolation = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(IsBezier));
            }
        }

        public float? TangentIn
        {
            get => Keyframe.TangentIn;
            set
            {
                Keyframe.TangentIn = value;
                this.RaisePropertyChanged();
            }
        }

        public float? TangentOut
        {
            get => Keyframe.TangentOut;
            set
            {
                Keyframe.TangentOut = value;
                this.RaisePropertyChanged();
            }
        }

        public float? Cp1x
        {
            get => Keyframe.Cp1x;
            set
            {
                Keyframe.Cp1x = value;
                this.RaisePropertyChanged();
            }
        }

        public float? Cp2x
        {
            get => Keyframe.Cp2x;
            set
            {
                Keyframe.Cp2x = value;
                this.RaisePropertyChanged();
            }
        }

        /// <summary>是否为贝塞尔类型 (UI 用于显示/隐藏控制手柄)</summary>
        public bool IsBezier => Interpolation == "bezier";

        /// <summary>事件名称 (触发时客户端可接收)</summary>
        public string EventName
        {
            get => Keyframe.Event?.EventName ?? "";
            set
            {
                EnsureEvent();
                Keyframe.Event!.EventName = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(HasEvent));
                // 如果名称和数据都为空, 清除事件对象
                if (string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(EventData))
                    Keyframe.Event = null;
            }
        }

        /// <summary>事件参数 (JSON 字符串)</summary>
        public string EventData
        {
            get => Keyframe.Event?.EventData ?? "";
            set
            {
                EnsureEvent();
                Keyframe.Event!.EventData = string.IsNullOrWhiteSpace(value) ? null : value;
                this.RaisePropertyChanged();
            }
        }

        /// <summary>是否有事件绑定</summary>
        public bool HasEvent =>
            Keyframe.Event != null && !string.IsNullOrWhiteSpace(Keyframe.Event.EventName);

        private void EnsureEvent()
        {
            Keyframe.Event ??= new MotionEvent();
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }
    }
}
