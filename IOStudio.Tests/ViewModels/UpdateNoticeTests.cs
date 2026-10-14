using System;
using HiMind.Distribution;
using Xunit;
using IOStudio.Models;
using IOStudio.ViewModels;

namespace IOStudio.Tests.ViewModels
{
    /// <summary>
    /// 启动时的更新提醒规则。
    ///
    /// <para><b>为什么要有这些东西</b>: 原实现是<b>模态弹窗</b> —— 会拦下用户手上的事,
    /// 而且只要没更新就<b>每次启动都拦一遍</b>, 没有任何"别再问我"的出路。
    /// 现在启动改走非模态横幅, 并给两条退路:</para>
    /// <list type="bullet">
    ///   <item><b>稍后</b> = 本次会话静默 (不落盘, 下次启动还会提);</item>
    ///   <item><b>忽略此版本</b> = 落盘, 该版本永久不再提, 但<b>更新的版本仍会提</b>。</item>
    /// </list>
    /// </summary>
    public class UpdateNoticeTests : IDisposable
    {
        private readonly string _savedSkip;

        public UpdateNoticeTests()
        {
            _savedSkip = IOStudioSettings.Instance.SkippedUpdateVersion;
        }

        public void Dispose()
        {
            // 忽略版本是持久化状态, 用例之间必须还原
            IOStudioSettings.Instance.SkippedUpdateVersion = _savedSkip;
        }

        private static SoftwareUpdateManifest Manifest(
            string version,
            bool mandatory = false,
            long size = 0
        ) => new() { Version = version, Mandatory = mandatory, Size = size };

        // ── 显隐与文案 ──

        [Fact]
        public void NoPendingUpdate_NoBanner()
        {
            var notice = new UpdateNotice(() => "1.0.10");
            Assert.False(notice.IsVisible);
            Assert.Equal("", notice.Text);
        }

        [Fact]
        public void PendingUpdate_ShowsVersionAndSize()
        {
            var notice = new UpdateNotice(() => "1.0.10");
            notice.Set(Manifest("1.0.12", size: 150607601));

            Assert.True(notice.IsVisible);
            Assert.Contains("1.0.12", notice.Text);
            Assert.Contains("1.0.10", notice.Text);   // 当前版本, 便于对照
            Assert.Contains("143.6 MB", notice.Text); // 150MB 级下载必须事先告知体积
        }

        [Fact]
        public void UnknownSize_OmitsTheSizeClause()
        {
            var notice = new UpdateNotice(() => "1.0.10");
            notice.Set(Manifest("1.0.12"));

            Assert.DoesNotContain("更新包", notice.Text);
        }

        // ── 「稍后」只在本次会话 ──

        [Fact]
        public void Dismiss_HidesButDoesNotPersist()
        {
            var notice = new UpdateNotice(() => "1.0.10");
            notice.Set(Manifest("1.0.12"));

            notice.Dismiss();

            Assert.False(notice.IsVisible);
            // 「稍后」不该污染持久化的忽略版本 —— 否则"下次再说"变成了"永远不说"
            Assert.NotEqual("1.0.12", IOStudioSettings.Instance.SkippedUpdateVersion ?? "");
        }

        // ── 「忽略此版本」持久化 ──

        [Fact]
        public void Skip_PersistsTheVersionAndHides()
        {
            var notice = new UpdateNotice(() => "1.0.10");
            notice.Set(Manifest("1.0.12"));

            Assert.Equal("1.0.12", notice.Skip());

            Assert.False(notice.IsVisible);
            Assert.Equal("1.0.12", IOStudioSettings.Instance.SkippedUpdateVersion);
        }

        [Fact]
        public void SkippedVersion_IsNotNotifiedAgain()
        {
            IOStudioSettings.Instance.SkippedUpdateVersion = "1.0.12";

            Assert.False(UpdateNotice.ShouldNotify(Manifest("1.0.12"), "1.0.12"));
        }

        [Fact]
        public void SkippedVersion_StillNotifiesForANewerRelease()
        {
            // 忽略的是**版本**, 不是"更新这件事" —— 出 1.0.13 必须再提
            Assert.True(UpdateNotice.ShouldNotify(Manifest("1.0.13"), "1.0.12"));
        }

        [Fact]
        public void SkippedVersionMatch_IsCaseInsensitive()
        {
            Assert.False(UpdateNotice.ShouldNotify(Manifest("1.0.12"), "1.0.12"));
            Assert.False(UpdateNotice.ShouldNotify(Manifest("v1.0.12"), "V1.0.12"));
        }

        [Fact]
        public void NoSkippedVersion_AlwaysNotifies()
        {
            Assert.True(UpdateNotice.ShouldNotify(Manifest("1.0.12"), null));
            Assert.True(UpdateNotice.ShouldNotify(Manifest("1.0.12"), ""));
        }

        // ── 强制更新不给忽略 ──

        [Fact]
        public void Mandatory_CannotBeSkipped_AndAlwaysNotifies()
        {
            var notice = new UpdateNotice(() => "1.0.10");
            notice.Set(Manifest("1.0.12", mandatory: true));

            Assert.False(notice.CanSkip);
            Assert.Null(notice.Skip());                  // 调用也不生效
            Assert.True(notice.IsVisible);               // 横幅还在
            Assert.NotEqual("1.0.12", IOStudioSettings.Instance.SkippedUpdateVersion ?? "");

            // 即使之前忽略过这个版本, 强制更新仍然要提
            Assert.True(UpdateNotice.ShouldNotify(Manifest("1.0.12", mandatory: true), "1.0.12"));
        }

        // ── 「查看更新」取走 ──

        [Fact]
        public void Take_ReturnsTheUpdateAndHidesTheBanner()
        {
            var notice = new UpdateNotice(() => "1.0.10");
            notice.Set(Manifest("1.0.12"));

            var taken = notice.Take();

            Assert.Equal("1.0.12", taken?.Version);
            Assert.False(notice.IsVisible);
            // 再取一次为空 (不会重复进入下载流程)
            Assert.Null(notice.Take());
        }
    }
}
