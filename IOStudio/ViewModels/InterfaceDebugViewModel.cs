using IOStudio.Models;
using IOStudio.Services;
using IOToolkit;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;

namespace IOStudio.ViewModels
{
    /// <summary>
    /// 接口调试（插件通道）窗口 ViewModel。
    ///
    /// 典型场景（NETIO 设备）：
    ///   - 订阅 <c>netio.udp.in</c> 实时查看外部 UDP 报文进入的 JSON 帧；
    ///   - 编辑 JSON 文本，向 <c>netio.udp.out</c> 下发，触发 UDP 广播到远端。
    /// </summary>
    public class InterfaceDebugViewModel : ViewModelBase
    {
        public ObservableCollection<PluginChannelLogEntry> Entries =>
            PluginChannelDebugService.Instance.Entries;

        /// <summary>候选设备列表（从 IORoot 中枚举 External 设备）。</summary>
        public ObservableCollection<string> Devices { get; } = new();

        /// <summary>常用通道建议（用户仍可手动输入任意通道名）。</summary>
        public ObservableCollection<string> SuggestedChannels { get; } =
            new() { "netio.udp.in", "netio.udp.out", };

        private string _selectedDevice = string.Empty;
        public string SelectedDevice
        {
            get => _selectedDevice;
            set => this.RaiseAndSetIfChanged(ref _selectedDevice, value);
        }

        private string _subscribeChannel = "netio.udp.in";
        public string SubscribeChannel
        {
            get => _subscribeChannel;
            set => this.RaiseAndSetIfChanged(ref _subscribeChannel, value);
        }

        private string _sendChannel = "netio.udp.out";
        public string SendChannel
        {
            get => _sendChannel;
            set => this.RaiseAndSetIfChanged(ref _sendChannel, value);
        }

        private string _sendText = "{\"id\":1,\"evt\":\"Heartbeat\",\"msg\":\"\",\"data\":{}}";
        public string SendText
        {
            get => _sendText;
            set => this.RaiseAndSetIfChanged(ref _sendText, value);
        }

        public ReactiveCommand<Unit, Unit> SubscribeCommand { get; }
        public ReactiveCommand<Unit, Unit> UnsubscribeCommand { get; }
        public ReactiveCommand<Unit, Unit> SendCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshDevicesCommand { get; }

        public InterfaceDebugViewModel()
        {
            RefreshDevices();

            SubscribeCommand = ReactiveCommand.Create(() =>
            {
                PluginChannelDebugService.Instance.Subscribe(SelectedDevice, SubscribeChannel);
            });

            UnsubscribeCommand = ReactiveCommand.Create(() =>
            {
                PluginChannelDebugService.Instance.Unsubscribe(SelectedDevice, SubscribeChannel);
            });

            SendCommand = ReactiveCommand.Create(() =>
            {
                PluginChannelDebugService.Instance.SendText(SelectedDevice, SendChannel, SendText);
            });

            ClearCommand = ReactiveCommand.Create(() =>
            {
                PluginChannelDebugService.Instance.Clear();
            });

            RefreshDevicesCommand = ReactiveCommand.Create(() => RefreshDevices());
        }

        /// <summary>
        /// 从 IORoot 枚举 External 设备；默认选中 NETIO（若存在）。
        /// </summary>
        public void RefreshDevices()
        {
            Devices.Clear();

            try
            {
                foreach (
                    var dev in IORoot.Instance.Devices
                        .Where(d => d.Type == "External")
                        .Select(d => d.Name)
                        .Distinct()
                )
                {
                    Devices.Add(dev);
                }
            }
            catch
            { /* IORoot 未初始化时容错 */
            }

            if (string.IsNullOrEmpty(SelectedDevice) && Devices.Count > 0)
            {
                // 优先选中 NETIO（大小写不敏感）
                var netio = Devices.FirstOrDefault(n => SafeDllNameEquals(n, "NETIO"));
                SelectedDevice = netio ?? Devices[0];
            }
        }

        private static bool SafeDllNameEquals(string deviceName, string dllName)
        {
            try
            {
                var dev = IORoot.Instance.Devices.FirstOrDefault(d => d.Name == deviceName);
                return dev != null
                    && string.Equals(dev.DllName, dllName, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
