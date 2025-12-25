using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using IOTester.Models;
using IOTester.Services.Senders;

namespace IOTester.Services
{
    /// <summary>
    /// 模拟量值处理器，负责轮询检测值变化并发送
    /// </summary>
    public class AnalogValueProcessor : IDisposable
    {
        private readonly ConcurrentDictionary<
            string,
            ConcurrentDictionary<string, float>
        > _currentValues = new();
        private readonly ConcurrentDictionary<
            string,
            ConcurrentDictionary<string, float>
        > _previousValues = new();

        private IDisposable? _pollingSubscription;
        private bool _disposed;
        private bool _hasAnalogMappings;

        private List<ProtocolGroupDto>? _config;
        private Func<string, IProtocolSender?>? _senderFactory;

        /// <summary>
        /// 初始化处理器
        /// </summary>
        /// <param name="config">协议组配置列表</param>
        /// <param name="senderFactory">根据协议类型获取发送器的工厂方法</param>
        public void Initialize(
            List<ProtocolGroupDto> config,
            Func<string, IProtocolSender?> senderFactory
        )
        {
            _config = config;
            _senderFactory = senderFactory;
            _hasAnalogMappings = false;

            foreach (var group in config)
            {
                // 跳过 DirectOutput（它直接在回调中处理）
                if (group.ProtocolType == "DirectOutput")
                    continue;

                _currentValues[group.Id] = new ConcurrentDictionary<string, float>();
                _previousValues[group.Id] = new ConcurrentDictionary<string, float>();
            }
        }

        /// <summary>
        /// 注册模拟量值更新（由设备回调调用）
        /// </summary>
        public void UpdateValue(string groupId, string targetKey, float value)
        {
            if (_currentValues.TryGetValue(groupId, out var values))
            {
                values[targetKey] = value;
            }
        }

        /// <summary>
        /// 标记有模拟量映射需要轮询
        /// </summary>
        public void MarkHasAnalogMappings()
        {
            _hasAnalogMappings = true;
        }

        /// <summary>
        /// 启动轮询
        /// </summary>
        /// <param name="intervalMs">轮询间隔（毫秒）</param>
        public void StartPolling(int intervalMs = 50)
        {
            _pollingSubscription?.Dispose();

            if (!_hasAnalogMappings)
            {
                Debug.WriteLine("[AnalogProcessor] No analog mappings, polling not started");
                return;
            }

            _pollingSubscription = Observable
                .Interval(TimeSpan.FromMilliseconds(intervalMs))
                .ObserveOn(ThreadPoolScheduler.Instance)
                .Subscribe(_ => ProcessAllGroups());

            Debug.WriteLine($"[AnalogProcessor] Polling started at {intervalMs}ms interval");
        }

        /// <summary>
        /// 停止轮询
        /// </summary>
        public void StopPolling()
        {
            _pollingSubscription?.Dispose();
            _pollingSubscription = null;
        }

        /// <summary>
        /// 清理所有状态
        /// </summary>
        public void Clear()
        {
            StopPolling();
            _hasAnalogMappings = false;
            _currentValues.Clear();
            _previousValues.Clear();
        }

        private void ProcessAllGroups()
        {
            if (_config == null || _senderFactory == null)
                return;

            foreach (var group in _config)
            {
                // 跳过 DirectOutput
                if (group.ProtocolType == "DirectOutput")
                    continue;

                ProcessGroup(group);
            }
        }

        private void ProcessGroup(ProtocolGroupDto group)
        {
            if (!_currentValues.TryGetValue(group.Id, out var currentValues))
                return;

            if (currentValues.Count == 0)
                return;

            var previousValues = _previousValues.GetOrAdd(
                group.Id,
                _ => new ConcurrentDictionary<string, float>()
            );

            // 找出变化的值
            var changedValues = new Dictionary<string, float>();
            foreach (var kvp in currentValues)
            {
                if (
                    !previousValues.TryGetValue(kvp.Key, out var prevValue)
                    || Math.Abs(prevValue - kvp.Value) > float.Epsilon
                )
                {
                    changedValues[kvp.Key] = kvp.Value;
                }
            }

            // 发送变化的值
            if (changedValues.Count > 0)
            {
                var sender = _senderFactory(group.ProtocolType);
                sender?.SendAnalog(group, changedValues);

                // 更新上一帧的值
                foreach (var kvp in changedValues)
                {
                    previousValues[kvp.Key] = kvp.Value;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Clear();
        }
    }
}
