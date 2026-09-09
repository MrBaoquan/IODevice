using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace IOStudio.Services
{
    /// <summary>
    /// 对话框服务接口 — 抽象文件选择/确认/输入等对话框操作,
    /// 使 ViewModel 可测试且不依赖 View 层的 TopLevel/Window 引用。
    /// </summary>
    public interface IDialogService
    {
        /// <summary>打开文件选择对话框。</summary>
        /// <param name="title">对话框标题。</param>
        /// <param name="filters">文件过滤器 (如 "Motion Files|*.motion")。</param>
        /// <returns>选中的文件路径, 取消返回 null。</returns>
        Task<string?> OpenFileAsync(string title, IReadOnlyList<DialogFileFilter>? filters = null);

        /// <summary>保存文件对话框。</summary>
        /// <param name="title">对话框标题。</param>
        /// <param name="defaultFileName">默认文件名。</param>
        /// <param name="filters">文件过滤器。</param>
        /// <returns>选中的保存路径, 取消返回 null。</returns>
        Task<string?> SaveFileAsync(
            string title,
            string? defaultFileName = null,
            IReadOnlyList<DialogFileFilter>? filters = null
        );

        /// <summary>确认对话框 (是/否)。</summary>
        /// <param name="title">标题。</param>
        /// <param name="message">消息内容。</param>
        /// <returns>用户是否确认。</returns>
        Task<bool> ConfirmAsync(string title, string message);

        /// <summary>输入对话框。</summary>
        /// <param name="title">标题。</param>
        /// <param name="prompt">提示文字。</param>
        /// <param name="defaultValue">默认值。</param>
        /// <returns>用户输入, 取消返回 null。</returns>
        Task<string?> InputAsync(string title, string prompt, string? defaultValue = null);

        /// <summary>显示模态子窗口。</summary>
        Task ShowDialogAsync(Window dialog);
    }

    /// <summary>对话框文件过滤器。</summary>
    public class DialogFileFilter
    {
        public string Name { get; set; } = "";
        public IReadOnlyList<string> Extensions { get; set; } = new List<string>();
    }
}
