using IOStudio.ViewModels;
using ReactiveUI;
using System;
using System.Reactive.Subjects;

namespace IOStudio.Services
{
    /// <summary>
    /// 按键录制管理器单例
    /// </summary>
    public class RecordingManager : ReactiveObject
    {
        private static readonly Lazy<RecordingManager> _instance = new Lazy<RecordingManager>(
            () => new RecordingManager()
        );
        public static RecordingManager Instance => _instance.Value;

        private IONodeBase? _recordingNode;
        private Device? _recordingDevice;

        private bool _isRecording = false;
        public bool IsRecording
        {
            get => _isRecording;
            private set => this.RaiseAndSetIfChanged(ref _isRecording, value);
        }

        public IONodeBase? RecordingNode => _recordingNode;
        public Device? RecordingDevice => _recordingDevice;

        /// <summary>
        /// 录制完成事件
        /// </summary>
        public Subject<(
            Device Device,
            IONodeBase Node,
            string KeyName
        )> OnRecordingComplete { get; } =
            new Subject<(Device Device, IONodeBase Node, string KeyName)>();

        private RecordingManager() { }

        /// <summary>
        /// 开始录制
        /// </summary>
        public void StartRecording(Device device, IONodeBase node)
        {
            if (node is not ViewModels.Action)
            {
                return; // 只允许 Action 节点录制
            }

            // 如果已经在录制，先停止
            if (IsRecording)
            {
                StopRecording();
            }

            _recordingDevice = device;
            _recordingNode = node;
            IsRecording = true;
        }

        /// <summary>
        /// 停止录制
        /// </summary>
        public void StopRecording()
        {
            _recordingDevice = null;
            _recordingNode = null;
            IsRecording = false;
        }

        /// <summary>
        /// 处理按键事件
        /// </summary>
        public void HandleKeyPressed(Device device, string keyName)
        {
            if (!IsRecording || _recordingNode == null || _recordingDevice == null)
            {
                return;
            }

            // 检查是否是当前录制的设备
            if (_recordingDevice.Name != device.Name)
            {
                return;
            }

            // 忽略鼠标相关按键
            if (IsMouseKey(keyName))
            {
                return;
            }

            // 触发录制完成事件
            OnRecordingComplete.OnNext((device, _recordingNode, keyName));

            // 自动停止录制
            StopRecording();
        }

        /// <summary>
        /// 判断是否是鼠标按键
        /// </summary>
        private bool IsMouseKey(string keyName)
        {
            if (string.IsNullOrEmpty(keyName))
            {
                return false;
            }

            var lowerKey = keyName.ToLower();
            return lowerKey.Contains("mouse")
                || lowerKey.Contains("wheel")
                || lowerKey.StartsWith("lmb")
                || lowerKey.StartsWith("rmb")
                || lowerKey.StartsWith("mmb");
        }

        /// <summary>
        /// 检查指定节点是否正在录制
        /// </summary>
        public bool IsNodeRecording(IONodeBase node)
        {
            return IsRecording && _recordingNode == node;
        }
    }
}
