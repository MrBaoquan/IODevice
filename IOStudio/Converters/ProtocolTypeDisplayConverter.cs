using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace IOStudio.Converters
{
    /// <summary>
    /// 协议类型显示转换器，将英文协议类型转换为中文显示
    /// </summary>
    public class ProtocolTypeDisplayConverter : IValueConverter
    {
        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture
        )
        {
            if (value is string protocolType)
            {
                return protocolType switch
                {
                    "NetIO" => "NetIO",
                    "Modbus-RTU" => "Modbus-RTU",
                    "Custom" => "自定义协议",
                    "DirectOutput" => "设备直出",
                    _ => protocolType
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
            // 不需要反向转换
            throw new NotImplementedException();
        }
    }
}
