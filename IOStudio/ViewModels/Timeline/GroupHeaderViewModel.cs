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

        public GroupHeaderViewModel(TrackGroup model)
        {
            Model = model;
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
            }
        }

        /// <summary>展开/折叠图标</summary>
        public string ExpandIcon => IsCollapsed ? "▶" : "▼";

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
        public string TrackCountDisplay => $"({TrackCount})";

        /// <summary>标记: 这是分组头 (用于 DataTemplate 选择)</summary>
        public bool IsGroupHeader => true;
    }
}
