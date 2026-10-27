using System;
using System.IO;
using System.Linq;
using Xunit;
using IOStudio.Services.Motion;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// SafeStop 契约门禁。目标: 把“估算完成不得冒充物理确认、每个输出必须有明确安全值、
    /// 切换/待机/仅加载必须先安全停止”变成可执行约束，而不是散落在 XML handler 里。
    /// </summary>
    public class MotionSafeStopContractTests
    {
        [Fact]
        public void Load_YdlProfile_IsEstimatedAndUsesTrackNeutralValues()
        {
            string path = FindContractFile("Safety", "mp-ydl.estimated.safety.json");
            var contract = MotionSafeStopContract.Load(path);

            Assert.Equal(MotionSafeStopCompletionMode.Estimated, contract.SafeStop.CompletionMode);
            Assert.Equal(3000, contract.SafeStop.EstimatedSettleMs);
            Assert.Equal(5000, contract.SafeStop.TimeoutMs);
            Assert.False(contract.Rules.TreatEstimatedAsConfirmed);
            Assert.True(contract.Rules.LockoutOnFailure);
            Assert.Equal(0f, contract.NeutralOutputs["OAxis_06"]);
            Assert.Equal(0f, contract.NeutralOutputs["OAxis_22"]);
        }

        [Fact]
        public void Validate_RejectsEstimatedMarkedAsConfirmed()
        {
            string path = FindContractFile("Safety", "mp-ydl.estimated.safety.json");
            var contract = MotionSafeStopContract.Load(path);
            contract.Rules.TreatEstimatedAsConfirmed = true;

            var issues = contract.Validate();

            Assert.Contains(issues, i => i.Code == "safety.estimated_marked_confirmed");
        }

        [Fact]
        public void Validate_RequiresNeutralForEveryDeclaredOutput()
        {
            string path = FindContractFile("Safety", "mp-ydl.estimated.safety.json");
            var contract = MotionSafeStopContract.Load(path);
            contract.NeutralOutputs.Remove("OAxis_06");

            var issues = contract.Validate();

            Assert.Contains(issues, i => i.Code == "safety.missing_neutral_output");
        }

        [Fact]
        public void Validate_RequiresSafeStopBeforeSwitchAndLoad()
        {
            string path = FindContractFile("Safety", "mp-ydl.estimated.safety.json");
            var contract = MotionSafeStopContract.Load(path);
            contract.Transitions.SwitchFilm = "start_directly";

            var issues = contract.Validate();

            Assert.Contains(issues, i => i.Code == "safety.unsafe_switch_rule");
        }

        [Fact]
        public void YdlMotionFile_MatchesSafeStopProfileAndHasNoErrors()
        {
            var contract = MotionSafeStopContract.Load(
                FindContractFile("Safety", "mp-ydl.estimated.safety.json")
            );
            var timeline = MotionFileReader.Read(FindContractFile("mp_ydl_game_demo.motion"));
            Assert.NotNull(timeline);

            var issues = MotionTimelineValidator.Validate(
                timeline,
                new MotionValidationOptions { SafeStopContract = contract }
            );

            Assert.False(
                MotionTimelineValidator.HasErrors(issues),
                string.Join(Environment.NewLine, issues.Select(i => i.ToString()))
            );
        }

        private static string FindContractFile(params string[] segments)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            string tail = Path.Combine(segments);
            for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "IOStudio", "Config", "Motion", tail);
                if (File.Exists(candidate)) return candidate;
            }
            throw new FileNotFoundException("找不到安全契约文件", tail);
        }
    }
}
