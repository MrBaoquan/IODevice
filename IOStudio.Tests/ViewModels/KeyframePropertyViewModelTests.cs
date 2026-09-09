using Xunit;
using IOStudio.ViewModels.Timeline;

namespace IOStudio.Tests.ViewModels
{
    /// <summary>
    /// KeyframePropertyViewModel 单元测试 — 检查器面板 (播放头驱动模式)
    /// </summary>
    public class KeyframePropertyViewModelTests
    {
        [Fact]
        public void Initial_Mode_Is_None()
        {
            var vm = new KeyframePropertyViewModel();

            Assert.Equal(KeyframePropertyViewModel.DisplayMode.None, vm.Mode);
            Assert.False(vm.HasSelection);
        }

        [Fact]
        public void ShowTrackState_Sets_TrackInspector_Mode()
        {
            var vm = new KeyframePropertyViewModel();

            vm.ShowTrackState(1000, 0.5f, "float", "Pitch", hasKfAtPlayhead: false);

            Assert.Equal(KeyframePropertyViewModel.DisplayMode.TrackInspector, vm.Mode);
            Assert.True(vm.HasSelection);
            Assert.True(vm.IsTrackInspector);
            Assert.False(vm.IsMultiSelect);
            Assert.Equal(1000, vm.TimeMs);
            Assert.Equal(0.5f, vm.Value);
            Assert.Equal("Pitch", vm.TrackName);
        }

        [Fact]
        public void ShowTrackState_WithKfAtPlayhead_ShowsInterpolation()
        {
            var vm = new KeyframePropertyViewModel();

            vm.ShowTrackState(
                500,
                0.3f,
                "float",
                "Roll",
                hasKfAtPlayhead: true,
                interpolation: "bezier",
                tangentIn: -0.5f,
                tangentOut: 0.5f
            );

            Assert.True(vm.HasKeyframeAtPlayhead);
            Assert.True(vm.ShowInterpolation);
            Assert.Equal("bezier", vm.Interpolation);
            Assert.Equal(-0.5f, vm.TangentIn);
            Assert.Equal(0.5f, vm.TangentOut);
        }

        [Fact]
        public void ShowTrackState_WithoutKf_HidesInterpolation()
        {
            var vm = new KeyframePropertyViewModel();

            vm.ShowTrackState(500, 0.3f, "float", "Pitch", hasKfAtPlayhead: false);

            Assert.False(vm.HasKeyframeAtPlayhead);
            Assert.False(vm.ShowInterpolation);
        }

        [Fact]
        public void ShowMultiSelection_Sets_MultiSelect_Mode()
        {
            var vm = new KeyframePropertyViewModel();

            vm.ShowMultiSelection(3, 100, 500, 0.1f, 0.9f, 0.5f);

            Assert.Equal(KeyframePropertyViewModel.DisplayMode.MultiSelect, vm.Mode);
            Assert.True(vm.IsMultiSelect);
            Assert.Equal(3, vm.MultiSelectCount);
        }

        [Fact]
        public void Clear_Resets_To_None_Mode()
        {
            var vm = new KeyframePropertyViewModel();
            vm.ShowTrackState(1000, 0.5f, "float", "Pitch", hasKfAtPlayhead: true);

            vm.Clear();

            Assert.Equal(KeyframePropertyViewModel.DisplayMode.None, vm.Mode);
            Assert.False(vm.HasSelection);
        }

        [Fact]
        public void CommitValue_Clamps_To_0_1_Range()
        {
            var vm = new KeyframePropertyViewModel();
            vm.ShowTrackState(0, 0.5f, "float", "Pitch", hasKfAtPlayhead: true);

            float? editedValue = null;
            vm.KeyframePropertyEdited += (t, v, interp, tin, tout, cp1x, cp2x) =>
            {
                editedValue = v;
            };

            vm.CommitValue(1.5f); // 超出范围

            Assert.NotNull(editedValue);
            Assert.Equal(1.0f, editedValue!.Value);
        }

        [Fact]
        public void CommitValue_WithoutKf_InAutoKey_TriggersAutoKeyCreate()
        {
            var vm = new KeyframePropertyViewModel();
            vm.ShowTrackState(1000, 0.5f, "float", "Pitch", hasKfAtPlayhead: false);
            vm.IsAutoKeyMode = true;

            double? createTime = null;
            float? createValue = null;
            vm.AutoKeyCreateRequested += (t, v) =>
            {
                createTime = t;
                createValue = v;
            };

            vm.CommitValue(0.8f);

            Assert.NotNull(createTime);
            Assert.Equal(1000, createTime!.Value);
            Assert.Equal(0.8f, createValue!.Value);
        }

