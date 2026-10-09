using System;
using System.IO;
using System.Xml.Serialization;
using DNHper;

namespace IOStudio.Models
{
    /// <summary>
    /// IOStudio 应用程序配置
    /// </summary>
    public class IOStudioSettings : SingletonConfig<IOStudioSettings>
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
        /// 时间轴编辑器「设备输出」开关的上次状态。
        /// 默认 false（编排默认仅预览，避免误驱动设备）。
        /// </summary>
        [XmlAttribute("TimelineLiveOutput")]
        public bool TimelineLiveOutput { get; set; } = false;

        /// <summary>
        /// 是否已展示过「设备输出」首次使用提示。
        /// </summary>
        [XmlAttribute("TimelineLiveOutputHintShown")]
        public bool TimelineLiveOutputHintShown { get; set; } = false;

        /// <summary>
        /// 时间轴「设备下发节流」间隔（毫秒）。0 = 不节流（每个 tick 都下发, 现状）。
        ///
        /// <para>为什么要它: 联调时 C# 以 60Hz 逐帧把动作推给 native → 写设备; 有些设备/链路
        /// 吃不下这个频率, 需要能把它降下来。&gt;0 时只在距上次下发达到该间隔才下发一次,
        /// 中间帧只更新 UI、不写设备。native 的 SafetyGuard 用内部时钟测实际间隔, 与调用频率
        /// 无关, 所以降频不会削弱限速保护。</para>
        /// </summary>
        [XmlAttribute("MotionDispatchIntervalMs")]
        public int MotionDispatchIntervalMs { get; set; } = 0;

        /// <summary>检查器「待机循环」区段是否展开 (默认收起)。</summary>
        [XmlAttribute("InspectorIdleExpanded")]
        public bool InspectorIdleExpanded { get; set; } = false;

        /// <summary>检查器「标记」区段是否展开。</summary>
        [XmlAttribute("InspectorMarkerExpanded")]
        public bool InspectorMarkerExpanded { get; set; } = false;

        /// <summary>检查器「事件」区段是否展开。</summary>
        [XmlAttribute("InspectorEventExpanded")]
        public bool InspectorEventExpanded { get; set; } = false;

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

            // 动作下发节流: 0=不节流; 否则限制在 8-1000ms, 既避免无意义的高频也避免明显卡顿
            if (MotionDispatchIntervalMs < 0)
                MotionDispatchIntervalMs = 0;
            else if (MotionDispatchIntervalMs is > 0 and < 8)
                MotionDispatchIntervalMs = 8;
            else if (MotionDispatchIntervalMs > 1000)
                MotionDispatchIntervalMs = 1000;
        }

        /// <summary>
        /// 加载配置，如果文件不存在则创建默认配置
        /// </summary>
        public static void LoadOrCreate()
        {
            var configPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Config",
                "IOStudioSettings.xml"
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
