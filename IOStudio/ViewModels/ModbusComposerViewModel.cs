using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using IOStudio.Services.ProtocolTest;
using ReactiveUI;

namespace IOStudio.ViewModels
{
    /// <summary>Modbus 功能码选项 (下拉显示用)。</summary>
    public sealed record ModbusFunctionOption(string Label, byte Code)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Modbus 报文生成器 (发送区里的一段)。
    ///
    /// <para>填好 从站 / 功能码 / 地址 / 数量 / 值 → 自动算好整帧 (RTU 补 CRC16, TCP 补 MBAP)
    /// 写入发送框, 复用通用发送链路; 收到应答时由会话按 <see cref="ProtocolSessionViewModel.DecodeModbus"/>
    /// 解析成一行"解析"日志。</para>
    /// </summary>
    public sealed class ModbusComposerViewModel : ViewModelBase
    {
        private readonly ProtocolSessionViewModel _session;

        public ModbusComposerViewModel(ProtocolSessionViewModel session)
        {
            _session = session;
            ApplyCommand = ReactiveCommand.Create(Apply);
        }

        public ReactiveCommand<Unit, Unit> ApplyCommand { get; }

        public IReadOnlyList<string> Modes { get; } = new[] { "RTU", "TCP" };

        public IReadOnlyList<ModbusFunctionOption> Functions { get; } = new[]
        {
            new ModbusFunctionOption("01 读线圈", Modbus.ReadCoils),
            new ModbusFunctionOption("02 读离散输入", Modbus.ReadDiscreteInputs),
            new ModbusFunctionOption("03 读保持寄存器", Modbus.ReadHoldingRegisters),
            new ModbusFunctionOption("04 读输入寄存器", Modbus.ReadInputRegisters),
            new ModbusFunctionOption("05 写单个线圈", Modbus.WriteSingleCoil),
            new ModbusFunctionOption("06 写单个寄存器", Modbus.WriteSingleRegister),
            new ModbusFunctionOption("0F 写多个线圈", Modbus.WriteMultipleCoils),
            new ModbusFunctionOption("10 写多个寄存器", Modbus.WriteMultipleRegisters),
        };

        private bool _expanded;
        public bool Expanded
        {
            get => _expanded;
            set => this.RaiseAndSetIfChanged(ref _expanded, value);
        }

        private string _mode = "RTU";
        public string Mode
        {
            get => _mode;
            set
            {
                this.RaiseAndSetIfChanged(ref _mode, value);
                RaiseDerived();
            }
        }

        private ModbusFunctionOption _function = null!;
        public ModbusFunctionOption Function
        {
            get => _function ??= Functions[2]; // 默认 03 读保持寄存器
            set
            {
                this.RaiseAndSetIfChanged(ref _function, value);
                RaiseDerived();
            }
        }

        private int _slaveId = 1;
        public int SlaveId
        {
            get => _slaveId;
            set
            {
                this.RaiseAndSetIfChanged(ref _slaveId, value);
                RaiseDerived();
            }
        }

        private int _startAddress;
        public int StartAddress
        {
            get => _startAddress;
            set
            {
                this.RaiseAndSetIfChanged(ref _startAddress, value);
                RaiseDerived();
            }
        }

        private int _quantity = 10;
        public int Quantity
        {
            get => _quantity;
            set
            {
                this.RaiseAndSetIfChanged(ref _quantity, value);
                RaiseDerived();
            }
        }

        private string _values = "";
        public string Values
        {
            get => _values;
            set
            {
                this.RaiseAndSetIfChanged(ref _values, value);
                RaiseDerived();
            }
        }

        private int _transactionId = 1;

        private string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            private set => this.RaiseAndSetIfChanged(ref _statusText, value);
        }

        /// <summary>是否需要填写"值" (写类功能码)。</summary>
        public bool NeedsValues => Function.Code is Modbus.WriteSingleCoil
            or Modbus.WriteSingleRegister
            or Modbus.WriteMultipleCoils
            or Modbus.WriteMultipleRegisters;

        public string ValuesHint => Function.Code switch
        {
            Modbus.WriteSingleCoil => "1/0 或 ON/OFF",
            Modbus.WriteSingleRegister => "寄存器值 (十进制或 0x..)",
            Modbus.WriteMultipleCoils => "位序列, 如 1,0,1,1",
            Modbus.WriteMultipleRegisters => "寄存器值, 如 100,200",
            _ => "",
        };

        /// <summary>生成报文的十六进制预览 (随字段即时更新)。</summary>
        public string Preview
        {
            get
            {
                try
                {
                    return string.Join(" ", Build().Select(b => b.ToString("X2")));
                }
                catch (Exception ex)
                {
                    return "无法生成: " + ex.Message;
                }
            }
        }

        public bool HasPreviewError
        {
            get
            {
                try
                {
                    Build();
                    return false;
                }
                catch
                {
                    return true;
                }
            }
        }

        private byte[] Build() => Modbus.BuildRequest(
            Mode,
            (byte)Math.Clamp(SlaveId, 0, 255),
            Function.Code,
            (ushort)Math.Clamp(StartAddress, 0, 65535),
            (ushort)Math.Clamp(Quantity, 0, 65535),
            Values,
            (ushort)_transactionId
        );

        private void Apply()
        {
            try
            {
                var bytes = Build();
                _session.SetSendHex(bytes);
                StatusText = $"已生成 {bytes.Length} 字节 → 发送框" + (_session.IsRunning ? "" : " (点开始后发送)");
                if (Modbus.IsTcp(Mode))
                    _transactionId = _transactionId >= 0xFFFF ? 1 : _transactionId + 1;
            }
            catch (Exception ex)
            {
                StatusText = "生成失败: " + ex.Message;
            }
        }

        private void RaiseDerived()
        {
            this.RaisePropertyChanged(nameof(NeedsValues));
            this.RaisePropertyChanged(nameof(ValuesHint));
            this.RaisePropertyChanged(nameof(Preview));
            this.RaisePropertyChanged(nameof(HasPreviewError));
        }
    }
}
