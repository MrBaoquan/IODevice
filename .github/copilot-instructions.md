# GitHub Copilot 项目约定

## 任务进度同步规则

当完成任何与 `IOStudio/动感平台_开发实施计划.md` 中任务对应的代码实现时，**必须**同步更新实施计划文档中的对应任务状态：

1. **已完成的任务**：在任务编号后追加 ✅ 标记
   - 示例：`| 1.1.1 | ✅ 定义 .motion JSON Schema | ...`
2. **进行中的任务**：在任务编号后追加 🔧 标记
   - 示例：`| 2.2.1 | 🔧 TimeRulerControl (SkiaSharp 绘制) | ...`
3. **Sprint 级别状态**：当一个 Sprint 所有任务完成后，在 Sprint 标题后追加 ✅
   - 示例：`### Sprint 1.1 — 数据模型 + 文件解析 (Day 1-2) ✅`
4. **Phase 级别状态**：当一个 Phase 所有 Sprint 完成后，在 Phase 标题后追加 ✅

## 执行流程

每次 coding session 结束前，Agent 应：

1. 回顾本次 session 中创建/修改的文件
2. 对照 `动感平台_开发实施计划.md` 的任务表，标记已完成项
3. 如果有新增的计划外任务，追加到对应 Sprint 表格末尾

## 代码规范

### IOStudio 项目 (Avalonia / C#)

- **框架**：Avalonia 11.x + ReactiveUI + .NET 6.0
- **MVVM 模式**：ViewModel 继承 `ViewModelBase : ReactiveObject`
- **编译绑定**：全局启用 `AvaloniaUseCompiledBindingsByDefault`，View 中使用 `x:DataType`
- **命名空间**：`IOStudio.Models.Motion` / `IOStudio.Services.Motion` / `IOStudio.ViewModels.Timeline` / `IOStudio.Views.Timeline` / `IOStudio.Controls.Timeline`
- **JSON 序列化**：使用 `System.Text.Json`，字段名使用 snake_case（通过 `[JsonPropertyName]`）
- **ReactiveCommand**：异步操作使用 `ReactiveCommand.CreateFromTask`，同步使用 `ReactiveCommand.Create`
- **Dispose 模式**：实现 `IActivatableViewModel` 时在 `WhenActivated` 中管理订阅

### .motion 文件格式

- 版本号：`"version": "1.0"`
- 值域：所有通道值归一化到 `0.0 ~ 1.0`
- 插值类型：`linear` | `bezier` | `step` | `ease_in_out`
- 时间单位：毫秒 (ms)
- 编码：UTF-8 JSON，缩进 2 空格
