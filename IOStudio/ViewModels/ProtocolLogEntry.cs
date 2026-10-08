using System;
using System.Text;
using IOStudio.Services.ProtocolTest;

namespace IOStudio.ViewModels
{
    /// <summary>
    /// 协议测试台日志的一行。
    ///
    /// <para>同一张表里同时承载"收/发的每一帧"与"每个步骤的结论" —— 人验证协议时,
    /// 需要把"收到了什么"和"这一步过没过"放在一起看, 分两个列表反而要来回对照。</para>
    /// </summary>
    public sealed class ProtocolLogEntry
    {
        public string Time { get; init; } = "";
        public string Direction { get; init; } = "";
        public string Channel { get; init; } = "";
        public string Size { get; init; } = "";
        public string Hex { get; init; } = "";
        public string Text { get; init; } = "";

        /// <summary>该行是否为步骤结论行 (而非收发帧)。</summary>
        public bool IsStep { get; init; }

        public bool IsReceived { get; init; }
        public bool IsSent { get; init; }

        /// <summary>步骤失败 / 收到但断言不匹配。</summary>
        public bool IsBad { get; init; }

        /// <summary>自动应答发出的帧 (与手动发送区分, 便于看清"是谁发的")。</summary>
        public bool IsAuto { get; init; }

        /// <summary>成功的步骤结论行 (用于绿色着色)。</summary>
        public bool IsPass => IsStep && !IsBad;

        public static ProtocolLogEntry Frame(string direction, string channel, byte[] data, bool? matched)
        {
            var text = SafeText(data);
            // 收到但未匹配时给出行内标记, 不必再去看报告
            var dir = matched == false ? direction + "✗" : direction;
            return new ProtocolLogEntry
            {
                Time = DateTime.Now.ToString("HH:mm:ss.fff"),
                Direction = dir,
                Channel = channel,
                Size = data.Length.ToString(),
                Hex = Convert.ToHexString(data),
                Text = text,
                IsReceived = direction == "收",
                IsSent = direction == "发",
                IsBad = matched == false,
            };
        }

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
                Hex = "",
                Text = detail,
                IsStep = true,
                IsBad = !r.Ok,
            };
        }

        public static ProtocolLogEntry Info(string message) =>
            new()
            {
                Time = DateTime.Now.ToString("HH:mm:ss.fff"),
                Direction = "··",
                Text = message,
                IsStep = true,
            };

        /// <summary>自动应答发出的帧。</summary>
        public static ProtocolLogEntry AutoReply(byte[] data)
        {
            var text = SafeText(data);
            return new ProtocolLogEntry
            {
                Time = DateTime.Now.ToString("HH:mm:ss.fff"),
                Direction = "自动",
                Channel = "",
                Size = data.Length.ToString(),
                Hex = Convert.ToHexString(data),
                Text = text,
                IsSent = true,
                IsAuto = true,
            };
        }

        private static string SafeText(byte[] data)
        {
            // 二进制内容按 UTF-8 解出来会是乱码; 只在"看起来是文本"时给文本, 否则留空让 Hex 说话
            try
            {
                var s = Encoding.UTF8.GetString(data);
                foreach (var ch in s)
                {
                    if (ch == '\uFFFD' || (char.IsControl(ch) && ch != '\r' && ch != '\n' && ch != '\t'))
                        return "";
                }
                return s;
            }
            catch
            {
                return "";
            }
        }
    }
}
