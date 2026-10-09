using System;
using System.Collections.Generic;
using System.Linq;

namespace IOStudio.Services.ProtocolTest
{
    /// <summary>
    /// Modbus 报文组帧与解析 (RTU / TCP)。
    ///
    /// <para>定位: 本工具是"字节级测试台", 不内置 Modbus 主站轮询栈; 这里只做两件
    /// 现场最常用的事 —— **把要发的请求帧算好** (含 CRC16 / MBAP), 以及**把收到的
    /// 应答看懂** (功能码 / 寄存器 / 异常码)。请求字节仍走通用发送框, 收帧仍进通用日志,
    /// 不偏离"收发字节"这一核心, 也不用为 Modbus 单独开一条链路。</para>
    ///
    /// <para>RTU: <c>[从站][功能][数据…][CRC16 低][CRC16 高]</c>;
    /// TCP: <c>[事务ID 2][协议ID=0 2][长度 2][单元ID][数据…]</c> (无 CRC)。</para>
    /// </summary>
    public static class Modbus
    {
        public const byte ReadCoils = 0x01;
        public const byte ReadDiscreteInputs = 0x02;
        public const byte ReadHoldingRegisters = 0x03;
        public const byte ReadInputRegisters = 0x04;
        public const byte WriteSingleCoil = 0x05;
        public const byte WriteSingleRegister = 0x06;
        public const byte WriteMultipleCoils = 0x0F;
        public const byte WriteMultipleRegisters = 0x10;

        private static readonly byte[] KnownFunctions =
            { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x0F, 0x10 };

        /// <summary>生成一条请求帧。mode = RTU | TCP。</summary>
        public static byte[] BuildRequest(
            string mode,
            byte slave,
            byte function,
            ushort address,
            ushort quantity,
            string values,
            ushort transactionId = 1
        )
        {
            var pdu = BuildPdu(function, address, quantity, values);
            return IsTcp(mode) ? BuildTcp(slave, pdu, transactionId) : BuildRtu(slave, pdu);
        }

        public static bool IsTcp(string? mode) =>
            string.Equals((mode ?? "").Trim(), "TCP", StringComparison.OrdinalIgnoreCase);

        private static byte[] BuildPdu(byte function, ushort address, ushort quantity, string values)
        {
            var list = new List<byte> { function };
            void AddU16(int v)
            {
                list.Add((byte)((v >> 8) & 0xFF));
                list.Add((byte)(v & 0xFF));
            }

            switch (function)
            {
                case ReadCoils:
                case ReadDiscreteInputs:
                case ReadHoldingRegisters:
                case ReadInputRegisters:
                    AddU16(address);
                    AddU16(quantity);
                    break;

                case WriteSingleCoil:
                {
                    var on = ParseCoilScalar(values);
                    AddU16(address);
                    AddU16(on ? 0xFF00 : 0x0000);
                    break;
                }

                case WriteSingleRegister:
                {
                    if (!TryParseNumber(values, out var reg))
                        throw new ArgumentException("写单个寄存器需要填写寄存器值 (十进制或 0x..)");
                    AddU16(address);
                    AddU16((int)reg);
                    break;
                }

                case WriteMultipleCoils:
                {
                    var bits = ParseBits(values);
                    if (bits.Count == 0)
                        throw new ArgumentException("写多个线圈需要填写位序列, 如 1,0,1,1");
                    var qty = quantity > 0 ? quantity : bits.Count;
                    AddU16(address);
                    AddU16(qty);
                    var byteCount = (bits.Count + 7) / 8;
                    list.Add((byte)byteCount);
                    var packed = new byte[byteCount];
                    for (var i = 0; i < bits.Count; i++)
                        if (bits[i])
                            packed[i / 8] |= (byte)(1 << (i % 8));
                    list.AddRange(packed);
                    break;
                }

                case WriteMultipleRegisters:
                {
                    var regs = ParseRegisters(values);
                    if (regs.Count == 0)
                        throw new ArgumentException("写多个寄存器需要填写寄存器值, 如 100,200");
                    var qty = quantity > 0 ? quantity : regs.Count;
                    AddU16(address);
                    AddU16(qty);
                    list.Add((byte)(regs.Count * 2));
                    foreach (var r in regs)
                        AddU16(r);
                    break;
                }

                default:
                    throw new ArgumentException($"不支持的功能码: 0x{function:X2}");
            }

            return list.ToArray();
        }

