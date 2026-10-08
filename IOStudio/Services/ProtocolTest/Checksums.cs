using System;
using System.Collections.Generic;

namespace IOStudio.Services.ProtocolTest
{
    /// <summary>
    /// 校验和计算 —— 工业协议最常用的几种。
    ///
    /// <para>为什么内置: 主流调试台 (SSCOM) 都带校验计算并可"追加到发送"。
    /// 手工算 CRC 再填进去既慢又容易错, 而这是现场每天都要做的事。</para>
    /// </summary>
    public static class Checksums
    {
        /// <summary>可选校验方式 (顺序即 UI 下拉顺序)。</summary>
        public static IReadOnlyList<string> Names { get; } = new[]
        {
            "无",
            "Sum8",
            "XOR8",
            "CRC16-Modbus",
            "CRC16-CCITT",
            "CRC32",
        };

        /// <summary>返回校验值字节 (小端存放, 与各协议惯例一致时由调用方再决定顺序)。</summary>
        public static byte[] Compute(string? name, byte[] data, bool littleEndian = true)
        {
            var n = (name ?? "无").Trim();
            switch (n)
            {
                case "Sum8":
                    return new[] { Sum8(data) };
                case "XOR8":
                    return new[] { Xor8(data) };
                case "CRC16-Modbus":
                    return Order(BitConverter.GetBytes(Crc16Modbus(data)), littleEndian);
                case "CRC16-CCITT":
                    return Order(BitConverter.GetBytes(Crc16Ccitt(data)), littleEndian);
                case "CRC32":
                    return Order(BitConverter.GetBytes(Crc32(data)), littleEndian);
                default:
                    return Array.Empty<byte>();
            }
        }

        /// <summary>校验方式的说明 (UI 提示用)。</summary>
        public static string Describe(string? name) =>
            (name ?? "无").Trim() switch
            {
                "Sum8" => "逐字节求和取低 8 位",
                "XOR8" => "逐字节异或",
                "CRC16-Modbus" => "多项式 0xA001, 初值 0xFFFF",
                "CRC16-CCITT" => "多项式 0x1021, 初值 0xFFFF",
                "CRC32" => "标准 CRC-32 (IEEE 802.3)",
                _ => "不追加",
            };

        public static byte Sum8(byte[] data)
        {
            byte sum = 0;
            foreach (var b in data)
                sum += b;
            return sum;
        }

        public static byte Xor8(byte[] data)
        {
            byte x = 0;
            foreach (var b in data)
                x ^= b;
            return x;
        }

        public static ushort Crc16Modbus(byte[] data)
        {
            ushort crc = 0xFFFF;
            foreach (var b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                    crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            }
            return crc;
        }

        public static ushort Crc16Ccitt(byte[] data)
        {
            ushort crc = 0xFFFF;
            foreach (var b in data)
            {
                crc ^= (ushort)(b << 8);
                for (int i = 0; i < 8; i++)
                    crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
            }
            return crc;
        }

        public static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (var b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
            return ~crc;
        }

        /// <summary>
        /// 按字节序重排。**注意**: CRC16-Modbus 在报文里是**小端**(低字节在前),
        /// 而 CRC16-CCITT 常按大端; 这里统一由调用方指定, 默认小端。
        /// </summary>
        private static byte[] Order(byte[] littleEndianBytes, bool littleEndian)
        {
            if (littleEndian)
                return littleEndianBytes;
            var copy = (byte[])littleEndianBytes.Clone();
            Array.Reverse(copy);
            return copy;
        }
    }
}
