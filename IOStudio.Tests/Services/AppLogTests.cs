using System;
using System.Linq;
using IOStudio.Services;
using IOStudio.Services.Motion;
using NLog;
using NLog.Config;
using NLog.Targets;
using Xunit;

namespace IOStudio.Tests.Services
{
    /// <summary>
    /// 日志可观测性回归 —— 保证"用户操作可回溯"这一能力不被打断。
    ///
    /// <para>背景: UI 侧此前无日志, 出问题后无法回答"我点了什么"。现在:
    /// 内容变更操作经 <see cref="UndoRedoService"/> 记录为 Action (执行/撤销/重做),
    /// 界面反馈经 PushStatus 记录为 Status, 异常经 App.Log 全局捕获。
    /// 这些链路一旦被改坏, 排查能力会静默退化 —— 故用测试锁住。</para>
    /// </summary>
    public class AppLogTests
    {
        /// <summary>把日志渲染进内存的采集目标 (自建, 避免 MemoryTarget 的并发枚举问题)。</summary>
        private sealed class CapturingTarget : TargetWithLayout
        {
            private readonly System.Collections.Generic.List<string> _lines = new();

            public string[] Lines
            {
                get
                {
                    lock (_lines)
                        return _lines.ToArray();
                }
            }

            protected override void Write(LogEventInfo logEvent)
            {
                string line = RenderLogEvent(Layout, logEvent);
                lock (_lines)
                    _lines.Add(line);
            }
        }

        /// <summary>在隔离的 NLog 配置下捕获日志 (layout 与 nlog.config 保持一致, 含异常)。</summary>
        private static string[] CaptureLogs(Action action)
        {
            var target = new CapturingTarget
            {
                Layout =
                    "[${level:uppercase=true}] [${logger}] ${message}"
                    + "${when:when=length('${exception}')>0:inner= | ${exception:format=ToString}}",
            };
            var config = new LoggingConfiguration();
            config.AddRuleForAllLevels(target);
            var original = LogManager.Configuration;
            LogManager.Configuration = config;
            try
            {
                action();
                return target.Lines;
            }
            finally
            {
                LogManager.Configuration = original;
            }
        }

        private static IUndoableCommand Command(string description) =>
            new LambdaCommand(description, () => { }, () => { });

        [Fact]
        public void Execute_Logs_Action()
        {
            var logs = CaptureLogs(() =>
            {
                var svc = new UndoRedoService();
                svc.Execute(Command("添加关键帧"));
            });

            Assert.Contains(logs, l => l.Contains("Action") && l.Contains("执行") && l.Contains("添加关键帧"));
        }

        [Fact]
        public void Undo_And_Redo_Log_Action()
        {
            var logs = CaptureLogs(() =>
            {
                var svc = new UndoRedoService();
                svc.Execute(Command("移动关键帧"));
                svc.Undo();
                svc.Redo();
            });

            Assert.Contains(logs, l => l.Contains("Action") && l.Contains("撤销") && l.Contains("移动关键帧"));
            Assert.Contains(logs, l => l.Contains("Action") && l.Contains("重做") && l.Contains("移动关键帧"));
        }

        [Fact]
        public void Status_Logs_To_Separate_Logger()
        {
            var logs = CaptureLogs(() => AppLog.Status("已复制轨道到剪贴板"));

            Assert.Contains(logs, l => l.Contains("[Status]") && l.Contains("已复制轨道到剪贴板"));
        }

        [Fact]
        public void Error_Carries_Exception_For_Forensics()
        {
            var logs = CaptureLogs(() =>
            {
                try
                {
                    throw new InvalidOperationException("模拟失败");
                }
                catch (Exception ex)
                {
                    AppLog.Error("App", "操作失败", ex);
                }
            });

            var entry = logs.First(l => l.Contains("操作失败"));
            Assert.Contains("ERROR", entry);
            Assert.Contains("InvalidOperationException", entry); // NLog 渲染异常堆栈
        }

        [Fact]
        public void Describe_Flattens_Inner_Exceptions()
        {
            var ex = new InvalidOperationException(
                "外层",
                new ArgumentException("内层")
            );

            var text = AppLog.Describe(ex);

            Assert.Contains("InvalidOperationException", text);
            Assert.Contains("外层", text);
            Assert.Contains("ArgumentException", text);
            Assert.Contains("内层", text);
        }

        [Fact]
        public void CurrentFile_Points_Into_Logs_Directory()
        {
            var file = AppLog.CurrentFile;

            Assert.Contains("Logs", file);
            Assert.Contains("IOStudio-", file);
            Assert.EndsWith(".log", file);
        }
    }
}
