using System.Collections.Generic;

namespace IOStudio.Services.Config
{
    /// <summary>
    /// config.ini 后端 —— 绝大多数设备的默认路径。纯委托，不含任何自有逻辑，
    /// 保证既有插件的行为与引入本抽象之前完全一致。
    /// </summary>
    public sealed class IniDeviceConfigStore : IDeviceConfigStore
    {
        public static IniDeviceConfigStore Instance { get; } = new();

        private IniDeviceConfigStore() { }

        public string GetPath(string dllName) => IniConfigService.Instance.GetConfigPath(dllName);

        public Dictionary<string, string> ReadConfig(string dllName, int deviceIndex) =>
            IniConfigService.Instance.ReadConfig(dllName, deviceIndex);

        public Dictionary<string, string> ReadSection(string dllName, string sectionName) =>
            IniConfigService.Instance.ReadSection(dllName, sectionName);

        public void SaveConfig(
            string dllName,
            int deviceIndex,
            Dictionary<string, string> values,
            Dictionary<string, string>? defaultValues = null,
            List<string>? deletedKeys = null
        ) => IniConfigService.Instance.SaveConfig(dllName, deviceIndex, values, defaultValues, deletedKeys);

        public void SaveSection(
            string dllName,
            string sectionName,
            Dictionary<string, string> values,
            List<string>? deletedKeys = null
        ) => IniConfigService.Instance.SaveSection(dllName, sectionName, values, deletedKeys);
    }
}
