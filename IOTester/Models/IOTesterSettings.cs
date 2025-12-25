using System;
using System.IO;
using System.Xml.Serialization;
using DNHper;

namespace IOTester.Models
{
    /// <summary>
    /// IOTester 应用程序配置
    /// </summary>
    public class IOTesterSettings : SingletonConfig<IOTesterSettings>
    {
        /// <summary>
        /// UI 刷新间隔（毫秒）
        /// 默认100ms，范围：40-500ms
        /// - 40ms (25fps): 流畅实时显示，CPU占用高
        /// - 100ms (10fps): 推荐，平衡性能和流畅度
        /// - 200ms (5fps): 省电模式，适合慢速监控
        /// </summary>
        [XmlAttribute("UIRefreshIntervalMs")]
        public int UIRefreshIntervalMs { get; set; } = 100;

        /// <summary>
        /// 模拟量处理器刷新间隔（毫秒）
        /// 默认100ms，范围：50-500ms
        /// </summary>
        [XmlAttribute("AnalogProcessorIntervalMs")]
        public int AnalogProcessorIntervalMs { get; set; } = 100;

        /// <summary>
        /// 验证并修正配置值
        /// </summary>
        public void Validate()
        {
            // UI刷新率限制在 40-500ms
            if (UIRefreshIntervalMs < 40)
                UIRefreshIntervalMs = 40;
            else if (UIRefreshIntervalMs > 500)
                UIRefreshIntervalMs = 500;

            // 模拟量刷新率限制在 50-500ms
            if (AnalogProcessorIntervalMs < 50)
                AnalogProcessorIntervalMs = 50;
            else if (AnalogProcessorIntervalMs > 500)
                AnalogProcessorIntervalMs = 500;
        }

        /// <summary>
        /// 加载配置，如果文件不存在则创建默认配置
        /// </summary>
        public static void LoadOrCreate()
        {
            var configPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Config",
                "IOTesterSettings.xml"
            );

            try
            {
                if (File.Exists(configPath))
                {
                    Instance.SetConfig(configPath).Load();
                }
                else
                {
                    // 创建默认配置
                    Directory.CreateDirectory(Path.GetDirectoryName(configPath));
                    Instance.SetConfig(configPath).Save();
                }

                Instance.Validate();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
                // 使用默认值
                Instance.Validate();
            }
        }
    }
}
