# Motion 架构边界与文档同步规则

## 1. Motion 处理核心必须在 IODevice (C++) 中实现

**核心规则：** Motion 动作的处理核心（MotionPlayer、ChannelMixer、InterpolationEngine、SafetyGuard、DeviceOutputBus 等）**必须**在 IODevice C++ 项目中实现，而非在 IOStudio C# 中实现。

### 架构层次

```
IODevice.dll (C++ 核心)
  └── MotionPlayer / ChannelMixer / SafetyGuard / InterpolationEngine — 核心逻辑

IODevice_C_Wrapper.dll (C ABI 导出)
  └── MotionLoad / MotionPlay / MotionLoadSlot 等 — stdcall + BSTR 导出

IODevice_CSharp_Wrapper (C# P/Invoke)
  └── IOToolkit.MotionPlayer 静态类 — [DllImport] 调用

IOStudio (C# Avalonia UI)
  └── 编辑器 UI / ViewModel — 通过 C# Wrapper 调用 C++ 核心
  └── 仅保留编辑器级别的 C# 逻辑（UI 状态、时间轴编辑操作、预览协调）
```

### 禁止事项

- ❌ 不得在 IOStudio C# 中实现完整的播放引擎逻辑（插值、安全保护、设备派发）
- ❌ 不得在 IOStudio 中重复实现 C++ 已有的 MotionPlayer 功能
- ❌ 不得绕过 C/C# Wrapper 直接在 IOStudio 中调用 IODevice 底层

### 允许事项

- ✅ IOStudio 中可有薄 C# 适配层，用于桥接 ViewModel ↔ C# Wrapper
- ✅ 原型阶段可临时使用 C# 实现快速验证，但必须标注 `// PROTOTYPE: 待迁移至 C++ IODevice`
- ✅ 编辑器特有的 UI 逻辑（时间轴缩放、选区、撤销/重做）留在 IOStudio

## 2. 文档同步规则

### 实施计划文档

- **路径**: `IOStudio/动感平台_开发实施计划.md`
- **时机**: 每次实施完成后，**立即**更新对应任务状态
- **标记**: ✅ 已完成 | 🔧 进行中 | ❌ 受阻
- **追加**: 计划外任务追加到对应 Sprint 末尾

### 架构设计文档

- **路径**: `IOStudio/动感平台编辑系统_架构设计.md`
- **时机**: 架构变更或对架构有重要沉淀时，**立即**更新
- **内容**: 新增/修改设计、废弃方案标注、决策推导记录
- **版本**: 有重大变更时递增版本号 (如 v8 → v9)

## 3. 汇报文档独立性规则

**核心规则：** 进度汇报文档（PPT、周报等）是独立的管理文件，**不一定反映实际开发状态**。

### 行为准则

- ❌ 禁止以汇报内容作为判断开发进度的依据
- ❌ 禁止根据汇报文档推断代码实现状态
- ✅ 开发实施状态以 `动感平台_开发实施计划.md` 为唯一事实来源
- ✅ 架构状态以 `动感平台编辑系统_架构设计.md` 为唯一事实来源
- ✅ 汇报文档仅用于对外沟通，不参与技术决策

### 同步检查清单

每次 coding session 结束前：

1. 对照实施计划，标记已完成/进行中的任务
2. 检查是否有架构级变更需要更新架构文档
3. 检查是否有新的风险项需要加入风险清单
