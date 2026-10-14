using System;
using System.Collections.Generic;
using System.Linq;
using IOStudio.Models.Motion;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// role → 设备通道的匹配结果 (OAction 或 OAxis 二选一)。
    /// </summary>
    public sealed class RoleChannelSuggestion
    {
        public string DeviceName { get; init; } = "";
        public string? OActionName { get; init; }
        public string? OAxisChannel { get; init; }
        public bool IsOAxis { get; init; }
    }

    /// <summary>
    /// 已声明设备目录 —— 「这个名字有没有设备认领」的判据源。
    ///
    /// <para><b>为什么需要它</b>: <c>.motion</c> 里的设备名是设计上写死的（它是面向
    /// <c>device + OAction/OAxis_xx</c> 的内容文件），<c>Config/IODevice.xml</c> 是它与真实插件之间的
    /// 逻辑通道映射表。当映射表里没有这个名字时，播放<b>不会报错</b> —— native 侧
    /// <c>MotionPlayer::DispatchChannel</c> 直接 <c>return</c>，轨道输出静默丢弃。
    /// 所以"名字是否被认领"必须在编排期就看得见。</para>
    ///
    /// <para><b>为什么不用 <c>IODevice.IsValid()</c> 当判据</b>: 那会把两件事混成一件 ——
    /// 「映射表里没有这个名字」（必然失败，是要修的 bug）与「映射表里有但设备还没装载」
    /// （作者离线编排时的正常态）。后者若也报警，离线编排就满屏误报。
    /// 设备是否已装载由 <see cref="IOToolkit.IODeviceController.IsLoaded"/> + <c>IsValid()</c>
    /// 在另一条检查里单独负责。</para>
    ///
    /// <para>本类只在「打开文件 / 开始播放 / 打开对话框」三处被调用，不进帧循环。</para>
    /// </summary>
    public static class MotionDeviceCatalog
    {
        private static readonly object Gate = new();
        private static List<DeviceSchemaInfo>? _devices;
        private static bool _tableAvailable;

        /// <summary>
        /// 映射表是否真的给出了设备 —— 文件缺失、为空、或解析不出设备时为 false。
        ///
        /// <para>注意判据是"解析出了设备"，不是"文件存在"：<c>Config/IODevice.xml</c> 存在但内容为空
        /// （只有 <c>&lt;IORoot /&gt;</c>）时同样没有任何设备被声明，此时任何修复动作都无目标可映射。
        /// 这个区别正是 <see cref="DeviceSchemaService.LoadDeclaredDevices"/> 存在的理由 ——
        /// <see cref="DeviceSchemaService.LoadDevices"/> 在这种情况会返回合成的 <c>Platform-0</c>，
        /// 而 native 侧读同一份文件、读不到就一个设备都不建，所以合成设备在播放时同样被静默丢弃。</para>
        /// </summary>
        public static bool IsTableAvailable
        {
            get
            {
                EnsureLoaded();
                return _tableAvailable;
            }
        }

        /// <summary>
        /// 映射表里真实声明的设备（可为空 —— 表缺失时就是空的）。
        ///
        /// 修复动作的目标只能从这里取：合成的 Platform-0 不是可映射目标。
        /// 需要"哪怕表是空的也给个占位"的创作期建议场景，请自行兜底。
        /// </summary>
        public static IReadOnlyList<DeviceSchemaInfo> Devices
        {
            get
            {
                EnsureLoaded();
                return _devices!;
            }
        }

        /// <summary>
        /// 设备名是否被映射表认领（大小写不敏感）。
        /// 未被认领 = 该轨道的输出在播放时必然被静默丢弃。
        /// </summary>
        public static bool IsDeclared(string? deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
                return false;

            EnsureLoaded();
            // 表不可用时全部视为未认领 —— 但这是"无法判断"而非"确实没有"，
            // 所以调用方应先看 IsTableAvailable 再决定是报警还是跳过检查。
            return _tableAvailable && DeviceSchemaMatcher.IsDeclared(_devices!, deviceName);
        }

        /// <summary>按名字取设备（未找到返回 null）。</summary>
        public static DeviceSchemaInfo? Find(string? deviceName)
        {
            EnsureLoaded();
            return DeviceSchemaMatcher.Find(_devices!, deviceName);
        }

        /// <summary>
        /// (设备名, 动作名) 是否被设备表认领（大小写不敏感）。
        /// 未被认领 = 播放时该轨道输出必然被静默丢弃，且<b>连"未解析"计数都不会有</b>
        /// （native 侧 <c>ExternalIO::SetDO</c> 查不到动作名直接 return 0，不打日志）。
        /// 设备本身未声明时同样返回 false。
        /// </summary>
        public static bool IsOActionDeclared(string? deviceName, string? oActionName)
        {
            if (string.IsNullOrWhiteSpace(deviceName) || string.IsNullOrWhiteSpace(oActionName))
                return false;

            EnsureLoaded();
            // 与 IsDeclared 同构: 表不可用时一律 false, 那是"无法判断"而非"确实没有"。
            return _tableAvailable
                && DeviceSchemaMatcher.FindOAction(_devices!, deviceName, oActionName) is not null;
        }

        /// <summary>
        /// 按 (设备名, 动作名) 取 OAction 定义 —— 供轨道头显示「动作 X → 通道 Y」这类回显。
        /// 未找到返回 null（显示侧应退化为只显示动作名）。
        /// </summary>
        public static OActionInfo? FindOAction(string? deviceName, string? oActionName)
        {
            EnsureLoaded();
            return DeviceSchemaMatcher.FindOAction(_devices!, deviceName, oActionName);
        }

        /// <summary>
        /// 用角色名启发式匹配设备通道 —— 「角色 → 物理通道」的自动绑定。
        /// 匹配顺序: OAction 名 → OAction 标签 → OAxis 通道名（大小写不敏感）。
        /// </summary>
        public static RoleChannelSuggestion? SuggestForRole(string? role)
        {
            EnsureLoaded();
            return DeviceSchemaMatcher.SuggestForRole(_devices!, role);
        }

        /// <summary>设备表已变更（增删设备 / 重载配置）后调用。</summary>
        public static void Invalidate()
        {
            lock (Gate)
            {
                _devices = null;
            }
        }

        private static void EnsureLoaded()
        {
            if (_devices is not null)
                return;

            lock (Gate)
            {
                if (_devices is not null)
                    return;

                // 用 LoadDeclaredDevices 而不是 LoadDevices: 后者在表为空时会塞一个合成的
                // Platform-0, 那会让"表不可用"被误判成"表可用、目标是 Platform-0"。
                _devices = new DeviceSchemaService().LoadDeclaredDevices();
                _tableAvailable = _devices.Count > 0;
            }
        }
    }
}
