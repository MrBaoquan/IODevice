using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace IOTester.Converters
{
    /// <summary>
    /// Bool转颜色转换器：启用=绿色，禁用=灰色
    /// </summary>
    public class BoolToColorConverter : IValueConverter
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
                return boolValue
                    ? new SolidColorBrush(Color.Parse("#10b981"))
                    : new SolidColorBrush(Color.Parse("#9ca3af"));
            }
            return new SolidColorBrush(Color.Parse("#9ca3af"));
        }

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture
        )
        {
            // 不支持反向转换，返回默认值
            return false;
        }
    }

    /// <summary>
    /// Bool转文本转换器：启用/禁用
    /// </summary>
    public class BoolToTextConverter : IValueConverter
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
                return boolValue ? "启用" : "禁用";
            }
            return "禁用";
        }

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture
        )
        {
            // 不支持反向转换，返回默认值
            return false;
        }
    }

    /// <summary>
    /// 字符串相等比较转换器（MultiValueConverter版本）
    /// 用于MultiBinding，比较第一个值和第二个值是否相等
    /// </summary>
    public class StringEqualsConverter : IMultiValueConverter
    {
        public object? Convert(
            System.Collections.Generic.IList<object?> values,
            Type targetType,
            object? parameter,
            CultureInfo culture
        )
        {
            if (values == null || values.Count < 2)
                return false;

            var value1 = values[0]?.ToString();
            var value2 = values[1]?.ToString();

            return value1 == value2;
        }

        public object?[] ConvertBack(
            object? value,
            Type[] targetTypes,
            object? parameter,
            CultureInfo culture
        )
        {
            throw new NotImplementedException();
        }
    }
}