        private static byte[] BuildRtu(byte slave, byte[] pdu)
        {
            var frame = new byte[1 + pdu.Length + 2];
            frame[0] = slave;
            Array.Copy(pdu, 0, frame, 1, pdu.Length);
            var crc = Checksums.Crc16Modbus(frame[..(1 + pdu.Length)]);
            frame[^2] = (byte)(crc & 0xFF); // Modbus CRC 低字节在前
            frame[^1] = (byte)(crc >> 8);
            return frame;
        }

        private static byte[] BuildTcp(byte slave, byte[] pdu, ushort transactionId)
        {
            var frame = new byte[7 + pdu.Length];
            frame[0] = (byte)((transactionId >> 8) & 0xFF);
            frame[1] = (byte)(transactionId & 0xFF);
            frame[2] = 0; // 协议 ID
            frame[3] = 0;
            var length = pdu.Length + 1; // 单元 ID + PDU
            frame[4] = (byte)((length >> 8) & 0xFF);
            frame[5] = (byte)(length & 0xFF);
            frame[6] = slave;
            Array.Copy(pdu, 0, frame, 7, pdu.Length);
            return frame;
        }

        // ───────────── 解析 ─────────────

        /// <summary>
        /// 尝试把一帧字节识别为 Modbus 应答/请求。优先按 TCP (MBAP) 判断, 否则校验
        /// RTU 的 CRC; 都不像就返回 false (普通二进制帧不该被误报成 Modbus)。
        /// </summary>
        public static bool TryDecode(byte[]? data, out ModbusDecode decode)
        {
            decode = null!;
            if (data is null || data.Length < 5)
                return false;

            // TCP: MBAP (协议 ID 必须为 0, 长度字段自洽)
            if (data.Length >= 8 && data[2] == 0 && data[3] == 0)
            {
                var len = (data[4] << 8) | data[5];
                if (len >= 2 && 6 + len == data.Length && IsKnownFunction(data[7]))
                {
                    decode = DecodePdu("TCP", data[6], data[7..]);
                    return true;
                }
            }

            // RTU: 末尾 CRC16 校验通过
            var body = data[..^2];
            var crc = Checksums.Crc16Modbus(body);
            if ((byte)(crc & 0xFF) == data[^2] && (byte)(crc >> 8) == data[^1])
            {
                var pdu = data[1..^2];
                if (pdu.Length >= 1 && IsKnownFunction(pdu[0]))
                {
                    decode = DecodePdu("RTU", data[0], pdu);
                    return true;
                }
            }

            return false;
        }

        private static bool IsKnownFunction(byte fn)
        {
            var baseFn = (byte)(fn & 0x7F);
            return KnownFunctions.Contains(baseFn);
        }

        private static ModbusDecode DecodePdu(string mode, byte slave, byte[] pdu)
        {
            var fn = pdu[0];

            if ((fn & 0x80) != 0)
            {
                var code = pdu.Length > 1 ? pdu[1] : (byte)0;
                return new ModbusDecode
                {
                    Mode = mode,
                    Slave = slave,
                    Function = fn,
                    IsException = true,
                    ExceptionCode = code,
                    Summary =
                        $"{mode} 从站 {slave} 异常响应 (功能 0x{fn:X2}): 码 {code} {ExceptionText(code)}",
                };
            }

            switch (fn)
            {
                case ReadCoils:
                case ReadDiscreteInputs:
                {
                    var bc = pdu.Length > 1 ? pdu[1] : 0;
                    var bits = new List<bool>();
                    for (var i = 0; i < bc; i++)
                        if (2 + i < pdu.Length)
                            for (var b = 0; b < 8; b++)
                                bits.Add((pdu[2 + i] & (1 << b)) != 0);
                    var shown = string.Join(",", bits.Take(32).Select(b => b ? "1" : "0"));
                    return new ModbusDecode
                    {
                        Mode = mode,
                        Slave = slave,
                        Function = fn,
                        Coils = bits.ToArray(),
                        Summary =
                            $"{mode} 从站 {slave} 功能 0x{fn:X2} 读线圈 {bits.Count} 位: [{shown}{(bits.Count > 32 ? ", …" : "")}]",
                    };
                }

                case ReadHoldingRegisters:
                case ReadInputRegisters:
                {
                    var bc = pdu.Length > 1 ? pdu[1] : 0;
                    var regs = new List<ushort>();
                    for (var i = 0; i + 1 < bc && 2 + i + 1 < pdu.Length; i += 2)
                        regs.Add((ushort)((pdu[2 + i] << 8) | pdu[3 + i]));
                    var shown = string.Join(", ", regs.Take(16).Select(r => $"{r}(0x{r:X4})"));
                    return new ModbusDecode
                    {
                        Mode = mode,
                        Slave = slave,
                        Function = fn,
                        Registers = regs.ToArray(),
                        Summary =
                            $"{mode} 从站 {slave} 功能 0x{fn:X2} 读寄存器 {regs.Count} 个: [{shown}{(regs.Count > 16 ? ", …" : "")}]",
                    };
                }

                case WriteSingleCoil:
                case WriteSingleRegister:
                    if (pdu.Length >= 5)
                    {
                        var addr = (pdu[1] << 8) | pdu[2];
                        var val = (pdu[3] << 8) | pdu[4];
                        var extra = fn == WriteSingleCoil
                            ? (val == 0xFF00 ? "ON" : "OFF")
                            : $"{val}(0x{val:X4})";
                        return new ModbusDecode
                        {
                            Mode = mode,
                            Slave = slave,
                            Function = fn,
                            Registers = fn == WriteSingleRegister
                                ? new[] { (ushort)val }
                                : Array.Empty<ushort>(),
                            Summary =
                                $"{mode} 从站 {slave} 功能 0x{fn:X2} 写单个: 地址 {addr} = {extra}",
                        };
                    }
                    break;

                case WriteMultipleCoils:
                case WriteMultipleRegisters:
                    if (pdu.Length >= 5)
                    {
                        var addr = (pdu[1] << 8) | pdu[2];
                        var qty = (pdu[3] << 8) | pdu[4];
                        return new ModbusDecode
                        {
                            Mode = mode,
                            Slave = slave,
                            Function = fn,
                            Summary =
                                $"{mode} 从站 {slave} 功能 0x{fn:X2} 写多个: 起始 {addr} 数量 {qty}",
                        };
                    }
                    break;
            }

            return new ModbusDecode
            {
                Mode = mode,
                Slave = slave,
                Function = fn,
                Summary = $"{mode} 从站 {slave} 功能 0x{fn:X2} ({pdu.Length} 字节)",
            };
        }

