using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IOToolkit.Core;

namespace IOToolkit
{
    public abstract partial class IODevice
    {
        public Task<ChannelResponse> Request(
            string name,
            object payload = null,
            ChannelRequestOptions options = null
        )
        {
            options = options ?? new ChannelRequestOptions();

            if (string.IsNullOrEmpty(name))
                return Task.FromResult(
                    ChannelResponse.Failure("channel name is empty", ChannelCompletionKind.Failed)
                );

            var payloadJson = ChannelPayload.ToJson(payload);
            if (!options.WaitResponse)
            {
                // 单向投递: 同步调用即可返回, 不会阻塞业务.
                NativeRequestChannel(name, payloadJson, options, out _);
                return Task.FromResult(ChannelResponse.Empty(ChannelCompletionKind.LocalAccepted));
            }

            // 等响应路径: 底层 P/Invoke 是同步阻塞的, 用 Task.Run 推到线程池避免占用 UI / 主线程.
            var capturedOptions = options;
            return Task.Run(() =>
            {
                int rc = NativeRequestChannel(
                    name,
                    payloadJson,
                    capturedOptions,
                    out var responseJson
                );
                if (rc <= 0)
                    return ChannelResponse.Failure(
                        "channel request failed",
                        ChannelCompletionKind.Timeout
                    );
                return ChannelResponse.FromJson(
                    responseJson,
                    ChannelCompletionKind.RemoteResponded
                );
            });
        }

        public IDisposable Subscribe(
            string name,
            Func<ChannelContext, Task<ChannelResponse>> handler
        )
        {
            if (handler == null || string.IsNullOrEmpty(name))
                return new ChannelSubscription(() => { });

            // 同时订阅: 普通单向消息 (SubscribeChannel) + 对端 RPC 请求 (SubscribeChannelRequest).
            // 业务回调通过 ctx.Respond(...) 触发响应; 单向消息路径上 CanRespond=false, Respond 为 no-op.

            PluginChannelCallback msgProxy = (_, dataPtr, size) =>
            {
                byte[] bytes = ReadBuffer(dataPtr, size);
                DispatchMessage(name, bytes, handler);
            };
            delegateRefs.Add(msgProxy);
            int msgHandlerId = IONativeWrapper.SubscribeChannel(ID, name, msgProxy);

            IONativeWrapper.ChannelRequestCallback reqProxy = (
                topic,
                requestId,
                payloadPtr,
                payloadSize,
                metadataPtr,
                metadataSize
            ) =>
            {
                string payloadJson = ReadString(payloadPtr, payloadSize);
                string metadataJson = ReadString(metadataPtr, metadataSize);
                DispatchRequest(name, requestId, payloadJson, metadataJson, handler);
            };
            delegateRefs.Add(reqProxy);
            int reqHandlerId = IONativeWrapper.SubscribeChannelRequest(ID, name, reqProxy);

            // capture proxy refs in closure so Dispose can release them from delegateRefs.
            var localMsgProxy = msgProxy;
            var localReqProxy = reqProxy;
            return new ChannelSubscription(() =>
            {
                if (msgHandlerId >= 0)
                    IONativeWrapper.UnsubscribeChannel(ID, msgHandlerId);
                if (reqHandlerId >= 0)
                    IONativeWrapper.UnsubscribeChannelRequest(ID, reqHandlerId);
                // 解除 delegateRefs 强引用, 允许 GC 回收 proxy 委托, 避免长程订阅导致的内存累积.
                delegateRefs.Remove(localMsgProxy);
                delegateRefs.Remove(localReqProxy);
            });
        }

        public string QueryCapabilities(int timeoutMs = 1000)
        {
            var buffer = new byte[64 * 1024];
            int size = IONativeWrapper.QueryChannelCapabilities(
                ID,
                buffer,
                (uint)buffer.Length,
                (uint)Math.Max(1, timeoutMs)
            );
            if (size <= 0)
                return string.Empty;
            int count = Math.Min(size, buffer.Length);
            return Encoding.UTF8.GetString(buffer, 0, count);
        }

        private int NativeRequestChannel(
            string name,
            string payloadJson,
            ChannelRequestOptions options,
            out string responseJson
        )
        {
            var requestBytes = Encoding.UTF8.GetBytes(payloadJson ?? string.Empty);
            var responseBuffer = new byte[1024 * 1024];

            // 若 options.Target 非空, 拼出 metadata.target; 否则 metadata 为空 (不携带).
            byte[] metaBytes = null;
            if (!string.IsNullOrEmpty(options.Target))
            {
                var metaJson = "{\"target\":" + ChannelPayload.Quote(options.Target) + "}";
                metaBytes = Encoding.UTF8.GetBytes(metaJson);
            }

            int size = IONativeWrapper.RequestChannel(
                ID,
                name,
                requestBytes,
                (uint)requestBytes.Length,
                options.WaitResponse ? 1 : 0,
                metaBytes,
                metaBytes == null ? 0u : (uint)metaBytes.Length,
                responseBuffer,
                (uint)responseBuffer.Length,
                (uint)Math.Max(1, options.TimeoutMs)
            );
            if (size <= 0)
            {
                responseJson = string.Empty;
                return size;
            }
            int count = Math.Min(size, responseBuffer.Length);
            responseJson = Encoding.UTF8.GetString(responseBuffer, 0, count);
            return size;
        }

