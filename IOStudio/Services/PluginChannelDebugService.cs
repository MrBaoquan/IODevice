using Avalonia.Threading;
using IOStudio.Models;
using IOToolkit;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace IOStudio.Services
{
    /// <summary>
    /// 插件通道调试服务（单例）。
    ///
    /// 职责：
    /// 1. 管理对某个设备-通道的订阅（Subscribe/Unsubscribe）；
    /// 2. 将每次读/写事件记录为 <see cref="PluginChannelLogEntry"/>，写入有界日志集合；
    /// 3. 提供 <see cref="SendText"/> 便捷方法向设备通道写入字节流。
    ///
    /// 线程模型：native 回调线程不可直接修改 UI 绑定集合，
    /// 内部统一通过 <c>Dispatcher.UIThread.Post</c> 回到 UI 线程追加日志。
    /// </summary>
    public sealed class PluginChannelDebugService
    {
        private static readonly Lazy<PluginChannelDebugService> _instance =
            new(() => new PluginChannelDebugService());
        public static PluginChannelDebugService Instance => _instance.Value;

        private PluginChannelDebugService() { }

        /// <summary>日志条目最大保留数（超出从头丢弃）。</summary>
        public int MaxEntries { get; set; } = 2000;

        /// <summary>UI 绑定的日志集合（始终在 UI 线程追加/裁剪）。</summary>
        public ObservableCollection<PluginChannelLogEntry> Entries { get; } = new();

        // key = "deviceName|channelName" → handlerId
        private readonly Dictionary<string, int> _subscriptions = new();
        private readonly object _lock = new();

        private static string Key(string dev, string ch) => $"{dev}|{ch}";

        /// <summary>
        /// 订阅设备的插件通道（例：deviceName="NetIO", channelName="netio.udp.in"）。
        /// 重复订阅同一 key 会先解绑旧的再绑新的。
        /// </summary>
        public bool Subscribe(string deviceName, string channelName)
        {
            if (string.IsNullOrEmpty(deviceName) || string.IsNullOrEmpty(channelName))
                return false;

            lock (_lock)
            {
                // 若已订阅则先解绑
                UnsubscribeNoLock(deviceName, channelName);

                var device = IODeviceController.GetIODevice(deviceName);
                int handlerId = device.DebugBindPluginRawChannel(
                    channelName,
                    (ch, data) =>
                    {
                        AppendEntry(
                            new PluginChannelLogEntry
                            {
                                Direction = "IN",
                                DeviceName = deviceName,
                                ChannelName = ch,
                                Size = data?.Length ?? 0,
                                Text = DecodePayload(data),
                            }
                        );
                    }
                );

                if (handlerId < 0)
                {
                    AppendEntry(
                        new PluginChannelLogEntry
                        {
                            Direction = "IN",
                            DeviceName = deviceName,
                            ChannelName = channelName,
                            Size = 0,
                            Text = $"[订阅失败] handlerId={handlerId}（设备未加载或通道不存在）",
                        }
                    );
                    return false;
                }

                _subscriptions[Key(deviceName, channelName)] = handlerId;
                AppendEntry(
                    new PluginChannelLogEntry
                    {
                        Direction = "IN",
                        DeviceName = deviceName,
                        ChannelName = channelName,
                        Size = 0,
                        Text = $"[已订阅] handlerId={handlerId}",
                    }
                );
                return true;
            }
        }

        /// <summary>取消订阅。</summary>
        public bool Unsubscribe(string deviceName, string channelName)
        {
            lock (_lock)
            {
                return UnsubscribeNoLock(deviceName, channelName);
            }
        }

        private bool UnsubscribeNoLock(string deviceName, string channelName)
        {
            var k = Key(deviceName, channelName);
            if (!_subscriptions.TryGetValue(k, out var handlerId))
                return false;

            try
            {
                var device = IODeviceController.GetIODevice(deviceName);
                device.DebugUnbindPluginRawChannel(channelName, handlerId);
            }
            catch (Exception ex)
            {
                AppendEntry(
                    new PluginChannelLogEntry
                    {
                        Direction = "IN",
                        DeviceName = deviceName,
                        ChannelName = channelName,
                        Text = $"[解绑异常] {ex.Message}",
                    }
                );
            }
            _subscriptions.Remove(k);
            AppendEntry(
                new PluginChannelLogEntry
                {
                    Direction = "IN",
                    DeviceName = deviceName,
                    ChannelName = channelName,
                    Text = "[已取消订阅]",
                }
            );
            return true;
        }

        /// <summary>取消所有订阅（窗口关闭时调用）。</summary>
        public void UnsubscribeAll()
        {
            lock (_lock)
            {
                var snapshot = new List<KeyValuePair<string, int>>(_subscriptions);
                foreach (var kv in snapshot)
                {
                    var parts = kv.Key.Split('|', 2);
                    if (parts.Length == 2)
                        UnsubscribeNoLock(parts[0], parts[1]);
                }
                _subscriptions.Clear();
            }
        }

        /// <summary>
        /// 向设备通道写入 UTF-8 文本（常用于 NETIO 的 <c>netio.udp.out</c>）。
        /// </summary>
        public bool SendText(string deviceName, string channelName, string text)
        {
            if (string.IsNullOrEmpty(deviceName) || string.IsNullOrEmpty(channelName))
                return false;

            try
            {
                var device = IODeviceController.GetIODevice(deviceName);
                int rc = device.DebugWritePluginRawChannel(channelName, text ?? string.Empty);
                var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
                AppendEntry(
                    new PluginChannelLogEntry
                    {
                        Direction = "OUT",
                        DeviceName = deviceName,
                        ChannelName = channelName,
                        Size = bytes.Length,
                        Text = rc > 0 ? text ?? string.Empty : $"[发送失败 rc={rc}] {text}",
                    }
                );
                return rc > 0;
            }
            catch (Exception ex)
            {
                AppendEntry(
                    new PluginChannelLogEntry
                    {
                        Direction = "OUT",
                        DeviceName = deviceName,
                        ChannelName = channelName,
                        Text = $"[发送异常] {ex.Message}",
                    }
                );
                return false;
            }
        }

        /// <summary>清空 UI 日志。</summary>
        public void Clear()
        {
            Dispatcher.UIThread.Post(() => Entries.Clear());
        }

        private void AppendEntry(PluginChannelLogEntry entry)
        {
            void doAppend()
            {
                Entries.Add(entry);
                while (Entries.Count > MaxEntries)
                    Entries.RemoveAt(0);
            }

            if (Dispatcher.UIThread.CheckAccess())
                doAppend();
            else
                Dispatcher.UIThread.Post(doAppend);
        }

        /// <summary>尝试将字节数组解码为 UTF-8 文本；若含非可打印字符则退化为 HEX。</summary>
        private static string DecodePayload(byte[]? data)
        {
            if (data == null || data.Length == 0)
                return string.Empty;

            bool printable = true;
            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                if (b == 0)
                {
                    // 忽略末尾 '\0'
                    if (i == data.Length - 1)
                        break;
                    printable = false;
                    break;
                }
                if (b < 0x09 || (b > 0x0D && b < 0x20))
                {
                    printable = false;
                    break;
                }
            }

            if (printable)
            {
                try
                {
                    return Encoding.UTF8.GetString(data).TrimEnd('\0');
                }
                catch
                { /* fallthrough */
                }
            }

            var sb = new StringBuilder(data.Length * 3);
            for (int i = 0; i < data.Length && i < 256; i++)
            {
                sb.Append(data[i].ToString("X2"));
                if (i < data.Length - 1)
                    sb.Append(' ');
            }
            if (data.Length > 256)
                sb.Append(" …");
            return sb.ToString();
        }
    }
}
