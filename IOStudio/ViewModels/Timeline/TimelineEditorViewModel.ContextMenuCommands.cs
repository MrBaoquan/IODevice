using System;
using System.Linq;
using System.Reactive;
using Avalonia.Threading;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using ReactiveUI;

namespace IOStudio.ViewModels.Timeline
{
    /// <summary>
    /// 上下文菜单命令 — 轨道/分组右键菜单操作。
    /// 替代 View code-behind 中的 OnCtx* 处理程序。
    /// </summary>
    public partial class TimelineEditorViewModel
    {
        // ---- 轨道上下文菜单命令 ----

        private ReactiveCommand<TrackViewModel, Unit>? _moveTrackUpCmd;

        /// <summary>上移轨道命令 (CommandParameter = TrackViewModel)。</summary>
        public ReactiveCommand<TrackViewModel, Unit> MoveTrackUpCommand =>
            _moveTrackUpCmd ??= ReactiveCommand.Create<TrackViewModel>(MoveTrackUp);

        private ReactiveCommand<TrackViewModel, Unit>? _moveTrackDownCmd;

        /// <summary>下移轨道命令。</summary>
        public ReactiveCommand<TrackViewModel, Unit> MoveTrackDownCommand =>
            _moveTrackDownCmd ??= ReactiveCommand.Create<TrackViewModel>(MoveTrackDown);

        private ReactiveCommand<TrackViewModel, Unit>? _deleteTrackCmd;

        /// <summary>删除轨道命令。</summary>
        public ReactiveCommand<TrackViewModel, Unit> DeleteTrackCommand =>
            _deleteTrackCmd ??= ReactiveCommand.Create<TrackViewModel>(RemoveTrack);

        private ReactiveCommand<TrackViewModel, Unit>? _toggleTrackLockCmd;

        /// <summary>锁定/解锁轨道命令。</summary>
        public ReactiveCommand<TrackViewModel, Unit> ToggleTrackLockCommand =>
            _toggleTrackLockCmd ??= ReactiveCommand.Create<TrackViewModel>(
                t => t.IsLocked = !t.IsLocked
            );

        private ReactiveCommand<TrackViewModel, Unit>? _toggleTrackMuteCmd;

        /// <summary>静音/取消静音轨道命令。</summary>
        public ReactiveCommand<TrackViewModel, Unit> ToggleTrackMuteCommand =>
            _toggleTrackMuteCmd ??= ReactiveCommand.Create<TrackViewModel>(
                t => t.IsMuted = !t.IsMuted
            );

        private ReactiveCommand<TrackViewModel, Unit>? _removeFromGroupCmd;

        /// <summary>移出分组命令。</summary>
        public ReactiveCommand<TrackViewModel, Unit> RemoveFromGroupCommand =>
            _removeFromGroupCmd ??= ReactiveCommand.Create<TrackViewModel>(RemoveTrackFromGroup);

        /// <summary>设置轨道颜色 — 由 View 层通过参数传入颜色字符串。</summary>
        public void SetTrackColor(TrackViewModel track, string color)
        {
            track.Color = color;
            MarkDirty();
        }

        // ---- 分组上下文菜单命令 ----

        private ReactiveCommand<GroupHeaderViewModel, Unit>? _deleteGroupCmd;

        /// <summary>删除分组命令 (CommandParameter = GroupHeaderViewModel)。</summary>
        public ReactiveCommand<GroupHeaderViewModel, Unit> DeleteGroupCommand =>
            _deleteGroupCmd ??= ReactiveCommand.Create<GroupHeaderViewModel>(
                g => DeleteGroup(g.Name)
            );

        private ReactiveCommand<Unit, Unit>? _collapseAllGroupsCmd;

        /// <summary>折叠所有分组命令。</summary>
        public ReactiveCommand<Unit, Unit> CollapseAllGroupsCommand =>
            _collapseAllGroupsCmd ??= ReactiveCommand.Create(() =>
            {
                foreach (var gn in GetGroupNames())
                    SetGroupCollapsed(gn, true);
            });

        private ReactiveCommand<Unit, Unit>? _expandAllGroupsCmd;

        /// <summary>展开所有分组命令。</summary>
        public ReactiveCommand<Unit, Unit> ExpandAllGroupsCommand =>
            _expandAllGroupsCmd ??= ReactiveCommand.Create(() =>
            {
                foreach (var gn in GetGroupNames())
                    SetGroupCollapsed(gn, false);
            });
    }
}
