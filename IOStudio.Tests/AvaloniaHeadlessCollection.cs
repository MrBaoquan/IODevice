using Xunit;

namespace IOStudio.Tests
{
    /// <summary>
    /// Avalonia Headless 会话测试的集合定义 —— 显式声明<b>不与任何其它集合并行</b>。
    ///
    /// <para><b>为什么只靠 <c>[Collection("AvaloniaHeadless")]</c> 不够</b>: 那个特性只让
    /// 同集合的类彼此串行; 该集合本身仍会与<b>其它集合</b>并行。而这里的用例除了占用进程级的
    /// <c>Dispatcher.UIThread</c> 之外, 还会绑真实 UDP 端口、读写真实工作区文件、触碰 native
    /// 设备状态 —— 于是别的集合一旦同时在跑, 就会以"端口占用 / 工作区被换 / 收帧回调不执行"
    /// 的形式随机变红。</para>
    ///
    /// <para><b>实测 (2026-10-01)</b>: <c>ProtocolTestUiTests</c> 单独跑 4/4 全绿 (每次 ~3s),
    /// 混在全量里跑则约一半概率红一个, 且每次红的是不同用例
    /// (<c>Workspace_RoundTrips_Session_Config</c> / <c>Detach_Then_Attach_...</c> /
    /// <c>AutoReply_Frame_...</c> / <c>QuickCommand_...</c>)。"红哪个会飘"说明是竞态,
    /// 而不是某个用例逻辑错。</para>
    ///
    /// <para>代价是全量耗时略增 (被串行化), 换来的是可复现的绿。</para>
    /// </summary>
    [CollectionDefinition("AvaloniaHeadless", DisableParallelization = true)]
    public class AvaloniaHeadlessCollection
    {
    }
}
