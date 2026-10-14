using System.Collections.Generic;

namespace IOStudio.Services.Config
{
    /// <summary>
    /// 设备配置读写后端。面板只依赖此接口，具体落到 config.ini 还是 config.xml
    /// 由 schema 的 backend 决定 —— 部分插件 (MP-YDL / MP-NJLJ / RPLIDAR ...) 只读 XML，
    /// 用 INI 服务去读写它们会得到一个"能看不能用"的面板。
    /// </summary>
    public interface IDeviceConfigStore
    {
        /// <summary>后端实际使用的配置文件完整路径。</summary>
        string GetPath(string dllName);

        /// <summary>读取设备配置（deviceIndex=0 表示只读默认层）。</summary>
        Dictionary<string, string> ReadConfig(string dllName, int deviceIndex);

        /// <summary>读取指定 section 的原始配置（不合并）。</summary>
        Dictionary<string, string> ReadSection(string dllName, string sectionName);

        /// <summary>保存设备配置。</summary>
        void SaveConfig(
            string dllName,
            int deviceIndex,
            Dictionary<string, string> values,
            Dictionary<string, string>? defaultValues = null,
            List<string>? deletedKeys = null
        );

        /// <summary>保存指定 section 的配置。</summary>
        void SaveSection(
            string dllName,
            string sectionName,
            Dictionary<string, string> values,
            List<string>? deletedKeys = null
        );
    }
}
