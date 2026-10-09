using IOStudio.ViewModels;

namespace IOStudio.Views
{
    /// <summary>
    /// 从主窗"弹出来"的单路会话窗口 —— 把一路会话放到独立窗口 / 第二块屏上盯。
    ///
    /// <para>窗口本身只是外壳, 会话工作面复用 <see cref="ProtocolSessionPanel"/>;
    /// 会话对象与主窗共享同一个实例, 所以收发的每一帧、统计都实时同步。</para>
    ///
    /// <para>关闭本窗口 = 把会话**放回主窗标签** (不销毁), 由
    /// <see cref="ProtocolTestWindow"/> 负责回收。</para>
    /// </summary>
    public partial class ProtocolSessionWindow : Avalonia.Controls.Window
    {
        public ProtocolSessionWindow()
        {
            InitializeComponent();
        }

        public ProtocolSessionWindow(ProtocolSessionViewModel session)
            : this()
        {
            DataContext = session;
            Title = string.IsNullOrWhiteSpace(session.Label) ? "协议会话" : session.Label;
        }
    }
}
