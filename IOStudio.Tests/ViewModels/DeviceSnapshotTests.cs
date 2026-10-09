using Xunit;
using IOStudio.ViewModels;

namespace IOStudio.Tests.ViewModels
{
    /// <summary>
    /// 设备值刷新（轮询 → UI）的线程边界回归测试。
    ///
    /// <para>背景: 窗口偶发长时间“未响应”。根因是设备轮询线程 <c>IODeviceController.Update()</c>
    /// 在整个 Tick 期间持有原生 <c>RegistryMutex</c>（每次 <c>GetKey</c> 都会经
    /// <c>IODevices::GetDevice</c> 再抢同一把锁），而 UI 线程此前逐键直读原生值，被轮询整轮
    /// 持锁的时间反复阻塞。修复后：原生读取放在轮询线程（<see cref="Device.ReadSnapshot"/>），
    /// UI 线程只做托管赋值（<see cref="Device.ApplySnapshot"/>），且 <c>ApplySnapshot</c> 不再回读
    /// 原生 <c>IsValid()</c>。</para>
    ///
    /// <para>这些用例在未加载原生设备的环境下运行：只要 <c>ApplySnapshot</c> 被喂入快照仍然不抛异常，
    /// 就说明它没有触碰原生接口（否则会因缺 DLL / 未初始化而失败）。</para>
    /// </summary>
    public class DeviceSnapshotTests
    {
        [Fact]
        public void ReadSnapshot_BeforeDeviceResolved_RequestsResolve_WithoutNative()
        {
            var device = new Device { Name = "UnitTestVirtualDevice_A" };

            var snapshot = device.ReadSnapshot();

            Assert.NotNull(snapshot);
            // 读取时还没有设备句柄 → 请求在 UI 线程解析，且不触达原生
            Assert.True(snapshot.NeedsResolve);
            Assert.False(snapshot.IsValid);
            Assert.Empty(snapshot.KeyValues);
            Assert.Empty(snapshot.NodeValues);
        }

        [Fact]
        public void ApplySnapshot_WithProvidedSnapshot_SkipsNativeIsValid()
        {
            var device = new Device { Name = "UnitTestVirtualDevice_B" };
            var snapshot = new Device.DeviceSnapshot { NeedsResolve = false, IsValid = false };

            // 不应抛异常：走到这里绝不能调用原生 IsValid()
            device.ApplySnapshot(snapshot);

            Assert.False(device.IsValid);
        }

        [Fact]
        public void ApplySnapshot_Null_IsNoOp()
        {
            var device = new Device { Name = "UnitTestVirtualDevice_C" };

            device.ApplySnapshot(null);

            Assert.NotNull(device);
        }
    }
}
