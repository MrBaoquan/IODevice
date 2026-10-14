using System;
using System.Collections.Generic;
using System.Linq;
using IOStudio.Models.Motion;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 设备架构的纯匹配逻辑 —— 不碰文件、不缓存。
    ///
    /// <para>从 <see cref="MotionDeviceCatalog"/> 里分出来, 是为了让"名字认不认领"和
    /// "role 该落到哪个通道"这两条判定能在没有配置文件的情况下被测到 ——
    /// 这两条判定错一个方向都会造成静默失效 (认领判太宽 = 放行必失的编排,
    /// 判太严 = 离线编排满屏误报), 值得有测试盯着。</para>
    /// </summary>
    public static class DeviceSchemaMatcher
    {
        /// <summary>设备名是否被设备表认领 (大小写不敏感)。空名一律不认领。</summary>
        public static bool IsDeclared(IReadOnlyList<DeviceSchemaInfo> devices, string? deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
                return false;

            return Find(devices, deviceName) is not null;
        }

        /// <summary>按名字取设备 (未找到返回 null)。</summary>
        public static DeviceSchemaInfo? Find(
            IReadOnlyList<DeviceSchemaInfo> devices,
            string? deviceName
        )
        {
            if (string.IsNullOrWhiteSpace(deviceName))
                return null;

            foreach (var d in devices)
            {
                if (string.Equals(d.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                    return d;
            }

            return null;
        }

        /// <summary>
        /// 按 (设备名, 动作名) 取 OAction 定义 —— 大小写不敏感; 设备或动作不在表里返回 null。
        ///
        /// <para>这条判定对应 native 侧 <c>ExternalIO::SetDO(const char*, float, bool)</c> 开头的
        /// <c>OActionMappings.count(InOAction)</c> 门: 设备表里没有这个名字时它直接 <c>return 0</c>,
        /// <b>既没有日志也没有计数</b> —— 比"设备查不到"(<c>MotionPlayer</c> 会累加未解析计数) 更隐蔽。</para>
        /// </summary>
        public static OActionInfo? FindOAction(
            IReadOnlyList<DeviceSchemaInfo> devices,
            string? deviceName,
            string? oActionName
        )
        {
            if (string.IsNullOrWhiteSpace(oActionName))
                return null;

            var device = Find(devices, deviceName);
            if (device is null)
                return null;

            foreach (var oa in device.OActions)
            {
                if (string.Equals(oa.OActionName, oActionName, StringComparison.OrdinalIgnoreCase))
                    return oa;
            }

            return null;
        }

        /// <summary>
        /// 用角色名启发式匹配设备通道 —— 「角色 → 物理通道」的自动绑定。
        ///
        /// <para>匹配顺序: 逐设备依次看 OAction 名 → OAction 标签 → OAxis 通道名, 全部大小写不敏感。
        /// 先设备后优先级是刻意的: 建议要整体落到一个设备上, 混着取只会得到跨设备的组合。</para>
        /// </summary>
        public static RoleChannelSuggestion? SuggestForRole(
            IReadOnlyList<DeviceSchemaInfo> devices,
            string? role
        )
        {
            if (string.IsNullOrWhiteSpace(role))
                return null;

            foreach (var device in devices)
            {
                foreach (var oa in device.OActions)
                {
                    if (
                        string.Equals(oa.OActionName, role, StringComparison.OrdinalIgnoreCase)
                        || (
                            !string.IsNullOrEmpty(oa.Label)
                            && string.Equals(oa.Label, role, StringComparison.OrdinalIgnoreCase)
                        )
                    )
                    {
                        return new RoleChannelSuggestion
                        {
                            DeviceName = device.DeviceName,
                            OActionName = oa.OActionName,
                            IsOAxis = false,
                        };
                    }
                }

                foreach (var ch in device.OAxisChannels)
                {
                    if (string.Equals(ch.ChannelName, role, StringComparison.OrdinalIgnoreCase))
                    {
                        return new RoleChannelSuggestion
                        {
                            DeviceName = device.DeviceName,
                            OAxisChannel = ch.ChannelName,
                            IsOAxis = true,
                        };
                    }
                }
            }

            return null;
        }
    }
}
