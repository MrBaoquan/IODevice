using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IOStudio.Services.Motion
{
    public enum MotionSafeStopCompletionMode
    {
        Estimated,
        Confirmed,
    }

    /// <summary>
    /// 设备无关的 SafeStop 契约。该契约规定何时必须停、设备安全值、完成语义和失败策略。
    /// 设备插件通过现有 _rpc.req/_rpc.res 实现控制面，不新增 IOUI 导出 ABI。
    /// </summary>
    public sealed class MotionSafeStopContract
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };

        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("profileId")]
        public string ProfileId { get; set; } = "";

        [JsonPropertyName("device")]
        public string Device { get; set; } = "";

        [JsonPropertyName("safeStop")]
        public MotionSafeStopSpec SafeStop { get; set; } = new();

        [JsonPropertyName("requiredOutputs")]
        public List<string> RequiredOutputs { get; set; } = new();

        [JsonPropertyName("neutralOutputs")]
        public Dictionary<string, float> NeutralOutputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("transitions")]
        public MotionSafeStopTransitions Transitions { get; set; } = new();

        [JsonPropertyName("rules")]
        public MotionSafeStopRules Rules { get; set; } = new();

        public static MotionSafeStopContract Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("contract path is empty", nameof(path));
            if (!File.Exists(path))
                throw new FileNotFoundException("SafeStop contract not found", path);

            var contract = JsonSerializer.Deserialize<MotionSafeStopContract>(
                File.ReadAllText(path),
                JsonOptions
            );
            if (contract is null)
                throw new InvalidDataException($"SafeStop contract is empty: {path}");
            return contract;
        }

        public static MotionSafeStopContract FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("contract json is empty", nameof(json));
            return JsonSerializer.Deserialize<MotionSafeStopContract>(json, JsonOptions)
                ?? throw new InvalidDataException("SafeStop contract json is empty");
        }

        public IReadOnlyList<MotionValidationIssue> Validate()
        {
            var issues = new List<MotionValidationIssue>();

            if (Version != 1)
                Add("safety.contract_version", "SafeStop 契约版本必须为 1。");
            if (string.IsNullOrWhiteSpace(ProfileId))
                Add("safety.missing_profile_id", "SafeStop 契约缺少 profileId。");
            if (string.IsNullOrWhiteSpace(Device))
                Add("safety.missing_device", "SafeStop 契约缺少 device。");

            if (SafeStop.StopTopic != "plugin.safety.stop")
                Add("safety.invalid_stop_topic", "SafeStop 必须使用 plugin.safety.stop。");
            if (SafeStop.ClearTopic != "plugin.safety.clear")
                Add("safety.invalid_clear_topic", "SafeStop clear 必须使用 plugin.safety.clear。");
            if (SafeStop.CompletionTopic != "plugin.safety.stateChanged")
                Add("safety.invalid_completion_topic", "完成通知必须使用 plugin.safety.stateChanged。");
            if (SafeStop.TimeoutMs <= 0)
                Add("safety.invalid_timeout", "SafeStop timeoutMs 必须大于 0。");
            if (SafeStop.SafeStopRate <= 0)
                Add("safety.invalid_safe_stop_rate", "safeStopRate 必须大于 0。");
            if (SafeStop.SafeStopMarginMs < 0)
                Add("safety.invalid_safe_stop_margin", "safeStopMarginMs 不能为负。");
            if (SafeStop.CompletionMode == MotionSafeStopCompletionMode.Estimated &&
                SafeStop.TimeoutMs > 0 && SafeStop.TimeoutMs <= SafeStop.SafeStopMarginMs)
                Add("safety.timeout_before_settle", "ACK 超时必须大于安全停止余量。");

            if (SafeStop.CompletionMode == MotionSafeStopCompletionMode.Estimated &&
                Rules.TreatEstimatedAsConfirmed)
                Add("safety.estimated_marked_confirmed", "estimated 完成不得当作物理 confirmed。");

            foreach (string output in RequiredOutputs)
            {
                if (string.IsNullOrWhiteSpace(output)) continue;
                if (!NeutralOutputs.ContainsKey(output))
                {
                    Add(
                        "safety.missing_neutral_output",
                        $"必需输出 {output} 未声明 neutral 安全值。",
                        output
                    );
                }
            }

            if (RequiredOutputs.Count == 0)
                Add("safety.no_required_outputs", "契约必须声明 requiredOutputs。");
            if (NeutralOutputs.Count == 0)
                Add("safety.no_neutral_outputs", "契约必须声明 neutralOutputs。");

            if (Transitions.ReturnToIdle != "safe_stop")
                Add("safety.unsafe_idle_rule", "返回待机必须先执行 safe_stop。");
            if (Transitions.SwitchFilm != "safe_stop_before_start")
                Add("safety.unsafe_switch_rule", "切换影片必须先安全停止旧动作。");
            if (Transitions.LoadOnly != "safe_stop_before_load")
                Add("safety.unsafe_load_rule", "仅 load 前必须先安全停止旧动作。");
            if (Transitions.NaturalEnd != "ramp_then_safe_stop")
                Add("safety.unsafe_natural_end_rule", "自然结束必须先回中再进入设备安全态。");
            if (Transitions.ApplicationPause != "safe_stop")
                Add("safety.unsafe_pause_rule", "应用暂停必须执行 safe_stop。");
            if (Transitions.EmergencyStop != "hard_safe_stop")
                Add("safety.unsafe_estop_rule", "紧急停止必须执行 hard_safe_stop。");
            if (Transitions.LinkLoss != "hard_safe_stop")
                Add("safety.unsafe_link_loss_rule", "断链必须执行 hard_safe_stop。");

            if (!Rules.LockoutOnFailure)
                Add("safety.missing_lockout", "SafeStop 失败必须锁存。");
            if (!Rules.RejectStaleGeneration)
                Add("safety.accept_stale_generation", "必须拒绝旧 generation 的动作帧。");
            if (!Rules.RequireSettledBeforeNextPlay)
                Add("safety.play_without_settled", "未 settled 前不得开始下一部影片。");

            return issues;

            void Add(string code, string message, string? trackId = null)
            {
                issues.Add(
                    new MotionValidationIssue
                    {
                        Severity = MotionIssueSeverity.Error,
                        Code = code,
                        Message = message,
                        TrackId = trackId,
                    }
                );
            }
        }
    }

    public sealed class MotionSafeStopSpec
    {
        [JsonPropertyName("stopTopic")]
        public string StopTopic { get; set; } = "";

        [JsonPropertyName("clearTopic")]
        public string ClearTopic { get; set; } = "";

        [JsonPropertyName("completionTopic")]
        public string CompletionTopic { get; set; } = "";

        [JsonPropertyName("completionMode")]
        public MotionSafeStopCompletionMode CompletionMode { get; set; }

        [JsonPropertyName("safeStopRate")]
        public double SafeStopRate { get; set; }

        [JsonPropertyName("safeStopMarginMs")]
        public int SafeStopMarginMs { get; set; }

        [JsonPropertyName("timeoutMs")]
        public int TimeoutMs { get; set; }
    }

    public sealed class MotionSafeStopTransitions
    {
        [JsonPropertyName("returnToIdle")]
        public string ReturnToIdle { get; set; } = "";

        [JsonPropertyName("switchFilm")]
        public string SwitchFilm { get; set; } = "";

        [JsonPropertyName("loadOnly")]
        public string LoadOnly { get; set; } = "";

        [JsonPropertyName("naturalEnd")]
        public string NaturalEnd { get; set; } = "";

        [JsonPropertyName("applicationPause")]
        public string ApplicationPause { get; set; } = "";

        [JsonPropertyName("emergencyStop")]
        public string EmergencyStop { get; set; } = "";

        [JsonPropertyName("linkLoss")]
        public string LinkLoss { get; set; } = "";
    }

    public sealed class MotionSafeStopRules
    {
        [JsonPropertyName("lockoutOnFailure")]
        public bool LockoutOnFailure { get; set; }

        [JsonPropertyName("rejectStaleGeneration")]
        public bool RejectStaleGeneration { get; set; }

        [JsonPropertyName("requireSettledBeforeNextPlay")]
        public bool RequireSettledBeforeNextPlay { get; set; }

        [JsonPropertyName("treatEstimatedAsConfirmed")]
        public bool TreatEstimatedAsConfirmed { get; set; }
    }
}
