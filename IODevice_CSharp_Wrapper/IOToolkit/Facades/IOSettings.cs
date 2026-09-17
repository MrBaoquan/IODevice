using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IOToolkit.Core;

namespace IOToolkit
{
    public class IOSettings
    {
        public static int SetIOConfigPath(string InFilePath)
        {
            return IONativeWrapper.SetIOConfigPath(InFilePath);
        }

        public static int SetIOLogDir(string InLogDir)
        {
            return IONativeWrapper.SetIOLogDir(InLogDir);
        }

        public static int SetIORuntimeRoot(string InRuntimeRoot)
        {
            return IONativeWrapper.SetIORuntimeRoot(InRuntimeRoot);
        }

        /// <summary>
        /// 设置插件配置根目录。项目模式下由 UNIPlayer 注入项目根，
        /// 使各插件读取项目内的 config.ini 而非全局配置。
        /// </summary>
        public static int SetPluginConfigRoot(string InRoot)
        {
            return IONativeWrapper.SetPluginConfigRoot(InRoot);
        }
    }
}
