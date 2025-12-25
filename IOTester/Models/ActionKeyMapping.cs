using ReactiveUI;

namespace IOTester.Models
{
    /// <summary>
    /// Key到Key的转发映射
    /// </summary>
    public class ActionKeyMapping : ReactiveObject
    {
        private string sourceKey = string.Empty;

        /// <summary>
        /// 源Key（接收的输入）
        /// </summary>
        public string SourceKey
        {
            get => sourceKey;
            set => this.RaiseAndSetIfChanged(ref sourceKey, value);
        }

        private string targetKey = string.Empty;

        /// <summary>
        /// 目标Key（转发的键值）
        /// </summary>
        public string TargetKey
        {
            get => targetKey;
            set => this.RaiseAndSetIfChanged(ref targetKey, value);
        }

        private string targetDevice = string.Empty;

        /// <summary>
        /// 目标设备地址（IP:Port 或串口信息）
        /// </summary>
        public string TargetDevice
        {
            get => targetDevice;
            set => this.RaiseAndSetIfChanged(ref targetDevice, value);
        }

        private bool isEnabled = true;

        /// <summary>
        /// 是否启用该映射
        /// </summary>
        public bool IsEnabled
        {
            get => isEnabled;
            set => this.RaiseAndSetIfChanged(ref isEnabled, value);
        }

        private string description = string.Empty;

        /// <summary>
        /// 映射说明
        /// </summary>
        public string Description
        {
            get => description;
            set => this.RaiseAndSetIfChanged(ref description, value);
        }

        // ===== 自定义模式字段 =====

        private string dataFormat = "ASCII"; // ASCII 或 HEX

        /// <summary>
        /// 数据格式：ASCII 或 HEX
        /// </summary>
        public string DataFormat
        {
            get => dataFormat;
            set => this.RaiseAndSetIfChanged(ref dataFormat, value);
        }

        private string pressedData = string.Empty;

        /// <summary>
        /// Pressed事件的原始数据（用于自定义模式）
        /// </summary>
        public string PressedData
        {
            get => pressedData;
            set => this.RaiseAndSetIfChanged(ref pressedData, value);
        }

        private string releasedData = string.Empty;

        /// <summary>
        /// Released事件的原始数据（用于自定义模式）
        /// </summary>
        public string ReleasedData
        {
            get => releasedData;
            set => this.RaiseAndSetIfChanged(ref releasedData, value);
        }
    }
}
