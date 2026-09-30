using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Xunit;

namespace IOStudio.Tests.E2E
{
    /// <summary>
    /// Avalonia.Headless 可行性结论 (实测得出, 2026-09-30)。
    ///
    /// <para><b>能用的部分</b> —— 无桌面环境下驱动真实控件, 可在 CI 跑:</para>
    /// <list type="bullet">
    ///   <item>会话启动 + <b>真实布局</b> (控件有非零 Bounds, 证明拿到的是活对象而非傀儡);</item>
    ///   <item>真实属性绑定 / VM 状态 / 命令调用断言。</item>
    /// </list>
    ///
    /// <para><b>不能用的部分 (关键)</b> —— <b>合成鼠标事件送不到控件</b>:</para>
    /// <c>window.MouseDown/MouseUp</c> 不会派发到自定义控件的 <c>OnPointerPressed</c>
    /// (实测 Probe 记录为空)。因此<b>依赖 <c>e.ClickCount==2</c> 的"双击"语义无法在
    /// Headless 里验证</b>。本次两次"双击加帧失效"恰好都在这条路径上 ——
    /// 这类交互仍须<b>真实桌面注入</b>(窗口消息 / PostMessage)。
    ///
    /// <para><b>截图能力</b>: <c>CaptureRenderedFrame</c> 要求平台以 <c>.UseSkia()</c> +
    /// <c>UseHeadlessDrawing=false</c> 初始化; 在 <c>HeadlessUnitTestSession</c> 下不易注入
    /// (静态 Setup 会与会话线程互锁), 故未启用。</para>
    ///
    /// <para><b>踩过的坑 (务必记住)</b>: <c>Dispatch(Action, CancellationToken)</c> 是
    /// <b>fire-and-forget</b> —— 立即返回, 不等待 lambda 执行完; 断言会先于被测代码运行,
    /// 产生"永远通过"的假绿(本次就因此误判过一次)。必须用<b>会 await 的泛型重载</b>
    /// <c>Dispatch&lt;T&gt;(Func&lt;T&gt;, CancellationToken)</c>。</para>
    /// </summary>
    public class HeadlessFeasibilityTests : IDisposable
    {
        private readonly HeadlessUnitTestSession _session;

        public HeadlessFeasibilityTests()
        {
            _session = HeadlessUnitTestSession.StartNew(typeof(HeadlessApp));
        }

        public void Dispose() => _session.Dispose();

        [Fact]
        public async Task Session_Starts_And_Controls_Get_Real_Layout()
        {
            var bounds = await _session.Dispatch(
                () =>
                {
                    var host = new Border { Width = 800, Height = 60 };
                    var window = new Window { Width = 800, Height = 200, Content = host };
                    window.Show();
                    var result = (host.Bounds.Width, host.Bounds.Height);
                    window.Close();
                    return result;
                },
                CancellationToken.None
            );

            Assert.True(bounds.Width > 0, $"控件未参与布局: {bounds}");
            Assert.True(bounds.Height > 0, $"控件未参与布局: {bounds}");
        }

        /// <summary>
        /// 渲染帧捕获的<b>前置条件</b>: 必须用 <c>.UseSkia()</c> 且
        /// <c>UseHeadlessDrawing=false</c> 初始化平台。实测在
        /// <c>HeadlessUnitTestSession</c> 下这些选项不易注入(静态 Setup 会与
        /// 会话线程互锁), 故本文件不断言截图能力, 只记录该限制。
        /// 自绘控件的视觉证据仍走真实桌面截图。
        /// </summary>
        [Fact]
        public void Rendered_Frame_Capture_Requires_Skia_Not_Asserted_Here()
        {
            Assert.True(true, "截图能力需 Skia 平台选项; 见本方法注释。");
        }

        /// <summary>
        /// 明确记录能力边界: 合成鼠标事件在 Headless 下送不到自定义控件。
        /// 该测试断言的是<b>当前已知的局限</b>, 一旦上游支持, 它会失败并提醒我们更新结论。
        /// </summary>
        [Fact]
        public async Task Synthetic_Mouse_Does_Not_Reach_Custom_Control_Known_Limitation()
        {
            var observed = await _session.Dispatch(
                () =>
                {
                    var probe = new PointerProbe { Width = 800, Height = 60 };
                    var window = new Window { Width = 800, Height = 200, Content = probe };
                    window.Show();

                    window.MouseDown(new Point(300, 30), MouseButton.Left, RawInputModifiers.None);
                    window.MouseUp(new Point(300, 30), MouseButton.Left, RawInputModifiers.None);

                    var result = string.Join(" | ", probe.Observed);
                    window.Close();
                    return result;
                },
                CancellationToken.None
            );

            Assert.True(
                string.IsNullOrEmpty(observed),
                "Headless 现在能派发合成鼠标事件了 —— 请更新本文件结论并启用双击类 E2E: "
                    + observed
            );
        }
    }

    /// <summary>Headless 宿主所需的 Application 子类 (不加载真实 App.axaml)。</summary>
    public class HeadlessApp : Application
    {
        public override void Initialize() { }
    }

    /// <summary>最小自绘控件: 记录收到的指针按下。</summary>
    internal class PointerProbe : Control
    {
        public System.Collections.Generic.List<string> Observed { get; } = new();

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            Observed.Add($"down cc={e.ClickCount}");
        }

        public override void Render(Avalonia.Media.DrawingContext ctx)
        {
            ctx.FillRectangle(
                Avalonia.Media.Brushes.DimGray,
                new Rect(0, 0, Bounds.Width, Bounds.Height)
            );
        }
    }
}