        private static byte[] ReadBuffer(IntPtr ptr, uint size)
        {
            if (ptr == IntPtr.Zero || size == 0)
                return new byte[0];
            var bytes = new byte[size];
            Marshal.Copy(ptr, bytes, 0, (int)size);
            return bytes;
        }

        private static string ReadString(IntPtr ptr, uint size)
        {
            if (ptr == IntPtr.Zero || size == 0)
                return string.Empty;
            var bytes = new byte[size];
            Marshal.Copy(ptr, bytes, 0, (int)size);
            return Encoding.UTF8.GetString(bytes);
        }

        private async void DispatchMessage(
            string name,
            byte[] bytes,
            Func<ChannelContext, Task<ChannelResponse>> handler
        )
        {
            var payloadJson =
                bytes == null || bytes.Length == 0 ? "null" : Encoding.UTF8.GetString(bytes);
            var context = new ChannelContext(ID, name, payloadJson, false, null, null);
            try
            {
                await handler(context).ConfigureAwait(false);
            }
            catch { }
        }

        private async void DispatchRequest(
            string name,
            string requestId,
            string payloadJson,
            string metadataJson,
            Func<ChannelContext, Task<ChannelResponse>> handler
        )
        {
            string sourceId = ExtractSrcSid(metadataJson);
            string deviceId = ID;
            var context = new ChannelContext(
                deviceId,
                name,
                string.IsNullOrEmpty(payloadJson) ? "null" : payloadJson,
                true,
                sourceId,
                metadataJson,
                async payload =>
                {
                    var bytes = Encoding.UTF8.GetBytes(ChannelPayload.ToJson(payload));
                    IONativeWrapper.RespondChannelRequest(
                        deviceId,
                        requestId,
                        bytes,
                        (uint)bytes.Length,
                        1,
                        null
                    );
                    await Task.FromResult<object>(null);
                }
            );
            try
            {
                var resp = await handler(context).ConfigureAwait(false);
                if (
                    !context.Responded
                    && resp != null
                    && resp.Completion != ChannelCompletionKind.Handled
                )
                {
                    var bytes = Encoding.UTF8.GetBytes(resp.PayloadJson ?? "null");
                    IONativeWrapper.RespondChannelRequest(
                        deviceId,
                        requestId,
                        bytes,
                        (uint)bytes.Length,
                        resp.Ok ? 1 : 0,
                        resp.Error
                    );
                }
            }
            catch (Exception ex)
            {
                if (!context.Responded)
                {
                    var errBytes = Encoding.UTF8.GetBytes("null");
                    IONativeWrapper.RespondChannelRequest(
                        deviceId,
                        requestId,
                        errBytes,
                        (uint)errBytes.Length,
                        0,
                        ex.Message
                    );
                }
            }
        }

        private static string ExtractSrcSid(string metadataJson)
        {
            // 极小化 JSON 抽取: 仅识别 "srcSid":"xxx" / "target":"xxx" 字符串值.
            // 避免引入外部 JSON 依赖; 业务侧若需完整解析自行处理 metadataJson.
            if (string.IsNullOrEmpty(metadataJson))
                return string.Empty;
            string src = TryExtractStringField(metadataJson, "srcSid");
            if (!string.IsNullOrEmpty(src))
                return src;
            return TryExtractStringField(metadataJson, "target");
        }

        private static string TryExtractStringField(string json, string field)
        {
            int idx = json.IndexOf("\"" + field + "\"", StringComparison.Ordinal);
            if (idx < 0)
                return string.Empty;
            int colon = json.IndexOf(':', idx);
            if (colon < 0)
                return string.Empty;
            int q1 = json.IndexOf('"', colon + 1);
            if (q1 < 0)
                return string.Empty;
            int q2 = json.IndexOf('"', q1 + 1);
            if (q2 < 0)
                return string.Empty;
            return json.Substring(q1 + 1, q2 - q1 - 1);
        }

        private sealed class ChannelSubscription : IDisposable
        {
            private Action _dispose;

            public ChannelSubscription(Action dispose)
            {
                _dispose = dispose;
            }

            public void Dispose()
            {
                var dispose = Interlocked.Exchange(ref _dispose, null);
                if (dispose != null)
                    dispose();
            }
        }
    }
}
