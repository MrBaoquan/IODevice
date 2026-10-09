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
        /// 动作编辑（联调）的「设备下发节流」运行时覆盖（毫秒）。
        /// -1 = 跟随动作文件（.motion 的 dispatch_interval_ms）；0 = 不节流；&gt;0 = 覆盖为指定间隔。
        /// <para>
        /// 这是**动作编辑环节的输出设置**，和影片/媒体无关：仅作为运行时覆盖推给 native，
        /// **不写回** .motion 配置（同一动作文件到放映端仍按文件自带的值派发）。
        /// </para>
        /// </summary>
        [XmlAttribute("MotionDispatchIntervalMs")]
        public int MotionDispatchIntervalMs { get; set; } = -1;

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

            // 下发节流: -1 = 跟随动作文件, 0 = 不节流, >0 = 覆盖 (上限 1000ms)
            if (MotionDispatchIntervalMs < -1)
                MotionDispatchIntervalMs = -1;
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
