using Avalonia.Input;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using Xunit;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// 快速编排快捷键映射回归 — Ctrl+A / Ctrl+X 与 Alt+方向键微调。
    /// 这些键必须与既有 ←→ 导航、↑↓ 切轨道互不冲突。
    /// </summary>
    public class KeyboardShortcutServiceTests
    {
        private static ShortcutAction Resolve(Key key, KeyModifiers mods = KeyModifiers.None) =>
            new KeyboardShortcutService().Resolve(key, mods);

        [Fact]
        public void CtrlA_SelectsAllOnCurrentTrack()
        {
            Assert.Equal(ShortcutAction.SelectAllKeyframes, Resolve(Key.A, KeyModifiers.Control));
        }

        [Fact]
        public void CtrlShiftA_SelectsAllTracks()
        {
            Assert.Equal(
                ShortcutAction.SelectAllTrackKeyframes,
                Resolve(Key.A, KeyModifiers.Control | KeyModifiers.Shift)
            );
        }

        [Fact]
        public void CtrlX_Cuts()
        {
            Assert.Equal(ShortcutAction.Cut, Resolve(Key.X, KeyModifiers.Control));
        }

        [Fact]
        public void CtrlE_Exports()
        {
            Assert.Equal(ShortcutAction.Export, Resolve(Key.E, KeyModifiers.Control));
        }

        [Fact]
        public void CtrlShiftI_Inverts()
        {
            Assert.Equal(
                ShortcutAction.InvertSelection,
                Resolve(Key.I, KeyModifiers.Control | KeyModifiers.Shift)
            );
        }

        [Fact]
        public void CtrlShiftW_SelectsWorkArea()
        {
            Assert.Equal(
                ShortcutAction.SelectInWorkArea,
                Resolve(Key.W, KeyModifiers.Control | KeyModifiers.Shift)
            );
        }

        [Fact]
        public void F1_OpensShortcuts()
        {
            Assert.Equal(ShortcutAction.ShowShortcuts, Resolve(Key.F1));
        }

        [Theory]
        [InlineData(Key.Left, ShortcutAction.NudgeTimeBackward)]
        [InlineData(Key.Right, ShortcutAction.NudgeTimeForward)]
        [InlineData(Key.Down, ShortcutAction.NudgeValueDown)]
        [InlineData(Key.Up, ShortcutAction.NudgeValueUp)]
        public void AltArrow_NudgesSmall(Key key, ShortcutAction expected)
        {
            Assert.Equal(expected, Resolve(key, KeyModifiers.Alt));
        }

        [Theory]
        [InlineData(Key.Left, ShortcutAction.NudgeTimeBackwardLarge)]
        [InlineData(Key.Right, ShortcutAction.NudgeTimeForwardLarge)]
        [InlineData(Key.Down, ShortcutAction.NudgeValueDownLarge)]
        [InlineData(Key.Up, ShortcutAction.NudgeValueUpLarge)]
        public void AltShiftArrow_NudgesLarge(Key key, ShortcutAction expected)
        {
            Assert.Equal(expected, Resolve(key, KeyModifiers.Alt | KeyModifiers.Shift));
        }

        [Fact]
        public void PlainArrows_Keep_ExistingNavigation()
        {
            Assert.Equal(ShortcutAction.PreviousKeyframe, Resolve(Key.Left));
            Assert.Equal(ShortcutAction.NextKeyframe, Resolve(Key.Right));
            Assert.Equal(ShortcutAction.SelectPreviousTrack, Resolve(Key.Up));
            Assert.Equal(ShortcutAction.SelectNextTrack, Resolve(Key.Down));
        }

        [Fact]
        public void CtrlArrows_Keep_MarkerNavigation()
        {
            Assert.Equal(ShortcutAction.PreviousMarker, Resolve(Key.Left, KeyModifiers.Control));
            Assert.Equal(ShortcutAction.NextMarker, Resolve(Key.Right, KeyModifiers.Control));
        }
    }
}
