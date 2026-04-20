using Avalonia.Input;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 键盘快捷键服务接口 — 将按键事件映射到编辑器动作
    /// </summary>
    public interface IKeyboardShortcutService
    {
        /// <summary>
        /// 解析按键事件并返回对应的快捷键动作
        /// </summary>
        /// <param name="key">按下的键</param>
        /// <param name="modifiers">修饰键状态</param>
        /// <param name="isVideoFullscreen">当前是否处于视频全屏模式</param>
        /// <param name="hasWorkArea">当前是否有工作区域</param>
        /// <returns>对应的 ShortcutAction, 无匹配返回 None</returns>
        Models.Motion.ShortcutAction Resolve(
            Key key,
            KeyModifiers modifiers,
            bool isVideoFullscreen = false,
            bool hasWorkArea = false
        );
    }
}
