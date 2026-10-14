using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using LibVLCSharp.Shared;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// LibVLC 原生库定位的单一入口。
    ///
    /// 发布形态：<c>libvlc\win-x64</c> 已作为 zip 内嵌进 IOStudio.exe（见 IOStudio.csproj 的
    /// <c>BuildLibVlcEmbedArchive</c>），安装目录不再有 libvlc 文件夹。原生插件必须落盘才能被
    /// LoadLibrary，故首次运行时把内嵌包解压到
    /// <c>%LOCALAPPDATA%\IOStudio\libvlc\win-x64-&lt;内容哈希&gt;</c>，之后复用（内容不变则缓存命中）。
    ///
    /// 开发/兜底：若输出目录旁存在松散 <c>libvlc\&lt;rid&gt;</c>（F5 由 VideoLAN 包复制），优先使用。
    ///
    /// 单文件下 LibVLCSharp 默认用程序集 <c>Assembly.Location</c> 推 libvlc 目录（值为空串）会失败，
    /// 因此这里始终显式传入目录。进程内只初始化一次。
    /// </summary>
    internal static class LibVlcInitializer
    {
        private const string ResourceSuffix = "libvlc.win-x64.zip";

        private static readonly object Gate = new();
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized)
                return;

            lock (Gate)
            {
                if (_initialized)
                    return;

                var rid = Environment.Is64BitProcess ? "win-x64" : "win-x86";

                // 1) 输出目录旁的松散 libvlc\<rid>（开发/兜底）
                var loose = Path.Combine(AppContext.BaseDirectory, "libvlc", rid);
                if (File.Exists(Path.Combine(loose, "libvlc.dll")))
                {
                    Core.Initialize(loose);
                    _initialized = true;
                    return;
                }

                // 2) 内嵌包 → 解压到 LocalAppData 缓存
                var extracted = TryExtractEmbedded(rid);
                if (extracted is not null)
                {
                    Core.Initialize(extracted);
                    _initialized = true;
                    return;
                }

                // 3) 最后兜底：交给 LibVLCSharp 自行探测
                Core.Initialize();
                _initialized = true;
            }
        }

        private static string? TryExtractEmbedded(string rid)
        {
            var asm = typeof(LibVlcInitializer).Assembly;
            var resourceName = FindResourceName(asm);
            if (resourceName is null)
                return null;

            using var src = asm.GetManifestResourceStream(resourceName);
            if (src is null)
                return null;

            string hash;
            using (var sha = SHA256.Create())
                hash = Convert.ToHexString(sha.ComputeHash(src)).Substring(0, 16).ToLowerInvariant();
            src.Position = 0;

            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IOStudio", "libvlc");
            var dir = Path.Combine(root, $"{rid}-{hash}");
            if (File.Exists(Path.Combine(dir, "libvlc.dll")))
                return dir;

            var tmp = $"{dir}.tmp-{Guid.NewGuid():N}";
            try
            {
                Directory.CreateDirectory(tmp);
                var tmpFull = Path.GetFullPath(tmp);
                using (var zip = new ZipArchive(src, ZipArchiveMode.Read))
                {
                    foreach (var entry in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name))
                            continue; // 目录项

                        var target = Path.GetFullPath(Path.Combine(tmp, entry.FullName));
                        if (!target.StartsWith(tmpFull, StringComparison.OrdinalIgnoreCase))
                            continue; // 防目录穿越

                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        entry.ExtractToFile(target, overwrite: true);
                    }
                }

                Directory.CreateDirectory(root);
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
                Directory.Move(tmp, dir);
            }
            catch
            {
                TryDeleteDirectory(tmp);
                throw;
            }

            return dir;
        }

        private static string? FindResourceName(Assembly asm)
        {
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (name.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase))
                    return name;
            }
            return null;
        }

        private static void TryDeleteDirectory(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch
            {
                // 尽力而为；缓存目录残留不影响下次（下次会换新临时名）
            }
        }
    }
}
