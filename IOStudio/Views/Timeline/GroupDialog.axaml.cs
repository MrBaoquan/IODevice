using System;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using IOStudio.ViewModels.Timeline;

namespace IOStudio.Views.Timeline
{
    /// <summary>
    /// 分组管理对话框 — 新建/重命名分组, 可选轨道多选。
    /// code-behind 仅处理窗口关闭与焦点设置。
    /// </summary>
    public partial class GroupDialog : Window
    {
        public GroupDialog()
        {
            InitializeComponent();
        }

        public GroupDialog(GroupDialogViewModel vm)
            : this()
        {
            DataContext = vm;
            vm.CloseRequested += () => Close();
        }

        protected override void OnOpened(System.EventArgs e)
        {
            base.OnOpened(e);

            // 自动聚焦名称输入框并全选
            var nameBox = this.FindControl<TextBox>("GroupNameBox");
            if (nameBox is not null)
            {
                nameBox.Focus();
                nameBox.SelectAll();
            }
        }

        /// <summary>Enter 键确认, Escape 取消。</summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (DataContext is not GroupDialogViewModel vm)
                return;

            if (e.Key == Key.Enter && vm.CanConfirm)
            {
                vm.ConfirmCommand.Execute().Subscribe();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                vm.CancelCommand.Execute().Subscribe();
                e.Handled = true;
            }
        }
    }
}
