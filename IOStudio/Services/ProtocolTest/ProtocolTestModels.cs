using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IOStudio.Services.ProtocolTest
{
    /// <summary>
    /// 协议测试场景 (申明式, 便于 CLI 与 AI 生成/消费)。
    ///
    /// <para>为什么用申明式而不是写代码: 协议自检需要**可组合**——同一套步骤要能
    /// 在 CI、现场联调、AI 生成新用例三种场景复用, 且结果要机器可读。
    /// 代码化的测试只能人肉维护。</para>
    /// </summary>
    public sealed class ProtocolTestScenario
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary>默认超时 (毫秒), 步骤未单独指定时使用。</summary>
        [JsonPropertyName("timeout_ms")]
        public int TimeoutMs { get; set; } = 3000;

        [JsonPropertyName("steps")]
        public List<ProtocolTestStep> Steps { get; set; } = new();
    }

    public sealed class ProtocolTestStep
    {
        /// <summary>listen | connect | send | expect | close | sleep</summary>
        [JsonPropertyName("op")]
        public string Op { get; set; } = "";

        /// <summary>通道别名, 供后续步骤通过 via/portFrom 引用。</summary>
        [JsonPropertyName("as")]
        public string Alias { get; set; } = "";

        /// <summary>操作目标通道别名。</summary>
        [JsonPropertyName("via")]
        public string Via { get; set; } = "";

        /// <summary>udp | tcp-client | tcp-server</summary>
        [JsonPropertyName("transport")]
        public string Transport { get; set; } = "udp";

        [JsonPropertyName("host")]
        public string Host { get; set; } = "127.0.0.1";

        /// <summary>端口; 0 = 自动分配 (listen 用)。</summary>
        [JsonPropertyName("port")]
        public int Port { get; set; }

        /// <summary>从某个 listen 步骤的实际端口取端口 (避免硬编码)。</summary>
        [JsonPropertyName("port_from")]
        public string PortFrom { get; set; } = "";

        /// <summary>发送内容 (text / hex / json)。</summary>
        [JsonPropertyName("data")]
        public string Data { get; set; } = "";

        /// <summary>utf8 | hex | gbk</summary>
        [JsonPropertyName("encoding")]
        public string Encoding { get; set; } = "utf8";

        [JsonPropertyName("timeout_ms")]
        public int TimeoutMs { get; set; }

        [JsonPropertyName("match")]
        public ProtocolTestMatch Match { get; set; } = new();
    }

    public sealed class ProtocolTestMatch
    {
        /// <summary>equals | contains | regex | json | length | hexEquals</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "contains";

        [JsonPropertyName("value")]
        public string Value { get; set; } = "";

        /// <summary>json 匹配时的取值路径, 例如 Cmd 或 Para</summary>
        [JsonPropertyName("path")]
        public string Path { get; set; } = "";
    }

    // ── 报告 (机器可读, 供 CLI 门禁与 AI 消费) ──

    public sealed class ProtocolTestReport
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("steps")]
        public List<ProtocolTestStepResult> Steps { get; set; } = new();

        [JsonPropertyName("summary")]
        public ProtocolTestSummary Summary { get; set; } = new();

        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public string ToJson() => JsonSerializer.Serialize(this, WriteOptions);
    }

    public sealed class ProtocolTestSummary
    {
        [JsonPropertyName("passed")]
        public int Passed { get; set; }

        [JsonPropertyName("failed")]
        public int Failed { get; set; }
    }

    public sealed class ProtocolTestStepResult
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("op")]
        public string Op { get; set; } = "";

        [JsonPropertyName("via")]
        public string Via { get; set; } = "";

        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("reason")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Reason { get; set; }

        [JsonPropertyName("sent_bytes")]
        public int SentBytes { get; set; }

        [JsonPropertyName("sent_hex")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SentHex { get; set; }

        [JsonPropertyName("received_hex")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ReceivedHex { get; set; }

        [JsonPropertyName("received_text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ReceivedText { get; set; }

        [JsonPropertyName("elapsed_ms")]
        public long ElapsedMs { get; set; }
    }
}
