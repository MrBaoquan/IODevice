namespace IOStudio.Models.Motion
{
    /// <summary>
    /// 时间轴编辑器键盘快捷键动作枚举
    /// 每个值对应一个可执行的编辑器操作
    /// </summary>
    public enum ShortcutAction
    {
        /// <summary>无操作</summary>
        None = 0,

        // ---- 文件操作 ----

        /// <summary>新建项目 (Ctrl+N)</summary>
        NewProject,

        /// <summary>打开文件 (Ctrl+O)</summary>
        OpenFile,

        /// <summary>保存 (Ctrl+S)</summary>
        Save,

        /// <summary>另存为 (Ctrl+Shift+S)</summary>
        SaveAs,

        // ---- 编辑操作 ----

        /// <summary>撤销 (Ctrl+Z)</summary>
        Undo,

        /// <summary>重做 (Ctrl+Y / Ctrl+Shift+Z)</summary>
        Redo,

        /// <summary>复制关键帧 (Ctrl+C)</summary>
        Copy,

        /// <summary>粘贴关键帧 (Ctrl+V)</summary>
        Paste,

        /// <summary>删除选中关键帧 (Delete / Backspace)</summary>
        Delete,

        /// <summary>复制关键帧到播放头 (Ctrl+D)</summary>
        DuplicateKeyframe,

        // ---- 播放控制 ----

        /// <summary>播放/暂停切换 (Space)</summary>
        PlayPauseToggle,

        /// <summary>跳到起始 (Home)</summary>
        SeekToStart,

        /// <summary>跳到末尾 (End)</summary>
        SeekToEnd,

        // ---- 关键帧/事件 ----

        /// <summary>在播放头位置添加关键帧 (K)</summary>
        AddKeyframeAtPlayhead,

        /// <summary>在播放头位置添加独立事件 (E)</summary>
        AddEventAtPlayhead,

        // ---- 导航 ----

        /// <summary>跳转到前一个关键帧 (←)</summary>
        PreviousKeyframe,

        /// <summary>跳转到后一个关键帧 (→)</summary>
        NextKeyframe,

        /// <summary>跳转到上一个标记 (Ctrl+←)</summary>
        PreviousMarker,

        /// <summary>跳转到下一个标记 (Ctrl+→)</summary>
        NextMarker,

        // ---- 视图 ----

        /// <summary>放大时间轴 (+)</summary>
        ZoomIn,

        /// <summary>缩小时间轴 (-)</summary>
        ZoomOut,

        /// <summary>自适应缩放 (F / Ctrl+0)</summary>
        ZoomToFit,

        /// <summary>循环切换轨道高度 (H)</summary>
        CycleTrackHeight,

        // ---- 标记 / 工作区域 ----

        /// <summary>在播放头位置添加标记 (M)</summary>
        AddMarkerAtPlayhead,

        /// <summary>设置工作区域入点 (I)</summary>
        SetWorkAreaIn,

        /// <summary>设置工作区域出点 (O)</summary>
        SetWorkAreaOut,

        /// <summary>清除工作区域 (Escape)</summary>
        ClearWorkArea,

        // ---- 特殊 ----

        /// <summary>退出视频全屏 (Escape, 仅在全屏时)</summary>
        ExitVideoFullscreen,

        /// <summary>重命名选中轨道 / 事件 / 标记 (F2) — UX-D1</summary>
        RenameSelected,

        /// <summary>选择上一轨道 (↑)</summary>
        SelectPreviousTrack,

        /// <summary>选择下一轨道 (↓)</summary>
        SelectNextTrack,

        // ---- 视图操作 ----
    }
}