        [Fact]
        public void CommitValue_WithoutKf_NoAutoKey_DoesNothing()
        {
            var vm = new KeyframePropertyViewModel();
            vm.ShowTrackState(1000, 0.5f, "float", "Pitch", hasKfAtPlayhead: false);
            vm.IsAutoKeyMode = false;

            bool edited = false;
            vm.KeyframePropertyEdited += (t, v, i, tin, tout, cp1x, cp2x) => edited = true;
            vm.ToggleKeyframeRequested += t => edited = true;

            vm.CommitValue(0.8f);

            Assert.False(edited);
        }

        [Fact]
        public void NavigatePrev_RaisesEvent()
        {
            var vm = new KeyframePropertyViewModel();
            bool raised = false;
            vm.NavigatePrevKeyframeRequested += () => raised = true;

            vm.RaiseNavigatePrev();

            Assert.True(raised);
        }

        [Fact]
        public void NavigateNext_RaisesEvent()
        {
            var vm = new KeyframePropertyViewModel();
            bool raised = false;
            vm.NavigateNextKeyframeRequested += () => raised = true;

            vm.RaiseNavigateNext();

            Assert.True(raised);
        }

        [Fact]
        public void ToggleKeyframe_RaisesEvent()
        {
            var vm = new KeyframePropertyViewModel();
            double? requestedTime = null;
            vm.ToggleKeyframeRequested += t => requestedTime = t;

            vm.RaiseToggleKeyframe(2500);

            Assert.NotNull(requestedTime);
            Assert.Equal(2500, requestedTime!.Value);
        }

        [Fact]
        public void IsTimeReadOnly_AlwaysTrue()
        {
            var vm = new KeyframePropertyViewModel();
            Assert.True(vm.IsTimeReadOnly);

            vm.ShowTrackState(1000, 0.5f, "float", "Pitch", hasKfAtPlayhead: true);
            Assert.True(vm.IsTimeReadOnly);
        }

        [Fact]
        public void SelectionVersion_Increments_OnShowTrackState()
        {
            var vm = new KeyframePropertyViewModel();
            int v0 = vm.SelectionVersion;

            vm.ShowTrackState(0, 0, "float", "T", false);
            Assert.Equal(v0 + 1, vm.SelectionVersion);

            vm.ShowTrackState(100, 0.5f, "float", "T", true);
            Assert.Equal(v0 + 2, vm.SelectionVersion);
        }

        [Fact]
        public void BoolMode_Detected_Correctly()
        {
            var vm = new KeyframePropertyViewModel();

            vm.ShowTrackState(0, 1f, "bool", "Trigger", hasKfAtPlayhead: true);

            Assert.True(vm.IsBoolMode);
            Assert.False(vm.IsFloatMode);
            Assert.True(vm.BoolValue);
            Assert.Equal("1.0", vm.BoolValueLabel);
            // bool 模式不显示插值
            Assert.False(vm.ShowInterpolation);
        }

        [Fact]
        public void CommitInterpolation_OnlyWorksWhenKfAtPlayhead()
        {
            var vm = new KeyframePropertyViewModel();
            vm.ShowTrackState(0, 0.5f, "float", "P", hasKfAtPlayhead: false);

            bool edited = false;
            vm.KeyframePropertyEdited += (t, v, i, tin, tout, cp1x, cp2x) => edited = true;

            vm.CommitInterpolationCommand.Execute("bezier").Subscribe();

            Assert.False(edited);
        }

        // ── P0: IsValueEditable 测试 ──

        [Fact]
        public void IsValueEditable_TrueWhenKfAtPlayhead()
        {
            var vm = new KeyframePropertyViewModel();
            vm.ShowTrackState(0, 0.5f, "float", "Pitch", hasKfAtPlayhead: true);

            Assert.True(vm.IsValueEditable);
            Assert.False(vm.ShowEditHint);
        }

        [Fact]
        public void IsValueEditable_TrueWhenAutoKey()
        {
            var vm = new KeyframePropertyViewModel();
            vm.ShowTrackState(0, 0.5f, "float", "Pitch", hasKfAtPlayhead: false);
            vm.IsAutoKeyMode = true;

            Assert.True(vm.IsValueEditable);
            Assert.False(vm.ShowEditHint);
        }

