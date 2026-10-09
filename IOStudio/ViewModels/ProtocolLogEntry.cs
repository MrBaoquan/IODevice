using System;
using System.Text;
using IOStudio.Services.ProtocolTest;
using ReactiveUI;

namespace IOStudio.ViewModels
{
    /// <summary>
    /// 协议测试台日志的一行。
    ///
    /// <para>同一张表里同时承载"收/发的每一帧"与"每个步骤的结论" —— 人验证协议时,
    /// 需要把"收到了什么"和"这一步过没过"放在一起看, 分两个列表反而要来回对照。</para>
    ///
    /// <para>显示方式是**可变**的 (文本 / HEX / HEX转储): 主流调试台都有"HEX 显示"开关,
    /// 且同一时刻只显示一种 —— 内容和 HEX 并排把每条报文显示两遍既占宽又难读。
    /// 切换时要让已经显示的行一起变, 所以这里做成会通知的属性。</para>
    /// </summary>
    public sealed class ProtocolLogEntry : ViewModelBase
    {
        // 对象初始化器不能写 readonly 字段, 故这两个字段不用 readonly (仅由工厂写入)
        private string _plainText = "";
        private string _hexText = "";
        private string? _dump;

        /// <summary>原始字节 (hexdump 转储懒计算; 未拿到原始字节时为空)。</summary>
        public byte[]? Raw { get; private init; }

        /// <summary>
        /// 归属会话 (行内右键"复制 / 重发"要回到会话执行)。
        /// 由 <c>AddEntry</c> 写入 —— 放在条目上而不是让 XAML 跨 popup 可视树找
        /// ListBox 的 DataContext, 后者在 ContextMenu 里不可靠。
        /// </summary>
        public ProtocolSessionViewModel? Owner { get; set; }

        /// <summary>能否重发 (只有拿到原始字节的收/发帧可以; 步骤/提示行不行)。</summary>
        public bool CanResend => Raw is { Length: > 0 };

        public string Time { get; private init; } = "";
        public string Direction { get; private init; } = "";
        public string Channel { get; private init; } = "";
        public string Size { get; private init; } = "";

        /// <summary>带单位的字节数 (详情面板用; 系统/步骤行没有字节数, 返回空)。</summary>
        public string SizeText => string.IsNullOrEmpty(Size) ? "" : Size + " 字节";

        /// <summary>该行是否为步骤结论行 (而非收发帧)。</summary>
        public bool IsStep { get; private init; }

        public bool IsReceived { get; private init; }
        public bool IsSent { get; private init; }

        /// <summary>步骤失败 / 收到但断言不匹配。</summary>
        public bool IsBad { get; private init; }

        /// <summary>自动应答发出的帧 (与手动发送区分, 便于看清"是谁发的")。</summary>
        public bool IsAuto { get; private init; }

        /// <summary>协议解析行 (如 Modbus 应答), 与普通收发/系统行区分着色。</summary>
        public bool IsDecoded { get; private init; }

        /// <summary>成功的步骤结论行 (用于绿色着色)。</summary>
        public bool IsPass => IsStep && !IsBad;

        // ── 时序 (协议排查最常看的量) ──

        /// <summary>与**上一次同方向**帧的间隔 (ms)。心跳/发送周期是否正常, 一眼可见。</summary>
        public long? DeltaMs { get; set; }

        /// <summary>往返延迟 (ms): 上一次发送到本次收到之间的时间 (无配对则为空)。</summary>
        public long? RttMs { get; set; }

        public string DeltaText => DeltaMs is null ? "" : DeltaMs.Value.ToString();
        public string RttText => RttMs is null ? "" : RttMs.Value.ToString();

        /// <summary>Δt 与 RTT 合并成一列显示 ("间隔 / 往返"), 省宽度也便于对照。</summary>
        public string TimingText =>
            DeltaMs is null ? (RttMs is null ? "" : $"– / {RttMs}")
            : RttMs is null ? DeltaMs.Value.ToString()
            : $"{DeltaMs.Value} / {RttMs.Value}";

        // ── 显示 ──

        private ProtocolContentMode _displayMode = ProtocolContentMode.TextAndHex;
        public ProtocolContentMode DisplayMode
        {
            get => _displayMode;
            set
            {
                if (_displayMode == value)
                    return;
                _displayMode = value;
                this.RaisePropertyChanged(nameof(ContentText));
                this.RaisePropertyChanged(nameof(HexColumn));
                this.RaisePropertyChanged(nameof(DisplayContent));
                this.RaisePropertyChanged(nameof(IsDumpDisplay));
            }
        }

        /// <summary>
        /// 内容列 (单列): 由显示模式决定这一列显示什么 —— 文本 / HEX / hexdump。
        ///
        /// <para>2026-10 起日志不再「内容 + HEX」两列并排: 同一条报文显示两遍既占宽度
        /// 又难扫读。改为一个模式开关, 同一时刻只渲染一种。</para>
        /// </summary>
        public string DisplayContent =>
            IsStep ? _plainText
            : _displayMode == ProtocolContentMode.Dump ? Dump
            : _displayMode == ProtocolContentMode.HexOnly ? SpacedHex
            : !string.IsNullOrEmpty(_plainText) ? _plainText
            : SpacedHex;   // 文本模式遇到不可读的二进制帧: 回退 HEX, 否则这一行会是空的

