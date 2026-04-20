using System;
using System.Reactive.Disposables;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using IOStudio.Models.Motion;
using IOStudio.ViewModels.Timeline;
using ReactiveUI;

namespace IOStudio.Views.Timeline
{
    /// <summary>
    /// 添加轨道对话框 — ViewModel 驱动, code-behind 仅处理窗口关闭和颜色选取视觉反馈。
    /// </summary>
    public partial class AddTrackDialog : Window, IViewFor<AddTrackDialogViewModel>
    {
        /// <summary>用户确认后的结果 (由 ViewModel 构建)。</summary>
        public AddTrackResult? Result => ViewModel?.Result;

        /// <inheritdoc/>
        object? IViewFor.ViewModel
        {
            get => ViewModel;
            set => ViewModel = value as AddTrackDialogViewModel;
        }

        /// <inheritdoc/>
        public AddTrackDialogViewModel? ViewModel
        {
            get => DataContext as AddTrackDialogViewModel;
            set => DataContext = value;
        }

        public AddTrackDialog()
        {
            InitializeComponent();
            ViewModel = new AddTrackDialogViewModel();

            // 订阅命令结果以关闭对话框
            ViewModel.ConfirmCommand.Subscribe(_ => Close());
            ViewModel.CancelCommand.Subscribe(_ => Close());

            // 订阅颜色变化以更新视觉预览
            ViewModel
                .WhenAnyValue(x => x.SelectedColor)
                .Subscribe(color =>
                {
                    if (!string.IsNullOrEmpty(color))
                    {
                        SelectedColorPreview.Background = new SolidColorBrush(Color.Parse(color));
                        SelectedColorText.Text = color;
                    }
                });
        }

        /// <summary>颜色面板点击 — 更新 ViewModel 的 SelectedColor 属性。</summary>
        private void OnColorPick(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && border.Tag is string color && ViewModel is not null)
            {
                ViewModel.SelectedColor = color;
            }
        }
    }
}
