using System;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using IOStudio.ViewModels;
using Key = Avalonia.Input.Key;

namespace IOStudio.Views
{
    /// <summary>
    /// 单路会话的工作面 (连接 → 日志 → 发送 → 工具)。
    ///
    /// <para>抽成独立控件是为了**同一份 UI 能在两个宿主里复用**: 主窗口的标签区,
    /// 以及"把会话弹到独立窗口"后的那个窗口。谁宿主它, 行为都一致 ——
    /// 发送框的 Ctrl+Enter/历史键、日志的自动滚动都随控件走, 不必在两个窗口里各写一遍。</para>
    /// </summary>
    public partial class ProtocolSessionPanel : UserControl
    {
        private ProtocolSessionViewModel? _watched;

        public ProtocolSessionPanel()
        {
            InitializeComponent();

            // 发送框的人机习惯: Ctrl+Enter 发送, ↑/↓ 调历史 (终端级肌肉记忆)
            SendBox.KeyDown += OnSendBoxKeyDown;

            // 「自动滚动」得真的滚才有意义: 跟着本控件承载的会话的消息集合走
            DataContextChanged += (_, _) =>
                WatchSession(DataContext as ProtocolSessionViewModel);
        }

        private void OnSendBoxKeyDown(object? sender, KeyEventArgs e)
        {
            if (DataContext is not ProtocolSessionViewModel vm)
                return;

            if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                e.Handled = true;
                if (vm.IsRunning && !vm.HasSendTextError)
                    vm.SendCommand.Execute().Subscribe();
                return;
            }

            // 只有无修饰键的上下键才当历史翻页 (留给将来的多行编辑)
            if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Up && vm.HistoryPrev())
                e.Handled = true;
            else if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Down && vm.HistoryNext())
                e.Handled = true;
        }

        private void WatchSession(ProtocolSessionViewModel? session)
        {
            if (_watched is not null)
                _watched.Messages.CollectionChanged -= OnMessagesChanged;

            _watched = session;

            if (_watched is not null)
                _watched.Messages.CollectionChanged += OnMessagesChanged;
        }

        private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // 集合只在 UI 线程被改动 (会话 VM 内部 Post 回 UI 线程), 故这里可直接操作控件
            if (e.Action != NotifyCollectionChangedAction.Add)
                return;

            var session = _watched;
            if (session is null || !session.AutoScroll)
                return;

            var last = session.Messages.Count - 1;
            if (last >= 0)
                MessageList.ScrollIntoView(last);
        }
    }
}
