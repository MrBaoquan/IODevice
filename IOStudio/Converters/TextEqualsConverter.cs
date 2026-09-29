using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace IOStudio.Converters
{
    /// <summary>
    /// 单值相等转换器 — 绑定值与 ConverterParameter 文本相等时返回 true。
    /// <para>与 <c>BoolToColorConverter.cs</c> 中的多值版 <c>StringEqualsConverter</c> 区分:
    /// 本类用于"单个绑定 + ConverterParameter"的场合, 例如
    /// <c>Classes.selected="{Binding Color, Converter={StaticResource TextEquals}, ConverterParameter=#22c55e}"</c>。</para>
    /// </summary>
    public class TextEqualsConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is null || parameter is null)
                return false;
            return string.Equals(
                value.ToString()?.Trim(),
                parameter.ToString()?.Trim(),
                StringComparison.OrdinalIgnoreCase
            );
        }

        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture
        ) => throw new NotSupportedException();
    }
}
