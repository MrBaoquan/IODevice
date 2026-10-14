using System;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Xunit;
using IOStudio.ViewModels;

namespace IOStudio.Tests.ViewModels
{
    /// <summary>
    /// 设备表里 <c>&lt;ChannelRate&gt;</c> 的往返。
    ///
    /// <para><b>守的是什么</b>: 这个节点由 native 侧读取并执行通道限速。设备表是
    /// <c>XmlSerializer</c> <b>整份重写</b>的 —— 只要 C# 模型里没有对应字段,
    /// 用户在设备管理里点一次保存, 限速配置就被静默抹掉, 现场表现为"配了没用"。
    /// 这与之前"撤销变成假的"是同一类问题。</para>
    /// </summary>
    public class ChannelRateRoundTripTests
    {
        private static string WriteTemp(string xml)
        {
            string path = Path.Combine(Path.GetTempPath(), $"chrate_{Guid.NewGuid():N}.xml");
            File.WriteAllText(path, xml);
            return path;
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }

        [Fact]
        public void ChannelRates_AreReadFromTheDeviceTable()
        {
            string path = WriteTemp("""
                <?xml version="1.0" encoding="utf-8"?>
                <IORoot>
                  <Device Name="YDL-0" Type="External" DllName="MP-YDL" Index="0">
                    <Properties />
                    <ChannelRate Name="OAxis_6" MaxPerSecond="0.5" />
                    <ChannelRate Name="OAxis_16" MaxPerSecond="2" />
                  </Device>
                </IORoot>
                """);

            var saved = IORoot.Instance.Devices;
            try
            {
                IORoot.Instance.SetConfig(path).Load();

                var dev = IORoot.Instance.Devices.Single(d => d.Name == "YDL-0");
                Assert.Equal(2, dev.ChannelRates.Count);
                Assert.Equal(0.5f, dev.ChannelRates.Single(r => r.Name == "OAxis_6").MaxPerSecond);
                Assert.Equal(2f, dev.ChannelRates.Single(r => r.Name == "OAxis_16").MaxPerSecond);
            }
            finally
            {
                IORoot.Instance.Devices = saved;
                TryDelete(path);
            }
        }

        [Fact]
        public void SavingTheDeviceTable_DoesNotDropChannelRates()
        {
            // 这条就是关键: 保存一次之后配置必须还在
            string path = WriteTemp("""
                <?xml version="1.0" encoding="utf-8"?>
                <IORoot>
                  <Device Name="YDL-0" Type="External" DllName="MP-YDL" Index="0">
                    <Properties />
                    <ChannelRate Name="OAxis_6" MaxPerSecond="0.5" />
                  </Device>
                </IORoot>
                """);

            var saved = IORoot.Instance.Devices;
            try
            {
                IORoot.Instance.SetConfig(path).Load();
                IORoot.Instance.Save();   // 设备管理里的任何一次保存都走这条路

                string text = File.ReadAllText(path);
                Assert.Contains("ChannelRate", text);
                Assert.Contains("OAxis_6", text);
                Assert.Contains("0.5", text);
            }
            finally
            {
                IORoot.Instance.Devices = saved;
                TryDelete(path);
            }
        }

        [Fact]
        public void DeviceWithoutChannelRates_StillLoads()
        {
            // 老设备表 (没有这个节点) 必须照常工作 —— 缺省即不限速
            string path = WriteTemp("""
                <?xml version="1.0" encoding="utf-8"?>
                <IORoot>
                  <Device Name="Simulator-1" Type="External" DllName="Simulator" Index="1">
                    <Properties />
                  </Device>
                </IORoot>
                """);

            var saved = IORoot.Instance.Devices;
            try
            {
                IORoot.Instance.SetConfig(path).Load();
                var dev = IORoot.Instance.Devices.Single(d => d.Name == "Simulator-1");
                Assert.Empty(dev.ChannelRates);
            }
            finally
            {
                IORoot.Instance.Devices = saved;
                TryDelete(path);
            }
        }
    }
}
