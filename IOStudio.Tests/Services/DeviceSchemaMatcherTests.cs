using System.Collections.Generic;
using Xunit;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// 设备表的纯匹配逻辑测试。
    ///
    /// 这两条判定各自错一个方向都会造成静默失效:
    /// 认领判太宽 → 放行必然会丢输出的编排; 判太严 → 离线编排满屏误报。
    /// </summary>
    public class DeviceSchemaMatcherTests
    {
        private static DeviceSchemaInfo Device(
            string name,
            params (string Name, string Label)[] oactions
        )
        {
            var d = new DeviceSchemaInfo { DeviceName = name };
            foreach (var (n, l) in oactions)
                d.OActions.Add(
                    new OActionInfo
                    {
                        DeviceName = name,
                        OActionName = n,
                        Label = l,
                        DisplayName = n,
                        KeyNames = new List<string>(),
                    }
                );
            return d;
        }

        private static void AddChannel(DeviceSchemaInfo device, string channel) =>
            device.OAxisChannels.Add(
                new OAxisChannelInfo
                {
                    DeviceName = device.DeviceName,
                    ChannelName = channel,
                    DisplayName = channel,
                }
            );

        private static IReadOnlyList<DeviceSchemaInfo> Catalog() => new List<DeviceSchemaInfo>
        {
            Device("YDL-0", ("heave", "升降"), ("roll", "横滚")),
            Device("YDL-1", ("heave", "升降")),
        };

        // ── IsDeclared ────────────────────────────────────

        [Fact]
        public void IsDeclared_MatchesNameIgnoringCase()
        {
            var devices = Catalog();
            Assert.True(DeviceSchemaMatcher.IsDeclared(devices, "YDL-0"));
            Assert.True(DeviceSchemaMatcher.IsDeclared(devices, "ydl-0"));
            Assert.True(DeviceSchemaMatcher.IsDeclared(devices, "YDL-1"));
        }

        [Theory]
        [InlineData("Device_1")] // 自动生成的名字 —— 真实踩过的坑
        [InlineData("YDL-0 ")] // 尾随空格不算同名
        [InlineData("YDL")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void IsDeclared_RejectsUnknownAndBlankNames(string? name)
        {
            Assert.False(DeviceSchemaMatcher.IsDeclared(Catalog(), name));
        }

        [Fact]
        public void IsDeclared_EmptyCatalog_RejectsEverything()
        {
            // 表里一个设备都没有时不能认领任何名字 —— 包括合成的 Platform-0,
            // native 侧对它是没有任何设备的。
            var empty = new List<DeviceSchemaInfo>();
            Assert.False(DeviceSchemaMatcher.IsDeclared(empty, "YDL-0"));
            Assert.False(DeviceSchemaMatcher.IsDeclared(empty, "Platform-0"));
        }

        // ── Find ──────────────────────────────────────────

        [Fact]
        public void Find_ReturnsDeviceOrNull()
        {
            var devices = Catalog();
            Assert.Equal("YDL-1", DeviceSchemaMatcher.Find(devices, "ydl-1")?.DeviceName);
            Assert.Null(DeviceSchemaMatcher.Find(devices, "nope"));
            Assert.Null(DeviceSchemaMatcher.Find(devices, null));
        }

        // ── SuggestForRole ────────────────────────────────

        [Fact]
        public void SuggestForRole_MatchesOActionName()
        {
            var s = DeviceSchemaMatcher.SuggestForRole(Catalog(), "heave");
            Assert.NotNull(s);
            Assert.Equal("YDL-0", s!.DeviceName); // 首个命中, 不是最后一个
            Assert.Equal("heave", s.OActionName);
            Assert.False(s.IsOAxis);
        }

        [Fact]
        public void SuggestForRole_MatchesOActionLabel_WhenNameDiffers()
        {
            // role 是人写的语义名, OAction 名可能是英文 key、Label 才是中文语义
            var s = DeviceSchemaMatcher.SuggestForRole(Catalog(), "横滚");
            Assert.NotNull(s);
            Assert.Equal("YDL-0", s!.DeviceName);
            Assert.Equal("roll", s.OActionName);
        }

        [Fact]
        public void SuggestForRole_MatchesOAxisChannel()
        {
            var devices = new List<DeviceSchemaInfo> { Device("YDL-0") };
            AddChannel(devices[0], "OAxis_48");
            var s = DeviceSchemaMatcher.SuggestForRole(devices, "oaxis_48");
            Assert.NotNull(s);
            Assert.Equal("YDL-0", s!.DeviceName);
            Assert.Equal("OAxis_48", s.OAxisChannel);
            Assert.True(s.IsOAxis);
        }

        [Fact]
        public void SuggestForRole_PrefersOActionOverOAxisWithinDevice()
        {
            var devices = new List<DeviceSchemaInfo> { Device("YDL-0", ("heave", "升降")) };
            AddChannel(devices[0], "heave");
            var s = DeviceSchemaMatcher.SuggestForRole(devices, "heave");
            Assert.False(s!.IsOAxis);
            Assert.Equal("heave", s.OActionName);
        }

        [Fact]
        public void SuggestForRole_SecondDeviceUsedWhenFirstDoesNotMatch()
        {
            var s = DeviceSchemaMatcher.SuggestForRole(Catalog(), "roll");
            Assert.Equal("YDL-0", s!.DeviceName);

            var s2 = DeviceSchemaMatcher.SuggestForRole(Catalog(), "横滚");
            Assert.Equal("YDL-0", s2!.DeviceName);
        }

        [Fact]
        public void SuggestForRole_KeepsDeviceTogether()
        {
            // 建议必须整体落到一个设备上 —— 跨设备组合在这里没有意义
            var devices = new List<DeviceSchemaInfo>
            {
                Device("A", ("pitch", "俯仰")),
                Device("B", ("heave", "升降")),
            };
            Assert.Equal("A", DeviceSchemaMatcher.SuggestForRole(devices, "pitch")!.DeviceName);
            Assert.Equal("B", DeviceSchemaMatcher.SuggestForRole(devices, "heave")!.DeviceName);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("no_such_role")]
        public void SuggestForRole_ReturnsNullWhenNoMatch(string? role)
        {
            Assert.Null(DeviceSchemaMatcher.SuggestForRole(Catalog(), role));
        }

        [Fact]
        public void SuggestForRole_EmptyCatalog_ReturnsNull()
        {
            Assert.Null(
                DeviceSchemaMatcher.SuggestForRole(new List<DeviceSchemaInfo>(), "heave")
            );
        }
    }
}
