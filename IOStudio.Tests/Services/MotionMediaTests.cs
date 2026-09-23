using System;
using System.IO;
using Xunit;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using IOStudio.Tests.Helpers;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// 影片媒体绑定测试 —— 决定"影片 + 动作"能否作为一个项目整体被复现。
    /// 覆盖: 路径可移植策略、缺失判定、以及 .motion 序列化往返。
    /// </summary>
    public class MotionMediaTests : IDisposable
    {
        private readonly string _dir;

        public MotionMediaTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), $"MediaTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_dir, true);
            }
            catch { }
        }

        private string Touch(string relative)
        {
            string p = Path.Combine(_dir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, "x");
            return p;
        }

        // ── 路径策略 ──────────────────────────────────────

        [Fact]
        public void ToPortablePath_SameDirectory_BecomesFileName()
        {
            string motion = Path.Combine(_dir, "show.motion");
            string video = Touch("film.mp4");
            Assert.Equal("film.mp4", MotionMediaResolver.ToPortablePath(video, motion));
        }

        [Fact]
        public void ToPortablePath_SubDirectory_BecomesRelative()
        {
            string motion = Path.Combine(_dir, "show.motion");
            string video = Touch(Path.Combine("media", "film.mp4"));
            Assert.Equal(Path.Combine("media", "film.mp4"), MotionMediaResolver.ToPortablePath(video, motion));
        }

        [Fact]
        public void ToPortablePath_OneLevelUp_StillRelative()
        {
            // 上移一层仍属"同一项目树", 存相对更可移植
            string motion = Path.Combine(_dir, "show.motion");
            string sibling = Path.Combine(Path.GetDirectoryName(_dir)!, "shared_film.mp4");
            Assert.Equal(
                Path.Combine("..", "shared_film.mp4"),
                MotionMediaResolver.ToPortablePath(sibling, motion)
            );
        }

        [Fact]
        public void ToPortablePath_FarAway_KeepsAbsolute()
        {
            string motion = Path.Combine(_dir, "show.motion");
            // 跑到盘根级别的浅路径 → 相对路径会出现多级 ..\, 此时保留绝对路径更清晰
            string far = Path.Combine(Path.GetPathRoot(_dir)!, "faraway_film.mp4");
            Assert.Equal(far, MotionMediaResolver.ToPortablePath(far, motion));
        }

        [Fact]
        public void ToPortablePath_NoMotionPath_KeepsAbsolute()
        {
            string video = Touch("film.mp4");
            Assert.Equal(video, MotionMediaResolver.ToPortablePath(video, null));
            Assert.Equal(video, MotionMediaResolver.ToPortablePath(video, ""));
        }

        // ── 解析与缺失判定 ────────────────────────────────

        [Fact]
        public void Resolve_AbsoluteExisting_ReturnsItself()
        {
            string video = Touch("film.mp4");
            Assert.Equal(video, MotionMediaResolver.Resolve(video, Path.Combine(_dir, "show.motion")));
        }

        [Fact]
        public void Resolve_AbsoluteMissing_ReturnsNull()
        {
            string missing = Path.Combine(_dir, "nope.mp4");
            Assert.Null(MotionMediaResolver.Resolve(missing, Path.Combine(_dir, "show.motion")));
        }

        [Fact]
        public void Resolve_RelativeExisting_ResolvesAgainstMotionDir()
        {
            string video = Touch(Path.Combine("media", "film.mp4"));
            string resolved = MotionMediaResolver.Resolve(
                Path.Combine("media", "film.mp4"),
                Path.Combine(_dir, "show.motion")
            )!;
            Assert.Equal(video, resolved);
        }

        [Fact]
        public void Resolve_RelativeMissing_ReturnsNull()
        {
            Assert.Null(
                MotionMediaResolver.Resolve("gone.mp4", Path.Combine(_dir, "show.motion"))
            );
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Resolve_EmptyRecord_ReturnsNull(string? recorded)
        {
            Assert.Null(MotionMediaResolver.Resolve(recorded, Path.Combine(_dir, "show.motion")));
        }

        /// <summary>关键性质: 存盘再读回必须得到同一个绝对路径 (项目可搬移的基础)。</summary>
        [Theory]
        [InlineData("film.mp4")]
        [InlineData("media/film.mp4")]
        public void RoundTrip_PortablePath_ResolvesBackToOriginal(string relative)
        {
            string motion = Path.Combine(_dir, "show.motion");
            string video = Touch(relative.Replace('/', Path.DirectorySeparatorChar));

            string stored = MotionMediaResolver.ToPortablePath(video, motion);
            string? resolved = MotionMediaResolver.Resolve(stored, motion);

            Assert.Equal(video, resolved);
        }

        // ── .motion 序列化 ────────────────────────────────

        [Fact]
        public void Timeline_Media_RoundTrips()
        {
            var tl = MotionTestFactory.CreateMinimalTimeline(5000);
            tl.Media = new MotionMedia { VideoPath = "film.mp4", DurationMs = 300000 };

            string json = MotionFileReader.ToJson(tl);
            Assert.Contains("\"media\"", json);
            Assert.Contains("video_path", json);

            var back = MotionFileReader.ReadFromJson(json);
            Assert.NotNull(back);
            Assert.NotNull(back!.Media);
            Assert.Equal("film.mp4", back.Media!.VideoPath);
            Assert.Equal(300000, back.Media.DurationMs);
        }

        [Fact]
        public void Timeline_WithoutMedia_OmitsField_AndStillLoads()
        {
            var tl = MotionTestFactory.CreateMinimalTimeline(5000);
            string json = MotionFileReader.ToJson(tl);

            // 纯动作项目不应写出空 media (老文件语义保持不变)
            Assert.DoesNotContain("\"media\"", json);

            var back = MotionFileReader.ReadFromJson(json);
            Assert.NotNull(back);
            Assert.Null(back!.Media);
        }
    }
}
