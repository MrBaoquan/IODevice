using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Reactive;

namespace IOTester.ViewModels
{
    /// <summary>
    /// 范围映射对话框ViewModel
    /// </summary>
    public class RangeMappingDialogViewModel : ViewModelBase
    {
        private string _sourceKeyPrefix = "Button_";
        private int _sourceStartChannel = 0;
        private int _sourceEndChannel = 15;
        private string _targetKeyPrefix = "Button_";
        private int _targetStartChannel = 0;
        private int _targetEndChannel = 15;
        private string _description = string.Empty;
        private bool _isEnabled = true;
        private string _mappingType = "开关量";
        private string _protocolType = "NetIO"; // 协议类型，用于判断是否为设备直出

        /// <summary>
        /// 映射类型（开关量 或 模拟量）
        /// </summary>
        public string MappingType
        {
            get => _mappingType;
            set
            {
                this.RaiseAndSetIfChanged(ref _mappingType, value);

                // 根据映射类型自动切换前缀
                if (value == "模拟量")
                {
                    SourceKeyPrefix = "Axis_";
                    // 设备直出使用 OAxis_ 前缀，其他协议使用 Axis_
                    TargetKeyPrefix = _protocolType == "DirectOutput" ? "OAxis_" : "Axis_";
                }
                else // 开关量
                {
                    SourceKeyPrefix = "Button_";
                    // 设备直出使用 OAxis_ 前缀，其他协议使用 Button_
                    TargetKeyPrefix = _protocolType == "DirectOutput" ? "OAxis_" : "Button_";
                }
            }
        }

        /// <summary>
        /// 可用的映射类型列表
        /// </summary>
        public ObservableCollection<string> AvailableMappingTypes { get; } =
            new ObservableCollection<string> { "开关量", "模拟量" };

        /// <summary>
        /// 源Key前缀（例如：Button_）
        /// </summary>
        public string SourceKeyPrefix
        {
            get => _sourceKeyPrefix;
            set
            {
                this.RaiseAndSetIfChanged(ref _sourceKeyPrefix, value);
                this.RaisePropertyChanged(nameof(SourceChannelPreview));
            }
        }

        /// <summary>
        /// 源起始通道号
        /// </summary>
        public int SourceStartChannel
        {
            get => _sourceStartChannel;
            set
            {
                this.RaiseAndSetIfChanged(ref _sourceStartChannel, value);
                this.RaisePropertyChanged(nameof(SourceChannelPreview));
                this.RaisePropertyChanged(nameof(MappingCount));
            }
        }

        /// <summary>
        /// 源结束通道号
        /// </summary>
        public int SourceEndChannel
        {
            get => _sourceEndChannel;
            set
            {
                this.RaiseAndSetIfChanged(ref _sourceEndChannel, value);
                this.RaisePropertyChanged(nameof(SourceChannelPreview));
                this.RaisePropertyChanged(nameof(MappingCount));
            }
        }

        /// <summary>
        /// 目标Key前缀（例如：Button_）
        /// </summary>
        public string TargetKeyPrefix
        {
            get => _targetKeyPrefix;
            set
            {
                this.RaiseAndSetIfChanged(ref _targetKeyPrefix, value);
                this.RaisePropertyChanged(nameof(TargetChannelPreview));
            }
        }

        /// <summary>
        /// 目标起始通道号
        /// </summary>
        public int TargetStartChannel
        {
            get => _targetStartChannel;
            set
            {
                this.RaiseAndSetIfChanged(ref _targetStartChannel, value);
                this.RaisePropertyChanged(nameof(TargetChannelPreview));
            }
        }

        /// <summary>
        /// 目标结束通道号
        /// </summary>
        public int TargetEndChannel
        {
            get => _targetEndChannel;
            set
            {
                this.RaiseAndSetIfChanged(ref _targetEndChannel, value);
                this.RaisePropertyChanged(nameof(TargetChannelPreview));
            }
        }

        /// <summary>
        /// 映射说明
        /// </summary>
        public string Description
        {
            get => _description;
            set => this.RaiseAndSetIfChanged(ref _description, value);
        }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => this.RaiseAndSetIfChanged(ref _isEnabled, value);
        }

        /// <summary>
        /// 源通道范围预览
        /// </summary>
        public string SourceChannelPreview
        {
            get
            {
                if (SourceStartChannel <= SourceEndChannel)
                {
                    return $"{SourceKeyPrefix}{SourceStartChannel:00} ~ {SourceKeyPrefix}{SourceEndChannel:00}";
                }
                return "起始通道号不能大于结束通道号";
            }
        }

        /// <summary>
        /// 目标通道范围预览
        /// </summary>
        public string TargetChannelPreview
        {
            get
            {
                if (TargetStartChannel <= TargetEndChannel)
                {
                    return $"{TargetKeyPrefix}{TargetStartChannel:00} ~ {TargetKeyPrefix}{TargetEndChannel:00}";
                }
                return "起始通道号不能大于结束通道号";
            }
        }

        /// <summary>
        /// 映射数量预览
        /// </summary>
        public string MappingCount
        {
            get
            {
                if (SourceStartChannel <= SourceEndChannel)
                {
                    int count = SourceEndChannel - SourceStartChannel + 1;
                    return $"将生成 {count} 个映射";
                }
                return "";
            }
        }

        /// <summary>
        /// 确认命令
        /// </summary>
        public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }

        /// <summary>
        /// 取消命令
        /// </summary>
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }

        public RangeMappingDialogViewModel(string protocolType = "NetIO")
        {
            _protocolType = protocolType;

            // 根据协议类型初始化目标前缀
            if (protocolType == "DirectOutput")
            {
                _targetKeyPrefix = "OAxis_"; // 设备直出默认使用 OAxis_
            }

            var canConfirm = this.WhenAnyValue(
                x => x.SourceKeyPrefix,
                x => x.SourceStartChannel,
                x => x.SourceEndChannel,
                x => x.TargetKeyPrefix,
                (srcPrefix, srcStart, srcEnd, tgtPrefix) =>
                    !string.IsNullOrWhiteSpace(srcPrefix)
                    && !string.IsNullOrWhiteSpace(tgtPrefix)
                    && srcStart <= srcEnd
                    && srcStart >= 0
                    && srcEnd >= 0
            );

            ConfirmCommand = ReactiveCommand.Create(() => { }, canConfirm);
            CancelCommand = ReactiveCommand.Create(() => { });
        }
    }
}
