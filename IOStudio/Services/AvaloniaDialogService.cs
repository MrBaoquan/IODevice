using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace IOStudio.Services
{
    /// <summary>
    /// Avalonia 对话框服务实现 — 通过 TopLevel/StorageProvider 执行文件选择、确认等操作。
    /// </summary>
    public class AvaloniaDialogService : IDialogService
    {
        private Window? GetMainWindow()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                return desktop.MainWindow;
            return null;
        }

        /// <inheritdoc/>
        public async Task<string?> OpenFileAsync(string title, IReadOnlyList<DialogFileFilter>? filters = null)
        {
            var window = GetMainWindow();
            if (window is null) return null;

            var storageProvider = window.StorageProvider;
            var avaloniaFilters = ConvertFilters(filters);

            var result = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = avaloniaFilters
            });

            return result.FirstOrDefault()?.Path.LocalPath;
        }

        /// <inheritdoc/>
        public async Task<string?> SaveFileAsync(string title, string? defaultFileName = null,
            IReadOnlyList<DialogFileFilter>? filters = null)
        {
            var window = GetMainWindow();
            if (window is null) return null;

            var storageProvider = window.StorageProvider;
            var avaloniaFilters = ConvertFilters(filters);

            var result = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = defaultFileName,
                FileTypeChoices = avaloniaFilters
            });

            return result?.Path.LocalPath;
        }

        /// <inheritdoc/>
        public async Task<bool> ConfirmAsync(string title, string message)
        {
            var box = MessageBoxManager
                .GetMessageBoxStandard(title, message, ButtonEnum.YesNo);
            var result = await box.ShowAsync();
            return result == ButtonResult.Yes;
        }

        /// <inheritdoc/>
        public async Task<string?> InputAsync(string title, string prompt, string? defaultValue = null)
        {
            // 使用简单 Window 作为输入对话框
            var dialog = new Window
            {
                Title = title,
                Width = 400,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            string? result = null;
            var textBox = new TextBox
            {
                Text = defaultValue ?? "",
                Watermark = prompt,
                Margin = new Thickness(20, 10),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };

            var okButton = new Button { Content = "确定", Margin = new Thickness(5) };
            var cancelButton = new Button { Content = "取消", Margin = new Thickness(5) };

            okButton.Click += (_, _) => { result = textBox.Text; dialog.Close(); };
            cancelButton.Click += (_, _) => { dialog.Close(); };

            var buttons = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Margin = new Thickness(20, 5),
                Spacing = 10,
                Children = { cancelButton, okButton }
            };

            dialog.Content = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = prompt,
                        Margin = new Thickness(20, 15, 20, 0),
                        FontSize = 13
                    },
                    textBox,
                    buttons
                }
            };

            var mainWindow = GetMainWindow();
            if (mainWindow is not null)
                await dialog.ShowDialog(mainWindow);

            return result;
        }

        /// <inheritdoc/>
        public async Task ShowDialogAsync(Window dialog)
        {
            var mainWindow = GetMainWindow();
            if (mainWindow is not null)
                await dialog.ShowDialog(mainWindow);
        }

        private static List<FilePickerFileType>? ConvertFilters(IReadOnlyList<DialogFileFilter>? filters)
        {
            if (filters is null || filters.Count == 0)
                return null;

            return filters.Select(f => new FilePickerFileType(f.Name)
            {
                Patterns = f.Extensions.Select(ext => $"*.{ext}").ToList()
            }).ToList();
        }
    }
}
