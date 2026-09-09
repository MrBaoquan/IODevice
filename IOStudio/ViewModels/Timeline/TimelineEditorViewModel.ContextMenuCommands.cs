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

        private ReactiveCommand<TrackViewModel, Unit>? _toggleTrackIdleCmd;

        /// <summary>启用/停用轨道 idle 待机循环命令。</summary>
        public ReactiveCommand<TrackViewModel, Unit> ToggleTrackIdleCommand =>
            _toggleTrackIdleCmd ??= ReactiveCommand.Create<TrackViewModel>(t =>
            {
                if (t.IdleLoop == null)
                {
                    // 首次启用: 给一个默认呼吸循环 (与轨道属性对话框默认一致)
                    t.IdleLoop = new IdleLoop
                    {
                        Enabled = true,
                        PeriodMs = 1600.0,
                        BlendMs = 300.0,
                        PhaseMode = "continuous",
                        Keyframes =
                        {
                            new MotionKeyframe
                            {
                                TimeMs = 0,
                                Value = 0.5f,
                                Interpolation = "bezier"
                            },
                            new MotionKeyframe
                            {
                                TimeMs = 800,
                                Value = 0.6f,
                                Interpolation = "bezier"
                            },
                            new MotionKeyframe
                            {
                                TimeMs = 1600,
                                Value = 0.5f,
                                Interpolation = "bezier"
                            },
                        },
                    };
                }
                else
                {
                    t.IdleLoop.Enabled = !t.IdleLoop.Enabled;
                }
                NotifyTrackDataChanged(t);
                MarkDirty();
            });

        private ReactiveCommand<string, Unit>? _applyIdleTemplateCmd;

        /// <summary>一键应用待机模板到指定轨道 (右键菜单调用, 模板名参数)。</summary>
        public void ApplyIdleTemplateToTrack(TrackViewModel t, string template)
        {
            if (t == null)
                return;
            var period = t.IdleLoop?.PeriodMs ?? 1600.0;
            if (period < 100)
                period = 1600.0;
            (double t0, float v)[] pts = template switch
            {
                "breathing"
                    => new[]
                    {
                        (0.0, 0.5f),
                        (period * 0.25, 0.62f),
                        (period * 0.55, 0.55f),
                        (period * 0.8, 0.68f),
                        (period, 0.5f)
                    },
                "sway"
                    => new[]
                    {
                        (0.0, 0.5f),
                        (period * 0.2, 0.75f),
                        (period * 0.5, 0.45f),
                        (period * 0.8, 0.7f),
                        (period, 0.5f)
                    },
                "micro"
                    => new[]
                    {
                        (0.0, 0.52f),
                        (period * 0.3, 0.5f),
                        (period * 0.6, 0.54f),
                        (period, 0.52f)
                    },
                _ => new[] { (0.0, 0.5f), (period, 0.5f) },
            };
            t.IdleLoop ??= new IdleLoop { Enabled = true };
            t.IdleLoop.Enabled = true;
            t.IdleLoop.PeriodMs = period;
            t.IdleLoop.Keyframes = pts.Select(
                    p =>
                        new MotionKeyframe
                        {
                            TimeMs = p.t0,
                            Value = p.v,
                            Interpolation = "bezier"
                        }
                )
                .ToList();
            NotifyTrackDataChanged(t);
            MarkDirty();
        }

        /// <summary>一键应用待机模板到当前选中轨道 (属性面板按钮绑定, CommandParameter=模板名)。</summary>
        public ReactiveCommand<string, Unit> ApplyIdleTemplateToTrackCommand =>
            _applyIdleTemplateCmd ??= ReactiveCommand.Create<string>(template =>
            {
                if (SelectedTrack != null)
                    ApplyIdleTemplateToTrack(SelectedTrack, template);
            });

        private ReactiveCommand<TrackViewModel, Unit>? _editTrackIdleCmd;

        /// <summary>编辑待机循环曲线命令 — 选中该轨并切到曲线视图聚焦 idle (由 View 层联动)。</summary>
        public ReactiveCommand<TrackViewModel, Unit> EditTrackIdleCommand =>
            _editTrackIdleCmd ??= ReactiveCommand.Create<TrackViewModel>(t =>
            {
                SelectedTrack = t;
                // View 层监听 IdleCurveEditRequested 切换视图/聚焦 (见 TimelineEditorWindow)
                IdleCurveEditRequested?.Invoke(t);
            });

        /// <summary>请求编辑某轨道的 idle 待机循环曲线 (View 层联动切换视图/聚焦)。</summary>
        public event Action<TrackViewModel>? IdleCurveEditRequested;

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
