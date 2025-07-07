using System;
using System.Diagnostics;
using System.IO;

namespace IOTester.Models
{
    /// <summary>
    /// Windows平台下，依次检测 Notepad++ 和 Notepad 是否存在，然后用第一个找到的打开指定文件。
    /// </summary>
    public static class EditorLauncher
    {
        /// <summary>
        /// 按优先级顺序检测并用对应编辑器打开文件。
        /// </summary>
        /// <param name="filePath">要打开的文件完整路径</param>
        /// <exception cref="FileNotFoundException">文件不存在时抛出</exception>
        /// <exception cref="InvalidOperationException">找不到编辑器时抛出</exception>
        public static void OpenWithPreferredEditor(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                Debug.WriteLine("文件路径不能为空", nameof(filePath));
            }
                

            if (!File.Exists(filePath))
            {
                Debug.WriteLine($"指定文件不存在: {filePath}");
            }
                

            string[] editors = new[] { "notepad++.exe", "notepad.exe" };

            foreach (string editor in editors)
            {
                string editorPath = FindExecutablePath(editor);
                if (!string.IsNullOrEmpty(editorPath) && File.Exists(editorPath))
                {
                    StartProcess(editorPath, $"\"{filePath}\"");
                    return;
                }
            }

            throw new InvalidOperationException("未找到可用的编辑器（Notepad++ 或 Notepad）。");
        }

        /// <summary>
        /// 通过执行 where 命令获取可执行文件的完整路径。
        /// </summary>
        /// <param name="exeName">可执行文件名（如 notepad++.exe）</param>
        /// <returns>找到的路径或空字符串</returns>
        private static string FindExecutablePath(string exeName)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = exeName,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using (var process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    // where 命令可能返回多行，取第一行最可能是有效路径
                    using (StringReader reader = new StringReader(output))
                    {
                        string line = reader.ReadLine();
                        if (!string.IsNullOrEmpty(line) && File.Exists(line.Trim()))
                        {
                            return line.Trim();
                        }
                    }
                }
            }
            catch
            {
                // 忽略异常，返回空字符串
            }

            return string.Empty;
        }

        /// <summary>
        /// 启动进程打开文件
        /// </summary>
        /// <param name="exePath">编辑器路径</param>
        /// <param name="arguments">参数，通常是文件路径</param>
        private static void StartProcess(string exePath, string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = true,
                CreateNoWindow = false,
            };

            Process.Start(psi);
        }
    }
}
