using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace IOTester.Converters
{
    /// <summary>
    /// 将 Min/Max 值转换为显示字符串
    /// 当值为无限制时显示"无限制"，否则显示具体数值
    /// </summary>
    public class MinMaxDisplayConverter : IValueConverter
    {
        // 无限制阈值（接近 FLT_MAX）
        private const float UnlimitedThreshold = 3.40282e+38f;

        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture
        )
        {
            if (value is float floatValue)
            {
                bool isMax = parameter?.ToString() == "max";

                if (isMax)
                {
                    // 对于 Max，检查是否接近 FLT_MAX
                    if (floatValue >= UnlimitedThreshold)
                        return "无限制";
                }
                else
                {
                    // 对于 Min，检查是否接近 -FLT_MAX
                    if (floatValue <= -UnlimitedThreshold)
                        return "无限制";
                }

                // 显示具体数值，保留2位小数
                return floatValue.ToString("F2", culture);
            }

            return value?.ToString();
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
