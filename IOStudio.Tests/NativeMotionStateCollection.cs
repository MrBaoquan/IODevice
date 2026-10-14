using Xunit;

namespace IOStudio.Tests
{
    /// <summary>
    /// 触碰 <b>进程级共享状态</b> 的测试类集合 —— 只能串行执行。
    ///
    /// <para><b>为什么需要它</b>: native 侧的量程表 / Slot 表是 <b>进程级全局</b> 的
    /// (<c>MotionPlayer.SetChannelRange</c> / <c>LoadSlotFromJson</c> 都作用在同一份静态状态上),
    /// 而设备表是磁盘上的<b>共享单文件</b> (<c>Config/IODevice.xml</c>)。
    /// 只要这些用例并行跑, 一个类的"清空/改写"就会落在另一个类的断言之间。</para>
    ///
    /// <para><b>实测症状 (2026-10-01)</b>: <see cref="Services.ChannelRangePushContractTests"/>
    /// 在 <c>finally</c> 里调 <c>MotionPlayer.ClearChannelRanges()</c> —— 这是<b>全表清空</b>,
    /// 会把 <see cref="Services.TrackEngineeringRangeAutoRegisterTests.HostPush_OverridesAutoRegisteredRange"/>
    /// 刚推的宿主量程一并抹掉, 于是它退回轨道自带的兜底量程 (500) 而断言期望 0。
    /// 两个类单跑都绿, 合跑必有一红, 且红的那个会在
    /// <c>ChannelRangePushContractTests</c> / <c>TrackEngineeringRangeAutoRegisterTests</c> /
    /// <c>ProtocolTestUiTests</c> 之间飘 —— 这正是竞态而非逻辑错误的特征。</para>
    ///
    /// <para><b>DisableParallelization 的含义</b>: 该集合不但内部串行, 也<b>不与其它集合并行</b>,
    /// 所以第三方用例不可能在中间插进来清表。加新的 native 全局态用例时, 把它挂到这个集合上,
    /// 而不是自己去发明随机设备名之类的"软隔离" —— 对手方是全表清空, 软隔离挡不住。</para>
    /// </summary>
    [CollectionDefinition("NativeMotionState", DisableParallelization = true)]
    public class NativeMotionStateCollection
    {
    }
}
