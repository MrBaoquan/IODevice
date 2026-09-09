using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using IOStudio.Models.Motion;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 设备架构服务 — 从 Config/IODevice.xml 解析设备、OAction、OAxis 通道信息。
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
            var devices = new List<DeviceSchemaInfo>();

            try
            {
                var configPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Config",
                    "IODevice.xml"
                );

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

                            var device = new DeviceSchemaInfo { DeviceName = name };
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

            // 无设备时提供默认 Platform-0
            if (devices.Count == 0)
            {
                devices.Add(CreateDefaultDevice());
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
