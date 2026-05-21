using System;
using System.Collections.Generic;
using System.Text;

namespace IOToolkit.NetIO
{
    /// <summary>
    /// 帧类型, 与 CinemaControl 前端 NetIOClient.ts 保持一致:
    ///   evt - 单向事件 (无 id)
    ///   req - 需要 ack 的请求 (携带 id)
    ///   ack - 对 req 的响应 (沿用同一 id)
    /// </summary>
    public enum FrameType
    {
        Evt,
        Req,
        Ack
    }

    /// <summary>
    /// NetIO 帧协议. 帧格式 (JSON 文本):
    /// <code>{ "type":"evt|req|ack", "name":"&lt;event&gt;", "id":&lt;number&gt;?, "data":&lt;any&gt;? }</code>
    /// </summary>
    public sealed class NetFrame
    {
        public FrameType Type;
        public string Name;
        public int? Id;

        /// <summary>载荷以原始 JSON 文本形式存储 (允许调用方自由选择序列化库).</summary>
        public string DataJson;

        /// <summary>
        /// 对端 session 标识 (仅入站帧且来自 inbound session 时由 NETIO 插件注入).
        /// req 帧依赖此字段让 Ack 能通过 _sendto 定向回发, 避免广播污染其他连接.
        /// </summary>
        public string SrcSid;

        public static NetFrame Event(string name, string dataJson = null) =>
            new NetFrame
            {
                Type = FrameType.Evt,
                Name = name,
                DataJson = dataJson
            };

        public static NetFrame Request(string name, int id, string dataJson = null) =>
            new NetFrame
            {
                Type = FrameType.Req,
                Name = name,
                Id = id,
                DataJson = dataJson
            };

        public static NetFrame Ack(string name, int id, string dataJson = null) =>
            new NetFrame
            {
                Type = FrameType.Ack,
                Name = name,
                Id = id,
                DataJson = dataJson
            };

        // ============ 序列化 ============

        public string ToJson()
        {
            var sb = new StringBuilder(64);
            sb.Append('{');
            sb.Append("\"type\":\"").Append(TypeToString(Type)).Append('"');
            sb.Append(",\"name\":").Append(EncodeString(Name ?? string.Empty));
            if (Id.HasValue)
                sb.Append(",\"id\":").Append(Id.Value);
            if (!string.IsNullOrEmpty(DataJson))
                sb.Append(",\"data\":").Append(DataJson);
            sb.Append('}');
            return sb.ToString();
        }

        [ThreadStatic]
        private static string _parsedSrcSid;

        /// <summary>从 JSON 文本解析帧. 仅解析信封 + 提取 data 原始 JSON 子串.</summary>
        public static bool TryParse(string json, out NetFrame frame)
        {
            frame = null;
            _parsedSrcSid = null;
            if (string.IsNullOrEmpty(json))
                return false;

            string type = null,
                name = null,
                dataJson = null;
            int? id = null;

            try
            {
                var fields = ParseTopLevelFields(json);
                string rawType,
                    rawName;
                if (!fields.TryGetValue("type", out rawType))
                    return false;
                if (!fields.TryGetValue("name", out rawName))
                    return false;
                type = StripQuotes(rawType);
                name = StripQuotes(rawName);
                string idRaw;
                if (fields.TryGetValue("id", out idRaw) && int.TryParse(idRaw.Trim(), out var idv))
                    id = idv;
                string dRaw;
                if (fields.TryGetValue("data", out dRaw))
                    dataJson = dRaw.Trim();
                // 可选 _src: 插件侧为 inbound 帧注入的源 session 标识
                string srcRaw;
                string srcSid = null;
                if (fields.TryGetValue("_src", out srcRaw))
                    srcSid = StripQuotes(srcRaw);
                // 延后赋值 (frame 还未构造)
                _parsedSrcSid = srcSid;
            }
            catch
            {
                return false;
            }

            FrameType ft;
            switch (type)
            {
                case "evt":
                    ft = FrameType.Evt;
                    break;
                case "req":
                    ft = FrameType.Req;
                    break;
                case "ack":
                    ft = FrameType.Ack;
                    break;
                default:
                    return false;
            }
            frame = new NetFrame
            {
                Type = ft,
                Name = name,
                Id = id,
                DataJson = dataJson,
                SrcSid = _parsedSrcSid,
            };
            return true;
        }

