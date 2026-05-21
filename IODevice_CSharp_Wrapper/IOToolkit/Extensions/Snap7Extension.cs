namespace IOToolkit.Snap7
{
    /// <summary>
    /// Snap7 (S7 系列 PLC) 插件的语法糖.
    /// 通道 240~249 用于动态写入任意地址:
    ///   OAxis_240 区域码   1=DB  2=PA  3=PE  4=MK
    ///   OAxis_241 DB区=DB号,      其他区=地址
    ///   OAxis_242 DB区=地址,      其他区=功能码
    ///   OAxis_243 DB区=功能码,    其他区=数据起始
    ///   OAxis_244 数据字节1
    ///   OAxis_245 数据字节2
    /// </summary>
    public static class Snap7Extension
    {
        static Key areaCodeKey = "OAxis_240";
        static Key param1Key = "OAxis_241";
        static Key param2Key = "OAxis_242";
        static Key param3Key = "OAxis_243";
        static Key param4Key = "OAxis_244";
        static Key param5Key = "OAxis_245";

        // ========== DB区 ==========

        public static void WriteDBByte(this IODevice d, int dbNumber, int address, byte value)
        {
            d.SetDO(areaCodeKey, 1);
            d.SetDO(param1Key, dbNumber);
            d.SetDO(param2Key, address);
            d.SetDO(param3Key, 1);
            d.SetDO(param4Key, value);
            d.DOImmediate();
        }

        public static void WriteDBWord(this IODevice d, int dbNumber, int address, ushort value)
        {
            d.SetDO(areaCodeKey, 1);
            d.SetDO(param1Key, dbNumber);
            d.SetDO(param2Key, address);
            d.SetDO(param3Key, 2);
            d.SetDO(param4Key, value);
            d.DOImmediate();
        }

        public static void WriteDBDWord(this IODevice d, int dbNumber, int address, uint value)
        {
            d.SetDO(areaCodeKey, 1);
            d.SetDO(param1Key, dbNumber);
            d.SetDO(param2Key, address);
            d.SetDO(param3Key, 3);
            d.SetDO(param4Key, (value >> 16) & 0xFFFF);
            d.SetDO(param5Key, value & 0xFFFF);
            d.DOImmediate();
        }

        public static void WriteDBBit(
            this IODevice d,
            int dbNumber,
            int address,
            int bitOffset,
            bool value
        )
        {
            d.SetDO(areaCodeKey, 1);
            d.SetDO(param1Key, dbNumber);
            d.SetDO(param2Key, address);
            d.SetDO(param3Key, 4);
            d.SetDO(param4Key, bitOffset);
            d.SetDO(param5Key, value ? 1 : 0);
            d.DOImmediate();
        }

        // ========== PA区 (物理输出) ==========

        public static void WriteOutputByte(this IODevice d, int address, byte value)
        {
            d.SetDO(areaCodeKey, 2);
            d.SetDO(param1Key, address);
            d.SetDO(param2Key, 1);
            d.SetDO(param3Key, value);
            d.DOImmediate();
        }

        public static void WriteOutputWord(this IODevice d, int address, ushort value)
        {
            d.SetDO(areaCodeKey, 2);
            d.SetDO(param1Key, address);
            d.SetDO(param2Key, 2);
            d.SetDO(param3Key, value);
            d.DOImmediate();
        }

        public static void WriteOutputDWord(this IODevice d, int address, uint value)
        {
            d.SetDO(areaCodeKey, 2);
            d.SetDO(param1Key, address);
            d.SetDO(param2Key, 3);
            d.SetDO(param3Key, (value >> 16) & 0xFFFF);
            d.SetDO(param4Key, value & 0xFFFF);
            d.DOImmediate();
        }

        public static void WriteOutputBit(this IODevice d, int address, int bitOffset, bool value)
        {
            d.SetDO(areaCodeKey, 2);
            d.SetDO(param1Key, address);
            d.SetDO(param2Key, 4);
            d.SetDO(param3Key, bitOffset);
            d.SetDO(param4Key, value ? 1 : 0);
            d.DOImmediate();
        }

        // ========== PE区 (物理输入, 仅测试) ==========

        public static void WriteInputByte(this IODevice d, int address, byte value)
        {
            d.SetDO(areaCodeKey, 3);
            d.SetDO(param1Key, address);
            d.SetDO(param2Key, 1);
            d.SetDO(param3Key, value);
            d.DOImmediate();
        }

        public static void WriteInputWord(this IODevice d, int address, ushort value)
        {
            d.SetDO(areaCodeKey, 3);
            d.SetDO(param1Key, address);
            d.SetDO(param2Key, 2);
            d.SetDO(param3Key, value);
            d.DOImmediate();
        }

        public static void WriteInputBit(this IODevice d, int address, int bitOffset, bool value)
        {
            d.SetDO(areaCodeKey, 3);
            d.SetDO(param1Key, address);
            d.SetDO(param2Key, 4);
            d.SetDO(param3Key, bitOffset);
            d.SetDO(param4Key, value ? 1 : 0);
            d.DOImmediate();
        }

        // ========== MK区 (标志位) ==========

        public static void WriteMarkerByte(this IODevice d, int address, byte value)
        {
            d.SetDO(areaCodeKey, 4);
            d.SetDO(param1Key, address);
            d.SetDO(param2Key, 1);
            d.SetDO(param3Key, value);
            d.DOImmediate();
        }

        public static void WriteMarkerWord(this IODevice d, int address, ushort value)
        {
            d.SetDO(areaCodeKey, 4);
            d.SetDO(param1Key, address);
            d.SetDO(param2Key, 2);
            d.SetDO(param3Key, value);
            d.DOImmediate();
        }

        public static void WriteMarkerDWord(this IODevice d, int address, uint value)
        {
            d.SetDO(areaCodeKey, 4);
            d.SetDO(param1Key, address);
            d.SetDO(param2Key, 3);
            d.SetDO(param3Key, (value >> 16) & 0xFFFF);
            d.SetDO(param4Key, value & 0xFFFF);
            d.DOImmediate();
        }

        public static void WriteMarkerBit(this IODevice d, int address, int bitOffset, bool value)
        {
            d.SetDO(areaCodeKey, 4);
            d.SetDO(param1Key, address);
            d.SetDO(param2Key, 4);
            d.SetDO(param3Key, bitOffset);
            d.SetDO(param4Key, value ? 1 : 0);
            d.DOImmediate();
        }

        // ========== S7-200 V区便捷方法 (通过 DB1) ==========

        public static void WriteVByte(this IODevice d, int address, byte value) =>
            d.WriteDBByte(1, address, value);

        public static void WriteVWord(this IODevice d, int address, ushort value) =>
            d.WriteDBWord(1, address, value);

        public static void WriteVDWord(this IODevice d, int address, uint value) =>
            d.WriteDBDWord(1, address, value);

        public static void WriteVBit(this IODevice d, int address, int bitOffset, bool value) =>
            d.WriteDBBit(1, address, bitOffset, value);
    }
}
