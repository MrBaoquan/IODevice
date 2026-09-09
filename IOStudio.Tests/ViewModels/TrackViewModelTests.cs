using IOStudio.Models.Motion;
using IOStudio.ViewModels.Timeline;
using Xunit;

namespace IOStudio.Tests.ViewModels
{
    public class TrackViewModelTests
    {
        private static MotionTrack MakeTrack() =>
            new()
            {
                Id = "t1",
                DeviceName = "Dev1",
                Label = "灯光",
                OutputType = "oaction",
                OActionName = "Light",
                Clips = new()
                {
                    new MotionClip
                    {
                        Keyframes = new()
                        {
                            new MotionKeyframe { TimeMs = 0, Value = 0 }
                        }
                    }
                }
            };

        [Theory]
        [InlineData(28, true, false)] // compact
        [InlineData(35, true, false)] // compact
        [InlineData(36, false, false)] // normal, no subtitle
        [InlineData(51, false, false)] // normal, no subtitle
        [InlineData(52, false, true)] // normal, with subtitle
        [InlineData(80, false, true)] // CurveEditor default
        [InlineData(120, false, true)] // CurveEditor expanded
        public void UpdateCompactDisplay_SetsCorrectFlags(
            double height,
            bool expectCompact,
            bool expectSubtitle
        )
        {
            var vm = new TrackViewModel(MakeTrack());

            vm.UpdateCompactDisplay(height);

            Assert.Equal(expectCompact, vm.IsCompactDisplay);
            Assert.Equal(expectSubtitle, vm.ShowSubtitle);
        }

        [Fact]
        public void ShowSubtitle_RaisesPropertyChanged()
        {
            var vm = new TrackViewModel(MakeTrack());
            bool raised = false;
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(TrackViewModel.ShowSubtitle))
                    raised = true;
            };

            vm.UpdateCompactDisplay(80); // should set ShowSubtitle = true

            Assert.True(raised, "ShowSubtitle PropertyChanged should fire");
            Assert.True(vm.ShowSubtitle);
        }
    }
}
