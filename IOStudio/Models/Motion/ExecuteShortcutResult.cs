namespace IOStudio.Models.Motion
{
    /// <summary>
    /// ExecuteShortcut 的返回结果 — 指示 View 层后续需要执行的 UI 同步操作
    /// </summary>
    public enum ExecuteShortcutResult
    {
        /// <summary>未识别或不可执行</summary>
        NotHandled,

        /// <summary>已处理, 无需 View 层额外操作</summary>
        Handled,

        /// <summary>已处理, View 层需要刷新控件和属性面板</summary>
        HandledNeedsRefresh,

        /// <summary>已处理, View 层需要同步多选状态并刷新</summary>
        HandledNeedsMultiSelectRefresh,

        /// <summary>已处理, View 层需要同步剪贴板状态</summary>
        HandledNeedsSyncClipboard,

        /// <summary>已处理, View 层需要同步缩放到时间尺</summary>
        HandledNeedsSyncZoom,

        /// <summary>已处理, View 层需要同步标记到时间尺</summary>
        HandledNeedsSyncMarkers,

        /// <summary>已处理, View 层需要同步工作区域到时间尺</summary>
        HandledNeedsSyncWorkArea,

        /// <summary>已处理, View 层需要同步事件到时间尺并刷新属性面板</summary>
        HandledNeedsSyncEvents,

        /// <summary>ViewModel 无法处理, 需要委托 View 层执行 (对话框/导航等)</summary>
        DelegateToView,
    }
}
