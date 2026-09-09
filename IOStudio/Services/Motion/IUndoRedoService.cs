using System;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 可撤销/重做服务接口
    /// 支持命令模式的操作历史管理
    /// </summary>
    public interface IUndoRedoService
    {
        /// <summary>是否可以撤销</summary>
        bool CanUndo { get; }

        /// <summary>是否可以重做</summary>
        bool CanRedo { get; }

        /// <summary>撤销栈描述 (用于 UI 提示)</summary>
        string? UndoDescription { get; }

        /// <summary>重做栈描述 (用于 UI 提示)</summary>
        string? RedoDescription { get; }

        /// <summary>当 Undo/Redo 栈状态变化时触发</summary>
        event Action? StateChanged;

        /// <summary>执行命令并压入撤销栈</summary>
        void Execute(IUndoableCommand command);

        /// <summary>撤销上一个操作</summary>
        void Undo();

        /// <summary>重做上一个撤销的操作</summary>
        void Redo();

        /// <summary>清空所有撤销/重做历史</summary>
        void Clear();
    }
}
