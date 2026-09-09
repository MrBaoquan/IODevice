using Avalonia.Input;
using IOStudio.Models.Motion;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 键盘快捷键服务 — 集中管理所有时间轴编辑器快捷键映射
    /// 将按键事件解析为语义化的 ShortcutAction 枚举
    /// </summary>
    public class KeyboardShortcutService : IKeyboardShortcutService
    {
        /// <summary>
        /// 解析按键事件并返回对应的快捷键动作
        /// </summary>
        public ShortcutAction Resolve(
            Key key,
            KeyModifiers modifiers,
            bool isVideoFullscreen = false,
            bool hasWorkArea = false
        )
        {
            bool ctrl = modifiers.HasFlag(KeyModifiers.Control);
            bool shift = modifiers.HasFlag(KeyModifiers.Shift);

            // ESC 上下文敏感: 视频全屏 > 工作区域 > 无操作
            if (key is Key.Escape)
            {
                if (isVideoFullscreen)
                    return ShortcutAction.ExitVideoFullscreen;
                if (hasWorkArea)
                    return ShortcutAction.ClearWorkArea;
                return ShortcutAction.None;
            }

            // Ctrl 组合键
            if (ctrl)
            {
                return (key, shift) switch
                {
                    (Key.Z, false) => ShortcutAction.Undo,
                    (Key.Z, true) => ShortcutAction.Redo,
                    (Key.Y, _) => ShortcutAction.Redo,
                    (Key.S, true) => ShortcutAction.SaveAs,
                    (Key.S, false) => ShortcutAction.Save,
                    (Key.N, _) => ShortcutAction.NewProject,
                    (Key.O, _) => ShortcutAction.OpenFile,
                    (Key.C, _) => ShortcutAction.Copy,
                    (Key.V, _) => ShortcutAction.Paste,
                    (Key.D, _) => ShortcutAction.DuplicateKeyframe,
                    (Key.Left, _) => ShortcutAction.PreviousMarker,
                    (Key.Right, _) => ShortcutAction.NextMarker,
                    (Key.D0, _) => ShortcutAction.ZoomToFit,
                    _ => ShortcutAction.None,
                };
            }

            // 无修饰符快捷键 (Shift 按下时不匹配符号键, 避免 _ 触发 ZoomOut)
            if (shift)
            {
                // Shift 组合但非 Ctrl: 仅匹配不会产生符号歧义的键
                return key switch
                {
                    Key.Delete or Key.Back => ShortcutAction.Delete,
                    Key.Home => ShortcutAction.SeekToStart,
                    Key.End => ShortcutAction.SeekToEnd,
                    Key.Left => ShortcutAction.PreviousKeyframe,
                    Key.Right => ShortcutAction.NextKeyframe,
                    _ => ShortcutAction.None,
                };
            }

            return key switch
            {
                Key.Delete or Key.Back => ShortcutAction.Delete,
                Key.Space => ShortcutAction.PlayPauseToggle,
                Key.Home => ShortcutAction.SeekToStart,
                Key.End => ShortcutAction.SeekToEnd,
                Key.K => ShortcutAction.AddKeyframeAtPlayhead,
                Key.E => ShortcutAction.AddEventAtPlayhead,
                Key.F => ShortcutAction.ZoomToFit,
                Key.F2 => ShortcutAction.RenameSelected,
                Key.H => ShortcutAction.CycleTrackHeight,
                Key.M => ShortcutAction.AddMarkerAtPlayhead,
                Key.I => ShortcutAction.SetWorkAreaIn,
                Key.O => ShortcutAction.SetWorkAreaOut,
                Key.Left => ShortcutAction.PreviousKeyframe,
                Key.Right => ShortcutAction.NextKeyframe,
                Key.Up => ShortcutAction.SelectPreviousTrack,
                Key.Down => ShortcutAction.SelectNextTrack,
                Key.OemPlus or Key.Add => ShortcutAction.ZoomIn,
                Key.OemMinus or Key.Subtract => ShortcutAction.ZoomOut,
                _ => ShortcutAction.None,
            };
        }
    }
}
