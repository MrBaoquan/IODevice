using System;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using IOStudio.ViewModels;
using ReactiveUI;
using ReactiveUI.Avalonia;
using Key = Avalonia.Input.Key;

namespace IOStudio.Views
{
    /// <summary>
    /// 协议测试台窗口。
    ///
    /// <para>用途: 用 UI 观察 TCP/UDP/WebSocket/串口 收发到的每一帧, 并可直接运行
    /// 内置场景 (与 CLI <c>--protocol-test</c> 共用同一套执行器, 结论一致)。
    /// 适合"CLI 跑门禁、人在 UI 侧核对收到的报文"这种分工。</para>
    /// </summary>
    public partial class ProtocolTestWindow : ReactiveWindow<ProtocolTestViewModel>
    {
        private readonly IDisposable _selectionSub;
        private ProtocolSessionViewModel? _watched;

        public ProtocolTestWindow()
        {
            InitializeComponent();
            DataContext = ViewModel = new ProtocolTestViewModel();

            // 文件选择器由窗口注入 —— ViewModel 不依赖 UI, 才能脱离桌面做确定性测试
            ViewModel.FilePicker = PickFileAsync;

            // 剪贴板同样由窗口注入 (行内"复制"要能落到系统剪贴板)
            ViewModel.ClipboardWriter = CopyToClipboardAsync;

            // 发送框的人机习惯: Ctrl+Enter 发送, ↑/↓ 调历史 (终端级肌肉记忆)
            SendBox.KeyDown += OnSendBoxKeyDown;

            // 「自动滚动」得真的滚才有意义: 跟着选中会话的消息集合走
            _selectionSub = ViewModel.WhenAnyValue(x => x.SelectedSession).Subscribe(WatchSession);

            // 关窗必须释放监听端口/取消接收循环, 否则端口会被窗口"占着"到进程退出
            Closed += (_, _) =>
            {
                WatchSession(null);
                _selectionSub.Dispose();
                ViewModel?.Dispose();
            };
        }

        private void OnSendBoxKeyDown(object? sender, KeyEventArgs e)
        {
            var vm = ViewModel?.SelectedSession;
            if (vm is null)
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

        private async System.Threading.Tasks.Task CopyToClipboardAsync(string text)
        {
            var clipboard = Avalonia.Controls.TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
                await clipboard.SetTextAsync(text ?? "");
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

        private async System.Threading.Tasks.Task<string?> PickFileAsync()
        {
            var files = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "选择要发送的文件",
                    AllowMultiple = false,
                }
            );
            var first = files?.FirstOrDefault();
            return first?.TryGetLocalPath();
        }
    }
}
