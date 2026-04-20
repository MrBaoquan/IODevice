using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;

namespace IOStudio.Views.Timeline
{
    public partial class ExportDialog : Window
    {
        private MotionTimeline? _timeline;

        public ExportDialog()
        {
            InitializeComponent();
        }

        public ExportDialog(MotionTimeline timeline)
            : this()
        {
            _timeline = timeline;
            UpdatePreview();
        }

        private MotionExportService.ExportFormat SelectedFormat
        {
            get
            {
                var combo = this.FindControl<ComboBox>("FormatComboBox");
                return combo?.SelectedIndex switch
                {
                    0 => MotionExportService.ExportFormat.MotionJson,
                    1 => MotionExportService.ExportFormat.Csv,
                    2 => MotionExportService.ExportFormat.CompactJson,
                    _ => MotionExportService.ExportFormat.MotionJson
                };
            }
        }

        private void OnFormatChanged(object? sender, SelectionChangedEventArgs e)
        {
            var panel = this.FindControl<Border>("SampleSettingsPanel");
            if (panel != null)
            {
                bool needsSampling = SelectedFormat != MotionExportService.ExportFormat.MotionJson;
                panel.IsVisible = needsSampling;
            }
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            var info = this.FindControl<TextBlock>("PreviewInfo");
            if (info == null || _timeline == null)
                return;

            int trackCount = _timeline.Tracks.Count;
            int mutedCount = _timeline.Tracks.Count(t => t.Muted);
            var excludeMuted = this.FindControl<CheckBox>("ExcludeMutedCheck");
            bool exclude = excludeMuted?.IsChecked == true;
            int exportTrackCount = exclude ? trackCount - mutedCount : trackCount;

            string formatName = SelectedFormat switch
            {
                MotionExportService.ExportFormat.MotionJson => "Motion JSON",
                MotionExportService.ExportFormat.Csv => "CSV 逐帧数据",
                MotionExportService.ExportFormat.CompactJson => "精简 JSON",
                _ => "未知"
            };

            info.Text =
                $"格式: {formatName} | 导出轨道: {exportTrackCount}/{trackCount} | "
                + $"时长: {_timeline.DurationMs:F0}ms";
        }

        private MotionExportService.ExportOptions BuildOptions()
        {
            var startMs = this.FindControl<NumericUpDown>("StartMsInput");
            var endMs = this.FindControl<NumericUpDown>("EndMsInput");
            var sampleRate = this.FindControl<NumericUpDown>("SampleRateInput");
            var precision = this.FindControl<NumericUpDown>("PrecisionInput");
            var excludeMuted = this.FindControl<CheckBox>("ExcludeMutedCheck");
            var includeHeader = this.FindControl<CheckBox>("IncludeHeaderCheck");

            return new MotionExportService.ExportOptions
            {
                Format = SelectedFormat,
                SampleRateHz = (int)(sampleRate?.Value ?? 60m),
                StartMs = (double)(startMs?.Value ?? 0m),
                EndMs = (double)(endMs?.Value ?? 0m),
                ValuePrecision = (int)(precision?.Value ?? 4m),
                ExcludeMutedTracks = excludeMuted?.IsChecked == true,
                IncludeHeader = includeHeader?.IsChecked == true
            };
        }

        private async void OnExport(object? sender, RoutedEventArgs e)
        {
            if (_timeline == null)
                return;

            var options = BuildOptions();
            string ext = MotionExportService.GetFileExtension(options.Format);

            var file = await StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "导出 Motion 时间轴",
                    DefaultExtension = ext.TrimStart('.'),
                    SuggestedFileName = (_timeline.Name ?? "untitled") + ext,
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType(GetFormatDisplayName(options.Format))
                        {
                            Patterns = new[] { "*" + ext }
                        }
                    }
                }
            );

            if (file == null)
                return;

            string filePath = file.Path.LocalPath;
            var result = MotionExportService.Export(_timeline, filePath, options);

            if (result.Success)
            {
                Close(filePath);
            }
            else
            {
                var info = this.FindControl<TextBlock>("PreviewInfo");
                if (info != null)
                    info.Text = $"❌ 导出失败: {result.Error}";
            }
        }

        private void OnCancel(object? sender, RoutedEventArgs e)
        {
            Close(null);
        }

        private static string GetFormatDisplayName(MotionExportService.ExportFormat format) =>
            format switch
            {
                MotionExportService.ExportFormat.MotionJson => "Motion JSON 文件",
                MotionExportService.ExportFormat.Csv => "CSV 文件",
                MotionExportService.ExportFormat.CompactJson => "JSON 文件",
                _ => "所有文件"
            };
    }
}
