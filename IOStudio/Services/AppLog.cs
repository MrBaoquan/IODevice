using System;
using System.IO;
using System.Linq;
using System.Text;
using NLog;

namespace IOStudio.Services
{
    /// <summary>
    /// IOStudio 日志门面 —— 底层为 <b>NLog</b>（成熟日志库），配置见 <c>nlog.config</c>。
    ///
    /// <para>背景: IODevice(C++) 侧已有成熟的 spdlog 日志 (Logs/), 但 C# UI 层此前只有
    /// <c>Debug.WriteLine</c> —— Release 下等于没有, 且无全局异常捕获。于是"用户点了某个东西
    /// 之后出问题"无法回溯。本门面把 UI 侧日志并入 NLog, 与 C++ 侧共用 Logs/ 目录。</para>
    ///
    /// <para>为何用门面而非直接散调 NLog: 让调用点表达业务语义（类别 + 用户操作），
    /// 日志库可替换；同时集中处理"异常摘要"这类展示需求。</para>
    ///
    /// <para>约定: 类别 (category) 即 NLog 的 logger 名, 用于过滤:
    /// <c>App</c> / <c>Action</c> / <c>Cli</c> / <c>Window</c> / <c>Device</c>。
    /// 级别与保留策略在 nlog.config 中调整, 改完重启即可, 无需重编译。</para>
    /// </summary>
    public static class AppLog
    {
        private const string LogDirName = "Logs";
        private static bool _initialized;

        /// <summary>日志目录 (&lt;AppRoot&gt;/Logs, 与 IODevice C++ 侧日志同目录)。</summary>
        public static string LogDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LogDirName);

        /// <summary>今天的日志文件完整路径 (供界面展示 / 复制给他人)。</summary>
        public static string CurrentFile =>
            Path.Combine(LogDir, $"IOStudio-{DateTime.Now:yyyyMMdd}.log");

        /// <summary>初始化 (幂等)。应在进程最早阶段调用。</summary>
        public static void Initialize()
        {
            if (_initialized)
                return;
            _initialized = true;
            try
            {
                Directory.CreateDirectory(LogDir);
            }
            catch
            {
                /* 目录建不出来也不能影响启动; NLog 会自行重试并写 internal log */
            }
        }

        public static void Info(string category, string message) =>
            Logger(category).Info(message);

        public static void Warn(string category, string message) =>
            Logger(category).Warn(message);

        /// <summary>记录错误。异常交给 NLog 渲染 (含堆栈), 便于事后定位。</summary>
        public static void Error(string category, string message, Exception? ex = null)
        {
            var logger = Logger(category);
            if (ex is null)
                logger.Error(message);
            else
                logger.Error(ex, message);
        }

        /// <summary>记录一次用户操作 (排查"我点了什么"的主要入口)。</summary>
        public static void Action(string action, string? detail = null) =>
            Logger("Action").Info(detail is null ? action : $"{action} | {detail}");

        /// <summary>记录一条界面状态反馈 (非内容变更操作, 如复制到剪贴板、切换设备输出)。</summary>
        public static void Status(string message) => Logger("Status").Info(message);

        /// <summary>退出前刷新并关闭 (避免异步/缓冲导致末尾日志丢失)。</summary>
        public static void Shutdown()
        {
            try
            {
                LogManager.Shutdown();
            }
            catch
            {
                /* ignore */
            }
        }

        /// <summary>
        /// 异常摘要 (类型 + 消息 + 首帧, 最多 4 层内部异常) —— 供界面提示使用。
        /// 完整堆栈由 NLog 写入日志文件, 这里只求"一眼能看懂"。
        /// </summary>
        public static string Describe(Exception ex)
        {
            var sb = new StringBuilder();
            int depth = 0;
            for (var cur = ex; cur is not null && depth < 4; cur = cur.InnerException, depth++)
            {
                if (depth > 0)
                    sb.Append(" <- ");
                sb.Append(cur.GetType().Name).Append(": ").Append(cur.Message);

                var frame = (cur.StackTrace ?? "")
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(frame))
                    sb.Append(" @").Append(frame.Trim());
            }
            return sb.ToString();
        }

        private static Logger Logger(string category) =>
            LogManager.GetLogger(string.IsNullOrWhiteSpace(category) ? "App" : category);
    }
}
