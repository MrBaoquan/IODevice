using System;
using System.Collections.Generic;

namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 通用撤销/重做服务 — Command 模式
    /// 支持关键帧增删改、轨道增删、事件增删等操作的撤销和重做
    /// </summary>
    public class UndoRedoService : IUndoRedoService
    {
        // 使用 List 模拟栈 (尾部 Add/RemoveAt), 以支持超出上限时丢弃最旧命令。
        private readonly List<IUndoableCommand> _undoStack = new();
        private readonly List<IUndoableCommand> _redoStack = new();
        private const int MaxUndoLevels = 100;

        /// <summary>当 Undo/Redo 栈状态变化时触发</summary>
        public event Action? StateChanged;

        /// <summary>是否可以撤销</summary>
        public bool CanUndo => _undoStack.Count > 0;

        /// <summary>是否可以重做</summary>
        public bool CanRedo => _redoStack.Count > 0;

        /// <summary>撤销栈描述 (用于 UI 提示)</summary>
        public string? UndoDescription => _undoStack.Count > 0 ? _undoStack[^1].Description : null;

        /// <summary>重做栈描述 (用于 UI 提示)</summary>
        public string? RedoDescription => _redoStack.Count > 0 ? _redoStack[^1].Description : null;

        /// <summary>压入撤销栈, 超出上限时丢弃最旧命令以限制内存。</summary>
        private void PushUndo(IUndoableCommand command)
        {
            _undoStack.Add(command);
            if (_undoStack.Count > MaxUndoLevels)
                _undoStack.RemoveAt(0);
        }

        /// <summary>
        /// 执行命令并压入撤销栈
        /// </summary>
        public void Execute(IUndoableCommand command)
        {
            command.Execute();
            PushUndo(command);
            _redoStack.Clear(); // 新操作清除重做栈
            StateChanged?.Invoke();
        }

        /// <summary>
        /// 撤销上一个操作
        /// </summary>
        public void Undo()
        {
            if (_undoStack.Count == 0)
                return;

            var command = _undoStack[^1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            command.Undo();
            _redoStack.Add(command);
            StateChanged?.Invoke();
        }

        /// <summary>
        /// 重做上一个撤销的操作
        /// </summary>
        public void Redo()
        {
            if (_redoStack.Count == 0)
                return;

            var command = _redoStack[^1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            command.Execute();
            PushUndo(command);
            StateChanged?.Invoke();
        }

        /// <summary>
        /// 清空所有撤销/重做历史
        /// </summary>
        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            StateChanged?.Invoke();
        }
    }

    /// <summary>
    /// 可撤销的命令接口
    /// </summary>
    public interface IUndoableCommand
    {
        /// <summary>操作描述 (用于 UI 提示, 如 "添加关键帧")</summary>
        string Description { get; }

        /// <summary>执行操作</summary>
        void Execute();

        /// <summary>撤销操作</summary>
        void Undo();
    }

    /// <summary>
    /// 通用 Lambda 命令 — 简单操作可直接用 Action 实现
    /// </summary>
    public class LambdaCommand : IUndoableCommand
    {
        private readonly Action _execute;
        private readonly Action _undo;

        public string Description { get; }

        public LambdaCommand(string description, Action execute, Action undo)
        {
            Description = description;
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _undo = undo ?? throw new ArgumentNullException(nameof(undo));
        }

        public void Execute() => _execute();

        public void Undo() => _undo();
    }

    /// <summary>
    /// 批量命令 — 将多个命令组合为一个可撤销的操作
    /// </summary>
    public class BatchCommand : IUndoableCommand
    {
        private readonly List<IUndoableCommand> _commands;

        public string Description { get; }

        public BatchCommand(string description, List<IUndoableCommand> commands)
        {
            Description = description;
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        }

        public void Execute()
        {
            foreach (var cmd in _commands)
                cmd.Execute();
        }

        public void Undo()
        {
            // 逆序撤销
            for (int i = _commands.Count - 1; i >= 0; i--)
                _commands[i].Undo();
        }
    }
}
