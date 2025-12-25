using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace IOTester.Services
{
    /// <summary>
    /// 键名验证器，用于检查键名是否合法以及是否重复
    /// </summary>
    public static class KeyNameValidator
    {
        /// <summary>
        /// EKeys 中定义的所有标准键名
        /// </summary>
        private static readonly HashSet<string> StandardKeyNames = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            // Keyboard & Mouse
            "AnyKey",
            "MouseX",
            "MouseY",
            "MouseScrollUp",
            "MouseScrollDown",
            "MouseWheelAxis",
            "LeftMouseButton",
            "RightMouseButton",
            "MiddleMouseButton",
            "ThumbMouseButton",
            "ThumbMouseButton2",
            "BackSpace",
            "Tab",
            "Enter",
            "Pause",
            "CapsLock",
            "Escape",
            "SpaceBar",
            "PageUp",
            "PageDown",
            "End",
            "Home",
            "Left",
            "Up",
            "Right",
            "Down",
            "Insert",
            "Delete",
            "Zero",
            "One",
            "Two",
            "Three",
            "Four",
            "Five",
            "Six",
            "Seven",
            "Eight",
            "Nine",
            "A",
            "B",
            "C",
            "D",
            "E",
            "F",
            "G",
            "H",
            "I",
            "J",
            "K",
            "L",
            "M",
            "N",
            "O",
            "P",
            "Q",
            "R",
            "S",
            "T",
            "U",
            "V",
            "W",
            "X",
            "Y",
            "Z",
            "NumPadZero",
            "NumPadOne",
            "NumPadTwo",
            "NumPadThree",
            "NumPadFour",
            "NumPadFive",
            "NumPadSix",
            "NumPadSeven",
            "NumPadEight",
            "NumPadNine",
            "Multiply",
            "Add",
            "Subtract",
            "Decimal",
            "Divide",
            "F1",
            "F2",
            "F3",
            "F4",
            "F5",
            "F6",
            "F7",
            "F8",
            "F9",
            "F10",
            "F11",
            "F12",
            "NumLock",
            "ScrollLock",
            "LeftShift",
            "RightShift",
            "LeftControl",
            "RightControl",
            "LeftAlt",
            "RightAlt",
            "LeftCommand",
            "RightCommand",
            "Invalid",
            // Joystick axes
            "JS_X",
            "JS_Y",
            "JS_Z",
            "JS_Rx",
            "JS_Ry",
            "JS_Rz",
            "JS_VX",
            "JS_VY",
            "JS_VZ",
            "JS_VRx",
            "JS_VRy",
            "JS_VRz",
            "JS_AX",
            "JS_AY",
            "JS_AZ",
            "JS_ARx",
            "JS_ARy",
            "JS_ARz",
            "JS_FX",
            "JS_FY",
            "JS_FZ",
            "JS_FRx",
            "JS_FRy",
            "JS_FRz",
            "JS_Slider_00",
            "JS_Slider_01",
            "JS_VSlider_00",
            "JS_VSlider_01",
            "JS_ASlider_00",
            "JS_ASlider_01",
            "JS_FSlider_00",
            "JS_FSlider_01",
            "JS_POV_00",
            "JS_POV_01",
            "JS_POV_02",
            "JS_POV_03"
        };

        /// <summary>
        /// 动态键名格式正则表达式
        /// 支持: Axis_00-Axis_99, Button_00-Button_99, OAxis_00-OAxis_99
        /// </summary>
        private static readonly Regex DynamicKeyPattern = new Regex(
            @"^(Axis|Button|OAxis)_(\d{2})$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        /// <summary>
        /// 检查键名是否合法
        /// </summary>
        /// <param name="keyName">要检查的键名</param>
        /// <returns>是否合法</returns>
        public static bool IsValidKeyName(string? keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName))
                return false;

            // 检查是否是标准键名
            if (StandardKeyNames.Contains(keyName))
                return true;

            // 检查是否符合动态键名格式 (Axis_xx, Button_xx, OAxis_xx)
            var match = DynamicKeyPattern.Match(keyName);
            if (match.Success)
            {
                // 验证数字部分是否在有效范围内 (00-31 是预定义的，但允许更大的范围)
                if (int.TryParse(match.Groups[2].Value, out int index))
                {
                    return index >= 0 && index <= 99;
                }
            }

            return false;
        }

        /// <summary>
        /// 获取键名验证错误信息
        /// </summary>
        /// <param name="keyName">要检查的键名</param>
        /// <returns>错误信息，如果合法则返回 null</returns>
        public static string? GetValidationError(string? keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName))
                return "键名不能为空";

            if (!IsValidKeyName(keyName))
                return $"键名 \"{keyName}\" 不合法。\n"
                    + "合法格式: Axis_00~Axis_99, Button_00~Button_99, OAxis_00~OAxis_99\n"
                    + "或标准键名如: MouseX, JS_X, A, F1 等";

            return null;
        }

        /// <summary>
        /// 检查键名是否与现有列表中的键重复
        /// </summary>
        /// <param name="keyName">要检查的键名</param>
        /// <param name="existingKeys">现有键名列表</param>
        /// <param name="excludeKey">要排除的键名（编辑时排除自身）</param>
        /// <returns>是否重复</returns>
        public static bool IsDuplicateKeyName(
            string? keyName,
            IEnumerable<string?> existingKeys,
            string? excludeKey = null
        )
        {
            if (string.IsNullOrWhiteSpace(keyName))
                return false;

            return existingKeys
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Where(
                    k =>
                        excludeKey == null
                        || !string.Equals(k, excludeKey, StringComparison.OrdinalIgnoreCase)
                )
                .Any(k => string.Equals(k, keyName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 获取重复键名错误信息
        /// </summary>
        /// <param name="keyName">要检查的键名</param>
        /// <param name="existingKeys">现有键名列表</param>
        /// <param name="excludeKey">要排除的键名（编辑时排除自身）</param>
        /// <returns>错误信息，如果不重复则返回 null</returns>
        public static string? GetDuplicateError(
            string? keyName,
            IEnumerable<string?> existingKeys,
            string? excludeKey = null
        )
        {
            if (IsDuplicateKeyName(keyName, existingKeys, excludeKey))
                return $"键名 \"{keyName}\" 已存在，不允许重复";

            return null;
        }

        /// <summary>
        /// 完整验证键名（合法性 + 重复性）
        /// </summary>
        /// <param name="keyName">要检查的键名</param>
        /// <param name="existingKeys">现有键名列表</param>
        /// <param name="excludeKey">要排除的键名（编辑时排除自身）</param>
        /// <returns>错误信息，如果通过验证则返回 null</returns>
        public static string? Validate(
            string? keyName,
            IEnumerable<string?>? existingKeys = null,
            string? excludeKey = null
        )
        {
            // 先检查合法性
            var validationError = GetValidationError(keyName);
            if (validationError != null)
                return validationError;

            // 再检查重复性
            if (existingKeys != null)
            {
                var duplicateError = GetDuplicateError(keyName, existingKeys, excludeKey);
                if (duplicateError != null)
                    return duplicateError;
            }

            return null;
        }

        /// <summary>
        /// 生成一个不重复的默认键名
        /// </summary>
        /// <param name="prefix">前缀 (Axis, Button, OAxis)</param>
        /// <param name="existingKeys">现有键名列表</param>
        /// <returns>不重复的键名</returns>
        public static string GenerateUniqueKeyName(string prefix, IEnumerable<string?> existingKeys)
        {
            var existingSet = new HashSet<string>(
                existingKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k!),
                StringComparer.OrdinalIgnoreCase
            );

            for (int i = 0; i <= 99; i++)
            {
                var name = $"{prefix}_{i:D2}";
                if (!existingSet.Contains(name))
                    return name;
            }

            // 如果 00-99 都用完了，返回带时间戳的名称
            return $"{prefix}_{DateTime.Now:HHmmss}";
        }

        /// <summary>
        /// 获取所有合法的标准键名列表（用于自动补全等功能）
        /// </summary>
        /// <returns>标准键名列表</returns>
        public static IReadOnlyCollection<string> GetStandardKeyNames()
        {
            return StandardKeyNames;
        }

        /// <summary>
        /// 获取常用的动态键名列表 (Axis_00-31, Button_00-31, OAxis_00-31)
        /// </summary>
        /// <returns>动态键名列表</returns>
        public static IEnumerable<string> GetCommonDynamicKeyNames()
        {
            for (int i = 0; i <= 31; i++)
            {
                yield return $"Axis_{i:D2}";
            }
            for (int i = 0; i <= 31; i++)
            {
                yield return $"Button_{i:D2}";
            }
            for (int i = 0; i <= 31; i++)
            {
                yield return $"OAxis_{i:D2}";
            }
        }
    }
}
