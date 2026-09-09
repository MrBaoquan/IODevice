using System.Text.RegularExpressions;

namespace IOToolkit.Extension
{
    /// <summary>
    /// <see cref="IOToolkit.Key"/> 的通用辅助扩展.
    /// 从 Unity 包迁入 Wrapper, 以便 IOStudio / CinemaControl / 非 Unity 环境共用.
    /// </summary>
    public static class KeyExtensions
    {
        /// <summary>
        /// 从形如 "OAxis_240" / "Button_01" 的 Key 文本里抽出末尾的整数下标.
        /// 解析失败返回 -1.
        /// </summary>
        public static int GetIntValue(this Key key)
        {
            var _numberPart = Regex.Replace(key.ToString(), @"^[A-Za-z]+_", string.Empty);
            if (!Regex.IsMatch(_numberPart, @"^\d+$"))
                return -1;
            if (!int.TryParse(_numberPart, out int _value))
                return -1;
            return _value;
        }
    }
}
