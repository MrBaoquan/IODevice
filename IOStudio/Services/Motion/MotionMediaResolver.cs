using System;
using System.IO;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 影片媒体路径的存取策略 — 决定 .motion 里的媒体引用是否可移植、缺失时能否给出明确结论。
    /// 抽成纯函数以便单测: 路径策略是"项目能不能复现"的关键, 不能只靠手点。
    /// </summary>
    public static class MotionMediaResolver
    {
        /// <summary>
        /// 存盘用路径: 优先相对于 .motion 所在目录 (项目整体可搬移);
        /// 跨目录过远 (上级两层以上) 时保留绝对路径, 避免存出难以理解的 <c>..\..\..\</c>。
        /// </summary>
        public static string ToPortablePath(string mediaPath, string? motionFilePath)
        {
            if (string.IsNullOrEmpty(mediaPath))
                return mediaPath;

            try
            {
                string baseDir = BaseDirOf(motionFilePath);
                if (string.IsNullOrEmpty(baseDir))
                    return mediaPath;

                string rel = Path.GetRelativePath(baseDir, mediaPath);
                string upTwo = ".." + Path.DirectorySeparatorChar + "..";
                return rel.StartsWith(upTwo, StringComparison.Ordinal) ? mediaPath : rel;
            }
            catch
            {
                return mediaPath;
            }
        }

        /// <summary>
        /// 加载用解析: 绝对路径直接校验存在性; 相对路径按 .motion 所在目录解析。
        /// 返回 null = 媒体缺失 (调用方应提示, 但**不得阻断**动作编辑)。
        /// </summary>
        public static string? Resolve(string? recorded, string? motionFilePath)
        {
            if (string.IsNullOrEmpty(recorded))
                return null;

            try
            {
                if (Path.IsPathRooted(recorded))
                    return File.Exists(recorded) ? recorded : null;

                string baseDir = BaseDirOf(motionFilePath);
                if (string.IsNullOrEmpty(baseDir))
                    return null;

                string combined = Path.GetFullPath(Path.Combine(baseDir, recorded));
                return File.Exists(combined) ? combined : null;
            }
            catch
            {
                return null;
            }
        }

        private static string BaseDirOf(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return "";
            return Path.GetDirectoryName(filePath) ?? "";
        }
    }
}
