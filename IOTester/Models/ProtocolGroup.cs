using ReactiveUI;
using System;
using System.Collections.ObjectModel;

namespace IOTester.Models
{
    /// <summary>
    /// 协议配置组，用于集中管理同一类型协议的配置参数及其下的所有映射
    /// </summary>
    public class ProtocolGroup : ReactiveObject
    {
        private string _id = Guid.NewGuid().ToString();
        private string _groupName = string.Empty;
        private string _protocolType = "NetIO";
        private bool _isExpanded = true;

        // NetIO 协议配置
        private string _targetIP = "127.0.0.1";
        private int _targetPort = 8000;

        // Modbus-RTU 协议配置
        private string _serialPort = "COM1";
        private int _baudRate = 9600;
        private int _dataBits = 8;
        private string _parity = "None";
        private int _stopBits = 1;

        // 自定义协议配置
        private string _customProtocolType = "TCP-Client"; // TCP-Client, TCP-Server, UDP, Serial
        private bool _isCustomMode = false;

        // 源设备配置
        private string _sourceDevice = string.Empty;
        private string _targetDevice = string.Empty;

        private string _description = string.Empty;

        /// <summary>
        /// 组ID（唯一标识）
        /// </summary>
        public string Id
        {
            get => _id;
            set => this.RaiseAndSetIfChanged(ref _id, value);
        }

        /// <summary>
        /// 是否展开显示子项
        /// </summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
        }

        /// <summary>
        /// 此协议组下的所有映射配置
        /// </summary>
        public ObservableCollection<MappingDto> Mappings { get; } =
            new ObservableCollection<MappingDto>();

        /// <summary>
        /// 协议组名称
        /// </summary>
        public string GroupName
        {
            get => _groupName;
            set => this.RaiseAndSetIfChanged(ref _groupName, value);
        }

        /// <summary>
        /// 协议类型：NetIO 或 Modbus-RTU 或 Custom
        /// </summary>
        public string ProtocolType
        {
            get => _protocolType;
            set
            {
                this.RaiseAndSetIfChanged(ref _protocolType, value);
                // 自动设置IsCustomMode
                IsCustomMode = value == "Custom";
            }
        }

        // ===== NetIO 协议参数 =====

        /// <summary>
        /// 目标IP地址（NetIO协议）
        /// </summary>
        public string TargetIP
        {
            get => _targetIP;
            set => this.RaiseAndSetIfChanged(ref _targetIP, value);
        }

        /// <summary>
        /// 目标端口（NetIO协议）
        /// </summary>
        public int TargetPort
        {
            get => _targetPort;
            set => this.RaiseAndSetIfChanged(ref _targetPort, value);
        }

        // ===== Modbus-RTU 协议参数 =====

        /// <summary>
        /// 串口号（Modbus-RTU协议）
        /// </summary>
        public string SerialPort
        {
            get => _serialPort;
            set => this.RaiseAndSetIfChanged(ref _serialPort, value);
        }

        /// <summary>
        /// 波特率（Modbus-RTU协议）
        /// </summary>
        public int BaudRate
        {
            get => _baudRate;
            set => this.RaiseAndSetIfChanged(ref _baudRate, value);
        }

        /// <summary>
        /// 数据位（Modbus-RTU协议）
        /// </summary>
        public int DataBits
        {
            get => _dataBits;
            set => this.RaiseAndSetIfChanged(ref _dataBits, value);
        }

        /// <summary>
        /// 校验位（Modbus-RTU协议）
        /// </summary>
        public string Parity
        {
            get => _parity;
            set => this.RaiseAndSetIfChanged(ref _parity, value);
        }

        /// <summary>
        /// 停止位（Modbus-RTU协议）
        /// </summary>
        public int StopBits
        {
            get => _stopBits;
            set => this.RaiseAndSetIfChanged(ref _stopBits, value);
        }

        /// <summary>
        /// 描述信息
        /// </summary>
        public string Description
        {
            get => _description;
            set => this.RaiseAndSetIfChanged(ref _description, value);
        }

        // ===== 自定义协议参数 =====

        /// <summary>
        /// 是否为自定义模式
        /// </summary>
        public bool IsCustomMode
        {
            get => _isCustomMode;
            set => this.RaiseAndSetIfChanged(ref _isCustomMode, value);
        }

        /// <summary>
        /// 自定义协议类型：TCP-Client, TCP-Server, UDP, Serial
        /// </summary>
        public string CustomProtocolType
        {
            get => _customProtocolType;
            set => this.RaiseAndSetIfChanged(ref _customProtocolType, value);
        }

        /// <summary>
        /// 源设备名称
        /// </summary>
        public string SourceDevice
        {
            get => _sourceDevice;
            set => this.RaiseAndSetIfChanged(ref _sourceDevice, value);
        }

        /// <summary>
        /// 目标设备名称（设备直出）
        /// </summary>
        public string TargetDevice
        {
            get => _targetDevice;
            set => this.RaiseAndSetIfChanged(ref _targetDevice, value);
        }
    }
}
