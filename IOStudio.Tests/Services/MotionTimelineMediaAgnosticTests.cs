using Xunit;
using IOStudio.Services.Motion;
using IOStudio.Tests.Helpers;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// 编排内容必须**媒体无关** —— 守住这条设计约定。
    ///
    /// 背景: 曾把影片路径写进 .motion (media.video_path)。但同一个 .motion 既可能用于影片,
    /// 也可能用于固定循环演出、测试序列或纯动作编排 —— 把影片路径写进去会把内容与某一次
    /// 具体放映耦合死, 也让"换一部影片复用同一套动作"变得别扭。
    ///
    /// 影片与动作的对应关系属于**演出/生产层**, 由那一层去绑定。
    /// 本测试的作用是防止这条约定被重新引入。
    /// </summary>
    public class MotionTimelineMediaAgnosticTests
    {
        [Fact]
        public void Timeline_Serialization_HasNoMediaField()
        {
            var tl = MotionTestFactory.CreateMinimalTimeline(5000);
            string json = MotionFileReader.ToJson(tl);

            Assert.DoesNotContain("\"media\"", json);
            Assert.DoesNotContain("video_path", json);
            Assert.DoesNotContain("video", json, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Timeline_Model_HasNoMediaMember()
        {
            // 反射锁定: 模型上不应存在任何媒体相关成员 (防止再次引入耦合)
            var props = typeof(IOStudio.Models.Motion.MotionTimeline).GetProperties();
            foreach (var p in props)
            {
                Assert.DoesNotContain("media", p.Name, System.StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("video", p.Name, System.StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("audio", p.Name, System.StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void Timeline_RoundTrip_StillWorks()
        {
            var tl = MotionTestFactory.CreateMinimalTimeline(5000);
            string json = MotionFileReader.ToJson(tl);
            var back = MotionFileReader.ReadFromJson(json);
            Assert.NotNull(back);
            Assert.Single(back!.Tracks);
        }
    }
}
