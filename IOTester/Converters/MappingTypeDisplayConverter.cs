using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace IOTester.Converters
{
    /// <summary>
    /// 映射类型显示转换器，在英文存储值和中文显示值之间转换
    /// </summary>
    public class MappingTypeDisplayConverter : IValueConverter
    {
        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture
        )
        {
            if (value is string type)
            {
                return type switch
                {
                    "Analog" => "模拟量",
                    "Digital" => "开关量",
                    _ => value
                };
            }
            return value;
        }

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture
        )
        {
            if (value is string displayText)
            {
                return displayText switch
                {
                    "模拟量" => "Analog",
                    "开关量" => "Digital",
                    _ => value
                };
            }
            return value;
        }
    }
}
