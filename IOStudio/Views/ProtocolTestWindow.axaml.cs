using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using IOStudio.ViewModels;
using ReactiveUI.Avalonia;

namespace IOStudio.Views
{
    /// <summary>
    /// 协议测试台窗口。
    ///
    /// <para>用途: 用 UI 观察 TCP/UDP/WebSocket/串口 收发到的每一帧, 并可直接运行
    /// 内置场景 (与 CLI <c>--protocol-test</c> 共用同一套执行器, 结论一致)。
    /// 适合"CLI 跑门禁、人在 UI 侧核对收到的报文"这种分工。</para>
    ///
    /// <para>本窗口只负责**外壳**: 工具条、会话标签条、状态栏, 以及把选中的会话交给
    /// <see cref="ProtocolSessionPanel"/> 承载。会话也可以被"弹"到独立窗口
    /// (<see cref="ProtocolSessionWindow"/>), 两处复用同一个面板控件。</para>
    /// </summary>
    public partial class ProtocolTestWindow : ReactiveWindow<ProtocolTestViewModel>
    {
        /// <summary>串口列表自动刷新 (插拔设备后下拉能自己更新)。窗口关闭即停。</summary>
        private readonly Avalonia.Threading.DispatcherTimer _serialPortTimer;

        /// <summary>已弹出的独立窗口 (会话 → 窗口), 用于回收入主窗与关窗时统一关闭。</summary>
        private readonly Dictionary<ProtocolSessionViewModel, ProtocolSessionWindow> _detached = new();

        private bool _closing;

        public ProtocolTestWindow()
        {
            InitializeComponent();
            DataContext = ViewModel = new ProtocolTestViewModel();

            // 文件选择器由窗口注入 —— ViewModel 不依赖 UI, 才能脱离桌面做确定性测试
            ViewModel.FilePicker = PickFileAsync;

            // 剪贴板同样由窗口注入 (行内"复制"要能落到系统剪贴板)
            ViewModel.ClipboardWriter = CopyToClipboardAsync;

            // "弹到独立窗口"由窗口实现 (VM 不 new 窗口, 才不依赖 UI)
            ViewModel.DetachRequested = DetachSession;

            // 串口热插拔: 每 2 秒重扫一次可用端口
            _serialPortTimer = new Avalonia.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2),
            };
            _serialPortTimer.Tick += (_, _) => ViewModel?.RefreshAllSerialPorts();
            _serialPortTimer.Start();

            // 关窗必须释放监听端口/取消接收循环, 否则端口会被窗口"占着"到进程退出
            Closed += (_, _) =>
            {
                _closing = true;
                _serialPortTimer.Stop();

                // 先关独立窗口, 再释放会话 (此时不必把它们放回主窗)
                foreach (var w in _detached.Values.ToList())
                {
                    try
                    {
                        w.Close();
                    }
                    catch
                    {
                        /* ignore */
                    }
                }
                _detached.Clear();

                ViewModel?.Dispose();
            };
        }

        /// <summary>
        /// 把一路会话弹到独立窗口: 从主窗标签移出 (会话对象不销毁), 关窗后再放回。
        /// </summary>
        private void DetachSession(ProtocolSessionViewModel session)
        {
            if (_detached.TryGetValue(session, out var existing))
            {
                existing.Activate();
                return;
            }

            var index = ViewModel?.DetachSession(session) ?? -1;
            var window = new ProtocolSessionWindow(session);
            _detached[session] = window;

            window.Closed += (_, _) =>
            {
                _detached.Remove(session);
                // 主窗还开着 → 把会话放回原位置; 主窗正在关 → 交给 Dispose
                if (!_closing)
                    ViewModel?.AttachSession(session, index);
            };

            window.Show(this);
        }

        private async Task CopyToClipboardAsync(string text)
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
                await clipboard.SetTextAsync(text ?? "");
        }

        private async Task<string?> PickFileAsync()
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