        // ============ 极简 JSON 工具 (仅用于本协议信封) ============

        private static string TypeToString(FrameType t) =>
            t == FrameType.Evt ? "evt" : (t == FrameType.Req ? "req" : "ack");

        private static string EncodeString(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var c in s)
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

        private static string StripQuotes(string s)
        {
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
                return UnescapeString(s.Substring(1, s.Length - 2));
            return s;
        }

        private static string UnescapeString(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    switch (n)
                    {
                        case '"':
                            sb.Append('"');
                            break;
                        case '\\':
                            sb.Append('\\');
                            break;
                        case '/':
                            sb.Append('/');
                            break;
                        case 'b':
                            sb.Append('\b');
                            break;
                        case 'f':
                            sb.Append('\f');
                            break;
                        case 'n':
                            sb.Append('\n');
                            break;
                        case 'r':
                            sb.Append('\r');
                            break;
                        case 't':
                            sb.Append('\t');
                            break;
                        case 'u':
                            if (i + 4 < s.Length)
                            {
                                int code = Convert.ToInt32(s.Substring(i + 1, 4), 16);
                                sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default:
                            sb.Append(n);
                            break;
                    }
                }
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private static Dictionary<string, string> ParseTopLevelFields(string json)
        {
            var result = new Dictionary<string, string>(4);
            int i = 0;
            int n = json.Length;
            SkipWs(json, ref i);
            if (i >= n || json[i] != '{')
                return result;
            i++;
            while (i < n)
            {
                SkipWs(json, ref i);
                if (i < n && json[i] == '}')
                    break;
                if (json[i] != '"')
                    break;
                int ks = i;
                i = SkipString(json, i);
                string key = UnescapeString(json.Substring(ks + 1, i - ks - 2));
                SkipWs(json, ref i);
                if (i >= n || json[i] != ':')
                    break;
                i++;
                SkipWs(json, ref i);
                int vs = i;
                i = SkipValue(json, i);
                result[key] = json.Substring(vs, i - vs);
                SkipWs(json, ref i);
                if (i < n && json[i] == ',')
                {
                    i++;
                    continue;
                }
                break;
            }
            return result;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r'))
                i++;
        }

        private static int SkipString(string s, int i)
        {
            i++;
            while (i < s.Length)
            {
                if (s[i] == '\\')
                {
                    i += 2;
                    continue;
                }
                if (s[i] == '"')
                    return i + 1;
                i++;
            }
            return i;
        }

        private static int SkipValue(string s, int i)
        {
            if (i >= s.Length)
                return i;
            char c = s[i];
            if (c == '"')
                return SkipString(s, i);
            if (c == '{' || c == '[')
                return SkipContainer(s, i);
            while (
                i < s.Length
                && s[i] != ','
                && s[i] != '}'
                && s[i] != ']'
                && s[i] != ' '
                && s[i] != '\t'
                && s[i] != '\n'
                && s[i] != '\r'
            )
                i++;
            return i;
        }

        private static int SkipContainer(string s, int i)
        {
            char open = s[i];
            char close = open == '{' ? '}' : ']';
            int depth = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '"')
                {
                    i = SkipString(s, i);
                    continue;
                }
                if (c == open)
                    depth++;
                if (c == close)
                {
                    depth--;
                    if (depth == 0)
                        return i + 1;
                }
                i++;
            }
            return i;
        }

        /// <summary>
        /// 从 ack.data 里抽取 code 与内层 data 子串 (若结构为 {"code":N,"data":...}).
        /// 返回 true 表示找到 code 字段.
        /// </summary>
        public static bool TryExtractCodeAndData(
            string ackDataJson,
            out int code,
            out string innerDataJson
        )
        {
            code = 0;
            innerDataJson = null;
            if (string.IsNullOrEmpty(ackDataJson) || ackDataJson[0] != '{')
                return false;
            try
            {
                var fields = ParseTopLevelFields(ackDataJson);
                string codeRaw;
                bool hasCode =
                    fields.TryGetValue("code", out codeRaw)
                    && int.TryParse(codeRaw.Trim(), out code);
                string dataRaw;
                if (fields.TryGetValue("data", out dataRaw))
                    innerDataJson = dataRaw.Trim();
                return hasCode;
            }
            catch
            {
                return false;
            }
        }
    }
}
