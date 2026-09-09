using IOStudio.Models.Motion;
using ReactiveUI;

namespace IOStudio.ViewModels.Timeline
{
    /// <summary>
    /// 分组头 ViewModel — 在轨道列表中作为独立行显示
    /// 对应 PS/AE 风格的分组文件夹行
    /// </summary>
    public class GroupHeaderViewModel : ViewModelBase
    {
        public TrackGroup Model { get; }

        /// <summary>当 Solo/Mute 变更时通知外部 (用于级联到组内轨道) — UX-B4</summary>
        public System.Action<GroupHeaderViewModel>? SoloMuteChanged;

        public GroupHeaderViewModel(TrackGroup model)
        {
            Model = model;
            _isMuted = model.Muted;
            _isSolo = model.Soloed;
            _isCollapsed = model.Collapsed;
        }

        /// <summary>分组名称</summary>
        public string Name
        {
            get => Model.Name;
            set
            {
                Model.Name = value;
                this.RaisePropertyChanged();
            }
        }

        /// <summary>分组颜色 (hex)</summary>
        public string Color
        {
            get => Model.Color;
            set
            {
                Model.Color = value;
                this.RaisePropertyChanged();
            }
        }

        /// <summary>是否折叠</summary>
        private bool _isCollapsed;
        public bool IsCollapsed
        {
            get => _isCollapsed;
            set
            {
                this.RaiseAndSetIfChanged(ref _isCollapsed, value);
                Model.Collapsed = value;
                this.RaisePropertyChanged(nameof(ExpandIcon));
                this.RaisePropertyChanged(nameof(ExpandActionLabel));
            }
        }

        /// <summary>展开/折叠图标</summary>
        public string ExpandIcon => IsCollapsed ? "▶" : "▼";

        /// <summary>展开/折叠动作提示</summary>
        public string ExpandActionLabel => IsCollapsed ? "展开分组" : "折叠分组";

        /// <summary>组内轨道数</summary>
        private int _trackCount;
        public int TrackCount
        {
            get => _trackCount;
            set
            {
                this.RaiseAndSetIfChanged(ref _trackCount, value);
                this.RaisePropertyChanged(nameof(TrackCountDisplay));
            }
        }

        /// <summary>轨道数显示</summary>
        public string TrackCountDisplay => $"{TrackCount} 条轨道";

        /// <summary>标记: 这是分组头 (用于 DataTemplate 选择)</summary>
        public bool IsGroupHeader => true;

        // ───── UX-B4 分组 Solo / Mute ─────

        private bool _isMuted;
        /// <summary>分组静音 — 级联到组内轨道的 IsMuted</summary>
        public bool IsMuted
        {
            get => _isMuted;
            set
            {
                if (_isMuted == value)
                    return;
                _isMuted = value;
                Model.Muted = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(MuteActionLabel));
                SoloMuteChanged?.Invoke(this);
            }
        }

        private bool _isSolo;
        /// <summary>分组独奏 — 级联到组内轨道的 IsSolo</summary>
        public bool IsSolo
        {
            get => _isSolo;
            set
            {
                if (_isSolo == value)
                    return;
                _isSolo = value;
                Model.Soloed = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(SoloActionLabel));
                SoloMuteChanged?.Invoke(this);
            }
        }

        /// <summary>分组静音动作提示</summary>
        public string MuteActionLabel => IsMuted ? "取消分组静音" : "分组静音";

        /// <summary>分组独奏动作提示</summary>
        public string SoloActionLabel => IsSolo ? "取消分组独奏" : "分组独奏";
    }
}
