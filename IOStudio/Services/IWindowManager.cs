using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace IOStudio.Services
{
    /// <summary>
    /// 工具窗口管理器 — 负责"单例 + 聚焦"的工具窗口生命周期。
    /// <para>
    /// 目标: 消除 VM 中散落的 `new XxxWindow().Show()` 多开问题;
    /// 菜单栏"窗口(_W)"通过 <see cref="WindowStateChanged"/> 事件驱动复选态。
    /// </para>
    /// </summary>
    public interface IWindowManager
    {
        /// <summary>每类工具窗口记录已注册的窗口类型。</summary>
        IReadOnlyCollection<Type> RegisteredWindowTypes { get; }

        /// <summary>窗口打开/关闭状态变化, 参数: (窗口类型, 是否打开)。供窗口菜单复选态刷新。</summary>
        event Action<Type, bool>? WindowStateChanged;

        /// <summary>
        /// 打开指定类型的工具窗口 — 已打开则聚焦(Activate), 未打开则创建并 Show。
        /// </summary>
        /// <typeparam name="TWindow">工具窗口类型 (需已注册)。</typeparam>
        /// <param name="factory">窗口工厂 (首次创建时调用)。</param>
        void ShowOrActivate<TWindow>(Func<Window> factory)
            where TWindow : Window;

        /// <summary>查询指定类型窗口是否已打开。</summary>
        bool IsOpen<TWindow>()
            where TWindow : Window;

        /// <summary>尝试获取已打开的窗口实例 (未打开返回 null)。</summary>
        TWindow? GetOpenWindow<TWindow>()
            where TWindow : Window;
    }

    /// <summary>
    /// Avalonia 实现 — 基于 <see cref="IClassicDesktopStyleApplicationLifetime"/> 的工具窗口单例管理。
    /// Enclosed windows kept strongly; Closed event removes from tracking.
    /// </summary>
    public class AvaloniaWindowManager : IWindowManager
    {
        private readonly Dictionary<Type, Window> _windows = new();

        public event Action<Type, bool>? WindowStateChanged;

        public IReadOnlyCollection<Type> RegisteredWindowTypes => _windows.Keys;

        public void ShowOrActivate<TWindow>(Func<Window> factory)
            where TWindow : Window
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            var existing = GetOpenWindow<TWindow>();
            if (existing != null)
            {
                existing.Activate();
                return;
            }

            var window = factory();
            if (window == null)
                return;

            _windows[typeof(TWindow)] = window;

            window.Closed += (_, _) =>
            {
                _windows.Remove(typeof(TWindow));
                WindowStateChanged?.Invoke(typeof(TWindow), false);
            };

            window.Show();
            WindowStateChanged?.Invoke(typeof(TWindow), true);
        }

        public bool IsOpen<TWindow>()
            where TWindow : Window => GetOpenWindow<TWindow>() != null;

        public TWindow? GetOpenWindow<TWindow>()
            where TWindow : Window
        {
            if (_windows.TryGetValue(typeof(TWindow), out var w) && w != null)
                return (TWindow)w;
            return null;
        }

        /// <summary>关闭某类型的工具窗口 (若已打开)。供程序退出/重置时调用。</summary>
        public void Close<TWindow>()
            where TWindow : Window
        {
            var w = GetOpenWindow<TWindow>();
            w?.Close();
        }
    }
}
