using Avalonia.Controls;
using Avalonia.Controls.Templates;
using IOStudio.Services;
using IOStudio.ViewModels;
using System;

namespace IOStudio
{
    public class ViewLocator : IDataTemplate
    {
        public Control Build(object data)
        {
            var name = data.GetType().FullName!.Replace("ViewModel", "View");
            var type = Type.GetType(name);

            if (type is not null)
            {
                return (Control)Activator.CreateInstance(type)!;
            }

            return new TextBlock { Text = "Not Found: " + name };
        }

        public bool Match(object? data)
        {
            return data is ViewModelBase;
        }

        /// <summary>
        /// 通过 DI 容器创建 ViewModel 实例 (优先), 回退到无参构造。
        /// </summary>
        public static T CreateViewModel<T>()
            where T : ViewModelBase
        {
            return ServiceLocator.TryResolve<T>() ?? Activator.CreateInstance<T>();
        }
    }
}