        [Fact]
        public void IsValueEditable_FalseWhenNoKfAndNoAutoKey()
        {
            var vm = new KeyframePropertyViewModel();
            vm.ShowTrackState(0, 0.5f, "float", "Pitch", hasKfAtPlayhead: false);
            vm.IsAutoKeyMode = false;

            Assert.False(vm.IsValueEditable);
            Assert.True(vm.ShowEditHint);
        }

        // ── P1: TrackColor 测试 ──

        [Fact]
        public void ShowTrackState_SetsTrackColor()
        {
            var vm = new KeyframePropertyViewModel();

            vm.ShowTrackState(0, 0.5f, "float", "Roll", false, trackColor: "#FF9800");

            Assert.Equal("#FF9800", vm.TrackColor);
        }

        [Fact]
        public void Clear_ResetsTrackColor()
        {
            var vm = new KeyframePropertyViewModel();
            vm.ShowTrackState(0, 0.5f, "float", "P", false, trackColor: "#FF0000");

            vm.Clear();

            Assert.Equal("#4FC3F7", vm.TrackColor);
            Assert.Equal(0, vm.TotalKfCount);
            Assert.Equal("", vm.IntervalInterpolation);
        }

        // ── P2: CommitValue _isSyncing 保护 + 播放头事件 测试 ──

        [Fact]
        public void CommitValue_DoesNotAutoKey_DuringSyncing()
        {
            var vm = new KeyframePropertyViewModel();
            bool autoKeyCalled = false;
            vm.AutoKeyCreateRequested += (_, _) => autoKeyCalled = true;
            vm.IsAutoKeyMode = true;

            // ShowTrackState 内部设 _isSyncing=true,
            // 绑定触发 CommitValue 时不应调用 AutoKeyCreateRequested
            vm.ShowTrackState(
                500,
                0.75f,
                "float",
                "Pitch",
                hasKfAtPlayhead: false,
                intervalInterpolation: "bezier"
            );

            Assert.False(autoKeyCalled);
        }

        [Fact]
        public void SetPlayheadEvent_ShowsEvent()
        {
            var vm = new KeyframePropertyViewModel();
            vm.ShowTrackState(500, 0.5f, "float", "Pitch", hasKfAtPlayhead: false);

            var evt = new IOStudio.Models.Motion.TimelineEvent
            {
                TimeMs = 500,
                EventName = "hit",
                EventData = "{}"
            };
            vm.SetPlayheadEvent(evt);

            Assert.True(vm.ShowPlayheadEvent);
            Assert.Equal("hit", vm.PlayheadEventName);
            Assert.Equal("{}", vm.PlayheadEventData);
        }

        [Fact]
        public void SetPlayheadEvent_HidesWhenNull()
        {
            var vm = new KeyframePropertyViewModel();
            vm.ShowTrackState(500, 0.5f, "float", "Pitch", hasKfAtPlayhead: false);

            vm.SetPlayheadEvent(null);

            Assert.False(vm.ShowPlayheadEvent);
        }

        // ── P3: IndexLabel 格式测试 ──

        [Fact]
        public void IndexLabel_ShowsKfPosition_WhenOnKF()
        {
            var vm = new KeyframePropertyViewModel();

            vm.ShowTrackState(
                0,
                0.5f,
                "float",
                "P",
                hasKfAtPlayhead: true,
                kfIdx: 2,
                totalKfCount: 5
            );

            Assert.Equal("KF 3/5", vm.IndexLabel);
        }

        [Fact]
        public void IndexLabel_ShowsTotal_WhenBetweenKFs()
        {
            var vm = new KeyframePropertyViewModel();

            vm.ShowTrackState(0, 0.5f, "float", "P", hasKfAtPlayhead: false, totalKfCount: 8);

            Assert.Equal("共 8 KF", vm.IndexLabel);
        }

        [Fact]
        public void IndexLabel_ShowsNoKF_WhenEmpty()
        {
            var vm = new KeyframePropertyViewModel();

            vm.ShowTrackState(0, 0.5f, "float", "P", hasKfAtPlayhead: false, totalKfCount: 0);

            Assert.Equal("无关键帧", vm.IndexLabel);
        }
    }
}
