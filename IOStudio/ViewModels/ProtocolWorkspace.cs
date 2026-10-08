using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IOStudio.ViewModels
{
    /// <summary>
    /// 调试台工作区 —— 会话配置的持久化。
    ///
    /// <para>主流同类工具 (SSCOM / NetAssist / Hercules) 都会记住上次的连接与指令列表;
    /// 每次开窗口都要重配端口/组帧/规则是明显不成熟的表现。这里把"会话"整体落盘,
    /// 下次打开原样恢复。</para>
    ///
    /// <para>只存**配置**, 不存日志与统计 (那些是运行态数据, 没有恢复价值)。</para>
    /// </summary>
    public sealed class ProtocolWorkspace
    {
        [JsonPropertyName("sessions")]
        public List<ProtocolSessionState> Sessions { get; set; } = new();

        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };

        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>工作区文件位置 (与其它运行期配置同处 Config/)。</summary>
        public static string DefaultPath =>
            Path.Combine(AppContext.BaseDirectory, "Config", "ProtocolTestWorkspace.json");

        public static ProtocolWorkspace Load(string? path = null)
        {
            try
            {
                var p = path ?? DefaultPath;
                if (!File.Exists(p))
                    return new ProtocolWorkspace();
                var json = File.ReadAllText(p, System.Text.Encoding.UTF8);
                return JsonSerializer.Deserialize<ProtocolWorkspace>(json, ReadOptions)
                    ?? new ProtocolWorkspace();
            }
            catch
            {
                // 工作区损坏不应让窗口打不开 —— 退回空配置
                return new ProtocolWorkspace();
            }
        }

        public void Save(string? path = null)
        {
            try
            {
                var p = path ?? DefaultPath;
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                // 不带 BOM: 便于脚本/AI 直接读
                File.WriteAllText(p, JsonSerializer.Serialize(this, WriteOptions), new System.Text.UTF8Encoding(false));
            }
            catch
            {
                /* 保存失败不应影响使用 */
            }
        }
    }

    public sealed class ProtocolSessionState
    {
        [JsonPropertyName("label")] public string Label { get; set; } = "";
        [JsonPropertyName("transport")] public string Transport { get; set; } = "udp";
        [JsonPropertyName("is_listener")] public bool IsListener { get; set; } = true;

        [JsonPropertyName("host")] public string Host { get; set; } = "127.0.0.1";
        [JsonPropertyName("port")] public int Port { get; set; }
        [JsonPropertyName("serial_device")] public string SerialDevice { get; set; } = "COM3";
        [JsonPropertyName("baud")] public int Baud { get; set; } = 115200;

        [JsonPropertyName("frame_mode")] public string FrameMode { get; set; } = "none";
        [JsonPropertyName("delimiter")] public string Delimiter { get; set; } = "\\r\\n";
        [JsonPropertyName("prefix_bytes")] public int PrefixBytes { get; set; } = 4;
        [JsonPropertyName("fixed_size")] public int FixedSize { get; set; } = 8;

        [JsonPropertyName("send_text")] public string SendText { get; set; } = "";
        [JsonPropertyName("send_encoding")] public string SendEncoding { get; set; } = "UTF-8";
        [JsonPropertyName("send_new_line")] public string SendNewLine { get; set; } = "无";
        [JsonPropertyName("loop_send")] public bool LoopSend { get; set; }
        [JsonPropertyName("loop_interval_ms")] public int LoopIntervalMs { get; set; } = 1000;

        [JsonPropertyName("show_timestamp")] public bool ShowTimestamp { get; set; } = true;
        [JsonPropertyName("auto_scroll")] public bool AutoScroll { get; set; } = true;

        [JsonPropertyName("rules")] public List<AutoReplyRuleState> Rules { get; set; } = new();
        [JsonPropertyName("quick_commands")] public List<QuickCommandState> QuickCommands { get; set; } = new();
    }

    public sealed class AutoReplyRuleState
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
        [JsonPropertyName("match_type")] public string MatchType { get; set; } = "包含";
        [JsonPropertyName("match_value")] public string MatchValue { get; set; } = "";
        [JsonPropertyName("reply_text")] public string ReplyText { get; set; } = "OK";
        [JsonPropertyName("reply_encoding")] public string ReplyEncoding { get; set; } = "UTF-8";
    }

    public sealed class QuickCommandState
    {
        [JsonPropertyName("text")] public string Text { get; set; } = "";
        [JsonPropertyName("encoding")] public string Encoding { get; set; } = "UTF-8";
        [JsonPropertyName("new_line")] public string NewLine { get; set; } = "无";
        [JsonPropertyName("periodic")] public bool Periodic { get; set; }
        [JsonPropertyName("interval_ms")] public int IntervalMs { get; set; } = 1000;
    }
}
