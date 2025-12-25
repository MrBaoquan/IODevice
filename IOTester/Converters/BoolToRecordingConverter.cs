using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace IOTester.Converters
{
    /// <summary>
    /// 录制状态到背景色转换器
    /// </summary>
    public class BoolToRecordingBgConverter : IValueConverter
    {
        public static readonly BoolToRecordingBgConverter Instance =
            new BoolToRecordingBgConverter();

        public object Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture
        )
        {
            if (value is bool isRecording && isRecording)
            {
                return new SolidColorBrush(Color.Parse("#fef3c7")); // 录制中：浅黄色背景
            }
            return Brushes.Transparent; // 未录制：透明背景
        }

        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture
        )
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 录制状态到边框色转换器
    /// </summary>
    public class BoolToRecordingBorderConverter : IValueConverter
    {
        public static readonly BoolToRecordingBorderConverter Instance =
            new BoolToRecordingBorderConverter();

        public object Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture
        )
        {
            if (value is bool isRecording && isRecording)
            {
                return new SolidColorBrush(Color.Parse("#fbbf24")); // 录制中：黄色边框
            }
            return new SolidColorBrush(Color.Parse("#9ca3af")); // 未录制：灰色边框
        }

        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture
        )
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 录制状态到前景色转换器
    /// </summary>
    public class BoolToRecordingFgConverter : IValueConverter
    {
        public static readonly BoolToRecordingFgConverter Instance =
            new BoolToRecordingFgConverter();

        public object Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture
        )
        {
            if (value is bool isRecording && isRecording)
            {
                return new SolidColorBrush(Color.Parse("#92400e")); // 录制中：深棕色文字
            }
            return new SolidColorBrush(Color.Parse("#6b7280")); // 未录制：灰色文字
        }

        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture
        )
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 录制状态到图标颜色转换器
    /// </summary>
    public class BoolToRecordingIconConverter : IValueConverter
    {
        public static readonly BoolToRecordingIconConverter Instance =
            new BoolToRecordingIconConverter();

        public object Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture
        )
        {
            if (value is bool isRecording && isRecording)
            {
                return new SolidColorBrush(Color.Parse("#dc2626")); // 录制中：红色实心圆
            }
            return Brushes.Transparent; // 未录制时不显示
        }

        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture
        )
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 录制状态到文本转换器
    /// </summary>
    public class BoolToRecordingTextConverter : IValueConverter
    {
        public static readonly BoolToRecordingTextConverter Instance =
            new BoolToRecordingTextConverter();

        public object Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture
        )
        {
            if (value is bool isRecording && isRecording)
            {
                return "停止录制";
            }
            return "录制";
        }

        public object ConvertBack(
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
