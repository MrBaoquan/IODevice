using System.Collections.Generic;
using Xunit;
using IOStudio.Cli;

namespace IOStudio.Tests.Services
{
    /// <summary>预检 Markdown 报告渲染测试 (纯函数)。</summary>
    public class ValidationReportTests
    {
        private static Dictionary<string, object?> Report(params Dictionary<string, object?>[] issues)
        {
            int errs = 0, warns = 0, infos = 0;
            foreach (var i in issues)
            {
                switch (i["severity"]?.ToString())
                {
                    case "Error": errs++; break;
                    case "Warning": warns++; break;
                    default: infos++; break;
                }
            }
            return new Dictionary<string, object?>
            {
                ["hasErrors"] = errs > 0,
                ["errorCount"] = errs,
                ["warningCount"] = warns,
                ["infoCount"] = infos,
                ["issues"] = new List<Dictionary<string, object?>>(issues),
            };
        }

        private static Dictionary<string, object?> Issue(string sev, string code, string msg, string track = "", double? t = null) =>
            new()
            {
                ["severity"] = sev,
                ["code"] = code,
                ["message"] = msg,
                ["track"] = track,
                ["timeMs"] = t,
            };

        [Fact]
        public void CleanReport_SaysNoIssues()
        {
            var md = CliRunner.BuildValidationReportMarkdown("demo.motion", Report());
            Assert.Contains("通过", md);
            Assert.Contains("无问题", md);
        }

        [Fact]
        public void Report_ListsIssuesBySeverityWithCode()
        {
            var md = CliRunner.BuildValidationReportMarkdown(
                "demo.motion",
                Report(
                    Issue("Error", "clip.invalid_range", "clip 区间非法", "俯仰", 1200),
                    Issue("Warning", "clip.overlap", "clip 重叠", "横滚", 3000),
                    Issue("Info", "track.always_neutral", "全程中性值", "升降")
                )
            );

            Assert.Contains("存在 Error", md);
            Assert.Contains("## Error (1)", md);
            Assert.Contains("## Warning (1)", md);
            Assert.Contains("## Info (1)", md);
            Assert.Contains("clip.invalid_range", md);
            Assert.Contains("clip.overlap", md);
            Assert.Contains("俯仰", md);
            Assert.Contains("1200", md);
        }

        [Fact]
        public void Report_EscapesPipeInMessage()
        {
            var md = CliRunner.BuildValidationReportMarkdown(
                "demo.motion",
                Report(Issue("Error", "x", "含 | 竖线 | 的消息"))
            );
            Assert.Contains("含 \\| 竖线 \\| 的消息", md);
        }
    }
}
