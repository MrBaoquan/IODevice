using System;
using System.IO;
using System.Linq;
using Xunit;
using IOStudio.Models;
using IOStudio.ViewModels;

namespace IOStudio.Tests.ViewModels
{
    /// <summary>
    /// 「将当前配置另存为预设」。
    ///
    /// <para>这条能力实现在 <c>IORoot</c> 上：临时把配置路径指向预设文件、Save、再还原。
    /// 其中<b>还原是安全关键</b> —— 不还原的话，此后设备管理里的每一次保存都会写到那份预设上，
    /// 用户以为在改当前表，实际在悄悄改另一个文件。所以这里重点锁它。</para>
    /// </summary>
    public class DeviceTablePresetTests : IDisposable
    {
        private readonly IORoot _root = IORoot.Instance;
        private readonly string _savedConfigDir = DeviceTableLocator.ConfigDir;

        public void Dispose()
        {
            // 预设是写到真实 Config 目录的（该路径由 BaseDirectory 决定，不可注入），
            // 因此用例里创建的临时预设必须删掉，否则会污染后续用例发现的预设列表。
            foreach (var f in Directory.Exists(_savedConfigDir)
                ? Directory.GetFiles(_savedConfigDir, "IODevice.test-*.xml")
                : Array.Empty<string>())
            {
                try { File.Delete(f); } catch { /* ignore */ }
            }
        }

        // ── 名称校验 ──

        [Theory]
        [InlineData("YDL", true)]
        [InlineData("  我的台  ", true)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData("a/b", false)]
        [InlineData("a\\b", false)]
        [InlineData("IODevice", false)]     // 容易和缺省表看混
        public void IsValidPresetName_RejectsPathsAndEmpty(string name, bool expected)
        {
            Assert.Equal(expected, DeviceTableLocator.IsValidPresetName(name, out _));
        }

        [Fact]
        public void IsValidPresetName_ExplainsWhy()
        {
            DeviceTableLocator.IsValidPresetName("a/b", out string err);
            Assert.False(string.IsNullOrEmpty(err));   // 不能只返回 false 让人去猜
        }

        [Fact]
        public void PresetFileNameFor_FollowsTheConvention()
        {
            // 命名约定定了才能被 Discover 发现
            Assert.Equal("IODevice.YDL.xml", DeviceTableLocator.PresetFileNameFor("YDL"));
            Assert.Equal("IODevice.我的台.xml", DeviceTableLocator.PresetFileNameFor("  我的台  "));
        }

        // ── 另存为 ──

        [Fact]
        public void SaveAsPreset_WritesTheFile_AndRestoresTheConfigPath()
        {
            string before = _root.FilePath;
            Assert.False(string.IsNullOrEmpty(before), "前置：IORoot 应当已 SetConfig 过");

            bool ok = DeviceTableLocator.SaveCurrentAsPreset("test-save", out string saved, out string error);

            Assert.True(ok, error);
            Assert.Equal("IODevice.test-save.xml", saved);

            string path = DeviceTableLocator.ResolvePath(saved);
            Assert.True(File.Exists(path), "预设文件没写出来");

            // 关键：路径必须还原，否则之后所有保存都会写到这份预设上
            Assert.Equal(before, _root.FilePath);

            // 而且新预设立刻能被菜单发现
            Assert.Contains(saved, DeviceTableLocator.Discover());
        }

        [Fact]
        public void SaveAsPreset_DoesNotSwitchOrMutateTheCurrentTable()
        {
            var beforeNames = _root.Devices.Select(d => d.Name).ToList();
            string before = _root.FilePath;

            DeviceTableLocator.SaveCurrentAsPreset("test-nomutate", out _, out _);

            Assert.Equal(beforeNames, _root.Devices.Select(d => d.Name));
            Assert.Equal(before, _root.FilePath);
        }

        [Fact]
        public void SaveAsPreset_RejectsBadName_WithoutTouchingAnything()
        {
            string before = _root.FilePath;

            bool ok = DeviceTableLocator.SaveCurrentAsPreset("bad/name", out string saved, out string error);

            Assert.False(ok);
            Assert.Equal("", saved);
            Assert.False(string.IsNullOrEmpty(error));
            Assert.Equal(before, _root.FilePath);   // 失败路径也不能改路径
        }
    }
}
