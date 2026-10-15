using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using IOStudio.Models;
using IOStudio.Models.Motion;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 设备架构服务 — 从**生效的**设备表（<see cref="DeviceTableLocator.ResolveEffectivePath"/>，
    /// 默认 Config/IODevice.xml，可被设置里的预设覆盖）解析设备、OAction、OAxis 通道信息。
    /// 若 XML 文件缺失或解析失败, 提供默认 Platform-0 设备 (Pitch/Roll/Heave/Yaw/Surge/Sway)。
    /// </summary>
    public class DeviceSchemaService : IDeviceSchemaService
    {
        private static readonly string[] DefaultOActions =
        {
            "Pitch",
            "Roll",
            "Heave",
            "Yaw",
            "Surge",
            "Sway"
        };

        /// <inheritdoc/>
        public List<DeviceSchemaInfo> LoadDevices()
        {
            var devices = LoadDeclaredDevices();

            // 无设备时提供默认 Platform-0
            if (devices.Count == 0)
                devices.Add(CreateDefaultDevice());

            return devices;
        }

        /// <summary>
        /// 只返回 <c>Config/IODevice.xml</c> 里**真实声明**的设备 —— 没有兜底。
        /// 文件缺失、为空、或解析不出设备时返回空列表。
        ///
        /// <para>需要区分"表里有没有"的调用方（如设备绑定预检）必须用这个：
        /// <see cref="LoadDevices"/> 的合成 Platform-0 只是 UI 兜底，native 侧读同一份文件、
        /// 读不到就一个设备都不建，所以那个合成设备在播放时同样会被静默丢弃。</para>
        /// </summary>
        public List<DeviceSchemaInfo> LoadDeclaredDevices()
        {
            var devices = new List<DeviceSchemaInfo>();

            try
            {
                // 必须用**生效的**设备表，而不是写死 IODevice.xml。
                //
                // 设备表支持切预设（设置里的 DeviceTableFile，如 IODevice.YDL.xml），
                // 写死默认表会让"设备是否声明"这个判据对着**另一份表**作答 ——
                // 用户切了预设之后，预检会把表里明明声明过的设备报成"未在设备表中声明"，
                // 一整片假错误把真问题淹掉。缺省表只在配置的文件不存在时才兜底，
                // 这个回落已由 DeviceTableLocator.ResolveEffectivePath 处理。
                var configPath = DeviceTableLocator.ResolveEffectivePath();

                if (File.Exists(configPath))
                {
                    var doc = new XmlDocument();
                    doc.Load(configPath);
                    var deviceNodes = doc.SelectNodes("//Device");

                    if (deviceNodes is not null)
                    {
                        foreach (XmlNode node in deviceNodes)
                        {
                            var name = node.Attributes?["Name"]?.Value;
                            if (string.IsNullOrEmpty(name))
                                continue;

                            var device = new DeviceSchemaInfo
                            {
                                DeviceName = name,
                                DllName = node.Attributes?["DllName"]?.Value ?? "",
                                Index = int.TryParse(node.Attributes?["Index"]?.Value, out int idx)
                                    ? idx
                                    : 0,
                            };
                            ParseOActions(node, device);
                            GenerateOAxisChannels(name, device);
                            devices.Add(device);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DeviceSchemaService] 加载设备失败: {ex.Message}");
            }

            return devices;
        }

        /// <summary>解析设备节点下的所有 OAction 定义。</summary>
        private static void ParseOActions(XmlNode deviceNode, DeviceSchemaInfo device)
        {
            var oactionNodes = deviceNode.SelectNodes("OAction");
            if (oactionNodes is null)
                return;

            foreach (XmlNode oaNode in oactionNodes)
            {
                var oaName = oaNode.Attributes?["Name"]?.Value ?? "";
                var oaLabel = oaNode.Attributes?["Label"]?.Value ?? "";

                // 收集该 OAction 下的所有 Key 名称
                var keyNames = new List<string>();
                var keys = oaNode.SelectNodes("Key");
                if (keys is not null)
                {
                    foreach (XmlNode keyNode in keys)
                    {
                        var keyName = keyNode.Attributes?["Name"]?.Value;
                        if (!string.IsNullOrEmpty(keyName))
                            keyNames.Add(keyName);
                    }
                }

                var displayName = string.IsNullOrEmpty(oaLabel) ? oaName : $"{oaName} ({oaLabel})";

                var keysInfo = keyNames.Count > 0 ? string.Join(", ", keyNames) : "无绑定 Key";

                device.OActions.Add(
                    new OActionInfo
                    {
                        DeviceName = device.DeviceName,
                        OActionName = oaName,
                        Label = oaLabel,
                        DisplayName = $"{displayName}  [{keysInfo}]",
                        KeyNames = keyNames
                    }
                );
            }
        }

        /// <summary>为设备生成 OAxis_00 ~ OAxis_31 通道列表。</summary>
        private static void GenerateOAxisChannels(string deviceName, DeviceSchemaInfo device)
        {
            for (int i = 0; i <= 31; i++)
            {
                var chName = $"OAxis_{i:D2}";
                device.OAxisChannels.Add(
                    new OAxisChannelInfo
                    {
                        DeviceName = deviceName,
                        ChannelName = chName,
                        DisplayName = $"{chName}  (通道 {i})"
                    }
                );
            }
        }

        /// <summary>创建默认 Platform-0 设备 (无 IODevice.xml 时的回退)。</summary>
        private static DeviceSchemaInfo CreateDefaultDevice()
        {
            var device = new DeviceSchemaInfo { DeviceName = "Platform-0" };

            foreach (var oa in DefaultOActions)
            {
                device.OActions.Add(
                    new OActionInfo
                    {
                        DeviceName = "Platform-0",
                        OActionName = oa,
                        Label = oa,
                        DisplayName = oa,
                        KeyNames = new List<string>()
                    }
                );
            }

            GenerateOAxisChannels("Platform-0", device);
            return device;
        }
    }
}
