using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace IOToolkit
{
    public enum ChannelCompletionKind
    {
        LocalAccepted,
        RemoteResponded,
        Failed,
        Timeout,
        Handled
    }

    public sealed class ChannelRequestOptions
    {
        public bool WaitResponse { get; set; }
        public int TimeoutMs { get; set; }
        public string Target { get; set; }

        public ChannelRequestOptions()
        {
            WaitResponse = true;
            TimeoutMs = 5000;
        }

        public static ChannelRequestOptions NoResponse
        {
            get { return new ChannelRequestOptions { WaitResponse = false }; }
        }
    }

    public sealed class ChannelResponse
    {
        public bool Ok { get; set; }
        public object Data { get; set; }
        public string PayloadJson { get; set; }
        public string Error { get; set; }
        public ChannelCompletionKind Completion { get; set; }

        public static ChannelResponse Empty(ChannelCompletionKind completion)
        {
            return new ChannelResponse
            {
                Ok = true,
                PayloadJson = "null",
                Completion = completion
            };
        }

        public static ChannelResponse FromJson(string payloadJson, ChannelCompletionKind completion)
        {
            return new ChannelResponse
            {
                Ok = true,
                PayloadJson = string.IsNullOrEmpty(payloadJson) ? "null" : payloadJson,
                Completion = completion
            };
        }

        public static ChannelResponse FromData(object data)
        {
            return new ChannelResponse
            {
                Ok = true,
                Data = data,
                PayloadJson = ChannelPayload.ToJson(data),
                Completion = ChannelCompletionKind.RemoteResponded
            };
        }

        public static ChannelResponse Failure(string error, ChannelCompletionKind completion)
        {
            return new ChannelResponse
            {
                Ok = false,
                Error = error ?? string.Empty,
                PayloadJson = "null",
                Completion = completion
            };
        }

        public static ChannelResponse Handled
        {
            get { return Empty(ChannelCompletionKind.Handled); }
        }
    }

    public sealed class ChannelContext
    {
        private readonly Func<object, Task> _respond;

        public string Device { get; private set; }
        public string Name { get; private set; }
        public object Data { get; set; }
        public string PayloadJson { get; private set; }
        public bool CanRespond { get; private set; }
        public string SourceId { get; private set; }

        /// <summary>
        /// 原始 metadata JSON (透传自插件 envelope.metadata, 由插件协议自行约定字段).
        /// IODevice / wrapper 不解释字段含义; 业务可按需 JSON 解析.
        /// </summary>
        public string MetadataJson { get; private set; }
        public bool Responded { get; private set; }

        public ChannelContext(
            string device,
            string name,
            string payloadJson,
            bool canRespond,
            string sourceId,
            Func<object, Task> respond
        )
            : this(device, name, payloadJson, canRespond, sourceId, null, respond) { }

        public ChannelContext(
            string device,
            string name,
            string payloadJson,
            bool canRespond,
            string sourceId,
            string metadataJson,
            Func<object, Task> respond
        )
        {
            Device = device ?? string.Empty;
            Name = name ?? string.Empty;
            PayloadJson = string.IsNullOrEmpty(payloadJson) ? "null" : payloadJson;
            CanRespond = canRespond;
            SourceId = sourceId ?? string.Empty;
            MetadataJson = metadataJson ?? string.Empty;
            _respond = respond;
        }

        public Task Respond(object payload = null)
        {
            if (!CanRespond || _respond == null)
                return Task.FromResult<object>(null);
            Responded = true;
            return _respond(payload);
        }
    }

    public static class ChannelPayload
    {
        public static string ToJson(object payload)
        {
            if (payload == null)
                return "null";
            var text = payload as string;
            if (text != null)
                return string.IsNullOrEmpty(text) ? "null" : text;
            if (payload is bool)
                return (bool)payload ? "true" : "false";
            if (
                payload is byte
                || payload is sbyte
                || payload is short
                || payload is ushort
                || payload is int
                || payload is uint
                || payload is long
                || payload is ulong
                || payload is float
                || payload is double
                || payload is decimal
            )
                return Convert.ToString(payload, CultureInfo.InvariantCulture);
            var dictionary = payload as IDictionary;
            if (dictionary != null)
                return DictionaryToJson(dictionary);
            var enumerable = payload as IEnumerable;
            if (enumerable != null)
                return EnumerableToJson(enumerable);
            return ObjectToJson(payload);
        }

        private static string DictionaryToJson(IDictionary dictionary)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            var first = true;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (!first)
                    sb.Append(',');
                first = false;
                sb.Append(Quote(Convert.ToString(entry.Key, CultureInfo.InvariantCulture)));
                sb.Append(':');
                sb.Append(ToJson(entry.Value));
            }
            sb.Append('}');
            return sb.ToString();
        }

        private static string EnumerableToJson(IEnumerable enumerable)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            var first = true;
            foreach (var item in enumerable)
            {
                if (!first)
                    sb.Append(',');
                first = false;
                sb.Append(ToJson(item));
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string ObjectToJson(object payload)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            var first = true;
            foreach (
                var property in payload
                    .GetType()
                    .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            )
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                    continue;
                if (!first)
                    sb.Append(',');
                first = false;
                sb.Append(Quote(property.Name));
                sb.Append(':');
                sb.Append(ToJson(property.GetValue(payload, null)));
            }
            sb.Append('}');
            return sb.ToString();
        }

        public static string Quote(string value)
        {
            if (value == null)
                return "null";
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\b':
                        sb.Append("\\b");
                        break;
                    case '\f':
                        sb.Append("\\f");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                            sb.AppendFormat("\\u{0:x4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
