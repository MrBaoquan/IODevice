using System;
using System.Collections.Generic;
using System.Linq;

namespace IOStudio.Models.Motion
{
    /// <summary>
    /// 一条「补全设备表动作」的执行计划 —— (设备, 动作) → 要写进 <c>&lt;Key&gt;</c> 的通道列表。
    ///
    /// <para>通道必须由人给出: 动作文件里<b>没有</b>这个信息 (oaction 轨道的身份就是动作名,
    /// <c>OAxisChannel</c> 对 oaction 类型无效), 而"动作名 → 通道"的映射只存在于设备表里。
    /// 所以这里永远不是推断出来的, 只能是用户选的。</para>
    /// </summary>
    public class OActionCompletion
    {
        public string DeviceName { get; set; } = "";
        public string OActionName { get; set; } = "";
        public List<string> Channels { get; set; } = new();

        /// <summary>
        /// 通道输入的解析 —— 逗号/分号/空白都当分隔符 (用户会怎么打就怎么认),
        /// 并按大小写不敏感去重 (通道名重复会写出重复的 <c>&lt;Key&gt;</c> 结点)。
        /// </summary>
        public static List<string> ParseChannels(string? text) =>
            (text ?? "")
                .Split(
                    new[] { ',', '，', ';', '；', '\n', '\r', '\t', ' ' },
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
    }
}

