using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace IOStudio.Converters
{
    /// <summary>
    /// Bool转展开图标转换器：True=▼, False=▶
    /// </summary>
    public class BoolToExpandIconConverter : IValueConverter
    {
        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture
        )
        {
            if (value is bool boolValue)
            {
                return boolValue ? "▼" : "▶";
            }
            return "▶";
        }

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture
        )
        {
            throw new NotImplementedException();
        }
    }
}
