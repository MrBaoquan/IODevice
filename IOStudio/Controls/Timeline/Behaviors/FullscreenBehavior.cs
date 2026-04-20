using System;
using Avalonia;
using Avalonia.Controls;

namespace IOStudio.Controls.Timeline.Behaviors
{
    /// <summary>
    /// 视频全屏切换行为 — 管理窗口进入/退出真全屏模式时的布局保存与恢复。
    /// 提取自 TimelineEditorWindow.axaml.cs 中 ~120 行全屏切换逻辑。
    /// </summary>
    /// <remarks>
    /// 真全屏: 无标题栏 + 窗口撑满屏幕, 隐藏属性面板和时间轴区域。
    /// </remarks>
    public class FullscreenBehavior
    {
        private readonly Window _window;
        private bool _isFullscreen;

        // 保存的布局状态
        private GridLength _savedUpperRowHeight;
        private GridLength _savedLowerRowHeight;
        private GridLength _savedPropertyColWidth;
        private WindowState _savedWindowState;
        private SystemDecorations _savedSystemDecorations;

        /// <summary>当前是否处于全屏模式。</summary>
        public bool IsFullscreen => _isFullscreen;

        public FullscreenBehavior(Window window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
        }

        /// <summary>
        /// 切换视频全屏模式。
        /// </summary>
        /// <param name="enterFullscreen">true = 进入全屏, false = 退出全屏。</param>
        public void Toggle(bool enterFullscreen)
        {
            _isFullscreen = enterFullscreen;

            var mainGrid = _window.FindControl<Grid>("MainContentGrid");
            var upperGrid = _window.FindControl<Grid>("UpperGrid");
            var splitter = _window.FindControl<GridSplitter>("MainHorizontalSplitter");
            var lowerPanel = _window.FindControl<DockPanel>("LowerPanel");
            var kfPanel = _window.FindControl<Control>("KfPropertyPanel");
            var vertSplitter = _window.FindControl<GridSplitter>("UpperVerticalSplitter");

            if (mainGrid == null || upperGrid == null)
                return;

            if (enterFullscreen)
            {
                EnterFullscreen(mainGrid, upperGrid, splitter, lowerPanel, kfPanel, vertSplitter);
            }
            else
            {
                ExitFullscreen(mainGrid, upperGrid, splitter, lowerPanel, kfPanel, vertSplitter);
            }
        }

        private void EnterFullscreen(
            Grid mainGrid,
            Grid upperGrid,
            GridSplitter? splitter,
            DockPanel? lowerPanel,
            Control? kfPanel,
            GridSplitter? vertSplitter
        )
        {
            // 保存当前布局和窗口状态
            _savedUpperRowHeight = mainGrid.RowDefinitions[0].Height;
            _savedLowerRowHeight = mainGrid.RowDefinitions[2].Height;
            _savedPropertyColWidth = upperGrid.ColumnDefinitions[2].Width;
            _savedWindowState = _window.WindowState;
            _savedSystemDecorations = _window.SystemDecorations;

            // 隐藏下半部和分隔条
            if (splitter != null)
                splitter.IsVisible = false;
            if (lowerPanel != null)
                lowerPanel.IsVisible = false;
            mainGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            mainGrid.RowDefinitions[0].MinHeight = 0;
            mainGrid.RowDefinitions[2].Height = new GridLength(0);
            mainGrid.RowDefinitions[2].MinHeight = 0;

            // 隐藏右侧属性面板
            if (kfPanel != null)
                kfPanel.IsVisible = false;
            if (vertSplitter != null)
                vertSplitter.IsVisible = false;
            upperGrid.ColumnDefinitions[2].Width = new GridLength(0);
            upperGrid.ColumnDefinitions[2].MinWidth = 0;

            // 真全屏: 去掉标题栏, 窗口撑满屏幕
            _window.SystemDecorations = SystemDecorations.None;
            _window.WindowState = WindowState.FullScreen;
        }

        private void ExitFullscreen(
            Grid mainGrid,
            Grid upperGrid,
            GridSplitter? splitter,
            DockPanel? lowerPanel,
            Control? kfPanel,
            GridSplitter? vertSplitter
        )
        {
            // 恢复窗口状态
            _window.WindowState = _savedWindowState;
            _window.SystemDecorations = _savedSystemDecorations;

            // 恢复下半部和分隔条
            if (splitter != null)
                splitter.IsVisible = true;
            if (lowerPanel != null)
                lowerPanel.IsVisible = true;
            mainGrid.RowDefinitions[0].Height = _savedUpperRowHeight;
            mainGrid.RowDefinitions[0].MinHeight = 120;
            mainGrid.RowDefinitions[2].Height = _savedLowerRowHeight;
            mainGrid.RowDefinitions[2].MinHeight = 180;

            // 恢复右侧属性面板
            if (kfPanel != null)
                kfPanel.IsVisible = true;
            if (vertSplitter != null)
                vertSplitter.IsVisible = true;
            upperGrid.ColumnDefinitions[2].Width = _savedPropertyColWidth;
            upperGrid.ColumnDefinitions[2].MinWidth = 220;
        }
    }
}