        /// <summary>是否处于「HEX 转储」模式 (多行) —— 内容列据此决定是否换行。</summary>
        public bool IsDumpDisplay => !IsStep && _displayMode == ProtocolContentMode.Dump;

        /// <summary>内容列 (旧双列模型): 转储模式下显示 hexdump, 其余显示文本。</summary>
        public string ContentText =>
            IsStep ? _plainText
            : _displayMode == ProtocolContentMode.Dump ? Dump
            : _displayMode == ProtocolContentMode.HexOnly ? ""
            : _plainText;

        /// <summary>HEX 列 (旧双列模型): 转储模式与纯文本模式下为空。</summary>
        public string HexColumn =>
            IsStep ? ""
            : _displayMode == ProtocolContentMode.Dump ? ""
            : _displayMode == ProtocolContentMode.Text ? ""
            : _hexText;

        private string Dump => _dump ??= HexDump.Format(Raw);

        private string? _spacedHex;

        /// <summary>按字节分组的 HEX (AA BB CC) —— 单列 HEX 视图用, 比连写更易读。</summary>
        private string SpacedHex
        {
            get
            {
                if (_spacedHex is not null)
                    return _spacedHex;
                if (Raw is null || Raw.Length == 0)
                    return _spacedHex = "";

                var sb = new StringBuilder(Raw.Length * 3);
                for (int i = 0; i < Raw.Length; i++)
                {
                    if (i > 0)
                        sb.Append(' ');
                    sb.Append(Raw[i].ToString("X2"));
                }
                return _spacedHex = sb.ToString();
            }
        }

        /// <summary>完整 hexdump (详情面板用: offset / 分组 hex / ASCII 三段式)。</summary>
        public string HexDumpText => Raw is null ? "" : Dump;

        /// <summary>原始文本表示 (与显示模式无关, 供过滤/导出使用)。</summary>
        public string PlainText => _plainText;

        /// <summary>原始 HEX 表示 (与显示模式无关, 供过滤/导出使用)。</summary>
        public string HexText => _hexText;

        // ── 工厂 ──

        public static ProtocolLogEntry Frame(string direction, string channel, byte[] data, bool? matched)
        {
            // 收到但未匹配时给出行内标记, 不必再去看报告
            var dir = matched == false ? direction + "✗" : direction;
            return new ProtocolLogEntry
            {
                Time = DateTime.Now.ToString("HH:mm:ss.fff"),
                Direction = dir,
                Channel = channel,
                Size = data.Length.ToString(),
                Raw = data,
                _plainText = ProtocolEncoding.DecodeForDisplay(data),
                _hexText = Convert.ToHexString(data),
                IsReceived = direction == "收",
                IsSent = direction == "发",
                IsBad = matched == false,
            };
        }

        public static ProtocolLogEntry AutoReply(byte[] data) =>
            new()
            {
                Time = DateTime.Now.ToString("HH:mm:ss.fff"),
                Direction = "自动",
                Size = data.Length.ToString(),
                Raw = data,
                _plainText = ProtocolEncoding.DecodeForDisplay(data),
                _hexText = Convert.ToHexString(data),
                IsSent = true,
                IsAuto = true,
            };

        public static ProtocolLogEntry Step(ProtocolTestStepResult r)
        {
            var verdict = r.Ok ? "PASS" : "FAIL";
            var detail = r.Ok
                ? $"#{r.Index} {r.Op}" + (string.IsNullOrEmpty(r.Via) ? "" : $" via={r.Via}") + $"  ({r.ElapsedMs}ms)"
                : $"#{r.Index} {r.Op}" + (string.IsNullOrEmpty(r.Via) ? "" : $" via={r.Via}") + $"  — {r.Reason}";

            return new ProtocolLogEntry
            {
                Time = DateTime.Now.ToString("HH:mm:ss.fff"),
                Direction = verdict,
                Channel = r.Via,
                Size = "",
                _plainText = detail,
                IsStep = true,
                IsBad = !r.Ok,
            };
        }

        public static ProtocolLogEntry Info(string message) =>
            new()
            {
                Time = DateTime.Now.ToString("HH:mm:ss.fff"),
                Direction = "系统",
                _plainText = message,
                IsStep = true,
            };

        /// <summary>协议解析行 (如 Modbus 应答的功能码 / 寄存器 / 异常码)。</summary>
        public static ProtocolLogEntry Decoded(string message) =>
            new()
            {
                Time = DateTime.Now.ToString("HH:mm:ss.fff"),
                Direction = "解析",
                _plainText = message,
                IsStep = true,
                IsDecoded = true,
            };
    }
}
