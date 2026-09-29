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

        // ── 关键帧时间可直接输入 ──

        [Fact]
        public void CommitKeyframeTime_RaisesEdited_WithAbsoluteMs()
        {
            var vm = new KeyframePropertyViewModel();
            vm.ShowTrackState(3250, 0.5f, "float", "Pitch", hasKfAtPlayhead: true);

            double? editedTime = null;
            vm.KeyframePropertyEdited += (t, v, i, tin, tout, cp1x, cp2x) => editedTime = t;

            Assert.True(vm.CommitKeyframeTime("00:03.500"));

            Assert.NotNull(editedTime);
            Assert.Equal(3500, editedTime!.Value, precision: 3);
        }

        [Fact]
        public void CommitKeyframeTime_Accepts_PlainMilliseconds()
        {
            var vm = new KeyframePropertyViewModel();
            vm.ShowTrackState(1000, 0.5f, "float", "Pitch", hasKfAtPlayhead: true);

            double? editedTime = null;
            vm.KeyframePropertyEdited += (t, v, i, tin, tout, cp1x, cp2x) => editedTime = t;

            // 纯数字按毫秒解析 (与既有事件时间输入一致)
            Assert.True(vm.CommitKeyframeTime("2500"));

            Assert.Equal(2500, editedTime!.Value, precision: 3);
        }

        [Fact]
        public void CommitKeyframeTime_Ignored_WithoutKeyframe()
        {
            var vm = new KeyframePropertyViewModel();
            vm.ShowTrackState(1000, 0.5f, "float", "Pitch", hasKfAtPlayhead: false);

            bool edited = false;
            vm.KeyframePropertyEdited += (t, v, i, tin, tout, cp1x, cp2x) => edited = true;

            Assert.False(vm.CommitKeyframeTime("1.0"));
            Assert.False(edited);
        }

        [Fact]
        public void ShowTrackState_Prefills_KeyframeTimeText()
        {
            var vm = new KeyframePropertyViewModel();
            vm.ShowTrackState(3250, 0.5f, "float", "Pitch", hasKfAtPlayhead: true);

            Assert.Equal("00:03.250", vm.KeyframeTimeText);

            vm.ShowTrackState(4000, 0.5f, "float", "Pitch", hasKfAtPlayhead: false);
            Assert.Equal("", vm.KeyframeTimeText);
        }

        // ── P2: 贝塞尔控制点 X ──

        [Fact]
        public void CommitBezier_Raises_With_Cp_Values()
        {
            var vm = new KeyframePropertyViewModel();
            vm.ShowTrackState(0, 0.5f, "float", "Pitch", hasKfAtPlayhead: true, interpolation: "bezier");

            float? cp1 = null,
                cp2 = null,
                tin = null,
                tout = null;
            vm.KeyframePropertyEdited += (t, v, i, ti, to, c1, c2) =>
            {
                tin = ti;
                tout = to;
                cp1 = c1;
                cp2 = c2;
            };

            vm.CommitBezier(0.3f, -0.2f, 0.8f, 1.4f);

            Assert.Equal(0.3f, tin);
            Assert.Equal(-0.2f, tout);
            Assert.Equal(0.8f, cp1);
            Assert.Equal(1.4f, cp2);
        }

        // ── P2: 标记属性编辑 ──

        [Fact]
        public void ShowMarker_Fills_Fields_And_Toggles_Visibility()
        {
            var vm = new KeyframePropertyViewModel();
            var marker = new IOStudio.Models.Motion.TimelineMarker
            {
                TimeMs = 1000,
                Name = "开场",
                Color = "#22c55e",
                Note = "第一幕",
            };

            vm.ShowMarker(marker);

            Assert.True(vm.ShowMarkerEditor);
            Assert.Equal("开场", vm.MarkerName);
            Assert.Equal("#22c55e", vm.MarkerColor);
            Assert.Equal("第一幕", vm.MarkerNote);

            vm.ShowMarker(null);
            Assert.False(vm.ShowMarkerEditor);
        }

        [Fact]
        public void CommitMarker_Raises_Edited()
        {
            var vm = new KeyframePropertyViewModel();
            vm.ShowMarker(
                new IOStudio.Models.Motion.TimelineMarker { TimeMs = 0, Name = "M1" }
            );

            string? name = null,
                color = null,
                note = null;
            vm.MarkerEdited += (n, c, no) =>
            {
                name = n;
                color = c;
                note = no;
            };

            vm.MarkerName = "高潮";
            vm.MarkerNote = "第二幕";
            vm.CommitMarker();

            Assert.Equal("高潮", name);
            Assert.NotNull(color);
            Assert.Equal("第二幕", note);
        }

        [Fact]
        public void Clear_Hides_MarkerEditor()
        {
            var vm = new KeyframePropertyViewModel();
            vm.ShowMarker(new IOStudio.Models.Motion.TimelineMarker { TimeMs = 0, Name = "M1" });
            Assert.True(vm.ShowMarkerEditor);

            vm.Clear();

            Assert.False(vm.ShowMarkerEditor);
        }

        // ── 区段折叠 (辅助区段默认收起, 相关时自动展开) ──

        [Fact]
        public void AuxSections_Default_Collapsed()
        {
            var vm = new KeyframePropertyViewModel();
            Assert.False(vm.IsIdleSectionExpanded);
            Assert.False(vm.IsMarkerSectionExpanded);
            Assert.False(vm.IsEventSectionExpanded);
        }

        [Fact]
        public void ShowMarker_AutoExpands_MarkerSection()
        {
            var vm = new KeyframePropertyViewModel();
            vm.ShowMarker(new IOStudio.Models.Motion.TimelineMarker { TimeMs = 0, Name = "M1" });
            Assert.True(vm.IsMarkerSectionExpanded);
        }

        [Fact]
        public void SetPlayheadEvent_AutoExpands_EventSection()
        {
            var vm = new KeyframePropertyViewModel();
            vm.SetPlayheadEvent(new IOStudio.Models.Motion.TimelineEvent { TimeMs = 0, EventName = "e" });
            Assert.True(vm.IsEventSectionExpanded);
        }

        [Fact]
        public void ToggleSectionExpanded_Toggles_Requested_Section()
        {
            var vm = new KeyframePropertyViewModel();

            vm.ToggleSectionExpanded("idle");
            Assert.True(vm.IsIdleSectionExpanded);
            Assert.False(vm.IsMarkerSectionExpanded); // 不影响其他区段

            vm.ToggleSectionExpanded("idle");
            Assert.False(vm.IsIdleSectionExpanded);
        }

        // ── 关键帧列表空态 (给出原因 + 下一步) ──

        [Fact]
        public void KeyframeListEmptyText_Tells_Why()
        {
            var vm = new KeyframePropertyViewModel();

            // 播放头不在片段内 (clipIdx = -1)
            vm.ShowTrackState(0, 0.5f, "float", "Pitch", hasKfAtPlayhead: false, clipIdx: -1);
            Assert.True(vm.ShowKeyframeListEmpty);
            Assert.Equal("播放头不在片段内", vm.KeyframeListEmptyText);

            // 播放头在片段内但片段无关键帧
            vm.ShowTrackState(0, 0.5f, "float", "Pitch", hasKfAtPlayhead: false, clipIdx: 0);
            Assert.Equal("该片段没有关键帧，按 K 添加", vm.KeyframeListEmptyText);
        }

        [Fact]
        public void KeyframeListEmpty_Hidden_When_List_Populated()
        {
            var vm = new KeyframePropertyViewModel();
            vm.ShowTrackState(0, 0.5f, "float", "Pitch", hasKfAtPlayhead: false, clipIdx: -1);
            Assert.True(vm.ShowKeyframeListEmpty);

            vm.Clear();
            Assert.False(vm.ShowKeyframeListEmpty); // 非 TrackInspector 模式不显示空态
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
