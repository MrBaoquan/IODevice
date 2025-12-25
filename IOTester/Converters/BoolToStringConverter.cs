using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace IOTester.Converters
{
    /// <summary>
    /// 布尔值转字符串转换器
    /// 参数格式: "TrueString|FalseString"
    /// </summary>
    public class BoolToStringConverter : IValueConverter
    {
        public static readonly BoolToStringConverter Instance = new BoolToStringConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not bool boolValue || parameter is not string paramStr)
            {
                return string.Empty;
            }

            var parts = paramStr.Split('|');
            if (parts.Length != 2)
            {
                return string.Empty;
            }

            return boolValue ? parts[0] : parts[1];
        }

        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture
        )
        {
            throw new NotImplementedException();
        }
    }
}
