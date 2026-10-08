using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IOStudio.Services.ProtocolTest
{
    /// <summary>
    /// 协议测试场景执行器。
    ///
    /// <para>职责边界: 只负责“按步骤驱动传输 + 断言收到的字节”, 不解释业务协议。
    /// 因此同一套执行器可同时服务 loopback 自测(无外部对端)与真实对端联调。</para>
    /// </summary>
    public static class ProtocolTestRunner
    {
        private static readonly Regex VarPattern = new(
            @"\$\{([A-Za-z_][A-Za-z0-9_]*)\}",
            RegexOptions.Compiled
        );

        public static async Task<ProtocolTestReport> RunFileAsync(
            string scenarioPath,
            IProtocolTestObserver? observer = null,
            CancellationToken ct = default
        )
        {
            var json = await File.ReadAllTextAsync(scenarioPath, ct).ConfigureAwait(false);
            // 环境变量替换: 让同一份场景可直接指向不同对端 (现场联调不必改 JSON)。
            //   { "host": "${PEER_HOST}", "port": ${PEER_PORT} }  -- 未定义的变量原样保留
            json = VarPattern.Replace(
                json,
                m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? m.Value
            );
            var scenario = JsonSerializer.Deserialize<ProtocolTestScenario>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
            if (scenario is null)
                throw new InvalidOperationException($"场景解析失败: {scenarioPath}");
            return await RunAsync(scenario, observer, ct).ConfigureAwait(false);
        }

        public static async Task<ProtocolTestReport> RunAsync(
            ProtocolTestScenario scenario,
            IProtocolTestObserver? observer = null,
            CancellationToken ct = default
        )
        {
            var report = new ProtocolTestReport { Name = scenario.Name };
            var endpoints = new Dictionary<string, IProtocolEndpoint>(StringComparer.Ordinal);

            try
            {
                int index = 0;
                foreach (var step in scenario.Steps)
                {
                    index++;
                    var result = new ProtocolTestStepResult
                    {
                        Index = index,
                        Op = step.Op,
                        Via = string.IsNullOrEmpty(step.Via) ? step.Alias : step.Via,
                    };
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        var transfer = await ExecuteAsync(step, endpoints, scenario, observer, ct)
                            .ConfigureAwait(false);
                        result.Ok = true;
                        result.SentBytes = transfer.SentBytes;
                        result.SentHex = transfer.SentHex;
                        if (transfer.Received is { } received)
                        {
                            result.ReceivedHex = Convert.ToHexString(received);
                            result.ReceivedText = Encoding.UTF8.GetString(received);
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Ok = false;
                        result.Reason = ex.Message;
                    }
                    result.ElapsedMs = sw.ElapsedMilliseconds;
                    report.Steps.Add(result);
                    observer?.OnStepResult(result);
                }
            }
            finally
            {
                foreach (var e in endpoints.Values)
                {
                    try { e.Dispose(); } catch { /* ignore */ }
                }
            }

            report.Summary.Passed = report.Steps.Count(s => s.Ok);
            report.Summary.Failed = report.Steps.Count(s => !s.Ok);
            report.Ok = report.Summary.Failed == 0 && report.Steps.Count > 0;
            return report;
        }

        /// <summary>单步执行后需要回填到报告的内容。</summary>
        private readonly record struct Transfer(int SentBytes, string? SentHex, byte[]? Received);

        private static async Task<Transfer> ExecuteAsync(
            ProtocolTestStep step,
            Dictionary<string, IProtocolEndpoint> endpoints,
            ProtocolTestScenario scenario,
            IProtocolTestObserver? observer,
            CancellationToken ct
        )
        {
            var op = (step.Op ?? "").Trim().ToLowerInvariant();
            switch (op)
            {
                case "listen":
                case "connect":
                {
                    if (string.IsNullOrWhiteSpace(step.Alias))
                        throw new InvalidOperationException($"{op} 步骤缺少 as (别名)");
                    if (endpoints.ContainsKey(step.Alias))
                        throw new InvalidOperationException($"别名 '{step.Alias}' 重复");

                    var endpoint = ProtocolEndpointFactory.Create(
                        step.Transport,
                        op == "listen",
                        ProtocolEndpointFactory.BuildCodec(step.Frame),
                        step.Device,
                        step.Baud,
                        step.Path
                    );
                    int port = ResolvePort(step, endpoints);
                    await endpoint.OpenAsync(step.Host, port, ct).ConfigureAwait(false);
                    endpoints[step.Alias] = endpoint;
                    return default;
                }
                case "send":
                {
                    var endpoint = Resolve(step, endpoints);
                    var bytes = Encode(step);
                    await endpoint.SendAsync(bytes, ct).ConfigureAwait(false);
                    observer?.OnSent(ChannelKey(step), bytes);
                    return new Transfer(bytes.Length, Convert.ToHexString(bytes), null);
                }
                case "expect":
                {
                    var endpoint = Resolve(step, endpoints);
                    var via = ChannelKey(step);
                    var timeout = TimeSpan.FromMilliseconds(
                        step.TimeoutMs > 0 ? step.TimeoutMs : scenario.TimeoutMs
                    );

                    // 扫描到匹配为止, 而不是"只看下一条" ——
                    // 真实链路上同一时刻会混有心跳/其它命令, 逐条严格匹配会误判为失败。
                    // 若需要"下一条必须匹配"的严格语义, 把 timeout 设小即可。
                    var deadline = DateTime.UtcNow + timeout;
                    byte[]? matched = null;
                    int scanned = 0;
                    string lastWhy = "";
                    while (DateTime.UtcNow < deadline)
                    {
                        var remaining = deadline - DateTime.UtcNow;
                        if (remaining <= TimeSpan.Zero)
                            break;

                        var data = await endpoint.ReceiveAsync(remaining, ct).ConfigureAwait(false);
                        if (data is null)
                            break; // 超时

                        scanned++;
                        var text = Encoding.UTF8.GetString(data);
                        var (ok, why) = Match(data, text, step.Match);
                        // 不匹配的帧也要抛给观察者 —— 人排查时正是靠看它们定位问题
                        observer?.OnReceived(via, data, ok);
                        if (ok)
                        {
                            matched = data;
                            break;
                        }
                        lastWhy = why;
                    }

                    if (matched is null)
                    {
                        throw new InvalidOperationException(
                            scanned == 0
                                ? $"等待数据超时 ({timeout.TotalMilliseconds:F0}ms)"
                                : $"超时前收到 {scanned} 条均不匹配; 最后一条: {lastWhy}"
                        );
                    }
                    return new Transfer(0, null, matched);
                }
                case "close":
                {
                    if (endpoints.TryGetValue(step.Via, out var e))
                    {
                        e.Dispose();
                        endpoints.Remove(step.Via);
                    }
                    return default;
                }
                case "sleep":
                    await Task.Delay(step.TimeoutMs > 0 ? step.TimeoutMs : 200, ct).ConfigureAwait(false);
                    return default;
                default:
                    throw new InvalidOperationException($"未知 op: {step.Op}");
            }
        }

        private static string ChannelKey(ProtocolTestStep step) =>
            string.IsNullOrEmpty(step.Via) ? step.Alias : step.Via;

        private static int ResolvePort(
            ProtocolTestStep step,
            Dictionary<string, IProtocolEndpoint> endpoints
        )
        {
            if (!string.IsNullOrWhiteSpace(step.PortFrom))
            {
                if (!endpoints.TryGetValue(step.PortFrom, out var src))
                    throw new InvalidOperationException($"port_from '{step.PortFrom}' 不存在");
                return src.Port;
            }
            return step.Port;
        }

        private static IProtocolEndpoint Resolve(
            ProtocolTestStep step,
            Dictionary<string, IProtocolEndpoint> endpoints
        )
        {
            var key = ChannelKey(step);
            if (!endpoints.TryGetValue(key, out var e))
                throw new InvalidOperationException($"通道 '{key}' 不存在 (先 listen/connect)");
            return e;
        }

        private static byte[] Encode(ProtocolTestStep step) =>
            ProtocolEncoding.Encode(step.Data, step.Encoding);

        /// <summary>断言收到的数据。match.type 见设计文档。</summary>
        private static (bool Ok, string Why) Match(byte[] data, string text, ProtocolTestMatch m)
        {
            var type = (m.Type ?? "contains").Trim();
            var value = m.Value ?? "";
            switch (type.ToLowerInvariant())
            {
                case "equals":
                    return text == value
                        ? (true, "")
                        : (false, $"内容不符: 期望 {Short(value)}, 实际 {Short(text)}");

                case "contains":
                    return text.Contains(value, StringComparison.Ordinal)
                        ? (true, "")
                        : (false, $"未包含 {Short(value)}; 实际 {Short(text)}");

                case "notcontains":
                    return !text.Contains(value, StringComparison.Ordinal)
                        ? (true, "")
                        : (false, $"不应包含 {Short(value)}; 实际 {Short(text)}");

                case "startswith":
                    return text.StartsWith(value, StringComparison.Ordinal)
                        ? (true, "")
                        : (false, $"未以 {Short(value)} 开头; 实际 {Short(text)}");

                case "endswith":
                    return text.EndsWith(value, StringComparison.Ordinal)
                        ? (true, "")
                        : (false, $"未以 {Short(value)} 结尾; 实际 {Short(text)}");

                case "regex":
                    return Regex.IsMatch(text, value)
                        ? (true, "")
                        : (false, $"不匹配正则 /{value}/; 实际 {Short(text)}");

                case "hexequals":
                    var hex = Convert.ToHexString(data);
                    return hex.Equals(value.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)
                        ? (true, "")
                        : (false, $"十六进制不符: 期望 {value}, 实际 {hex}");

                case "length":
                    return data.Length.ToString() == value
                        ? (true, "")
                        : (false, $"长度不符: 期望 {value}, 实际 {data.Length}");

                case "json":
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(text);
                        var actual = GetPath(doc.RootElement, m.Path);
                        return actual == value
                            ? (true, "")
                            : (false, $"JSON 路径 '{m.Path}' 不符: 期望 {Short(value)}, 实际 {Short(actual)}");
                    }
                    catch (JsonException ex)
                    {
                        return (false, $"报文不是合法 JSON: {ex.Message}");
                    }
                }

                default:
                    return (false, $"未知 match.type: {m.Type}");
            }
        }

        /// <summary>取 JSON 点路径 (如 Cmd 或 Para.GameState)。</summary>
        private static string? GetPath(JsonElement root, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return root.ValueKind == JsonValueKind.String ? root.GetString() : root.ToString();

            var cur = root;
            foreach (var seg in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(seg, out var next))
                    return null;
                cur = next;
            }
            return cur.ValueKind == JsonValueKind.String ? cur.GetString() : cur.ToString();
        }

        private static string Short(string? s) =>
            s is null ? "<null>" : (s.Length <= 120 ? s : s[..120] + "...");
    }
}
