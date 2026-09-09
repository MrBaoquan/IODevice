using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Threading;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;
using ReactiveUI;

namespace IOStudio.ViewModels.Timeline
{
    public partial class TimelineEditorViewModel
    {
        // ---- 文件操作 ----

        /// <summary>标记文件已修改 (dirty)</summary>
        public void MarkDirty()
        {
            IsDirty = true;
        }

        /// <summary>执行可撤销的命令</summary>
        public void ExecuteCommand(IUndoableCommand command)
        {
            _undoRedo.Execute(command);
            MarkDirty();
            PushStatus(command.Description);
        }

        /// <summary>撤销</summary>
        public void Undo()
        {
            if (!_undoRedo.CanUndo)
                return;
            var desc = _undoRedo.UndoDescription;
            _undoRedo.Undo();
            MarkDirty();
            PushStatus($"已撤销: {desc}");
        }

        /// <summary>重做</summary>
        public void Redo()
        {
            if (!_undoRedo.CanRedo)
                return;
            var desc = _undoRedo.RedoDescription;
            _undoRedo.Redo();
            MarkDirty();
            PushStatus($"已重做: {desc}");
        }

        /// <summary>更新窗口标题 (含 dirty 标记)</summary>
        private void UpdateWindowTitle()
        {
            string name = Timeline?.Name ?? "新建项目";
            if (!string.IsNullOrEmpty(CurrentFilePath))
                name = System.IO.Path.GetFileNameWithoutExtension(CurrentFilePath);
            string dirty = IsDirty ? " *" : "";
            WindowTitle = $"动作编排 — {name}{dirty}";
        }

        /// <summary>启动自动保存定时器</summary>
        private void StartAutoSave()
        {
            StopAutoSave();
            _autoSaveSubscription = Observable
                .Interval(TimeSpan.FromSeconds(AutoSaveIntervalSeconds))
                .Where(_ => IsDirty && !string.IsNullOrEmpty(CurrentFilePath))
                .Subscribe(_ =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (IsDirty && Timeline != null && !string.IsNullOrEmpty(CurrentFilePath))
                        {
                            if (MotionFileReader.Write(Timeline, CurrentFilePath))
                            {
                                IsDirty = false;
                                PushStatus("已自动保存");
                            }
                        }
                    });
                });
        }

        /// <summary>停止自动保存定时器</summary>
        private void StopAutoSave()
        {
            _autoSaveSubscription?.Dispose();
            _autoSaveSubscription = null;
        }

        private void NewProject()
        {
            var timeline = new MotionTimeline
            {
                Name = "新建项目",
                DurationMs = 30000, // 默认 30 秒
                Fps = 60,
            };

            LoadTimeline(timeline);
            CurrentFilePath = "";
            IsDirty = false;
            _undoRedo.Clear();
            UpdateWindowTitle();
            StartAutoSave();
        }

        private void OpenFile()
        {
            if (_dialogService is not null)
            {
                _ = OpenFileWithDialogAsync();
                return;
            }
            // 后备: 通知 View 层打开对话框
            OpenFileRequested = true;
            OpenFileRequested = false;
        }

        /// <summary>通过 IDialogService 打开文件 (无需 View 层参与)。</summary>
        private async System.Threading.Tasks.Task OpenFileWithDialogAsync()
        {
            var path = await _dialogService!.OpenFileAsync("打开动作文件", MotionFileFilters);
            if (!string.IsNullOrEmpty(path))
                LoadFromFile(path);
        }

        /// <summary>
        /// 从文件路径加载 (由 View 层调用)
        /// </summary>
        public void LoadFromFile(string filePath)
        {
            var timeline = MotionFileReader.Read(filePath);
            if (timeline == null)
                return;

            LoadTimeline(timeline);
            CurrentFilePath = filePath;
            IsDirty = false;
            _undoRedo.Clear();
            UpdateWindowTitle();
            StartAutoSave();
        }

        private void SaveFile()
        {
            _ = SaveAsync();
        }

        /// <summary>
        /// 保存当前项目并返回是否真正写入成功。
        /// 关闭窗口等数据安全路径必须等待此方法，不能仅触发保存命令。
        /// </summary>
        public async System.Threading.Tasks.Task<bool> SaveAsync()
        {
            if (Timeline is null)
                return false;

            // 同步标记到 Timeline 模型
            _markerService.SyncToTimeline();

            if (!string.IsNullOrEmpty(CurrentFilePath))
            {
                if (MotionFileReader.Write(Timeline, CurrentFilePath))
                {
                    IsDirty = false;
                    UpdateWindowTitle();
                    PushStatus("已保存");
                    return true;
                }
                PushStatus("保存失败，请检查磁盘空间或文件权限");
                return false;
            }

            return await SaveAsAsync();
        }

        private void SaveAs()
        {
            _ = SaveAsAsync();
        }

        /// <summary>另存为并返回是否真正写入成功；取消选择文件返回 false。</summary>
        public async System.Threading.Tasks.Task<bool> SaveAsAsync()
        {
            if (_dialogService is null)
            {
                // 后备: 通知 View 层触发另存为对话框。该路径无法同步获知结果，
                // 因此对关闭窗口的调用方返回 false，保持窗口和 dirty 状态。
                SaveAsRequested = true;
                SaveAsRequested = false;
                return false;
            }

            var suggestedName = Timeline?.Name ?? "untitled";
            var path = await _dialogService!.SaveFileAsync(
                "保存动作文件",
                suggestedName,
                MotionFileFilters
            );
            if (string.IsNullOrEmpty(path))
                return false;
            return SaveToFile(path);
        }

        /// <summary>
        /// 另存为 (由 View 层调用)
        /// </summary>
        public bool SaveToFile(string filePath)
        {
            if (Timeline == null)
                return false;

            // 同步标记到 Timeline 模型
            _markerService.SyncToTimeline();

            if (MotionFileReader.Write(Timeline, filePath))
            {
                CurrentFilePath = filePath;
                IsDirty = false;
                UpdateWindowTitle();
                StartAutoSave();
                PushStatus($"已保存: {System.IO.Path.GetFileName(filePath)}");
                return true;
            }
            PushStatus("保存失败，请检查磁盘空间或文件权限");
            return false;
        }
    }
}