        public static string ExceptionText(byte code) => code switch
        {
            0x01 => "(非法功能)",
            0x02 => "(非法数据地址)",
            0x03 => "(非法数据值)",
            0x04 => "(从站设备故障)",
            0x05 => "(确认)",
            0x06 => "(从站忙)",
            0x08 => "(存储奇偶校验错)",
            0x0A => "(网关路径不可用)",
            0x0B => "(网关目标设备无响应)",
            _ => "(未知异常码)",
        };

        // ───────────── 输入解析 ─────────────

        private static IEnumerable<string> SplitValues(string? s) =>
            (s ?? "").Split(
                new[] { ',', ';', ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries
            );

        private static bool ParseCoilScalar(string? value)
        {
            var s = (value ?? "").Trim();
            if (s.Length == 0)
                throw new ArgumentException("写单个线圈需要填写 1/0 或 ON/OFF");
            if (s.Equals("ON", StringComparison.OrdinalIgnoreCase))
                return true;
            if (s.Equals("OFF", StringComparison.OrdinalIgnoreCase))
                return false;
            if (TryParseNumber(s, out var n))
                return n != 0;
            throw new ArgumentException($"无法识别的线圈值: {value}");
        }

        private static List<bool> ParseBits(string? values)
        {
            var bits = new List<bool>();
            foreach (var part in SplitValues(values))
            {
                if (part.Equals("ON", StringComparison.OrdinalIgnoreCase))
                    bits.Add(true);
                else if (part.Equals("OFF", StringComparison.OrdinalIgnoreCase))
                    bits.Add(false);
                else if (TryParseNumber(part, out var n))
                    bits.Add(n != 0);
            }
            return bits;
        }

        private static List<ushort> ParseRegisters(string? values)
        {
            var regs = new List<ushort>();
            foreach (var part in SplitValues(values))
                if (TryParseNumber(part, out var n))
                    regs.Add((ushort)(n & 0xFFFF));
            return regs;
        }

        /// <summary>解析十进制或 0x 十六进制整数。</summary>
        public static bool TryParseNumber(string? text, out long value)
        {
            value = 0;
            var s = (text ?? "").Trim();
            if (s.Length == 0)
                return false;
            try
            {
                value = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? Convert.ToInt64(s[2..], 16)
                    : long.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>一帧 Modbus 的解析结果。</summary>
    public sealed class ModbusDecode
    {
        public string Mode { get; init; } = "";
        public byte Slave { get; init; }
        public byte Function { get; init; }
        public bool IsException { get; init; }
        public byte ExceptionCode { get; init; }
        public ushort[] Registers { get; init; } = Array.Empty<ushort>();
        public bool[] Coils { get; init; } = Array.Empty<bool>();

        /// <summary>给人看的一行摘要 (落日志用)。</summary>
        public string Summary { get; init; } = "";
    }
}
