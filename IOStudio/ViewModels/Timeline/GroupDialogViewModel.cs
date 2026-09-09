using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;

namespace IOStudio.ViewModels.Timeline
{
    /// <summary>
    /// 分组对话框操作模式。
    /// </summary>
    public enum GroupDialogMode
    {
        /// <summary>新建分组 (简单 — 仅输入名称)</summary>
        NewSimple,

        /// <summary>新建分组 (面板级 — 输入名称 + 选择轨道)</summary>
        NewWithTracks,

        /// <summary>重命名已有分组</summary>
        Rename,
    }

    /// <summary>
    /// 分组对话框 ViewModel — 支持新建/重命名分组, 可选轨道多选。
    /// </summary>
    public class GroupDialogViewModel : ViewModelBase
    {
        private string _groupName = "";

        /// <summary>分组名称 (双向绑定)。</summary>
        public string GroupName
        {
            get => _groupName;
            set
            {
                this.RaiseAndSetIfChanged(ref _groupName, value);
                this.RaisePropertyChanged(nameof(CanConfirm));
            }
        }

        private string _dialogTitle = "新建分组";

        /// <summary>对话框标题。</summary>
        public string DialogTitle
        {
            get => _dialogTitle;
            set => this.RaiseAndSetIfChanged(ref _dialogTitle, value);
        }

        private string _prompt = "请输入分组名称:";

        /// <summary>提示文本。</summary>
        public string Prompt
        {
            get => _prompt;
            set => this.RaiseAndSetIfChanged(ref _prompt, value);
        }

        /// <summary>对话框模式。</summary>
        public GroupDialogMode Mode { get; }

        /// <summary>是否显示轨道选择列表。</summary>
        public bool ShowTrackSelection => Mode == GroupDialogMode.NewWithTracks;

        /// <summary>可选轨道列表 (带勾选状态)。</summary>
        public ObservableCollection<SelectableTrackItem> AvailableTracks { get; } = new();

        /// <summary>用户是否确认了操作 (而非取消)。</summary>
        public bool IsConfirmed { get; private set; }

        /// <summary>是否可以确认 (名称非空)。</summary>
        public bool CanConfirm => !string.IsNullOrWhiteSpace(GroupName);

        /// <summary>确认命令。</summary>
        public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }

        /// <summary>取消命令。</summary>
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }

        /// <summary>请求关闭对话框的事件。</summary>
        public event System.Action? CloseRequested;

        /// <summary>
        /// 创建分组对话框 ViewModel。
        /// </summary>
        /// <param name="mode">对话框模式。</param>
        /// <param name="existingName">重命名模式时的旧名称。</param>
        public GroupDialogViewModel(GroupDialogMode mode, string? existingName = null)
        {
            Mode = mode;

            switch (mode)
            {
                case GroupDialogMode.NewSimple:
                    DialogTitle = "新建分组";
                    Prompt = "请输入分组名称:";
                    break;
                case GroupDialogMode.NewWithTracks:
                    DialogTitle = "新建分组";
                    Prompt = "分组名称:";
                    break;
                case GroupDialogMode.Rename:
                    DialogTitle = "重命名分组";
                    Prompt = "分组名称:";
                    GroupName = existingName ?? "";
                    break;
            }

            var canConfirm = this.WhenAnyValue(x => x.GroupName)
                .Select(name => !string.IsNullOrWhiteSpace(name));

            ConfirmCommand = ReactiveCommand.Create(OnConfirm, canConfirm);
            CancelCommand = ReactiveCommand.Create(OnCancel);
        }

        /// <summary>
        /// 填充可选轨道列表 (仅 NewWithTracks 模式使用)。
        /// </summary>
        /// <param name="tracks">轨道列表。</param>
        /// <param name="preSelectedTrack">预选中的轨道。</param>
        public void PopulateTracks(
            ObservableCollection<TrackViewModel> tracks,
            TrackViewModel? preSelectedTrack = null
        )
        {
            AvailableTracks.Clear();
            foreach (var t in tracks)
            {
                var display =
                    $"{t.Label}  ({t.DeviceName}/{t.OActionName})"
                    + (string.IsNullOrEmpty(t.Group) ? "" : $"  [{t.Group}]");
                var item = new SelectableTrackItem(t, display)
                {
                    IsSelected = t == preSelectedTrack
                };
                AvailableTracks.Add(item);
            }
        }

        /// <summary>获取用户选中的轨道列表。</summary>
        public TrackViewModel[] GetSelectedTracks()
        {
            return AvailableTracks.Where(t => t.IsSelected).Select(t => t.Track).ToArray();
        }

        private void OnConfirm()
        {
            IsConfirmed = true;
            CloseRequested?.Invoke();
        }

        private void OnCancel()
        {
            IsConfirmed = false;
            CloseRequested?.Invoke();
        }
    }

    /// <summary>
    /// 可选轨道项 — 用于分组对话框中的轨道多选列表。
    /// </summary>
    public class SelectableTrackItem : ViewModelBase
    {
        /// <summary>关联的轨道 ViewModel。</summary>
        public TrackViewModel Track { get; }

        /// <summary>显示文本。</summary>
        public string DisplayText { get; }

        private bool _isSelected;

        /// <summary>是否选中。</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }

        public SelectableTrackItem(TrackViewModel track, string displayText)
        {
            Track = track;
            DisplayText = displayText;
        }
    }
}
