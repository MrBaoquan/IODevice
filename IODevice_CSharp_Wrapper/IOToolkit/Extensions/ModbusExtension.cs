namespace IOToolkit.Modbus
{
    /// <summary>
    /// Modbus 协议设备 (IOUI-*-MODBUS) 的语法糖.
    /// 通过 OAxis_240~242 约定通道向底层 MODBUS 插件下发功能码与参数.
    /// </summary>
    public static class ModbusExtension
    {
        static Key funcKey = "OAxis_240";
        static Key funcParam1Key = "OAxis_241";
        static Key funcParam2Key = "OAxis_242";
        static Key writeFuncCodeKey = "OAxis_250";

        public static void SwitchWriteCoilFuncCode(this IODevice modbusDevice)
        {
            modbusDevice.SetWriteFuncCode(5);
        }

        public static void SwitchWriteRegisterFuncCode(this IODevice modbusDevice)
        {
            modbusDevice.SetWriteFuncCode(6);
        }

        private static void SetWriteFuncCode(this IODevice modbusDevice, int funcCode)
        {
            modbusDevice.SetDO(writeFuncCodeKey, funcCode);
        }

        /// <summary>写线圈值.</summary>
        public static void WriteCoil(this IODevice modbusDevice, int address, bool value)
        {
            modbusDevice.SetDO(funcKey, 1);
            modbusDevice.SetDO(funcParam1Key, address);
            modbusDevice.SetDO(funcParam2Key, value ? 1 : 0);
            modbusDevice.DOImmediate();
        }

        /// <summary>写寄存器值.</summary>
        public static void WriteRegister(this IODevice modbusDevice, int address, int value)
        {
            modbusDevice.SetDO(funcKey, 2);
            modbusDevice.SetDO(funcParam1Key, address);
            modbusDevice.SetDO(funcParam2Key, value);
            modbusDevice.DOImmediate();
        }
    }
}
