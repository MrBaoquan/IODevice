using System;
using Microsoft.Extensions.DependencyInjection;

namespace IOStudio.Services
{
    /// <summary>
    /// 全局服务定位器 — 提供对 DI 容器的访问。
    /// 在 App.OnFrameworkInitializationCompleted 中通过 <see cref="Configure"/> 初始化。
    /// </summary>
    /// <remarks>
    /// 推荐优先使用构造函数注入; 仅在无法注入的场景 (如 View code-behind、ValueConverter)
    /// 才通过 <see cref="Current"/> 获取服务。
    /// </remarks>
    public static class ServiceLocator
    {
        private static IServiceProvider? _provider;

        /// <summary>当前 DI 容器。在 <see cref="Configure"/> 之前访问将抛出异常。</summary>
        public static IServiceProvider Current =>
            _provider
            ?? throw new InvalidOperationException(
                "ServiceLocator 尚未初始化, 请先调用 ServiceLocator.Configure()。"
            );

        /// <summary>初始化 DI 容器 (仅在应用启动时调用一次)。</summary>
        public static void Configure(IServiceProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        /// <summary>获取指定类型的服务实例。</summary>
        public static T Resolve<T>()
            where T : notnull => Current.GetRequiredService<T>();

        /// <summary>尝试获取指定类型的服务实例 (不存在时返回 null)。</summary>
        public static T? TryResolve<T>()
            where T : class => Current.GetService<T>();

        /// <summary>
        /// 重置容器 (仅用于测试)。
        /// </summary>
        internal static void Reset() => _provider = null;
    }
}
