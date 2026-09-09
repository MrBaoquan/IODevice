# 动感平台编辑系统 — 开发实施计划

> 本文档是 [动感平台编辑系统\_架构设计.md](动感平台编辑系统_架构设计.md) (v9) 的落地执行文件。
> 精确到每个任务的文件、类、方法、依赖、验收标准和工时。
>
> Motion 处理核心在 IODevice C++ 中实现。IOStudio C# 侧通过 C# Wrapper (`IOToolkit.MotionPlayer`) 调用 C++ 核心，不独立实现播放逻辑。架构采用 Slot-Based Multi-Engine 模式，所有 API 通过 Slot 寻址。
>
> **起始日期：** 2026-03-09 (周一)
> **预估总工期：** ~20 周 (至 2026-07-24)
> **单人全职开发**
>
> ### 执行顺序原则
>
> ```
> ① IOStudio 编辑面板先行 → 稳定 .motion 文件格式
> ② 格式稳定后 → C++ MotionPlayer 照格式实现
> ③ TCP MotionServer 延后 → 编辑器自身已可播放预览
> ```

---

## 目录

> ✅ 表示代码已验证完成

| 执行顺序  | 阶段                                                                     | 内容                                                  | 周期    | 状态              |
| --------- | ------------------------------------------------------------------------ | ----------------------------------------------------- | ------- | ----------------- |
| ★ **1st** | [Phase 1: 数据模型 + 播放引擎](#phase-1-播放引擎--motionserver-网络协议) | 数据模型 / 文件 I/O / 插值 / PlaybackEngine           | W01-W03 | ✅ 已完成         |
| ★ **2nd** | [Phase 2: 时间轴编辑器 UI](#phase-2-时间轴基础-ui)                       | 编排面板 / 轨道 / 关键帧编辑                          | W03-W06 | ✅ 已完成         |
| ★ **3rd** | [Phase 3: 播放预览 + 导出 + 加密](#phase-3-播放预览--导出--加密)         | 编辑器内预览 / .motion 导出 / .mtn 加密               | W07-W09 | 🔧 90% 加密待完成 |
| 4th       | [Phase 4: 曲线编辑器](#phase-4-曲线编辑器)                               | 贝塞尔曲线精编                                        | W09-W10 | ✅ 已完成         |
| **4.1**   | [Phase 4.13: 架构改进-短期](#phase-413-架构改进--短期-s1-s4)             | Undo补全/快捷键服务/Dialog MVVM/属性面板绑定          | W10+    | ✅ 已完成         |
| **4.2**   | [Phase 4.14: 架构改进-中期](#phase-414-架构改进--中期-m1-m4)             | ViewModel拆分/DragReorder/IDialogService/IVideoPlayer | W10+    | ✅ 已完成         |
| **4.3**   | [Phase 4.15: 架构改进-长期](#phase-415-架构改进--长期-l1-l4)             | DI容器/静态服务接口化/单元测试/code-behind迁移        | W10+    | ✅ 已完成         |
| 5th       | [Phase 5: 媒体同步 ✅](#phase-5-媒体同步-)                               | 视频/音频联动                                         | W11-W12 | ✅ 已完成         |
| 6th       | [Phase 6: 效果预设](#phase-6-效果预设)                                   | 预设库 / 拖入轨道                                     | W11-W12 | ⏸️ 未开始         |
| 7th       | [Phase 0→7: IODevice C++ MotionPlayer](#phase-0-iodevice-c-motionplayer) | .motion 格式稳定后实施 C++ 运行时                     | W13-W14 | ⏸️ 未开始         |
| 8th       | Phase 1b: TCP MotionServer                                               | 网络协议（从旧 Phase 1 Sprint 1.4 拆出）              | W15-W16 | ⏸️ 未开始         |
| 9th       | [Phase 7→9: LicHper + 打磨](#phase-7-lichper-授权集成--高级功能--打磨)   | 授权 / Undo / 自动保存 / 快捷键                       | W17-W18 | ⏸️ 未开始         |
| 10th      | [Phase 8→10: Unity/UE SDK](#phase-8-unityue-客户端-sdk)                  | 两条路径 SDK                                          | W19-W20 | ⏸️ 未开始         |
| 11th      | [Phase 9: 飞荇客平台协议对接](#phase-9-飞荇客平台协议通讯接口开发对接)   | 飞荇客平台通讯协议开发与联调                          | W15-W17 | ⏸️ 未开始         |
| 12th      | [Phase 10: Unity 插件开发](#phase-10-unity-插件开发)                     | Unity 编辑器插件 + 运行时集成                         | W18-W19 | ⏸️ 未开始         |
| 13th      | [Phase 11: UE 插件开发](#phase-11-ue-插件开发)                           | Unreal Engine 插件 + 蓝图集成                         | W19-W20 | ⏸️ 未开始         |

- [代码级进度审查 (v9 新增)](#代码级进度审查-v9-新增)
- [人员分工与倒排计划](#人员分工与倒排计划)
- [更新后的节点倒排计划 (v9)](#更新后的节点倒排计划-v9)
- [甘特图](#甘特图)
- [风险清单](#风险清单)
- [验收标准总表](#验收标准总表)

---

## 代码级进度审查 (v9 新增)

> **审查日期：** 2026-04-13 | **审查方式：** 逐文件代码仓库扫描
> **当前时间进度：** 35/113 天 (31.0%)

### 已完成模块（代码验证通过）

| 模块                          | 文件数         | 核心类/接口                                                                                                                        | 代码验证结论 |
| ----------------------------- | -------------- | ---------------------------------------------------------------------------------------------------------------------------------- | ------------ |
| **数据模型 (Models/Motion/)** | 13             | MotionTimeline, MotionTrack, MotionClip, MotionKeyframe, MotionEvent, TimelineEvent, TimelineMarker, TrackGroup, ShortcutAction 等 | ✅ 全部实现  |
| **播放引擎**                  | 1 (12.5KB)     | MotionPlaybackEngine — 状态机 (Idle→Playing⇄Paused→Stopping→Idle), Tick 驱动                                                       | ✅ 完整实现  |
| **插值引擎**                  | 1 (4.7KB)      | InterpolationEngine — Linear/Bezier/Step/EaseInOut，二分查找 O(log n)                                                              | ✅ 完整实现  |
| **安全保护**                  | 1 (3.7KB)      | SafetyGuard — Clamp + 速度限制 + SmoothStep 回中生成器                                                                             | ✅ 完整实现  |
| **设备分发**                  | 1 (4.5KB)      | DeviceDispatcher — OAction + OAxis 分发至 IOToolkit.SetDO()                                                                        | ✅ 完整实现  |
| **文件读写**                  | 1 (4.0KB)      | MotionFileReader — JSON 读写 (⚠️ .mtn 加密为 stub)                                                                                 | ✅ JSON 完整 |
| **导出服务**                  | 1 (10.5KB)     | MotionExportService — .motion JSON + CSV + CompactJson                                                                             | ✅ 三种格式  |
| **Undo/Redo**                 | 1 (4.7KB)      | UndoRedoService — Command 模式，100 级栈，批量分组                                                                                 | ✅ 完整实现  |
| **快捷键**                    | 1 (3.2KB)      | KeyboardShortcutService — Ctrl 组合键 + 上下文感知                                                                                 | ✅ 完整实现  |
| **曲线预设**                  | 1 (7.2KB)      | CurvePresetService — 10 种内置预设，按分类分组                                                                                     | ✅ 完整实现  |
| **标记服务**                  | 1 (7.8KB)      | MarkerService — CRUD + Undo 集成 + 导航                                                                                            | ✅ 完整实现  |
| **视频播放**                  | 1 (6.7KB)      | LibVlcVideoPlayerService — 懒加载 LibVLC, HWND 缓存                                                                                | ✅ 完整实现  |
| **音频波形**                  | 1 (12.9KB)     | AudioWaveformExtractor — LibVLC PCM 捕获 + 渐进式更新                                                                              | ✅ 完整实现  |
| **节拍检测**                  | 1 (4.7KB)      | BeatDetector — 能量阈值 + 滑动窗口 + 最小间隔过滤                                                                                  | ✅ 完整实现  |
| **接口层**                    | 10             | IPlaybackEngine, IUndoRedoService, IInterpolationEngine 等                                                                         | ✅ 全部定义  |
| **ViewModel**                 | 16 (7 partial) | TimelineEditorViewModel (6 个 partial), TrackVM, ClipVM, CurveEditorVM 等                                                          | ✅ 完整实现  |
| **自定义控件**                | 10 + 3 BH      | CurveEditorControl, TimeRulerControl, TrackClipControl, PlayheadOverlay, WaveformControl, VlcVideoHost                             | ✅ 完整实现  |
| **对话框/窗口**               | 4              | TimelineEditorWindow, AddTrackDialog, ExportDialog, GroupDialog                                                                    | ✅ 完整实现  |
| **测试**                      | 3              | UndoRedoServiceTests, MotionModelSerializationTests, KeyframePropertyViewModelTests                                                | ✅ 通过      |

### 未实现模块（代码不存在）

| 模块                         | 计划阶段           | 代码现状                                                                   | 影响                            |
| ---------------------------- | ------------------ | -------------------------------------------------------------------------- | ------------------------------- |
| **.mtn AES-256-GCM 加密**    | Phase 3 Sprint 3.3 | ❌ `MotionFileReader.IsEncrypted()` 仅检查扩展名，无 `MotionCryptoService` | 阻塞 C++ MotionPlayer .mtn 解密 |
| **TCP MotionServer**         | Phase 1b           | ❌ 无 `MotionServerService` / `MotionCommandHandler`                       | 阻塞网络播控和 Unity/UE 客户端  |
| **影院视频播放器核心**       | 节点二             | ❌ LibVlcVideoPlayerService 仅服务编辑器预览                               | 独立播放器需额外开发            |
| **效果预设系统**             | Phase 6            | ❌ CurvePresetService ≠ 效果模板                                           | 需新建预设库系统                |
| **C++ MotionPlayer**         | Phase 0→7          | ✅ 已实现: MotionPlayer.h/.cpp (多 Slot 管理器, 插值, 混合, 安全保护, 设备派发) | —                               |
| **C/C# Wrapper Motion 函数** | Phase 0            | ✅ 已实现: 20 个 Slot API 函数 (C Wrapper + C# Wrapper)                        | —                               |
| **LicHper 授权**             | Phase 7→9          | ❌ 无 LicHper 相关文件                                                         | 阻塞 Basic/Pro 分级             |
| **多 Slot 架构**             | 节点二             | ✅ C++ MotionPlayer 为生产实现, IOStudio C# 侧通过 C# Wrapper 消费             | —                               |

### 架构隐患

| 隐患             | 等级  | 详情                                                              |
| ---------------- | ----- | ----------------------------------------------------------------- |
| NuGet 版本不一致 | 🟡 中 | `Avalonia.Controls.ItemsRepeater` 11.1.5 vs 其他 Avalonia 11.3.10 |
| 目标框架过旧     | 🟡 中 | `net6.0` 已于 2024-11 停止支持                                    |
| 测试覆盖不足     | 🟡 中 | 仅 3 个测试文件，核心引擎 (Playback/Interpolation/Safety) 无覆盖  |

---

## 更新后的节点倒排计划 (v9)

> 基于 2026-04-13 代码审查基线重新排布，以各节点截止日期为锚点倒排。
> Motion 处理核心在 IODevice C++ 中实现。IOStudio C# 侧通过 C# Wrapper 消费。所有 API 通过 Slot 寻址。

### 节点二剩余任务（截止 4/30，剩余约 12 个工作日）

| 优先级 | 任务                                                               | 负责人 | 预估工时 | 计划周期  | 前置依赖                    |
| ------ | ------------------------------------------------------------------ | ------ | -------- | --------- | --------------------------- |
| P0     | ✅ MotionPlayerManager + MotionSlot + ChannelMixer 编排层实现      | 马宝全 | 8h       | 4/14-4/15 | PlaybackEngine ✅           |
| P0     | ✅ MotionPlaybackEngine 重构：求值/派发分离 (EvaluateChannels)    | 马宝全 | 4h       | 4/15-4/16 | Manager 框架                |
| P0     | ✅ SafetyGuard 后混合阶段迁移 + DeviceDispatcher.DispatchByChannelKey | 马宝全 | 2h       | 4/16      | Engine 重构                 |
| P0     | MotionCryptoService: .mtn AES-256-GCM 加密导出 + 解密加载          | 马宝全 | 6h       | 4/17-4/18 | 无                          |
| P0     | TCP MotionServer (Slot 寻址, play_all/stop_all/list_slots)         | 马宝全 | 20h      | 4/18-4/24 | Manager + .mtn              |
| P0     | 影院视频播放器核心 (独立播放窗口 + 控制接口)                       | 马宝全 | 16h      | 4/24-4/28 | LibVlcVideoPlayerService ✅ |
| P1     | 端到端联调：多 Slot 并发 → 编辑→导出→TCP 加载→设备播放             | 马宝全 | 4h       | 4/29-4/30 | 以上全部                    |
| —      | 业主端控制程序架构设计 + 影片播放控制模块                          | 居向前 | 28h      | 4/14-4/30 | —                           |
| —      | 飞荇客协议 DLL 架构 + 基础通讯层                                   | 邵向阳 | 20h      | 4/14-4/30 | —                           |

### 节点三任务（截止 5/15，10 个工作日）

| 优先级 | 任务                                                                              | 负责人 | 预估工时 | 计划周期 | 前置依赖         |
| ------ | --------------------------------------------------------------------------------- | ------ | -------- | -------- | ---------------- |
| P0     | ✅ C++ MotionPlayer 多 Slot 管理器 (pImpl, 内含 ChannelMixer/SafetyGuard)         | 马宝全 | 24h      | 5/1-5/7  | .mtn 加密方案 ✅ |
| P0     | ✅ IODeviceController::Update() 集成 MotionPlayer::Tick()                         | 马宝全 | 1h       | 5/7      | MotionPlayer 类  |
| P0     | ✅ C Wrapper: 20 个 Slot API 函数                                                 | 马宝全 | 4h       | 5/8      | C++ MotionPlayer |
| P0     | ✅ C# Wrapper: IOToolkit.MotionPlayer 静态类 (Slot API)                           | 马宝全 | 3.5h     | 5/9      | C Wrapper        |
| P0     | IOStudio C# 侧替换：通过 C# Wrapper 薄调用层消费 C++ 核心                        | 马宝全 | 4h       | 5/10     | C# Wrapper       |
| P1     | 核心引擎单元测试 (PlaybackEngine / ChannelMixer / SafetyGuard)                    | 马宝全 | 8h       | 5/9-5/13 | —                |
| P2     | Avalonia 版本统一 + net6.0→net8.0 升级评估                                        | 马宝全 | 4h       | 5/14     | —                |
| —      | 动感平台控制模块 + 播放器通信接口                                                 | 居向前 | 28h      | 5/1-5/15 | —                |
| —      | 飞荇客指令处理 + 状态回传 + 通道映射                                              | 邵向阳 | 20h      | 5/1-5/15 | —                |
| —      | Unity UPM Package + MotionClient + 多 Slot Demo                                   | 郝晓阳 | 19h      | 5/1-5/15 | C# Wrapper       |
| —      | UE Plugin + FMotionClient + 多 Slot Demo                                          | 穆大强 | 18h      | 5/1-5/15 | C Wrapper        |

### 节点四任务（截止 5/31，12 个工作日）

| 优先级 | 任务                                                        | 负责人 | 预估工时 | 计划周期  | 前置依赖              |
| ------ | ----------------------------------------------------------- | ------ | -------- | --------- | --------------------- |
| P1     | 效果预设系统 (预设库面板 + 拖拽到轨道 + 自动生成关键帧)     | 马宝全 | 16h      | 5/16-5/20 | CurvePresetService ✅ |
| P1     | 性能优化 (>1000 关键帧 >30fps)                              | 马宝全 | 3h       | 5/21      | —                     |
| P2     | 系统级端到端联调 (IOStudio + C++ MotionPlayer + 飞荇客 DLL) | 马宝全 | 8h       | 5/22-5/26 | 节点三全部            |
| P2     | Bug 回归修复 + 全功能测试                                   | 马宝全 | 4h       | 5/27-5/28 | —                     |
| —      | 业主端 ↔ 播放器联调 + 异常处理                              | 居向前 | 12h      | 5/16-5/31 | —                     |
| —      | 飞荇客平台联调 + 异常处理 + 性能测试                        | 邵向阳 | 15h      | 5/16-5/31 | —                     |
| —      | Unity 编辑器扩展 + P/Invoke 嵌入式路径                      | 郝晓阳 | 16h      | 5/16-5/31 | —                     |
| —      | UE 蓝图 + 编辑器扩展 + 嵌入式路径                           | 穆大强 | 19h      | 5/16-5/31 | —                     |

### 节点五任务（截止 6/30，22 个工作日）

| 优先级 | 任务                                    | 负责人 | 预估工时 | 计划周期  |
| ------ | --------------------------------------- | ------ | -------- | --------- |
| P0     | LicHper 授权集成 (Basic/Pro 运行时分级) | 马宝全 | 12h      | 6/1-6/6   |
| P1     | IOStudio.exe 安装包打包                 | 马宝全 | 8h       | 6/7-6/10  |
| P1     | 用户操作手册 + TCP 播控协议 API 文档    | 马宝全 | 12h      | 6/11-6/16 |
| P2     | 全流程验收测试支持                      | 马宝全 | 8h       | 6/17-6/25 |
| P2     | Buffer / 紧急 Bug 修复                  | 马宝全 | 8h       | 6/25-6/30 |
| —      | 业主端优化 + 部署 + 操作手册            | 居向前 | 16h      | 6/1-6/30  |
| —      | DLL API 文档 + 集成指南 + 稳定性测试    | 邵向阳 | 12h      | 6/1-6/30  |
| —      | Unity Demo 场景 + 文档 + UPM 发布       | 郝晓阳 | 16h      | 6/1-6/30  |
| —      | UE Demo 关卡 + 文档 + .uplugin 发布     | 穆大强 | 16h      | 6/1-6/30  |

### 关键路径图

```
多 Slot 架构 (4/16) → .mtn 加密 (4/18) → TCP MotionServer (4/24) → 播放器 (4/28) → 联调 (4/30)  ← 节点二
  (Manager+Mixer+            ↘
   Engine重构)              C++ MotionPlayer 多 Slot (5/7) → C/C# Wrapper (5/9)                   ← 节点三
                                                                ↓
                                                      Unity/UE 多 Slot Demo (5/9+)
                                                                ↓
                            效果预设 (5/20) → 系统联调 (5/26)                                      ← 节点四
                                                                ↓
                            LicHper (6/6) → 打包 (6/10) → 文档 (6/16) → 验收 (6/30)              ← 节点五
```

### 待优化技术事项（非阻塞，穿插处理）

| 事项                                                               | 优先级 | 建议时机                                 |
| ------------------------------------------------------------------ | ------ | ---------------------------------------- |
| MotionPlaybackEngine → 纯求值器重构 (EvaluateChannels)             | 🔴 高  | 节点二 Sprint 开始前必须完成             |
| ChannelMixer 多策略单元测试 (Priority/Blend/Max/Additive/Override) | 🔴 高  | 紧跟 ChannelMixer 实现                   |
| 统一 `Avalonia.Controls.ItemsRepeater` 至 11.3.10                  | 🟡 中  | 节点三期间一并处理                       |
| `net6.0` → `net8.0` 升级                                           | 🟡 中  | 节点三评估，节点五执行                   |
| 核心引擎单元测试补齐                                               | 🟡 中  | 节点三安排 8h 集中补齐 (含 ChannelMixer) |
| UndoRedoService MaxUndoLevels 底部移除不精确                       | 🟢 低  | 随修随改                                 |
| MotionExportService 增加 .mtn 加密导出路径                         | 🔴 高  | 节点二 Sprint 3.3 必须完成               |

---

## 人员分工与倒排计划

> **倒排原则：** 以立项报告 5 个里程碑节点为锚点，从验收日期 2026/6/30 向前推导，明确每位成员在各节点的具体任务与交付物。
>
> **团队分工：**
>
> - **马宝全（项目执行）**：IODevice 动作核心、动作编辑器、影院视频播放器核心功能、整体影院技术架构
> - **居向前（软件架构设计）**：业主端控制程序（影片控制 + 平台控制，与影院播放器通信实现）
> - **邵向阳（功能模块设计）**：基于飞荇客等影院协议的 C++ DLL 封装开发
> - **郝晓阳（功能模块设计）**：Unity 插件集成
> - **穆大强（功能模块设计）**：UE 插件集成

### 倒排总览表

| 节点      | 截止日期 | 马宝全                                      | 居向前                | 邵向阳                  | 郝晓阳                 | 穆大强                      |
| --------- | -------- | ------------------------------------------- | --------------------- | ----------------------- | ---------------------- | --------------------------- |
| 节点一 ✅ | 3/31     | 架构设计 + 播放引擎 + 编辑器原型 + 测试工具 | —                     | —                       | —                      | —                           |
| 节点二    | 4/30     | **多 Slot 架构** + TCP 播控 + 播放器核心    | 业主端架构 + 影片控制 | 协议 DLL 架构 + 通讯层  | —                      | —                           |
| 节点三    | 5/15     | C++ MotionPlayer **多 Slot** + 编排优化     | 平台控制 + 播放器通信 | DLL 核心封装 + 指令处理 | Unity Package + 运行时 | UE 插件 + FMotionClient     |
| 节点四    | 5/31     | 效果预设 + 性能优化 + 联调                  | 业主端联调测试        | 协议联调 + 异常处理     | Unity 编辑器扩展       | UE 蓝图 + 编辑器扩展        |
| 节点五    | 6/30     | 授权系统 + 打包 + 验收                      | 业主端优化 + 部署     | DLL 文档 + 稳定性       | Demo + 文档 + UPM 发布 | Demo + 文档 + .uplugin 发布 |

### 马宝全 — IODevice 动作核心 / 动作编辑器 / 影院播放器 / 技术架构

| 节点      | 时间区间    | 具体任务                                                                                                                                               | 交付物                                                    |
| --------- | ----------- | ------------------------------------------------------------------------------------------------------------------------------------------------------ | --------------------------------------------------------- |
| 节点一 ✅ | 3/9 - 3/31  | ① 系统架构设计 ② .motion 格式定义 + JSON Schema ③ 播放引擎原型（加载/插值/设备输出）④ 编辑器窗口骨架 ⑤ 飞荇客协议测试工具 ⑥ 厂家协议收集与沟通         | 架构文档 v8、.motion v1.0、播放引擎、编辑器原型、测试工具 |
| 节点二    | 4/1 - 4/30  | ① **多 Slot 并发架构** (MotionPlayerManager / ChannelMixer / Engine 求值分离) ② .mtn 加密导出 ③ TCP 网络播控 (Slot 寻址) ④ 影院视频播放器 ⑤ 端到端联调 | 多 Slot 架构、编辑器完整版、播放器核心、TCP 服务          |
| 节点三    | 5/1 - 5/15  | ① C++ MotionPlayer **多 Slot** 实现 (Slot 管理 / ChannelMixer / .mtn 解密) ② C Wrapper (17+6 函数) ③ C# Wrapper ④ 编排优化                             | IODevice.dll 含多 Slot MotionPlayer、Wrapper 层           |
| 节点四    | 5/16 - 5/31 | ① 效果预设系统（预设库 / 拖入轨道 / 自动生成关键帧）② 性能优化（>1000 关键帧流畅 >30fps）③ 系统级端到端联调 ④ Bug 回归修复                             | 预设库、性能达标、联调通过                                |
| 节点五    | 6/1 - 6/30  | ① 授权系统集成（Basic/Pro 双模式）② IOStudio.exe 安装包打包 ③ 用户操作手册 ④ TCP 播控协议 API 文档 ⑤ 全流程验收测试支持                                | 安装包、手册、API 文档、验收报告                          |

### 居向前 — 业主端控制程序（影片控制 + 平台控制 + 通信实现）

| 节点   | 时间区间    | 具体任务                                                                                             | 交付物                     |
| ------ | ----------- | ---------------------------------------------------------------------------------------------------- | -------------------------- |
| 节点二 | 4/1 - 4/30  | ① 业主端控制程序架构设计与技术方案 ② 影片播放控制模块（片单管理 / 播放 / 暂停 / 停止）③ UI 框架搭建  | 技术方案文档、影片控制原型 |
| 节点三 | 5/1 - 5/15  | ① 动感平台控制模块（平台启停 / 姿态控制 / 状态监控）② 与影院播放器通信接口实现 ③ 影片 + 平台联动逻辑 | 平台控制功能、通信接口     |
| 节点四 | 5/16 - 5/31 | ① 业主端 ↔ 播放器端到端联调 ② 异常处理（断线重连 / 状态同步 / 超时保护）③ UI 完善与操作流程优化      | 联调通过、稳定运行         |
| 节点五 | 6/1 - 6/30  | ① 业主端操作手册 ② 现场部署方案 ③ 验收测试                                                           | 操作手册、部署文档         |

### 邵向阳 — 基于飞荇客等影院协议的 C++ DLL 封装开发

| 节点   | 时间区间    | 具体任务                                                                                                               | 交付物                             |
| ------ | ----------- | ---------------------------------------------------------------------------------------------------------------------- | ---------------------------------- |
| 节点二 | 4/1 - 4/30  | ① 飞荇客协议深度分析与规范整理 ② C++ DLL 工程架构设计 ③ 基础通讯层实现（TCP/UDP 连接 / 心跳 / 重连）                   | 协议规范文档、DLL 工程框架、通讯层 |
| 节点三 | 5/1 - 5/15  | ① 飞荇客指令处理（动感指令 → 设备驱动映射）② 状态回传服务（设备状态 / 播放状态 → 平台）③ 通道映射配置                  | DLL 核心功能、指令处理链路         |
| 节点四 | 5/16 - 5/31 | ① 飞荇客平台联调测试 ② 异常处理（协议错误 / 连接断开 / 指令冲突）③ 和利时等其他协议扩展评估 ④ 性能测试（高频指令场景） | 联调通过、异常处理完善             |
| 节点五 | 6/1 - 6/30  | ① DLL API 文档 ② 集成指南（供飞荇客平台侧参考）③ 稳定性测试与优化                                                      | API 文档、集成指南                 |

### 郝晓阳 — Unity 插件集成

| 节点   | 时间区间    | 具体任务                                                                                                                                         | 交付物                   |
| ------ | ----------- | ------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------ |
| 节点三 | 5/1 - 5/15  | ① Unity UPM Package 工程搭建（com.iotoolkit.motion）② MotionClient 运行时组件（TCP 路径）③ MotionPlayerComponent（MonoBehaviour 封装）④ 事件系统 | Package 结构、运行时核心 |
| 节点四 | 5/16 - 5/31 | ① P/Invoke 嵌入式路径（直接调用 IODevice.dll）② Unity 编辑器扩展（Inspector / Motion Preview Window）③ .motion/.mtn 文件 Importer                | 嵌入式路径、编辑器扩展   |
| 节点五 | 6/1 - 6/30  | ① CinemaDemo 场景（视频 + 动感联动）② VRCoasterDemo 场景 ③ 集成文档（README + API 参考 + 常见问题）④ UPM 包发布验证                              | Demo 场景、UPM 包、文档  |

### 穆大强 — UE 插件集成

| 节点   | 时间区间    | 具体任务                                                                                                                  | 交付物                  |
| ------ | ----------- | ------------------------------------------------------------------------------------------------------------------------- | ----------------------- |
| 节点三 | 5/1 - 5/15  | ① UE 插件工程搭建（IOToolkitMotion）② FMotionClient 核心（TCP 连接 + 帧协议）③ AMotionPlayerActor（Actor 封装）           | 插件框架、TCP 路径      |
| 节点四 | 5/16 - 5/31 | ① 蓝图函数库（BlueprintFunctionLibrary）② 嵌入式路径（链接 IODevice.dll C API）③ Motion Asset 自定义资产类型 ④ 编辑器扩展 | 蓝图集成、资产系统      |
| 节点五 | 6/1 - 6/30  | ① CinemaDemo 关卡（蓝图触发 + 动感联动）② VRDemo 关卡 ③ 集成文档（README + API 参考 + 蓝图使用指南）④ .uplugin 打包验证   | Demo 关卡、插件包、文档 |

### 关键依赖关系

```
节点一 ✅ 马宝全：架构+引擎+格式
    │
    ▼
节点二   马宝全：编辑器+播放器+TCP ───→ 居向前：业主端架构+影片控制
    │                                    邵向阳：协议DLL架构+通讯层
    ▼
节点三   马宝全：C++ MotionPlayer ──────→ 郝晓阳：Unity Package+运行时
    │    居向前：平台控制+通信接口         穆大强：UE 插件+FMotionClient
    │    邵向阳：DLL核心封装
    ▼
节点四   马宝全：效果预设+性能优化
    │    居向前：业主端联调       邵向阳：协议联调
    │    郝晓阳：编辑器扩展      穆大强：蓝图+编辑器扩展
    ▼
节点五   全员：联调 → 文档 → 打包 → 验收
```

### 里程碑检查点

| 检查日期 | 检查内容                                                                 | 责任人                            |
| -------- | ------------------------------------------------------------------------ | --------------------------------- |
| 4/15     | 编辑器核心编辑功能可用 + 业主端架构评审 + DLL 工程框架评审               | 马宝全 / 居向前 / 邵向阳          |
| 4/30     | 编辑器 + TCP 服务端到端打通 + 影片控制原型可演示 + 基础通讯层心跳正常    | 马宝全 / 居向前 / 邵向阳          |
| 5/15     | C++ MotionPlayer 驱动设备 + Unity/UE 运行时连接成功 + DLL 指令处理链路通 | 马宝全 / 郝晓阳 / 穆大强 / 邵向阳 |
| 5/31     | 全系统联调通过 + Unity/UE 编辑器扩展可用 + 协议联调通过                  | 全员                              |
| 6/15     | Demo 场景 / 关卡完成 + 文档初稿 + 安装包初版                             | 全员                              |
| 6/30     | 产品验收：全部交付物就绪                                                 | 全员                              |

---

## Phase 0: IODevice C++ MotionPlayer

> ⚠️ **v8 执行顺序调整：** 本阶段从最优先降级至 **Phase 6 之后 (W13-W14)** 执行。
> 理由：先在 IOStudio 编辑面板中稳定 `.motion` 文件格式和插值算法，再在 C++ 层复现，避免格式变更引起的返工。
>
> **目标：** 在 IODevice.dll C++ 层实现 `MotionPlayer` 单例类，完成 C Wrapper / C# Wrapper 对接；Unity/UE 无需 IOStudio 进程即可通过 `MotionPlayer.Load/Play` 驱动设备
> **周期：** W13~W14（2026-06-01 ~ 2026-06-12），.motion 格式稳定后启动
> **涉及项目：** IODevice (C++)、IODevice_C_Wrapper (C++)、IODevice_CSharp_Wrapper (C#)
> **里程碑：** C# 测试代码调用 `IOToolkit.MotionPlayer.Load("demo.motion"); MotionPlayer.Play();` 驱动座椅运动

### Sprint 0.1 — C++ MotionPlayer 类 (Day 1-4)

| 编号  | 任务                                                                                    | 新建/修改文件                                         | 工时 |
| ----- | --------------------------------------------------------------------------------------- | ----------------------------------------------------- | ---- |
| 0.1.1 | `MotionPlayer.h` 公开头文件（pImpl 声明, IOAPI 导出）                                   | 新建 `IODevice/Source/Public/MotionPlayer.h`          | 2h   |
| 0.1.2 | `MotionPlayer::Impl` 内部实现（文件加载 / 插值 / 安全保护 / 设备分发）                  | 新建 `IODevice/Source/Private/MotionPlayer.cpp`       | 8h   |
| 0.1.3 | `InterpolationEngine`（Linear/Bezier/Step/EaseInOut）                                   | Impl 内部                                             | 3h   |
| 0.1.4 | `SafetyGuard`（限位 Clamp + 速度限制）                                                  | Impl 内部                                             | 2h   |
| 0.1.5 | 修改 `IODeviceController::Update()` 插入 `MotionPlayer::Instance().Tick(_deltaSeconds)` | 修改 `IODevice/Source/Private/IODeviceController.cpp` | 0.5h |
| 0.1.6 | 将 MotionPlayer.h/.cpp 加入 `IODevice.vcxproj` 构建                                     | 修改 `IODevice/IODevice.vcxproj`                      | 0.5h |
| 0.1.7 | .motion JSON 解析（nlohmann/json，已有依赖）                                            | Impl 内部                                             | 3h   |
| 0.1.8 | .mtn AES-256-GCM 解密（Crypto++，已有依赖）                                             | Impl 内部                                             | 2h   |
| 0.1.9 | 单元验证：加载 demo.motion → Tick 100 次 → SetDO 被调用                                 | 控制台测试                                            | 2h   |

**关键代码：**

```cpp
// IODevice/Source/Public/MotionPlayer.h
namespace IOToolkit {
    enum class MotionState { Idle, Playing, Paused, Stopping };
    enum class MotionEvent { PlaybackComplete, SafetyTriggered, StateChanged };

    class IOAPI MotionPlayer {
    public:
        static MotionPlayer& Instance();
        int  Load(const char* filePath);
        void Unload();
        int  Play(); int PlayFrom(float timeMs);
        int  Pause(); int Resume(); int Stop(); int Seek(float timeMs);
        void SetSpeed(float speed); void SetLoop(bool loop);
        MotionState GetState() const;
        float GetCurrentTime() const; float GetDuration() const;
        void BindMotionEvent(MotionEvent evt, std::function<void()> cb);
        void Tick(float deltaSeconds);  // 由 IODeviceController::Update() 调用
    private:
        MotionPlayer(); ~MotionPlayer();
        MotionPlayer(const MotionPlayer&) = delete;
        MotionPlayer& operator=(const MotionPlayer&) = delete;
        class Impl; std::unique_ptr<Impl> _impl;
    };
}
```

```cpp
// IODevice/Source/Private/IODeviceController.cpp — Update() 修改
void IODeviceController::Update()
{
    // Step 1: 刷新设备
    for (auto& d : _devices) d->Tick();
    // Step 2: 输入侧状态机
    PlayerInput::Instance().Tick(_deltaSeconds);
    // Step 3: 输出侧状态机 ★ 新增
    MotionPlayer::Instance().Tick(_deltaSeconds);
    // Step 4: 帧尾
    ProcessFrameEnd();
}
```

**验收：** 纯 C++ 控制台 `IODeviceController::Instance().Load()` → `MotionPlayer::Instance().Load("demo.motion")` → 循环 `Update()` 1000 次 → 无崩溃，`SetDO` 被调用。

---

### Sprint 0.2 — C Wrapper 扩展 (Day 5-6)

| 编号  | 任务                                                       | 修改文件                                   | 工时 |
| ----- | ---------------------------------------------------------- | ------------------------------------------ | ---- |
| 0.2.1 | 声明 13 个 `Motion*` 函数（无 `InDeviceName` 参数）        | `IODevice_C_Wrapper/IODevice_CWrapper.h`   | 1h   |
| 0.2.2 | 实现 13 个函数（转发到 `MotionPlayer::Instance()`）        | `IODevice_C_Wrapper/IODevice_CWrapper.cpp` | 2h   |
| 0.2.3 | 验证：从 C# 调用 P/Invoke `MotionLoad` / `MotionPlay` 成功 | 控制台验证                                 | 1h   |

**新增函数清单（全部无 `InDeviceName` 参数）：**

| 函数名                 | 返回值  | 参数                                     |
| ---------------------- | ------- | ---------------------------------------- |
| `MotionLoad`           | `int`   | `BSTR InFilePath`                        |
| `MotionUnload`         | `void`  | —                                        |
| `MotionPlay`           | `int`   | —                                        |
| `MotionPlayFrom`       | `int`   | `float InTimeMs`                         |
| `MotionPause`          | `int`   | —                                        |
| `MotionResume`         | `int`   | —                                        |
| `MotionStop`           | `int`   | —                                        |
| `MotionSeek`           | `int`   | `float InTimeMs`                         |
| `MotionSetSpeed`       | `void`  | `float InSpeed`                          |
| `MotionSetLoop`        | `void`  | `bool InLoop`                            |
| `MotionGetState`       | `int`   | — (0=Idle/1=Playing/2=Paused/3=Stopping) |
| `MotionGetCurrentTime` | `float` | — (ms)                                   |
| `MotionGetDuration`    | `float` | — (ms)                                   |

---

### Sprint 0.3 — C# Wrapper (Day 7-8)

| 编号  | 任务                                                  | 新建/修改文件                                                    | 工时 |
| ----- | ----------------------------------------------------- | ---------------------------------------------------------------- | ---- |
| 0.3.1 | `MotionState` 枚举                                    | 新建 `IODevice_CSharp_Wrapper/IOToolkit/MotionState.cs`          | 0.5h |
| 0.3.2 | `MotionPlayer` 静态类（P/Invoke → C Wrapper）         | 新建 `IODevice_CSharp_Wrapper/IOToolkit/Facades/MotionPlayer.cs` | 2h   |
| 0.3.3 | 在 `NativeMethods.cs` 添加 13 个 Motion P/Invoke 声明 | 修改 `IODevice_CSharp_Wrapper/IOToolkit/NativeMethods.cs`        | 1h   |
| 0.3.4 | 验证脚本（直接引用 IODevice_CSharp_Wrapper.dll）      | 控制台测试                                                       | 1h   |

```csharp
// IODevice_CSharp_Wrapper/IOToolkit/Facades/MotionPlayer.cs
namespace IOToolkit
{
    public static class MotionPlayer
    {
        public static int  Load(string filePath) => NativeMethods.MotionLoad(filePath);
        public static void Unload()               => NativeMethods.MotionUnload();
        public static int  Play()                => NativeMethods.MotionPlay();
        public static int  PlayFrom(float ms)    => NativeMethods.MotionPlayFrom(ms);
        public static int  Pause()               => NativeMethods.MotionPause();
        public static int  Resume()              => NativeMethods.MotionResume();
        public static int  Stop()                => NativeMethods.MotionStop();
        public static int  Seek(float ms)        => NativeMethods.MotionSeek(ms);
        public static void SetSpeed(float s)     => NativeMethods.MotionSetSpeed(s);
        public static void SetLoop(bool loop)    => NativeMethods.MotionSetLoop(loop);
        public static MotionState State          => (MotionState)NativeMethods.MotionGetState();
        public static float       CurrentTime    => NativeMethods.MotionGetCurrentTime();
        public static float       Duration       => NativeMethods.MotionGetDuration();
    }

    public enum MotionState { Idle = 0, Playing = 1, Paused = 2, Stopping = 3 }
}
```

**Unity 使用验证（3 行完成完整播放）：**

```csharp
IODeviceController.Load("Config/IODevice.xml");
MotionPlayer.Load("Config/Motion/demo.mtn");
// 每帧
IODeviceController.Update();  // 内部自动驱动 MotionPlayer.Tick()
MotionPlayer.Play();
```

---

### 验收标准 (Phase 0 整体)

| #   | 验收项        | 通过条件                                                                                         |
| --- | ------------- | ------------------------------------------------------------------------------------------------ |
| Z1  | C++ 编译      | IODevice.dll 含 MotionPlayer 导出符号，无编译错误                                                |
| Z2  | Tick 集成     | `IODeviceController::Update()` 中调用了 `MotionPlayer::Instance().Tick()`                        |
| Z3  | .motion 加载  | `MotionPlayer::Instance().Load("demo.motion")` 返回 0                                            |
| Z4  | .mtn 解密加载 | `MotionPlayer::Instance().Load("demo.mtn")` 解密成功                                             |
| Z5  | Tick 驱动输出 | 循环 `Update()` → `SetDO` 被调用，值随关键帧变化                                                 |
| Z6  | 安全保护      | 超出 Min/Max 的值被 Clamp，速度超限被截断                                                        |
| Z7  | C Wrapper     | `MotionLoad/MotionPlay/MotionStop` P/Invoke 调用成功                                             |
| Z8  | C# Wrapper    | `IOToolkit.MotionPlayer.Load/Play/Stop` C# 调用正常                                              |
| Z9  | 平级使用      | `IODeviceController.Load()` + `MotionPlayer.Load()` + `IODeviceController.Update()` 三行驱动设备 |

---

## Phase 1: 数据模型 + 播放引擎

> **目标：** 建立 `Models/Motion/` 数据模型 + `Services/Motion/` 播放引擎，稳定 `.motion` 文件格式
> **周期：** W01-W03 (2026-03-09 ~ 2026-03-27)，共 2.5 周
> **里程碑：** MotionFileReader 能读写 `.motion` 文件；MotionPlaybackEngine 能加载→插值→输出到设备
>
> ⚠️ TCP MotionServer (原 Sprint 1.4) 延后至 Phase 1b (W15-W16)，本阶段专注 C# 数据模型和播放引擎

### Sprint 1.1 — 数据模型 + 文件解析 (Day 1-2) ✅

| 编号  | 任务                            | 新建文件                               | 工时 |
| ----- | ------------------------------- | -------------------------------------- | ---- |
| 1.1.1 | ✅ 定义 .motion JSON Schema     | `Config/Motion/motion.schema.json`     | 2h   |
| 1.1.2 | ✅ MotionTimeline 数据模型      | `Models/Motion/MotionTimeline.cs`      | 2h   |
| 1.1.3 | ✅ MotionTrack 数据模型         | `Models/Motion/MotionTrack.cs`         | 1h   |
| 1.1.4 | ✅ MotionClip 数据模型          | `Models/Motion/MotionClip.cs`          | 1h   |
| 1.1.5 | ✅ MotionKeyframe 数据模型      | `Models/Motion/MotionKeyframe.cs`      | 1h   |
| 1.1.6 | ✅ PlaybackState 枚举           | `Models/Motion/PlaybackState.cs`       | 0.5h |
| 1.1.7 | ✅ MotionFileReader (JSON→模型) | `Services/Motion/MotionFileReader.cs`  | 3h   |
| 1.1.8 | ✅ 编写示例 .motion 文件        | `Config/Motion/demo_chair_3dof.motion` | 1h   |
| 1.1.9 | 单元测试: 文件解析往返          | 手动控制台验证                         | 1h   |

**文件详细设计：**

```csharp
// Models/Motion/MotionTimeline.cs
namespace IOStudio.Models.Motion
{
    public class MotionTimeline
    {
        public string Version { get; set; } = "1.0";
        public string Name { get; set; } = "";
        public double DurationMs { get; set; }
        public double Fps { get; set; } = 60;
        public List<MotionTrack> Tracks { get; set; } = new();
    }
}

// Models/Motion/MotionTrack.cs
public class MotionTrack
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string DeviceName { get; set; } = "";   // 对应 IODevice.xml Device.Name
    public string OActionName { get; set; } = "";   // 对应 IODevice.xml OAction.Name (如 "Light")
    public string Label { get; set; } = "";         // 显示名 (如 "俯仰")
    public string Color { get; set; } = "#4FC3F7";
    public bool Muted { get; set; }
    public bool Locked { get; set; }
    public List<MotionClip> Clips { get; set; } = new();
}

// Models/Motion/MotionClip.cs
public class MotionClip
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public double StartMs { get; set; }
    public double EndMs { get; set; }
    public List<MotionKeyframe> Keyframes { get; set; } = new();
}

// Models/Motion/MotionKeyframe.cs
public class MotionKeyframe
{
    public double TimeMs { get; set; }              // 相对于 Clip.StartMs 的偏移
    public float Value { get; set; }                // 归一化值 0.0 ~ 1.0
    public string Interpolation { get; set; } = "linear"; // linear|bezier|step|ease_in_out
    public float? TangentIn { get; set; }           // 贝塞尔入切线
    public float? TangentOut { get; set; }          // 贝塞尔出切线
}

// Models/Motion/PlaybackState.cs
public enum PlaybackState
{
    Idle,       // 未加载或已停止
    Playing,    // 播放中
    Paused,     // 暂停
    Stopping    // 平滑回中中
}
```

**示例 .motion 文件：**

```json
{
  "version": "1.0",
  "name": "demo_chair_3dof",
  "duration_ms": 10000,
  "fps": 60,
  "tracks": [
    {
      "id": "trk_pitch",
      "device_name": "Chair",
      "oaction_name": "Pitch",
      "label": "俯仰",
      "color": "#4FC3F7",
      "muted": false,
      "locked": false,
      "clips": [
        {
          "id": "clip_01",
          "start_ms": 0,
          "end_ms": 10000,
          "keyframes": [
            { "time_ms": 0, "value": 0.5, "interpolation": "linear" },
            {
              "time_ms": 2000,
              "value": 0.8,
              "interpolation": "bezier",
              "tangent_out": 0.3
            },
            { "time_ms": 5000, "value": 0.2, "interpolation": "ease_in_out" },
            { "time_ms": 8000, "value": 0.7, "interpolation": "linear" },
            { "time_ms": 10000, "value": 0.5, "interpolation": "linear" }
          ]
        }
      ]
    }
  ]
}
```

**验收：** `MotionFileReader.Read("demo_chair_3dof.motion")` 返回正确的 `MotionTimeline` 对象，`MotionFileReader.Write(timeline, path)` 输出的 JSON 与输入等价。

---

### Sprint 1.2 — 插值引擎 + 安全保护 (Day 3-5) ✅

| 编号  | 任务                                   | 新建文件                                 | 工时 |
| ----- | -------------------------------------- | ---------------------------------------- | ---- |
| 1.2.1 | ✅ InterpolationEngine (4 种插值)      | `Services/Motion/InterpolationEngine.cs` | 4h   |
| 1.2.2 | ✅ SafetyGuard (限位/速度/回中)        | `Services/Motion/SafetyGuard.cs`         | 4h   |
| 1.2.3 | ✅ DeviceDispatcher (→ IODevice SetDO) | `Services/Motion/DeviceDispatcher.cs`    | 3h   |
| 1.2.4 | 验证: 对示例文件插值, 检查输出范围     | 控制台打印                               | 2h   |

**InterpolationEngine 设计：**

```csharp
// Services/Motion/InterpolationEngine.cs
namespace IOStudio.Services.Motion
{
    public static class InterpolationEngine
    {
        /// <summary>
        /// 给定时间, 在关键帧列表中计算插值后的值
        /// </summary>
        public static float Evaluate(List<MotionKeyframe> keyframes, double timeMs)
        {
            // 1. 二分查找当前时间所处的两个关键帧
            // 2. 根据 interpolation 类型调用对应插值函数
            // 3. 返回 0.0~1.0 归一化值
        }

        public static float Linear(float a, float b, float t) => a + (b - a) * t;

        public static float Bezier(float a, float b, float t,
            float tangentOut, float tangentIn)
        {
            // 三次贝塞尔: P = (1-t)³P0 + 3(1-t)²tP1 + 3(1-t)t²P2 + t³P3
            // P0 = a, P3 = b, P1/P2 由切线计算
        }

        public static float Step(float a, float b, float t) => t < 1.0f ? a : b;

        public static float EaseInOut(float a, float b, float t)
        {
            // Smoothstep: t = t*t*(3 - 2*t)
            float s = t * t * (3f - 2f * t);
            return a + (b - a) * s;
        }
    }
}
```

**SafetyGuard 设计：**

```csharp
// Services/Motion/SafetyGuard.cs
namespace IOStudio.Services.Motion
{
    public class SafetyGuard
    {
        public float MaxVelocityPerSecond { get; set; } = 2.0f;  // 归一化单位/秒

        // 每个通道的上一帧值 (用于速度检查)
        private readonly Dictionary<string, float> _lastValues = new();

        /// <summary>
        /// 检查并修正输出值, 返回安全值
        /// </summary>
        public float Check(string channelKey, float rawValue, double deltaTimeSec,
            float minValue = 0f, float maxValue = 1f)
        {
            // 1. Clamp 到 [min, max]
            // 2. 速度限制: |value - lastValue| / dt ≤ maxVelocity
            // 3. 记录 _lastValues[channelKey]
            // 4. 超限时触发 SafetyTriggered 事件
        }

        /// <summary>
        /// 生成平滑回中序列: 当前值 → 0.5 (中位), 在 durationMs 内完成
        /// </summary>
        public IEnumerable<float> GenerateReturnToCenter(
            float currentValue, float centerValue = 0.5f,
            double durationMs = 1000, double stepMs = 16.67)
        {
            // EaseInOut 插值序列
        }

        public event Action<string>? SafetyTriggered;
    }
}
```

**DeviceDispatcher 设计：**

```csharp
// Services/Motion/DeviceDispatcher.cs
namespace IOStudio.Services.Motion
{
    public class DeviceDispatcher
    {
        /// <summary>
        /// 将归一化值写入 IODevice 通道
        /// </summary>
        public void Dispatch(string deviceName, string keyName, float normalizedValue)
        {
            // IORoot.Instance.Devices → FindDevice → FindKey →
            // actualValue = key.Min + normalizedValue * (key.Max - key.Min)
            // device.SetDO(keyName, actualValue)
        }

        /// <summary>
        /// 查询设备通道的 Min/Max/Scale 参数 (SafetyGuard 用)
        /// </summary>
        public (float min, float max)? GetChannelRange(string deviceName, string keyName)
        {
            // IORoot.Instance.Devices → Key → (Min, Max)
        }
    }
}
```

**验收：** 对 demo 文件插值 10000 帧, 所有值在 [0,1], 速度不超限。DeviceDispatcher 对未连接设备不崩溃。

---

### Sprint 1.3 — 播放引擎状态机 (Day 6-7) ✅

| 编号  | 任务                                                     | 新建文件                                  | 工时 |
| ----- | -------------------------------------------------------- | ----------------------------------------- | ---- |
| 1.3.1 | ✅ MotionPlaybackEngine (状态机+Tick)                    | `Services/Motion/MotionPlaybackEngine.cs` | 6h   |
| 1.3.2 | ✅ 集成 InterpolationEngine+SafetyGuard+DeviceDispatcher | (同上)                                    | 2h   |
| 1.3.3 | 验证: 加载demo→Play→Tick模拟→值正确输出                  | 控制台测试                                | 2h   |

**MotionPlaybackEngine 核心逻辑：**

```csharp
// Services/Motion/MotionPlaybackEngine.cs
namespace IOStudio.Services.Motion
{
    public class MotionPlaybackEngine
    {
        private readonly DeviceDispatcher _dispatcher;
        private readonly SafetyGuard _safetyGuard;
        private readonly Stopwatch _stopwatch = new();

        // ---- 状态 ----
        public PlaybackState State { get; private set; } = PlaybackState.Idle;
        public MotionTimeline? CurrentTimeline { get; private set; }
        public double CurrentTimeMs { get; private set; }
        public double DurationMs => CurrentTimeline?.DurationMs ?? 0;
        public double Speed { get; set; } = 1.0;
        public bool Loop { get; set; }
        public string? CurrentFile { get; private set; }

        // ---- 回中 ----
        private Dictionary<string, float>? _returnTargets;  // Stopping 时的回中目标
        private double _returnElapsedMs;
        private const double ReturnDurationMs = 1000;       // 回中时间 1 秒

        // ---- 事件 ----
        public event Action? PlaybackCompleted;
        public event Action<string>? SafetyTriggered;
        public event Action<PlaybackState>? StateChanged;

        // ---- 操作 ----
        public bool Load(string motionFilePath) { /* MotionFileReader → CurrentTimeline */ }
        public void Play() { /* Idle/Paused → Playing, _stopwatch.Restart */ }
        public void PlayFrom(double startMs) { /* CurrentTimeMs = startMs, Play */ }
        public void Pause() { /* Playing → Paused, _stopwatch.Stop */ }
        public void Resume() { /* Paused → Playing, _stopwatch.Start */ }
        public void Stop() { /* → Stopping (记录回中起始值), 或直接 → Idle */ }
        public void Seek(double timeMs) { /* 设置 CurrentTimeMs, 立即 EvaluateAndDispatch */ }
        public void SetChannel(string device, string key, float value) { /* 直接控制 */ }

        /// <summary>
        /// 核心 Tick, 由外部 Timer/Observable.Interval 调用
        /// 推荐间隔: 16.67ms (60fps)
        /// </summary>
        public void Tick(double deltaTimeMs)
        {
            switch (State)
            {
                case PlaybackState.Playing:
                    CurrentTimeMs += deltaTimeMs * Speed;
                    if (CurrentTimeMs >= DurationMs)
                    {
                        if (Loop) { CurrentTimeMs %= DurationMs; }
                        else { Stop(); PlaybackCompleted?.Invoke(); return; }
                    }
                    EvaluateAndDispatch();
                    break;

                case PlaybackState.Stopping:
                    _returnElapsedMs += deltaTimeMs;
                    float t = (float)Math.Min(_returnElapsedMs / ReturnDurationMs, 1.0);
                    float eased = t * t * (3f - 2f * t); // smoothstep
                    foreach (var (key, startVal) in _returnTargets!)
                    {
                        float val = startVal + (0.5f - startVal) * eased; // → 中位
                        _dispatcher.Dispatch(/* parse device+key */, val);
                    }
                    if (t >= 1.0f) { SetState(PlaybackState.Idle); }
                    break;
            }
        }

        private void EvaluateAndDispatch()
        {
            foreach (var track in CurrentTimeline!.Tracks)
            {
                if (track.Muted) continue;
                foreach (var clip in track.Clips)
                {
                    if (CurrentTimeMs < clip.StartMs || CurrentTimeMs > clip.EndMs) continue;
                    double localTime = CurrentTimeMs - clip.StartMs;
                    float raw = InterpolationEngine.Evaluate(clip.Keyframes, localTime);
                    float safe = _safetyGuard.Check(
                        $"{track.DeviceName}.{track.KeyName}", raw, deltaTimeSec);
                    _dispatcher.Dispatch(track.DeviceName, track.KeyName, safe);
                }
            }
        }
    }
}
```

**Tick 驱动方式 (在 IOStudio 启动时初始化)：**

```csharp
// 在 MainWindowViewModel 或专门的 MotionService 初始化类中
Observable.Interval(TimeSpan.FromMilliseconds(16.67))
    .ObserveOn(RxApp.TaskpoolScheduler)
    .Subscribe(_ => _engine.Tick(16.67));
```

**验收：** Load → Play → 连续 Tick 600 帧 (10 秒) → 引擎经历 Playing → 播放完成 → Stop → Stopping → Idle 全流程, 值平滑变化无突跳。

---

### Sprint 1.4 — TCP MotionServer (Day 8-10)

| 编号  | 任务                                       | 新建文件                                  | 工时 |
| ----- | ------------------------------------------ | ----------------------------------------- | ---- |
| 1.4.1 | MotionProtocol (帧编解码)                  | `Services/Motion/MotionProtocol.cs`       | 2h   |
| 1.4.2 | MotionCommandHandler (命令分发)            | `Services/Motion/MotionCommandHandler.cs` | 4h   |
| 1.4.3 | MotionServerService (TCP Server)           | `Services/Motion/MotionServerService.cs`  | 6h   |
| 1.4.4 | IOStudioSettings 增加 MotionServer 配置    | 修改 `Models/IOStudioSettings.cs`         | 1h   |
| 1.4.5 | MainWindowViewModel 启动/停止 MotionServer | 修改 `ViewModels/MainWindowViewModel.cs`  | 1h   |
| 1.4.6 | Python 测试脚本                            | `Tools/motion_test_client.py`             | 2h   |
| 1.4.7 | 端到端集成测试                             | 手动验证                                  | 2h   |

**MotionProtocol 实现：**

```csharp
// Services/Motion/MotionProtocol.cs
using System.Buffers.Binary;

namespace IOStudio.Services.Motion
{
    public static class MotionProtocol
    {
        /// <summary>4 字节大端长度 + UTF-8 JSON</summary>
        public static byte[] EncodeFrame(string json)
        {
            byte[] payload = Encoding.UTF8.GetBytes(json);
            byte[] frame = new byte[4 + payload.Length];
            BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)payload.Length);
            payload.CopyTo(frame, 4);
            return frame;
        }

        public static async Task<string?> ReadFrameAsync(
            NetworkStream stream, CancellationToken ct)
        {
            byte[] lenBuf = new byte[4];
            if (await ReadExactAsync(stream, lenBuf, 4, ct) < 4) return null;
            uint len = BinaryPrimitives.ReadUInt32BigEndian(lenBuf);
            if (len > 1024 * 1024) return null; // 1MB 安全上限
            byte[] payload = new byte[len];
            if (await ReadExactAsync(stream, payload, (int)len, ct) < (int)len) return null;
            return Encoding.UTF8.GetString(payload);
        }

        private static async Task<int> ReadExactAsync(
            NetworkStream s, byte[] buf, int count, CancellationToken ct)
        {
            int total = 0;
            while (total < count)
            {
                int n = await s.ReadAsync(buf.AsMemory(total, count - total), ct);
                if (n == 0) return total;
                total += n;
            }
            return total;
        }
    }
}
```

**IOStudioSettings 新增字段：**

```csharp
// 在 IOStudioSettings.cs 中增加:
[XmlAttribute("MotionServerEnabled")]
public bool MotionServerEnabled { get; set; } = false;

[XmlAttribute("MotionServerPort")]
public int MotionServerPort { get; set; } = 9600;

// Validate() 中增加:
if (MotionServerPort < 1024 || MotionServerPort > 65535) MotionServerPort = 9600;
```

**MotionCommandHandler 命令清单 (Phase 1 完整实现)：**

| 命令           | 参数                 | 调用                          |
| -------------- | -------------------- | ----------------------------- |
| `ping`         | —                    | 返回 `{ok:true}`              |
| `load`         | `file: string`       | `_engine.Load(file)`          |
| `play`         | —                    | `_engine.Play()`              |
| `play_from`    | `time_ms: double`    | `_engine.PlayFrom(time_ms)`   |
| `pause`        | —                    | `_engine.Pause()`             |
| `resume`       | —                    | `_engine.Resume()`            |
| `stop`         | —                    | `_engine.Stop()`              |
| `seek`         | `time_ms: double`    | `_engine.Seek(time_ms)`       |
| `set_speed`    | `speed: double`      | `_engine.Speed = speed`       |
| `set_loop`     | `loop: bool`         | `_engine.Loop = loop`         |
| `set_channel`  | `device, key, value` | `_engine.SetChannel(...)`     |
| `get_state`    | —                    | 返回完整状态                  |
| `list_files`   | —                    | 扫描 `Config/Motion/*.motion` |
| `list_devices` | —                    | `IORoot.Instance.Devices`     |

**Python 测试脚本：**

```python
#!/usr/bin/env python3
"""motion_test_client.py — Motion 协议测试工具"""
import socket, struct, json, time, sys

def send_cmd(sock, cmd_dict):
    payload = json.dumps(cmd_dict).encode('utf-8')
    sock.sendall(struct.pack('>I', len(payload)) + payload)

def recv_msg(sock):
    header = sock.recv(4)
    if len(header) < 4: return None
    length = struct.unpack('>I', header)[0]
    data = b''
    while len(data) < length:
        data += sock.recv(length - len(data))
    return json.loads(data.decode('utf-8'))

def main():
    host = sys.argv[1] if len(sys.argv) > 1 else '127.0.0.1'
    port = int(sys.argv[2]) if len(sys.argv) > 2 else 9600

    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.connect((host, port))
    print(f"Connected to {host}:{port}")

    # 心跳
    send_cmd(sock, {"cmd": "ping"})
    print("ping →", recv_msg(sock))

    # 列出设备
    send_cmd(sock, {"cmd": "list_devices"})
    print("devices →", recv_msg(sock))

    # 列出文件
    send_cmd(sock, {"cmd": "list_files"})
    print("files →", recv_msg(sock))

    # 加载并播放
    send_cmd(sock, {"cmd": "load", "file": "demo_chair_3dof.motion"})
    print("load →", recv_msg(sock))

    send_cmd(sock, {"cmd": "play"})
    print("play →", recv_msg(sock))

    # 接收状态推送 5 秒
    sock.settimeout(0.1)
    end = time.time() + 5
    while time.time() < end:
        try:
            msg = recv_msg(sock)
            if msg and msg.get("type") == "state":
                print(f"  t={msg['time_ms']:.0f}ms  state={msg['playback']}")
        except socket.timeout:
            pass

    send_cmd(sock, {"cmd": "stop"})
    print("stop →", recv_msg(sock))
    sock.close()

if __name__ == "__main__":
    main()
```

**验收标准 (Phase 1 整体)：**

| #   | 验收项          | 通过条件                                          |
| --- | --------------- | ------------------------------------------------- |
| A1  | TCP 连接        | Python 脚本连接 9600 端口成功                     |
| A2  | ping            | 返回 `{"type":"response","cmd":"ping","ok":true}` |
| A3  | list_devices    | 返回当前 IODevice.xml 中的设备列表                |
| A4  | list_files      | 返回 Config/Motion/ 下的 .motion 文件列表         |
| A5  | load            | 加载 demo 文件, 返回 duration_ms 和 track_count   |
| A6  | play → 状态推送 | 收到 50ms 间隔的 state 消息, time_ms 递增         |
| A7  | pause/resume    | 暂停时 time_ms 停止增长, resume 后继续            |
| A8  | stop → 回中     | state 变为 "stopping" → 约 1 秒后变为 "idle"      |
| A9  | seek            | seek 后 time_ms 跳到指定值                        |
| A10 | set_channel     | 直接控制通道值, SetDO 被调用                      |
| A11 | 设备输出        | 连接实际 IODevice 时, 设备动作与时间轴一致        |
| A12 | 安全保护        | 值不超出 Min/Max, 速度不超限                      |
| A13 | 多客户端        | 2 个 Python 脚本同时连接, 都收到状态推送          |
| A14 | 播放完成        | 非循环播放到末尾, 收到 playback_complete 事件     |
| A15 | IOStudio 设置   | 可在 IOStudioSettings.xml 中配置端口和开关        |

---

## Phase 2: 时间轴基础 UI

> **目标：** IOStudio 时间轴编辑窗口，完整的轨道和关键帧编辑能力
> **周期：** W04-W06 (2026-03-30 ~ 2026-04-17)，共 2.5 周
> **前置：** Phase 1 完成
> **里程碑：** IOStudio 可视化编辑轨道和关键帧，Python 测试脚本验证编辑→播放链路
>
> _Unity/UE 客户端 SDK 已移至 Phase 8，基础功能完善后再实施_

### Sprint 2.1 — 时间轴编辑器窗口骨架 (Day 1-3) ✅

| 编号  | 任务                                       | 新建文件                                         | 工时 |
| ----- | ------------------------------------------ | ------------------------------------------------ | ---- |
| 2.1.1 | ✅ MainWindowViewModel 增加打开时间轴命令  | 修改 `MainWindowViewModel.cs`                    | 1h   |
| 2.1.2 | ✅ TimelineEditorWindow (独立窗口)         | `Views/Timeline/TimelineEditorWindow.axaml(.cs)` | 3h   |
| 2.1.3 | ✅ TimelineEditorViewModel                 | `ViewModels/Timeline/TimelineEditorViewModel.cs` | 4h   |
| 2.1.4 | ✅ 窗口布局: 工具栏/轨道面板/曲线区/属性区 | (同 2.1.2)                                       | 2h   |
| 2.1.5 | MotionProjectService (新建/打开/保存)      | `Services/Motion/MotionProjectService.cs`        | 3h   |

**窗口布局草图：**

```
┌─────────────────────────────────────────────────────────────────┐
│  [新建] [打开] [保存] │ [▶ 播放] [⏸ 暂停] [⏹ 停止] [🔁 循环] │ 00:05.234 / 01:00.000 │ 1.0x │
├─────────────────────────────────────────────────────────────────┤
│ 设备面板 (左)              │  时间标尺 + 轨道区 (中)              │
│ ┌─────────────────┐       │  |0s    |5s    |10s   |15s   |20s  │
│ │ Chair            │       │  ├──────────────────────────────────│
│ │  └ 俯仰 OAxis_00│  ◀──▶ │  │ ████████████ clip_01 ████████  ││
│ │  └ 横滚 OAxis_01│       │  │ ████████ clip_02 ████          ││
│ │  └ 升降 OAxis_02│       │  ├──────────────────────────────────│
│ │ Wind             │       │  │ ██████ clip_03 ██████████████  ││
│ │  └ 风速 OAxis_00│       │  ├──────────────────────────────────│
│ └─────────────────┘       │                                     │
├─────────────────────────────────────────────────────────────────┤
│ 属性面板 / 曲线编辑器 (底部, Phase 4 实现)                        │
└─────────────────────────────────────────────────────────────────┘
```

**新建文件清单：**

```
IOStudio/
  Views/
    Timeline/
      TimelineEditorWindow.axaml       # 独立窗口
      TimelineEditorWindow.axaml.cs
  ViewModels/
    Timeline/
      TimelineEditorViewModel.cs       # 主 VM
      TrackViewModel.cs                # 轨道 VM
      ClipViewModel.cs                 # 片段 VM
      KeyframeViewModel.cs             # 关键帧 VM
```

---

### Sprint 2.2 — 时间标尺 + 轨道面板 (Day 4-6) ✅

| 编号  | 任务                                                                                | 新建文件                                   | 工时 |
| ----- | ----------------------------------------------------------------------------------- | ------------------------------------------ | ---- |
| 2.2.1 | ✅ TimeRulerControl (Avalonia DrawingContext 绘制)                                  | `Controls/Timeline/TimeRulerControl.cs`    | 4h   |
| 2.2.2 | ✅ TrackHeaderControl (轨道标题/颜色/静音/锁定) — 内联于 TimelineEditorWindow.axaml | (内联实现)                                 | 3h   |
| 2.2.3 | ✅ TrackClipControl (片段矩形/曲线/关键帧菱形)                                      | `Controls/Timeline/TrackClipControl.cs`    | 4h   |
| 2.2.4 | ✅ 设备/通道选择器 (添加轨道对话框)                                                 | `Views/Timeline/AddTrackDialog.axaml(.cs)` | 2h   |

**TimeRulerControl (SkiaSharp)：**

```csharp
// Controls/Timeline/TimeRulerControl.cs
// 使用 Avalonia 内置的 SKCanvasView 绘制时间标尺
// 功能: 时间刻度标记, 滚轮缩放, 拖拽平移, 播放光标
// 参考 IOStudio 现有的 SkiaSharp 使用模式
```

---

### Sprint 2.3 — 关键帧编辑 (Day 7-10) ✅

| 编号  | 任务                                                                                | 新建文件                                             | 工时 |
| ----- | ----------------------------------------------------------------------------------- | ---------------------------------------------------- | ---- |
| 2.3.1 | ✅ KeyframeMarkerControl (菱形标记 + 选中高亮, 集成在 TrackClipControl)             | `Controls/Timeline/TrackClipControl.cs`              | 2h   |
| 2.3.2 | ✅ 添加关键帧 (双击轨道)                                                            | (交互逻辑)                                           | 2h   |
| 2.3.3 | ✅ 删除关键帧 (Delete 键/右键菜单)                                                  | (交互逻辑)                                           | 1h   |
| 2.3.4 | ✅ 拖拽关键帧 (时间+值)                                                             | (交互逻辑)                                           | 3h   |
| 2.3.5 | ✅ 关键帧属性面板 (时间/值/插值类型)                                                | `Controls/Timeline/KeyframePropertyPanel.axaml(.cs)` | 2h   |
| 2.3.6 | ✅ OAction 映射重构 (KeyName→OActionName, AddTrackDialog解析OAction)                | 多文件重构                                           | 1.5h |
| 2.3.7 | ✅ Bug修复: 新建/打开/保存/添加轨道无反应 (自动初始化项目, 打开/保存接入文件对话框) | ViewModel + View                                     | 0.5h |

### Sprint 2.4 — UI布局重构 + 视频预览 + 视图模式 (Day 11-13) ✅

| 编号  | 任务                                                            | 新建/修改文件                                                           | 工时 |
| ----- | --------------------------------------------------------------- | ----------------------------------------------------------------------- | ---- |
| 2.4.1 | ✅ 布局重构: 参考视频剪辑软件设计 (上:预览+属性, 下:时间轴)     | `TimelineEditorWindow.axaml(.cs)` 全面重写                              | 3h   |
| 2.4.2 | ✅ 视频预览控件 (VideoPreviewControl, 加载/时间同步/传输栏)     | `Controls/Timeline/VideoPreviewControl.axaml(.cs)`                      | 2h   |
| 2.4.3 | ✅ 关键帧属性面板移至右侧 (BorderThickness+ScrollViewer 适配)   | `KeyframePropertyPanel.axaml`                                           | 0.5h |
| 2.4.4 | ✅ Dopesheet/Curves 视图模式切换 (ToggleButton + ViewMode 枚举) | `Models/Motion/TimelineViewMode.cs`, `TrackClipControl.cs`, `ViewModel` | 2h   |
| 2.4.5 | ✅ Dopesheet 渲染 (菱形标记在中心线, 无曲线, 紧凑轨道高度)      | `TrackClipControl.cs`                                                   | 1.5h |
| 2.4.6 | ✅ 添加关键帧按钮 + K 快捷键 (播放头位置插入, 时间轴工具栏)     | `ViewModel` + `Window.axaml.cs`                                         | 1h   |
| 2.4.7 | ✅ 轨道头/片段垂直滚动同步 + 视频时间同步                       | `Window.axaml.cs`                                                       | 0.5h |
| 2.4.8 | ✅ 操作提示栏 (双击/K/Del/右键 快捷键提示)                      | `TimelineEditorWindow.axaml`                                            | 0.3h |

### Sprint 2.5 — 播控重构 + 动态时长 + 值类型 (Day 14-15) ✅

| 编号  | 任务                                                          | 新建/修改文件                                                                  | 工时 |
| ----- | ------------------------------------------------------------- | ------------------------------------------------------------------------------ | ---- |
| 2.5.1 | ✅ 播放控制简化为图标式传输栏 (⏮▶⏸⏹🔁, 置于预览与时间轴之间) | `TimelineEditorWindow.axaml` 重构                                              | 1.5h |
| 2.5.2 | ✅ 传输栏居中时间显示 + 速度选择 + 视图模式快捷切换           | `TimelineEditorWindow.axaml`                                                   | 0.5h |
| 2.5.3 | ✅ MotionTrack.ValueType 字段 (float/bool) + Schema 更新      | `Models/Motion/MotionTrack.cs`, `motion.schema.json`, `demo_chair_3dof.motion` | 1h   |
| 2.5.4 | ✅ AddTrackDialog 新增值类型选择 (Float连续值/Bool开关值)     | `AddTrackDialog.axaml(.cs)`, `AddTrackResult`                                  | 1h   |
| 2.5.5 | ✅ TrackViewModel.ValueType + ValueTypeDisplay + 轨道头徽章   | `TrackViewModel.cs`, `TimelineEditorWindow.axaml`                              | 0.5h |
| 2.5.6 | ✅ 动态时长 RecalculateDuration (关键帧驱动, 自动扩展/收缩)   | `TimelineEditorViewModel.cs`                                                   | 1.5h |
| 2.5.7 | ✅ VideoDurationMs 参考标记 (紫色虚线🎬标签显示在TimeRuler)   | `TimeRulerControl.cs`, `VideoPreviewControl.axaml.cs`                          | 1h   |
| 2.5.8 | ✅ 视频预览为辅助 (所有编辑操作不依赖视频, 无视频正常工作)    | 架构验证                                                                       | 0.3h |

### Sprint 2.6 — UI 打磨 + Bug 修复 + LibVLCSharp (Day 16) ✅

| 编号  | 任务                                                               | 新建/修改文件                                                       | 工时 |
| ----- | ------------------------------------------------------------------ | ------------------------------------------------------------------- | ---- |
| 2.6.1 | ✅ 播控图标改为几何 Path 图形 (替换 emoji ⏮▶⏸⏹🔁)                 | `TimelineEditorWindow.axaml`                                        | 0.5h |
| 2.6.2 | ✅ 视图模式图标改为几何 Path (◇→菱形, 〰→曲线)                     | `TimelineEditorWindow.axaml`                                        | 0.3h |
| 2.6.3 | ✅ 修复跨轨道关键帧选择残留 (ClearSelection 联动)                  | `TimelineEditorWindow.axaml.cs`                                     | 0.5h |
| 2.6.4 | ✅ Bool/Float 值类型区分属性面板 (Bool→ToggleSwitch, Float→Slider) | `KeyframePropertyPanel.axaml(.cs)`, `TimelineEditorWindow.axaml.cs` | 1h   |
| 2.6.5 | ✅ LibVLCSharp 视频预览集成 (加载/播放/暂停/seek/时间同步)         | `VideoPreviewControl.axaml(.cs)`, `IOStudio.csproj`                 | 2h   |
| 2.6.6 | ✅ 视频预览与时间轴播放状态同步 (Play/Pause/Stop 联动)             | `TimelineEditorWindow.axaml.cs`                                     | 0.5h |

### Sprint 2.7 — 视频预览优化 + UI 打磨 (Day 17) ✅

| 编号  | 任务                                                                              | 新建/修改文件                                             | 工时 |
| ----- | --------------------------------------------------------------------------------- | --------------------------------------------------------- | ---- |
| 2.7.1 | ✅ 视频预览最小宽度缩减 + Viewbox 等比缩放 (MinWidth 300→120, Stretch=Uniform)    | `TimelineEditorWindow.axaml`, `VideoPreviewControl.axaml` | 0.5h |
| 2.7.2 | ✅ LibVLC 延迟初始化优化 (EnsureVlcInitialized 首次加载时才初始化)                | `VideoPreviewControl.axaml.cs`                            | 0.5h |
| 2.7.3 | ✅ 播控按钮 Hover 背景色修复 (浅白→深色 #334155)                                  | `TimelineEditorWindow.axaml`                              | 0.3h |
| 2.7.4 | ✅ 视图模式按钮 Hover/选中背景色修复 (#334155/#1e3a5f)                            | `TimelineEditorWindow.axaml`                              | 0.3h |
| 2.7.5 | ✅ 视频文件列表侧栏 + 双击切换预览 (VideoFileItem, ObservableCollection, ListBox) | `VideoPreviewControl.axaml(.cs)`                          | 1.5h |
| 2.7.6 | ✅ Loop ToggleButton 统一 transport-toggle 样式                                   | `TimelineEditorWindow.axaml`                              | 0.2h |

### Sprint 2.8 — 视频渲染修复 + 时间轴统一 (Day 18) ✅

| 编号  | 任务                                                                                   | 新建/修改文件                                                     | 工时 |
| ----- | -------------------------------------------------------------------------------------- | ----------------------------------------------------------------- | ---- |
| 2.8.1 | ✅ VlcVideoHost 子窗口渲染 (修复 VLC 全屏覆盖问题, Win32 CreateWindowEx 创建独立 HWND) | `VlcVideoHost.cs` (新建), `VideoPreviewControl.axaml(.cs)`        | 1.5h |
| 2.8.2 | ✅ 移除视频独立时间轴 (去掉 VideoSeekBar/TimeDisplay, 统一由主时间轴驱动)              | `VideoPreviewControl.axaml(.cs)`, `TimelineEditorWindow.axaml.cs` | 0.5h |
| 2.8.3 | ✅ SyncTime 智能同步 (仅暂停时 seek, 播放时 VLC 独立运行避免时钟冲突)                  | `VideoPreviewControl.axaml.cs`                                    | 0.3h |

### Sprint 2.9 — 专业布局重构 + 事件编辑器 (Day 19) ✅

| 编号  | 任务                                                                                           | 新建/修改文件                                                                                                                                                           | 工时 |
| ----- | ---------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---- |
| 2.9.1 | ✅ 视频列表与预览面板可调宽度 (GridSplitter 列分隔, MinWidth/MaxWidth 约束)                    | `VideoPreviewControl.axaml`                                                                                                                                             | 0.5h |
| 2.9.2 | ✅ 播控栏固定高度 (5 行 Grid 布局: 预览/分隔/36px 播控/分隔/时间轴, 拖拽时播控不变形)          | `TimelineEditorWindow.axaml`                                                                                                                                            | 0.5h |
| 2.9.3 | ✅ 视频预览黑色背景修复 (移除外层 Border 包裹, DockPanel 直接设 Background="Black")            | `VideoPreviewControl.axaml`                                                                                                                                             | 0.3h |
| 2.9.4 | ✅ 专业菜单栏 (Avalonia Menu: 文件/编辑/视图/播放, InputGesture 快捷键, SaveAsCommand)         | `TimelineEditorWindow.axaml`, `TimelineEditorViewModel.cs`                                                                                                              | 1h   |
| 2.9.5 | ✅ PlayheadOverlay 空轨道可见播放头 (自定义 Control + StyledProperty + Render 红色线)          | `PlayheadOverlay.cs` (新建), `TimelineEditorWindow.axaml`                                                                                                               | 0.8h |
| 2.9.6 | ✅ 关键帧事件编辑器 (MotionEvent 模型 + KeyframeViewModel 扩展 + 属性面板 UI + ViewModel 联动) | `MotionEvent.cs` (新建), `MotionKeyframe.cs`, `KeyframeViewModel.cs`, `KeyframePropertyPanel.axaml(.cs)`, `TimelineEditorWindow.axaml.cs`, `TimelineEditorViewModel.cs` | 1.5h |

### Sprint 2.10 — 时间轴交互优化 + 事件独立管理 (Day 20) ✅

| 编号   | 任务                                                                                                             | 新建/修改文件                                                                                                                          | 工时 |
| ------ | ---------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------- | ---- |
| 2.10.1 | ✅ 播放头无限拖拽 (移除 SeekToX/SeekTo 中 DurationMs 上限 clamp)                                                 | `TimeRulerControl.cs`, `TimelineEditorViewModel.cs`                                                                                    | 0.2h |
| 2.10.2 | ✅ 缩放适配窗口按钮 (ZoomToFit: PixelsPerMs = viewportWidth / DurationMs, Ctrl+0 快捷键)                         | `TimelineEditorViewModel.cs`, `TimelineEditorWindow.axaml(.cs)`                                                                        | 0.5h |
| 2.10.3 | ✅ Duration UI 反馈修复 (RecalculateDuration 重写: clip.EndMs 精确跟踪关键帧 + 500ms 余量, 不再无限增长)         | `TimelineEditorViewModel.cs`                                                                                                           | 0.5h |
| 2.10.4 | ✅ 自动吸附工具 (IsSnapEnabled 开关, 吸附点收集: 关键帧/事件/视频结尾, SeekTo + MoveKeyframe 吸附逻辑)           | `TimelineEditorViewModel.cs`, `TimelineEditorWindow.axaml`                                                                             | 0.8h |
| 2.10.5 | ✅ 独立事件管理 (TimelineEvent 模型 + MotionTimeline.Events 列表 + 添加/删除/编辑 + 时间标尺事件标记 + E 快捷键) | `TimelineEvent.cs` (新建), `MotionTimeline.cs`, `TimelineEditorViewModel.cs`, `TimeRulerControl.cs`, `TimelineEditorWindow.axaml(.cs)` | 1.5h |
| 2.10.6 | ✅ 轨道删除 + 顺序调整 (删除按钮 ✕ + 上移▲下移▼按钮 + 菜单"删除选中轨道" + MoveTrackUp/Down)                     | `TimelineEditorViewModel.cs`, `TimelineEditorWindow.axaml(.cs)`                                                                        | 0.8h |
| 2.10.7 | ✅ Bool 曲线阶梯渲染 (ValueType StyledProperty + DrawBoolStepCurve 水平→垂直阶梯形, 无过渡)                      | `TrackClipControl.cs`, `TimelineEditorWindow.axaml`                                                                                    | 0.5h |

### Sprint 2.11 — 编辑器 Bug 修复 + 交互增强 (Day 21) ✅

| 编号   | 任务                                                                                                                                                   | 新建/修改文件                                                                            | 工时 |
| ------ | ------------------------------------------------------------------------------------------------------------------------------------------------------ | ---------------------------------------------------------------------------------------- | ---- |
| 2.11.1 | ✅ 传输栏高度异常修复 (3行Grid替代5行: 预览/分隔/DockPanel(传输栏+时间轴), 消除 GridSplitter 影响传输栏)                                               | `TimelineEditorWindow.axaml`                                                             | 0.3h |
| 2.11.2 | ✅ 吸附截断修复 (SeekTo 移除 ApplySnap, 仅 MoveKeyframe 保留吸附; RecalculateDuration 只增不减)                                                        | `TimelineEditorViewModel.cs`                                                             | 0.3h |
| 2.11.3 | ✅ 事件选中/编辑 (TimeRulerControl 事件命中测试 + 左键选中/拖拽 + 右键删除菜单 + 属性面板 DisplayEvent + 选中高亮)                                     | `TimeRulerControl.cs`, `KeyframePropertyPanel.axaml.cs`, `TimelineEditorWindow.axaml.cs` | 1h   |
| 2.11.4 | ✅ 轨道右键菜单 + 拖拽排序 (ContextMenu: 上移/下移/静音/锁定/删除 + PointerPressed/Moved/Released 拖拽重排)                                            | `TimelineEditorWindow.axaml`, `TimelineEditorWindow.axaml.cs`                            | 0.8h |
| 2.11.5 | ✅ Bool 关键帧值修正 (AddKeyframeAtTime/MoveKeyframe/UpdateKeyframeProperties/TrackClipControl 拖拽 + HitTest + DrawMarkers 全面 ≥0.5→1 / <0.5→0 约束) | `TimelineEditorViewModel.cs`, `TrackClipControl.cs`                                      | 0.5h |

### Sprint 2.12 — 事件校验 + 属性面板增强 (Day 23) ✅

| 编号   | 任务                                                                                                                                                                                                                       | 修改文件                                                                                          | 工时 |
| ------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------- | ---- |
| 2.12.1 | ✅ 事件名唯一性校验 (IsEventNameUnique + GenerateUniqueEventName 自动生成不重复名; UpdateEvent 返回 bool 拒绝重复; 属性面板 EventNameDuplicateWarning 红色提示)                                                            | `TimelineEditorViewModel.cs`, `KeyframePropertyPanel.axaml(.cs)`, `TimelineEditorWindow.axaml.cs` | 0.8h |
| 2.12.2 | ✅ 属性面板关键帧/事件区分 (PanelTitle 动态切换 "关键帧属性"↔"⚡ 事件属性"; DeleteButton 动态切换 "删除关键帧"↔"删除事件"; DeleteEventRequested 独立事件删除; NoSelectionHint 提示同时包含关键帧和事件; ClearDisplay 重置) | `KeyframePropertyPanel.axaml(.cs)`, `TimelineEditorWindow.axaml.cs`                               | 0.5h |

**验收标准 (Phase 2 整体)：**

| #   | 验收项     | 通过条件                                  |
| --- | ---------- | ----------------------------------------- |
| B1  | 打开编辑器 | 菜单/按钮打开 TimelineEditorWindow        |
| B2  | 新建项目   | 新建 → 选择设备/OAction → 生成空时间轴    |
| B3  | 添加轨道   | 从设备列表选择 → 轨道出现在面板中         |
| B4  | 添加关键帧 | 双击轨道 → 菱形标记出现                   |
| B5  | 拖拽关键帧 | 鼠标拖拽改变时间/值, 实时更新             |
| B6  | 保存/打开  | 保存 .motion → 关闭 → 重新打开 → 内容一致 |
| B7  | 时间标尺   | 滚轮缩放, 刻度自适应                      |

---

## Phase 2.5: 编辑器增强 — 专业改进

> **目标：** 提升编辑器专业度和用户体验, 补齐核心编辑能力短板
> **周期：** Phase 2 完成后立即开始, 预估 1.5~2 周
> **前置：** Phase 2 完成
> **来源：** Sprint 2.11 Task 6 专业改进方向建议 + 用户反馈
> **优先级说明：** P0 = 核心体验必备, P1 = 专业工具标配, P2 = 锦上添花

### Sprint 2.5A — 核心编辑增强 (Day 1-4) ✅

| 编号   | 任务                                                                          | 修改/新建文件                                                                | 工时 | 优先级 |
| ------ | ----------------------------------------------------------------------------- | ---------------------------------------------------------------------------- | ---- | ------ |
| 2.5A.1 | ✅ Undo/Redo 撤销重做系统 (Command 模式, 关键帧增删改/轨道增删/事件增删)      | 新建 `Services/Motion/UndoRedoService.cs`, 修改 `TimelineEditorViewModel.cs` | 6h   | P0     |
| 2.5A.2 | ✅ 多关键帧框选 (Shift+Click 追加选择, Ctrl+Click 切换选择, 拖框批量选择)     | 修改 `TrackClipControl.cs`, `TimelineEditorViewModel.cs`                     | 4h   | P0     |
| 2.5A.3 | ✅ 多关键帧批量操作 (批量删除/批量移动/批量修改插值类型)                      | 修改 `TimelineEditorViewModel.cs`, `KeyframePropertyPanel.axaml.cs`          | 3h   | P0     |
| 2.5A.4 | ✅ 自动保存 + Dirty 标记 (60s 间隔自动保存, 标题栏 \* 标记未保存, 关闭时提示) | 修改 `TimelineEditorViewModel.cs`, `TimelineEditorWindow.axaml(.cs)`         | 2h   | P0     |

### Sprint 2.5B — 交互体验提升 (Day 5-8) ✅

| 编号   | 任务                                                                                    | 修改/新建文件                                                              | 工时 | 优先级 |
| ------ | --------------------------------------------------------------------------------------- | -------------------------------------------------------------------------- | ---- | ------ |
| 2.5B.1 | ✅ 键盘快捷键基础体系 (Space 播放, Del 删除, Ctrl+Z/Y 撤销, Ctrl+S 保存, Home/End 跳转) | 修改 `TimelineEditorWindow.axaml(.cs)`                                     | 3h   | P1     |
| 2.5B.2 | ✅ 缩放滑块 + 时间轴全局视图 (底部 Slider 控制 PixelsPerSecond, 可选 Minimap 缩略)      | 修改 `TimelineEditorWindow.axaml`, `TimeRulerControl.cs`                   | 3h   | P1     |
| 2.5B.3 | ✅ 网格对齐辅助 (拖拽关键帧时显示虚线对齐参考, 对齐到其他轨道同时间关键帧)              | 修改 `TrackClipControl.cs`                                                 | 2h   | P2     |
| 2.5B.4 | ✅ 轨道分组/折叠 (TrackGroup 模型, 可折叠/展开一组轨道, 批量静音/锁定)                  | 新建 `Models/Motion/TrackGroup.cs`, 修改 `TimelineEditorWindow.axaml(.cs)` | 3h   | P2     |
| 2.5B.5 | ✅ 轨道颜色自定义 (右键菜单选色, 关键帧/曲线/波形同色)                                  | 修改 `TrackViewModel.cs`, `TrackClipControl.cs`                            | 1h   | P2     |

**验收标准 (Phase 2.5)：**

| #   | 验收项    | 通过条件                                               |
| --- | --------- | ------------------------------------------------------ |
| B+1 | Undo/Redo | 添加关键帧→Ctrl+Z→关键帧消失→Ctrl+Y→关键帧恢复         |
| B+2 | 多选      | Shift+Click 选择多个关键帧, 属性面板显示批量模式       |
| B+3 | 批量操作  | 选中 3 个关键帧→按 Del→全部删除; 批量修改插值→全部更新 |
| B+4 | 自动保存  | 编辑后 60s 自动保存, 标题显示 \*, 关闭提示保存         |
| B+5 | 快捷键    | Space 切换播放, Delete 删除, Ctrl+Z 撤销               |
| B+6 | 缩放滑块  | 拖动底部滑块, 时间轴缩放级别变化                       |

---

## Phase 3: 播放预览 + 导出 + 加密

> **目标：** 编辑器内实时预览 + 完整的编辑→播放链路 + .mtn 加密导出
> **周期：** W07-W09 (2026-04-20 ~ 2026-05-08)，共 2 周
> **前置：** Phase 2 完成
> **里程碑：** 编辑 → 预览 → 导出 .motion (内部) / .mtn (买家) → TCP 触发播放。加密链路验证。

### Sprint 3.1 — 编辑器内预览播放 (Day 1-4) ✅

> **注：** 以下任务大部分已在 Phase 2 迭代中提前完成（Sprint 2.3/2.5/2.6）

| 编号  | 任务                                                              | 修改/新建文件                     | 工时 |
| ----- | ----------------------------------------------------------------- | --------------------------------- | ---- |
| 3.1.1 | ✅ TimelineEditorVM 集成 MotionPlaybackEngine (Sprint 2.3 已完成) | 修改 `TimelineEditorViewModel.cs` | 3h   |
| 3.1.2 | ✅ 播放控制工具栏 UI (Sprint 2.5 已完成, 几何 Path 图标)          | 修改 `TimelineEditorWindow.axaml` | 2h   |
| 3.1.3 | ✅ 播放光标跟随 (Sprint 2.3/2.5 已完成, PlayheadOverlay)          | 修改 `TimeRulerControl.cs`        | 3h   |
| 3.1.4 | ✅ 播放时高亮当前关键帧                                           | 修改 `TrackClipControl.cs`        | 1h   |
| 3.1.5 | ✅ 播放时主窗口 DO 值同步变化                                     | 联动事件                          | 2h   |

**预览播放流程：**

```
[▶ 按钮] → TimelineEditorVM.PlayCommand
  → MotionPlaybackEngine.Load(当前编辑的Timeline)
  → MotionPlaybackEngine.Play()
  → Observable.Interval(16.67ms) → Engine.Tick()
  → DeviceDispatcher → IODevice.SetDO()
  → 同时: 更新 TimeRuler 光标位置 (UI 线程)
  → 同时: MotionServer 广播状态给已连接客户端
```

---

### Sprint 3.2 — 导出 + 联动 (Day 5-7)

| 编号  | 任务                                      | 修改/新建文件                            | 工时 |
| ----- | ----------------------------------------- | ---------------------------------------- | ---- |
| 3.2.1 | ✅ MotionExportService (VM→JSON 明文导出) | `Services/Motion/MotionExportService.cs` | 2h   |
| 3.2.2 | ✅ 导出对话框 (文件名/路径/格式选择)      | `Views/Timeline/ExportDialog.axaml(.cs)` | 1.5h |
| 3.2.3 | 导出后自动通知 MotionServer 客户端        | 修改 `MotionServerService.cs`            | 1h   |
| 3.2.4 | MotionServer list_files 自动刷新          | 修改 `MotionCommandHandler.cs`           | 0.5h |
| 3.2.5 | 端到端测试: 编辑→导出→Python 加载播放     | 手动验证                                 | 2h   |

---

### Sprint 3.3 — .mtn 加密导出 (Day 8-10) 🔒 延后实施

| 编号  | 任务                                              | 修改/新建文件                                             | 工时 |
| ----- | ------------------------------------------------- | --------------------------------------------------------- | ---- |
| 3.3.1 | MotionCryptoService (AES-256-GCM 加解密)          | `Services/Motion/MotionCryptoService.cs`                  | 4h   |
| 3.3.2 | AppSecret 分散存储 + 运行时组装                   | (同上, 密钥管理代码)                                      | 2h   |
| 3.3.3 | MotionFileReader 支持 .mtn 解密加载               | 修改 `Services/Motion/MotionFileReader.cs`                | 2h   |
| 3.3.4 | "导出发行版" 按钮 → 生成 .mtn 文件                | 修改 `ExportDialog.axaml(.cs)` + `MotionExportService.cs` | 2h   |
| 3.3.5 | MotionCommandHandler.list_files 区分 .motion/.mtn | 修改 `MotionCommandHandler.cs`                            | 0.5h |
| 3.3.6 | 验证: .mtn 加密→解密往返, 篡改检测                | 手动测试                                                  | 1.5h |

**MotionCryptoService 核心实现：**

```csharp
// Services/Motion/MotionCryptoService.cs
namespace IOStudio.Services.Motion
{
    public static class MotionCryptoService
    {
        // .mtn 文件结构: Magic(4) + Ver(1) + Salt(16) + IV(12) + Cipher(N) + Tag(16)
        private static readonly byte[] Magic = { 0x4D, 0x54, 0x4E, 0x00 };
        private const byte FormatVersion = 0x01;

        public static byte[] Encrypt(string motionJson)
        {
            byte[] plaintext = Encoding.UTF8.GetBytes(motionJson);
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] iv = RandomNumberGenerator.GetBytes(12);
            byte[] key = DeriveKey(GetAppSecret(), salt);

            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(iv, plaintext, ciphertext, tag);

            var result = new byte[4 + 1 + 16 + 12 + ciphertext.Length + 16];
            int offset = 0;
            Magic.CopyTo(result, offset); offset += 4;
            result[offset++] = FormatVersion;
            salt.CopyTo(result, offset); offset += 16;
            iv.CopyTo(result, offset); offset += 12;
            ciphertext.CopyTo(result, offset); offset += ciphertext.Length;
            tag.CopyTo(result, offset);
            return result;
        }

        public static string? Decrypt(byte[] mtnData) { /* 见架构设计 § 十 */ }
        private static byte[] DeriveKey(byte[] secret, byte[] salt) { /* HMAC-SHA256 */ }
        private static byte[] GetAppSecret() { /* 分散常量 + 运行时组装 */ }
    }
}
```

**验收标准 (Phase 3)：**

| #   | 验收项       | 通过条件                                   |
| --- | ------------ | ------------------------------------------ |
| C1  | 预览播放     | 编辑器内按 ▶, 设备运动, 光标移动           |
| C2  | 暂停/恢复    | 光标停止/继续, 设备输出一致                |
| C3  | 循环播放     | 到末尾自动回到开头继续播放                 |
| C4  | 速度控制     | 0.5x/1x/2x 速度, 设备运动速率对应变化      |
| C5  | 导出 .motion | 导出的 JSON 完整, 重新加载内容一致         |
| C6  | 导出 .mtn    | 加密文件可被 MotionFileReader 正确解密加载 |
| C7  | .mtn 防篡改  | 修改 .mtn 任意字节后, 解密失败返回 null    |
| C8  | 导出通知     | 导出后已连接客户端收到 file_loaded 事件    |
| C9  | 完整链路     | 编辑→导出→Python 加载播放→设备运动         |

---

## Phase 4: 曲线编辑器

> **目标：** 精细的贝塞尔曲线编辑, 可视化调整关键帧和插值
> **周期：** W09-W10 (2026-05-04 ~ 2026-05-15)，共 2 周
> **前置：** Phase 3 完成

### Sprint 4.1 — 曲线渲染 (Day 1-4) ✅

| 编号  | 任务                                     | 新建文件                                  | 工时 |
| ----- | ---------------------------------------- | ----------------------------------------- | ---- |
| 4.1.1 | ✅ CurveEditorControl (Avalonia 画布)    | `Controls/Timeline/CurveEditorControl.cs` | 5h   |
| 4.1.2 | ✅ 曲线绘制: 根据关键帧+插值类型绘制曲线 | (同上)                                    | 4h   |
| 4.1.3 | ✅ 关键帧绘制: 菱形节点 + 选中高亮       | (同上)                                    | 2h   |
| 4.1.4 | ✅ 网格/坐标轴绘制                       | (同上)                                    | 2h   |

### Sprint 4.2 — 曲线交互 (Day 5-10) ✅

| 编号  | 任务                       | 修改文件                                      | 工时 |
| ----- | -------------------------- | --------------------------------------------- | ---- |
| 4.2.1 | ✅ 关键帧点击选中          | `CurveEditorControl.cs`                       | 2h   |
| 4.2.2 | ✅ 关键帧拖拽 (时间+值)    | (同上)                                        | 3h   |
| 4.2.3 | ✅ 贝塞尔控制手柄显示+拖拽 | (同上)                                        | 4h   |
| 4.2.4 | ✅ 框选/多选关键帧         | (同上)                                        | 3h   |
| 4.2.5 | ✅ 滚轮缩放/中键拖拽平移   | (同上)                                        | 2h   |
| 4.2.6 | ✅ 网格吸附 (Shift 禁用)   | (同上)                                        | 1h   |
| 4.2.7 | ✅ 插值类型切换 (右键菜单) | (同上)                                        | 1h   |
| 4.2.8 | ✅ CurveEditorViewModel    | `ViewModels/Timeline/CurveEditorViewModel.cs` | 3h   |

**验收标准 (Phase 4)：**

| #   | 验收项     | 通过条件                               |
| --- | ---------- | -------------------------------------- |
| D1  | 曲线显示   | 选中轨道后底部显示对应曲线             |
| D2  | 贝塞尔编辑 | 拖拽控制手柄, 曲线实时变化             |
| D3  | 框选       | 鼠标拖出矩形, 框内关键帧全部选中       |
| D4  | 缩放/平移  | 滚轮缩放, 中键拖拽, 响应流畅           |
| D5  | 实时预览   | 编辑曲线后立即按播放, 设备输出匹配曲线 |

---

## Phase 4.5: 优化与体验提升 ✅

> **目标：** 对标行业标准 (After Effects / Premiere / Blender NLA)，全面提升编辑器交互体验、视觉风格和操作效率
> **周期：** W10.5-W12 (Phase 4 完成后立即开始)，约 2 周
> **前置：** Phase 4 完成
> **优先级说明：** P0 = 必须 (影响基本体验), P1 = 重要 (专业级), P2 = 增强 (锦上添花), P3 = 美化

### Sprint 4.5.1 — P0 核心体验优化 (Day 1-3) ✅

| 编号    | 任务                                                                                          | 修改文件                                                                                         | 工时 | 优先级 |
| ------- | --------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------ | ---- | ------ |
| 4.5.1.1 | ✅ 时间标尺刻度自适应增强 (增加 10ms/20ms/50ms 细粒度 + 120s/300s/600s 大范围 + 标签密度控制) | `Controls/Timeline/TimeRulerControl.cs`                                                          | 1h   | P0     |
| 4.5.1.2 | ✅ 播放时自动滚动 (播放头到达可视区右侧 85% 时自动平滑滚动)                                   | `ViewModels/Timeline/TimelineEditorViewModel.cs`, `Views/Timeline/TimelineEditorWindow.axaml.cs` | 2h   | P0     |
| 4.5.1.3 | ✅ 深色主题支持 (Dark/Light 切换, 全局资源字典)                                               | 新建 `Assets/Themes/DarkTheme.axaml`, `Assets/Themes/LightTheme.axaml`, 修改 `App.axaml`         | 4h   | P0     |

### Sprint 4.5.2 — P1 专业功能增强 (Day 4-7) ✅

| 编号    | 任务                                                                | 修改文件                                                                                         | 工时 | 优先级 |
| ------- | ------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------ | ---- | ------ |
| 4.5.2.1 | ✅ 轨道分组折叠/展开 UI (分组行 + 折叠切换 + 子轨道缩进)            | `Views/Timeline/TimelineEditorWindow.axaml(.cs)`                                                 | 3h   | P1     |
| 4.5.2.2 | ✅ Dopesheet 框选 (鼠标拖出矩形选择区域, 框内关键帧全部选中)        | `Controls/Timeline/TrackClipControl.cs`                                                          | 3h   | P1     |
| 4.5.2.3 | ✅ 关键帧复制/粘贴 (Ctrl+C/V, 支持跨轨道粘贴, 相对时间偏移)         | `ViewModels/Timeline/TimelineEditorViewModel.cs`, `Views/Timeline/TimelineEditorWindow.axaml.cs` | 2h   | P1     |
| 4.5.2.4 | ✅ 轨道高度自定义 (全局紧凑/展开模式切换 + 快捷键)                  | `ViewModels/Timeline/TrackViewModel.cs`, `Views/Timeline/TimelineEditorWindow.axaml(.cs)`        | 2h   | P1     |
| 4.5.2.5 | ✅ 紧凑轨道头布局 (缩减 padding, 图标化静音/锁定, 信息密度提升)     | `Views/Timeline/TimelineEditorWindow.axaml`                                                      | 1.5h | P1     |
| 4.5.2.6 | ✅ 属性面板增强 (显示选中关键帧数量/时间范围统计, 批量编辑插值类型) | `Controls/Timeline/KeyframePropertyPanel.axaml(.cs)`                                             | 2h   | P1     |

### Sprint 4.5.3 — P2/P3 视觉增强 (Day 8-10) ✅

| 编号    | 任务                                                              | 修改文件                                                                                  | 工时 | 优先级 |
| ------- | ----------------------------------------------------------------- | ----------------------------------------------------------------------------------------- | ---- | ------ |
| 4.5.3.1 | ✅ 工作区域标记 (I/O 键设置入点/出点, 循环/导出范围可视化)        | `ViewModels/Timeline/TimelineEditorViewModel.cs`, `Controls/Timeline/TimeRulerControl.cs` | 3h   | P2     |
| 4.5.3.2 | ✅ 拖拽排序视觉反馈 (拖拽时半透明预览 + 插入位置指示线)           | `Views/Timeline/TimelineEditorWindow.axaml.cs`                                            | 2h   | P2     |
| 4.5.3.3 | ✅ 图标化操作按钮 + 工具栏图标体系 (替换文字按钮为图标, 统一风格) | `Views/Timeline/TimelineEditorWindow.axaml`, `Assets/Icons/`                              | 2h   | P2     |
| 4.5.3.4 | ✅ 迷你曲线预览 (轨道片段内显示缩略波形/曲线)                     | `Controls/Timeline/TrackClipControl.cs`                                                   | 2h   | P2     |
| 4.5.3.5 | ✅ UI 动画过渡 (面板展开/折叠/选中 状态的平滑过渡动画)            | `Views/Timeline/TimelineEditorWindow.axaml`                                               | 1.5h | P3     |

**验收标准 (Phase 4.5)：**

| #   | 验收项       | 通过条件                                                   |
| --- | ------------ | ---------------------------------------------------------- |
| D+1 | 刻度自适应   | 缩放到极限 (0.001~1.0 ppm) 时刻度始终可读, 不重叠          |
| D+2 | 播放自动滚动 | 播放中播放头始终在可视区域内, 滚动平滑无跳跃               |
| D+3 | 深色主题     | 一键切换 Dark/Light, 所有控件正确适配                      |
| D+4 | 分组折叠     | 点击分组行折叠/展开, 子轨道正确隐藏/显示                   |
| D+5 | 框选         | 鼠标拖出矩形, 框内关键帧高亮选中, 框外取消                 |
| D+6 | 复制粘贴     | Ctrl+C 选中关键帧 → 移动播放头 → Ctrl+V 粘贴, 时间偏移正确 |
| D+7 | 工作区域     | I/O 设置入出点, 循环播放限定在该范围内                     |
| D+8 | 拖拽反馈     | 拖拽轨道排序时有插入位置蓝线指示                           |

---

## Phase 4.6: 交互修复与功能完善 ✅

> **目标：** 修复用户测试反馈的 7 项交互问题，完善分组管理、视频预览、框选、时长设置等功能
> **周期：** 紧随 Phase 4.5

### Sprint 4.6.1 — 交互 Bug 修复 + 功能增强 ✅

| 编号    | 任务                                                                                    | 修改文件                                                                                           | 状态 |
| ------- | --------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------- | ---- |
| 4.6.1.1 | ✅ 播放头吸附修复 (SeekTo 应用 ApplySnap)                                               | `ViewModels/Timeline/TimelineEditorViewModel.cs`                                                   | ✅   |
| 4.6.1.2 | ✅ 框选关键帧修复 (Dopesheet 模式忽略 Y 约束 + BoxSelectCompleted 事件接线)             | `Controls/Timeline/TrackClipControl.cs`, `Views/Timeline/TimelineEditorWindow.axaml.cs`            | ✅   |
| 4.6.1.3 | ✅ 视频加载不自动播放 (静音初始化 + seek 到播放头位置)                                  | `Controls/Timeline/VideoPreviewControl.axaml.cs`                                                   | ✅   |
| 4.6.1.4 | ✅ 视频预览工具栏 (缩放 + 全屏按钮)                                                     | `Controls/Timeline/VideoPreviewControl.axaml(.cs)`                                                 | ✅   |
| 4.6.1.5 | ✅ 可编辑总时长 (双击编辑 + ManualDurationMs 手动覆盖 + RecalculateDuration 尊重手动值) | `ViewModels/Timeline/TimelineEditorViewModel.cs`, `Views/Timeline/TimelineEditorWindow.axaml(.cs)` | ✅   |
| 4.6.1.6 | ✅ 分组管理重构 (面板级右键菜单 + 拖拽轨道到分组 + 全部折叠/展开)                       | `Views/Timeline/TimelineEditorWindow.axaml(.cs)`, `ViewModels/Timeline/TimelineEditorViewModel.cs` | ✅   |
| 4.6.1.7 | ✅ 帧模式分析 (结论: 时间模式 ms 已满足 API 需求, 无需帧模式)                           | 设计分析                                                                                           | ✅   |

---

## Phase 4.7: 深度交互修复与轴输出支持 ✅

> **目标：** 修复 6 项用户测试反馈问题 (分组创建、关键帧拖拽、视频预览、轴输出支持)
> **周期：** 紧随 Phase 4.6

### Sprint 4.7.1 — Bug 修复 + 新功能 ✅

| 编号    | 任务                                                                                                                                                               | 修改文件                                                                                                                                                                                                                                                | 状态 |
| ------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---- |
| 4.7.1.1 | ✅ 面板右键新建分组修复 (弹出轨道多选列表, 选择轨道加入分组)                                                                                                       | `Views/Timeline/TimelineEditorWindow.axaml.cs`                                                                                                                                                                                                          | ✅   |
| 4.7.1.2 | ✅ 关键帧右拖拽修复 (移除 clip.EndMs 上限 clamp, 允许拖拽扩展 clip)                                                                                                | `Controls/Timeline/TrackClipControl.cs`                                                                                                                                                                                                                 | ✅   |
| 4.7.1.3 | ✅ 追加关键帧修复 (允许在 clip.EndMs 之后添加关键帧, 自动扩展 clip)                                                                                                | `ViewModels/Timeline/TimelineEditorViewModel.cs`                                                                                                                                                                                                        | ✅   |
| 4.7.1.4 | ✅ 视频自动播放彻底修复 (添加 \_isInitializing 标志, OnMediaPlaying 回调跳过初始化阶段)                                                                            | `Controls/Timeline/VideoPreviewControl.axaml.cs`                                                                                                                                                                                                        | ✅   |
| 4.7.1.5 | ✅ 视频全屏改为原地全屏 (隐藏属性面板+时间轴, 视频区占满窗口, ESC 退出)                                                                                            | `Controls/Timeline/VideoPreviewControl.axaml(.cs)`, `Views/Timeline/TimelineEditorWindow.axaml(.cs)`                                                                                                                                                    | ✅   |
| 4.7.1.6 | ✅ 新建轨道支持 Axis 输出轴 (AddTrackDialog 增加输出类型选择, MotionTrack 增加 OutputType/AxisName, DeviceDispatcher 增加 DispatchAxis, PlaybackEngine 支持轴分发) | `Models/Motion/MotionTrack.cs`, `Views/Timeline/AddTrackDialog.axaml(.cs)`, `ViewModels/Timeline/TimelineEditorViewModel.cs`, `ViewModels/Timeline/TrackViewModel.cs`, `Services/Motion/DeviceDispatcher.cs`, `Services/Motion/MotionPlaybackEngine.cs` | ✅   |

---

## Phase 4.8: 回归测试修复 (3 项) ✅

> **目标：** 修复 Phase 4.7 后用户测试发现的 3 项回归/遗留问题
> **周期：** 紧随 Phase 4.7

### Sprint 4.8.1 — 回归修复 ✅

| 编号    | 任务                                                                                                                                                         | 修改文件                                                                                         | 状态 |
| ------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------ | ---- |
| 4.8.1.1 | ✅ AddTrackDialog 打开崩溃修复 (XAML 初始化期间 OutputTypeComboBox 触发 SelectionChanged, FindControl 抛 InvalidOperationException; 增加 \_initialized 守卫) | `Views/Timeline/AddTrackDialog.axaml.cs`                                                         | ✅   |
| 4.8.1.2 | ✅ 轨道面板右键新建分组修复 (在轨道级别 ContextMenu 中追加"添加轨道"和"新建分组"菜单项; 移除 Tracks.Count==0 的 early return)                                | `Views/Timeline/TimelineEditorWindow.axaml`, `Views/Timeline/TimelineEditorWindow.axaml.cs`      | ✅   |
| 4.8.1.3 | ✅ 视频自动播放深度修复 (移除 DispatcherTimer 延迟, 改用事件驱动: Playing 回调直接 SetPause(true), Paused 回调 seek 到首帧; 加载视频时播放头归零)            | `Controls/Timeline/VideoPreviewControl.axaml.cs`, `Views/Timeline/TimelineEditorWindow.axaml.cs` | ✅   |

---

## Phase 4.9: 分组系统重构 + OAxis 输出类型 ✅

> **目标：** 1) 重构分组系统为 PS/AE 风格的持久化分组实体 (空分组可见, 独立折叠/展开); 2) 修正输出类型概念 — 从旧的 Axis (IODevice.xml 解析) 改为 OAxis 索引通道 (OAxis_00~OAxis_31)
> **周期：** 紧随 Phase 4.8

### Sprint 4.9.1 — 分组系统重构 ✅

| 编号    | 任务                                                                                                                                                                  | 修改文件                                                         | 状态 |
| ------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------- | ---- |
| 4.9.1.1 | ✅ 分组模型持久化 — MotionTimeline 新增 Groups 属性 (List\<TrackGroup\>), 序列化到 .motion JSON                                                                       | `Models/Motion/MotionTimeline.cs`, `Models/Motion/TrackGroup.cs` | ✅   |
| 4.9.1.2 | ✅ GroupHeaderViewModel — 新建分组头 ViewModel (Name/Color/IsCollapsed/TrackCount/ExpandIcon), 实现 IsGroupHeader=true                                                | `ViewModels/Timeline/GroupHeaderViewModel.cs` (新建)             | ✅   |
| 4.9.1.3 | ✅ DisplayItems 显示列表 — ViewModel 新增 DisplayItems (交替排列 GroupHeader + Track), RefreshDisplayList() 按分组顺序构建                                            | `ViewModels/Timeline/TimelineEditorViewModel.cs`                 | ✅   |
| 4.9.1.4 | ✅ 分组 CRUD — CreateGroup/DeleteGroup/RenameGroup, 自动同步 Timeline.Groups, LoadGroupsFromTimeline 兼容旧文件                                                       | `ViewModels/Timeline/TimelineEditorViewModel.cs`                 | ✅   |
| 4.9.1.5 | ✅ TrackViewModel 清理 — 移除旧的 IsGroupHeaderVisible/IsGroupExpanded/GroupExpandIcon, 新增 IsGroupHeader=false                                                      | `ViewModels/Timeline/TrackViewModel.cs`                          | ✅   |
| 4.9.1.6 | ✅ AXAML 双 DataTemplate — 左面板 ItemsSource 绑定 DisplayItems, GroupHeaderViewModel 模板 (展开按钮/📁/名称/计数/右键菜单), TrackViewModel 模板 (移除嵌入分组头横幅) | `Views/Timeline/TimelineEditorWindow.axaml`                      | ✅   |
| 4.9.1.7 | ✅ 分组右键菜单 — 重命名分组/删除分组 Handler, 更新新建分组使用 CreateGroup()                                                                                         | `Views/Timeline/TimelineEditorWindow.axaml.cs`                   | ✅   |

### Sprint 4.9.2 — OAxis 输出通道 ✅

| 编号    | 任务                                                                                                             | 修改文件                                                                        | 状态 |
| ------- | ---------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------- | ---- |
| 4.9.2.1 | ✅ MotionTrack 模型 — OutputType "axis"→"oaxis", AxisName→OAxisChannel (oaxis_channel), 旧版 axis_name JSON 兼容 | `Models/Motion/MotionTrack.cs`                                                  | ✅   |
| 4.9.2.2 | ✅ AddTrackDialog OAxis 通道选择 — 移除 Axis XML 解析, 改为生成 OAxis_00~31 列表, AddTrackResult.OAxisChannel    | `Views/Timeline/AddTrackDialog.axaml`, `Views/Timeline/AddTrackDialog.axaml.cs` | ✅   |
| 4.9.2.3 | ✅ DeviceDispatcher OAxis — 新增 DispatchOAxis() 使用 Key 类型 SetDO, DispatchTrack 判断 "oaxis"                 | `Services/Motion/DeviceDispatcher.cs`                                           | ✅   |
| 4.9.2.4 | ✅ MotionPlaybackEngine OAxis — 全部 "axis"→"oaxis", track.AxisName→track.OAxisChannel                           | `Services/Motion/MotionPlaybackEngine.cs`                                       | ✅   |

---

## Phase 4.10: 交互体验优化 + 架构审查 ✅

> **目标：** 1) 增强关键帧/时间轴右键菜单; 2) 分组添加轨道自动归入; 3) 轨道可拖拽移入/移出分组; 4) 修复空格键被按钮焦点劫持的问题; 5) 编辑器架构审查与扩展分析
> **周期：** 紧随 Phase 4.9

### Sprint 4.10.1 — 右键菜单增强 ✅

| 编号     | 任务                                                                                                                                                  | 修改文件                                                                                | 状态 |
| -------- | ----------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------- | ---- |
| 4.10.1.1 | ✅ 关键帧右键菜单增强 — 新增复制 (Ctrl+C)、粘贴 (Ctrl+V) 菜单项, 改进插值类型显示为中文描述                                                           | `Controls/Timeline/TrackClipControl.cs`                                                 | ✅   |
| 4.10.1.2 | ✅ 时间轴空白区域右键菜单 — 右键空白区域显示"在此处添加关键帧"、"在此处添加事件"、"粘贴关键帧"菜单                                                    | `Controls/Timeline/TrackClipControl.cs`                                                 | ✅   |
| 4.10.1.3 | ✅ 事件绑定接线 — 新增 CopyKeyframesRequested/PasteKeyframesRequested/AddKeyframeAtTimeRequested/AddEventAtTimeRequested 事件, 在 Window 代码后台接线 | `Controls/Timeline/TrackClipControl.cs`, `Views/Timeline/TimelineEditorWindow.axaml.cs` | ✅   |
| 4.10.1.4 | ✅ 剪贴板状态同步 — SyncClipboardState() 同步 HasClipboardKeyframes 到所有 TrackClipControl                                                           | `Views/Timeline/TimelineEditorWindow.axaml.cs`                                          | ✅   |
| 4.10.1.5 | ✅ ViewModel 添加事件辅助方法 — AddEventAtTime(double timeMs) 包装                                                                                    | `ViewModels/Timeline/TimelineEditorViewModel.cs`                                        | ✅   |

### Sprint 4.10.2 — 分组交互增强 ✅

| 编号     | 任务                                                                                                                                                                      | 修改文件                                                                                    | 状态 |
| -------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------- | ---- |
| 4.10.2.1 | ✅ 分组右键添加轨道自动归入 — "添加轨道到此分组..." 菜单项, Tag 传递 GroupHeaderViewModel, 新建轨道后自动调用 SetTrackGroup                                               | `Views/Timeline/TimelineEditorWindow.axaml`, `Views/Timeline/TimelineEditorWindow.axaml.cs` | ✅   |
| 4.10.2.2 | ✅ 轨道拖拽到分组 — 扩展 OnTrackHeaderPointerReleased hit-test, 支持拖拽到 GroupHeaderViewModel (加入分组)、拖拽到有分组轨道 (同组)、拖拽到无分组轨道/空白区域 (移出分组) | `Views/Timeline/TimelineEditorWindow.axaml.cs`                                              | ✅   |

### Sprint 4.10.3 — 快捷键焦点修复 ✅

| 编号     | 任务                                                                                                                                     | 修改文件                                      | 状态 |
| -------- | ---------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------- | ---- |
| 4.10.3.1 | ✅ 传输栏按钮 Focusable=False — transport-btn / transport-play / transport-toggle 样式全部添加 Focusable=False, 防止空格键被按钮焦点劫持 | `Views/Timeline/TimelineEditorWindow.axaml`   | ✅   |
| 4.10.3.2 | ✅ 工具栏按钮 Focusable=False — tl-btn / viewmode-btn 样式添加 Focusable=False                                                           | `Views/Timeline/TimelineEditorWindow.axaml`   | ✅   |
| 4.10.3.3 | ✅ 视频预览按钮 Focusable=False — BtnFullscreen / BtnZoomIn / BtnZoomOut / BtnZoomFit 添加 Focusable=False                               | `Controls/Timeline/VideoPreviewControl.axaml` | ✅   |

### Sprint 4.10.4 — 架构审查报告 ✅

> **详见：** 本文档末尾附录《动感平台编辑器架构审查报告》

---

## Phase 4.11: Bug 修复 + 架构优化 + 短期拓展 ✅

> **目标：** 1) 修复复制粘贴/分组对齐 Bug; 2) 架构风险治理 (接口抽象 + undo/redo 覆盖); 3) 曲线预设系统; 4) 时间轴标记/书签系统
> **周期：** 紧随 Phase 4.10

### Sprint 4.11.1 — Bug 修复 ✅

| 编号     | 任务                                                                                                      | 修改文件                                         | 状态 |
| -------- | --------------------------------------------------------------------------------------------------------- | ------------------------------------------------ | ---- |
| 4.11.1.1 | ✅ 关键帧粘贴无效 — PasteKeyframes() 遇重复关键帧时覆盖值而非跳过, 支持空白/选中位置粘贴                  | `ViewModels/Timeline/TimelineEditorViewModel.cs` | ✅   |
| 4.11.1.2 | ✅ Ctrl+C 剪贴板状态同步 — 键盘快捷键复制后调用 SyncClipboardState()                                      | `Views/Timeline/TimelineEditorWindow.axaml.cs`   | ✅   |
| 4.11.1.3 | ✅ 分组后轨道/时间轴不对齐 — 右面板改绑 DisplayItems + 双 DataTemplate (GroupHeader 透明占位 + TrackClip) | `Views/Timeline/TimelineEditorWindow.axaml`      | ✅   |

### Sprint 4.11.2 — 架构风险治理: 接口抽象 ✅

| 编号     | 任务                                                                               | 修改文件                                         | 状态 |
| -------- | ---------------------------------------------------------------------------------- | ------------------------------------------------ | ---- |
| 4.11.2.1 | ✅ IUndoRedoService 接口 — Undo/Redo/Execute/Clear/CanUndo/CanRedo/事件            | `Services/Motion/IUndoRedoService.cs`            | ✅   |
| 4.11.2.2 | ✅ IPlaybackEngine 接口 — Play/Pause/Stop/Resume/Seek/Tick/事件/Dispose            | `Services/Motion/IPlaybackEngine.cs`             | ✅   |
| 4.11.2.3 | ✅ ITimelineContext 接口 — 跨模块共享状态 (Timeline/Tracks/CurrentTimeMs/UndoRedo) | `Services/Motion/ITimelineContext.cs`            | ✅   |
| 4.11.2.4 | ✅ ViewModel 实现 ITimelineContext, 字段改用接口类型                               | `ViewModels/Timeline/TimelineEditorViewModel.cs` | ✅   |
| 4.11.2.5 | ✅ UndoRedoService 实现 IUndoRedoService                                           | `Services/Motion/UndoRedoService.cs`             | ✅   |
| 4.11.2.6 | ✅ MotionPlaybackEngine 实现 IPlaybackEngine + Dispose                             | `Services/Motion/MotionPlaybackEngine.cs`        | ✅   |

### Sprint 4.11.3 — 架构风险治理: Undo/Redo 覆盖 ✅

| 编号     | 任务                                                            | 修改文件                                         | 状态 |
| -------- | --------------------------------------------------------------- | ------------------------------------------------ | ---- |
| 4.11.3.1 | ✅ 轨道上移/下移 undo/redo (MoveTrackUp/MoveTrackDown)          | `ViewModels/Timeline/TimelineEditorViewModel.cs` | ✅   |
| 4.11.3.2 | ✅ 删除分组 undo/redo (DeleteGroup)                             | `ViewModels/Timeline/TimelineEditorViewModel.cs` | ✅   |
| 4.11.3.3 | ✅ 设置/移出分组 undo/redo (SetTrackGroup/RemoveTrackFromGroup) | `ViewModels/Timeline/TimelineEditorViewModel.cs` | ✅   |
| 4.11.3.4 | ✅ 事件增删 undo/redo (AddEvent/RemoveEvent)                    | `ViewModels/Timeline/TimelineEditorViewModel.cs` | ✅   |

### Sprint 4.11.4 — 短期拓展: 曲线预设系统 ✅

| 编号     | 任务                                                                                              | 修改文件                                                                                | 状态 |
| -------- | ------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------- | ---- |
| 4.11.4.1 | ✅ CurvePresetService — 10 内置预设 (3 分类: 基础/缓动/动感), ApplyPreset/ApplyPresetBatch + undo | `Services/Motion/CurvePresetService.cs`                                                 | ✅   |
| 4.11.4.2 | ✅ 右键菜单集成 — 关键帧上下文菜单添加"预设"子菜单, 按分类展开                                    | `Controls/Timeline/TrackClipControl.cs`                                                 | ✅   |
| 4.11.4.3 | ✅ ApplyPresetRequested 事件 + Window 接线                                                        | `Controls/Timeline/TrackClipControl.cs`, `Views/Timeline/TimelineEditorWindow.axaml.cs` | ✅   |

### Sprint 4.11.5 — 短期拓展: 时间轴标记/书签系统 ✅

| 编号     | 任务                                                                                          | 修改文件                                         | 状态 |
| -------- | --------------------------------------------------------------------------------------------- | ------------------------------------------------ | ---- |
| 4.11.5.1 | ✅ TimelineMarker 模型 — TimeMs/Name/Color/Note, JSON 序列化                                  | `Models/Motion/TimelineMarker.cs`                | ✅   |
| 4.11.5.2 | ✅ MotionTimeline.Markers 属性 — 可选标记列表, 条件序列化                                     | `Models/Motion/MotionTimeline.cs`                | ✅   |
| 4.11.5.3 | ✅ MarkerService — 增删改导航, 全部支持 undo/redo, MarkersChanged 事件                        | `Services/Motion/MarkerService.cs`               | ✅   |
| 4.11.5.4 | ✅ ViewModel 集成 — MarkerService 实例, LoadTimeline 加载, Save 同步                          | `ViewModels/Timeline/TimelineEditorViewModel.cs` | ✅   |
| 4.11.5.5 | ✅ TimeRulerControl 标记渲染 — 彩色旗帜 + 虚线 + 名称标签, 命中测试, 拖拽, 右键删除           | `Controls/Timeline/TimeRulerControl.cs`          | ✅   |
| 4.11.5.6 | ✅ Window 标记事件接线 — MarkerSelected/MarkerDeleteRequested/MarkerMoved, SyncMarkersToRuler | `Views/Timeline/TimelineEditorWindow.axaml.cs`   | ✅   |
| 4.11.5.7 | ✅ 键盘快捷键 — M 添加标记, Ctrl+←/→ 导航标记                                                 | `Views/Timeline/TimelineEditorWindow.axaml.cs`   | ✅   |

---

## Phase 4.12: 编辑器体验全面增强 ✅

> **目标：** 修复核心交互 Bug + 工具栏增强 + 专业级 UX 优化 (全屏/拖拽反馈/独立轨道高度/曲线预设集成/时间输入)
> **周期：** 紧随 Phase 4.11

### Sprint 4.12.1 — Bug 修复 ✅

| 编号     | 任务                                                                                                     | 修改文件                                                                                         | 状态 |
| -------- | -------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------ | ---- |
| 4.12.1.1 | ✅ 删除关键帧无效 — DeleteKeyframe() 保护条件从 ≤2 改为 ≤1; DeleteSelectedKeyframes() 增加完整 undo/redo | `ViewModels/Timeline/TimelineEditorViewModel.cs`                                                 | ✅   |
| 4.12.1.2 | ✅ 选中关键帧不更新属性面板 — SelectKeyframe() 强制 RaisePropertyChanged; 曲线编辑器同步轨道选中态       | `ViewModels/Timeline/TimelineEditorViewModel.cs`, `Views/Timeline/TimelineEditorWindow.axaml.cs` | ✅   |
| 4.12.1.3 | ✅ 拖拽时间头视频预览延迟 — PlayheadSeek 直接调用 SyncTime(); SyncTime 增加 \_needsResync 防丢帧         | `Views/Timeline/TimelineEditorWindow.axaml.cs`, `Controls/Timeline/VideoPreviewControl.axaml.cs` | ✅   |

### Sprint 4.12.2 — 工具栏增强 ✅

| 编号     | 任务                                                                                    | 修改文件                                                                                    | 状态 |
| -------- | --------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------- | ---- |
| 4.12.2.1 | ✅ 跳转到开始/结尾按钮 — ⏮/⏭ 几何图标按钮, SeekTo(0)/SeekTo(Duration) + 直接 SyncTime | `Views/Timeline/TimelineEditorWindow.axaml`, `Views/Timeline/TimelineEditorWindow.axaml.cs` | ✅   |
| 4.12.2.2 | ✅ 当前时间可编辑输入 — 双击时间显示切换 TextBox, 支持秒("3.5")/mm:ss.fff 两种格式      | `Views/Timeline/TimelineEditorWindow.axaml`, `Views/Timeline/TimelineEditorWindow.axaml.cs` | ✅   |

### Sprint 4.12.3 — 曲线编辑器预设集成 ✅

| 编号     | 任务                                                                                              | 修改文件                                                                                  | 状态 |
| -------- | ------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------- | ---- |
| 4.12.3.1 | ✅ 曲线上下文菜单增加预设子菜单 — 按分类 (基础/缓动/动感) 展开, 高亮当前预设, 应用后更新插值+切线 | `Controls/Timeline/CurveEditorControl.cs`                                                 | ✅   |
| 4.12.3.2 | ✅ PresetApplyRequested 事件 + Window 接线同步到 ViewModel                                        | `Controls/Timeline/CurveEditorControl.cs`, `Views/Timeline/TimelineEditorWindow.axaml.cs` | ✅   |

### Sprint 4.12.4 — 专业 UX 增强 ✅

| 编号     | 任务                                                                                                      | 修改文件                                                                                                                                                                               | 状态 |
| -------- | --------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---- |
| 4.12.4.1 | ✅ 添加轨道对话框专业化重设计 — 紫色 Header + 三分区卡片 (设备/属性/外观) + 10 色盘 + ScrollViewer        | `Views/Timeline/AddTrackDialog.axaml`                                                                                                                                                  | ✅   |
| 4.12.4.2 | ✅ 独立轨道高度 — TrackViewModel 增加 IndividualTrackHeight; 右键菜单 5 档 (24/36/52/80/120px) + 恢复全局 | `ViewModels/Timeline/TrackViewModel.cs`, `ViewModels/Timeline/TimelineEditorViewModel.cs`, `Views/Timeline/TimelineEditorWindow.axaml`, `Views/Timeline/TimelineEditorWindow.axaml.cs` | ✅   |
| 4.12.4.3 | ✅ 轨道拖拽视觉反馈 — 拖拽时目标轨道蓝色 (#60a5fa) 顶边框高亮, 释放后清除                                 | `Views/Timeline/TimelineEditorWindow.axaml.cs`                                                                                                                                         | ✅   |
| 4.12.4.4 | ✅ 真全屏视频预览 — SystemDecorations.None + WindowState.FullScreen, ESC 退出恢复原状态                   | `Views/Timeline/TimelineEditorWindow.axaml.cs`                                                                                                                                         | ✅   |

---

## Phase 4.13: 架构改进 — 短期 (S1-S4)

> **目标：** 补全 Undo/Redo 盲区 + 提取键盘快捷键服务 + AddTrackDialog MVVM 化 + KeyframePropertyPanel 数据绑定化
> **周期：** 紧随 Phase 4.12
> **前置：** Phase 4.12 完成 ✅
> **依据：** 架构审查报告 v2 (Phase 4.12) §六.短期

### Sprint 4.13.1 — ✅ Undo/Redo 补全残留盲区

> **目标：** 补全 8 个仅 `MarkDirty()` 而未走 `UndoRedoService` 的编辑操作

| 编号     | 任务                                                                                         | 修改文件                                         | 状态 |
| -------- | -------------------------------------------------------------------------------------------- | ------------------------------------------------ | ---- |
| 4.13.1.1 | ✅ CreateGroup() 增加 Undo 支持 — 撤销时删除分组并恢复轨道归属                               | `ViewModels/Timeline/TimelineEditorViewModel.cs` | ✅   |
| 4.13.1.2 | ✅ RenameGroup() 增加 Undo 支持 — 撤销时恢复旧名称+更新轨道/header                           | `ViewModels/Timeline/TimelineEditorViewModel.cs` | ✅   |
| 4.13.1.3 | ✅ SetSelectedKeyframesInterpolation() 增加 Undo — 快照旧插值, 撤销时批量恢复                | `ViewModels/Timeline/TimelineEditorViewModel.cs` | ✅   |
| 4.13.1.4 | ✅ MoveSelectedKeyframes() 增加 Undo — 快照旧时间, 撤销时批量恢复                            | `ViewModels/Timeline/TimelineEditorViewModel.cs` | ✅   |
| 4.13.1.5 | ✅ UpdateKeyframeProperties() 增加 Undo — 快照旧属性五元组 (time/value/interp/tangentIn/Out) | `ViewModels/Timeline/TimelineEditorViewModel.cs` | ✅   |
| 4.13.1.6 | ✅ UpdateKeyframeEvent() 增加 Undo — 快照旧 eventName/eventData                              | `ViewModels/Timeline/TimelineEditorViewModel.cs` | ✅   |
| 4.13.1.7 | ✅ UpdateEvent() 增加 Undo — 快照旧属性, 撤销时恢复                                          | `ViewModels/Timeline/TimelineEditorViewModel.cs` | ✅   |
| 4.13.1.8 | ✅ SetManualDuration() 增加 Undo — 快照旧 ManualDurationMs/DurationMs                        | `ViewModels/Timeline/TimelineEditorViewModel.cs` | ✅   |

### Sprint 4.13.2 — ✅ 提取 KeyboardShortcutService

> **目标：** 从 TimelineEditorWindow.axaml.cs OnKeyDown (~190 行 30+ if/else) 提取到 ViewModel 层

| 编号     | 任务                                                                               | 新建/修改文件                                                                               | 状态 |
| -------- | ---------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------- | ---- |
| 4.13.2.1 | ✅ 新建 IKeyboardShortcutService 接口 + KeyboardShortcutService 实现               | `Services/Motion/IKeyboardShortcutService.cs`, `Services/Motion/KeyboardShortcutService.cs` | ✅   |
| 4.13.2.2 | ✅ 定义 ShortcutAction 枚举 (Play/Pause/Delete/Undo/Redo/Copy/Paste/SelectAll/...) | `Models/Motion/ShortcutAction.cs`                                                           | ✅   |
| 4.13.2.3 | ✅ ViewModel 新增 ExecuteShortcut(ShortcutAction) 方法, 分发到对应 ReactiveCommand | `ViewModels/Timeline/TimelineEditorViewModel.cs`                                            | ✅   |
| 4.13.2.4 | ✅ Window.OnKeyDown 简化为: 解析按键→ShortcutAction→ViewModel.ExecuteShortcut()    | `Views/Timeline/TimelineEditorWindow.axaml.cs`                                              | ✅   |

### Sprint 4.13.3 — ✅ AddTrackDialog MVVM 化

> **目标：** 消除 AddTrackDialog 零 ViewModel + XML 解析在 UI 中的问题

| 编号     | 任务                                                                                               | 新建/修改文件                                                                       | 状态 |
| -------- | -------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------- | ---- |
| 4.13.3.1 | ✅ 新建 IDeviceSchemaService 接口 + DeviceSchemaService 实现 — 解析 IODevice.xml 获取设备/通道信息 | `Services/Motion/IDeviceSchemaService.cs`, `Services/Motion/DeviceSchemaService.cs` | ✅   |
| 4.13.3.2 | ✅ 新建 AddTrackDialogViewModel (ReactiveUI + ConfirmCommand/CancelCommand + WhenAnyValue 联动)    | `ViewModels/Timeline/AddTrackDialogViewModel.cs`                                    | ✅   |
| 4.13.3.3 | ✅ 迁移 AddTrackResult/OActionInfo/OAxisChannelInfo/DeviceSchemaInfo 到 Models 层                  | `Models/Motion/DeviceSchema.cs`                                                     | ✅   |
| 4.13.3.4 | ✅ AddTrackDialog.axaml 改为编译绑定 x:DataType, code-behind 精简到 ~65 行                         | `Views/Timeline/AddTrackDialog.axaml`, `Views/Timeline/AddTrackDialog.axaml.cs`     | ✅   |

### Sprint 4.13.4 — 🔧 KeyframePropertyPanel 数据绑定化

> **目标：** 消除命令式 UpdateFromKeyframe() 推送, 改为直接绑定 SelectedKeyframe 属性

| 编号     | 任务                                                                                   | 新建/修改文件                                      | 状态 |
| -------- | -------------------------------------------------------------------------------------- | -------------------------------------------------- | ---- |
| 4.13.4.1 | ✅ 新建 KeyframePropertyViewModel (包装 SelectedKeyframe + 双向绑定属性)               | `ViewModels/Timeline/KeyframePropertyViewModel.cs` | ✅   |
| 4.13.4.2 | ✅ Panel.axaml 改为编译绑定 x:DataType=KeyframePropertyViewModel                       | `Controls/Timeline/KeyframePropertyPanel.axaml`    | ✅   |
| 4.13.4.3 | ✅ 删除 \_isUpdating 标志 + 7 个自定义事件, 改为 PropertyChanged 订阅                  | `Controls/Timeline/KeyframePropertyPanel.axaml.cs` | ✅   |
| 4.13.4.4 | 🔧 Window 接线层: 消除 UpdateKeyframeUI() 等手动推送调用 (RefreshPropertyPanel 仍存在) | `Views/Timeline/TimelineEditorWindow.axaml.cs`     | 🔧   |

---

## Phase 4.14: 架构改进 — 中期 (M1-M4)

> **目标：** ViewModel 拆分 + DragReorder 行为提取 + IDialogService + IVideoPlayerService
> **周期：** Phase 5~6 前实施
> **前置：** Phase 4.13 完成
> **依据：** 架构审查报告 v2 (Phase 4.12) §六.中期

### Sprint 4.14.1 — ✅ ViewModel 拆分 (partial class 6 文件方式)

> **目标：** 将 2,608 行 TimelineEditorViewModel 拆分为职责单一的 partial class 文件 (适配方案：保持单一类型、避免跨 VM 协调复杂度)

| 编号     | 任务                                                            | 新建/修改文件                                                | 状态 |
| -------- | --------------------------------------------------------------- | ------------------------------------------------------------ | ---- |
| 4.14.1.1 | ✅ 拆分 FileOps partial (New/Open/Save/SaveAs/AutoSave)         | `ViewModels/Timeline/TimelineEditorViewModel.FileOps.cs`     | ✅   |
| 4.14.1.2 | ✅ 拆分 Playback partial (Play/Pause/Stop/Tick/Speed/Loop)      | `ViewModels/Timeline/TimelineEditorViewModel.Playback.cs`    | ✅   |
| 4.14.1.3 | ✅ 拆分 TrackGroups partial (轨道增删移/分组 CRUD/DisplayList)  | `ViewModels/Timeline/TimelineEditorViewModel.TrackGroups.cs` | ✅   |
| 4.14.1.4 | ✅ 拆分 Keyframes partial (选中/增删移/多选/框选/复制粘贴/插值) | `ViewModels/Timeline/TimelineEditorViewModel.Keyframes.cs`   | ✅   |
| 4.14.1.5 | ✅ 拆分 Events partial (独立事件增删改/事件排序)                | `ViewModels/Timeline/TimelineEditorViewModel.Events.cs`      | ✅   |
| 4.14.1.6 | ✅ 主 VM 瘦身为协调层 (共享状态 + 属性 + 构造 + 快捷键 ~825 行) | `ViewModels/Timeline/TimelineEditorViewModel.cs`             | ✅   |
| 4.14.1.7 | ✅ View 层绑定不变 (partial class 对外接口兼容, 无需改 AXAML)   | —                                                            | ✅   |

### Sprint 4.14.2 — 提取 DragReorderBehavior

| 编号     | 任务                                                          | 新建/修改文件                                        | 状态 |
| -------- | ------------------------------------------------------------- | ---------------------------------------------------- | ---- |
| 4.14.2.1 | ✅ 新建 DragReorderBehavior (Avalonia AttachedProperty 方式)  | `Controls/Timeline/Behaviors/DragReorderBehavior.cs` | ✅   |
| 4.14.2.2 | 迁移 ~180 行拖拽排序逻辑 (Pointer 跟踪 + VisualTree 命中测试) | 修改 `Views/Timeline/TimelineEditorWindow.axaml.cs`  |      |
| 4.14.2.3 | AXAML 中声明式挂载 Behavior                                   | 修改 `Views/Timeline/TimelineEditorWindow.axaml`     |      |

### Sprint 4.14.3 — ✅ 引入 IDialogService

| 编号     | 任务                                                                                                      | 新建/修改文件                                         | 状态 |
| -------- | --------------------------------------------------------------------------------------------------------- | ----------------------------------------------------- | ---- |
| 4.14.3.1 | ✅ 新建 IDialogService 接口 (OpenFile/SaveFile/Confirm/InputDialog)                                       | `Services/IDialogService.cs`                          | ✅   |
| 4.14.3.2 | ✅ 新建 AvaloniaDialogService 实现 (TopLevel 文件选择/弹窗)                                               | `Services/AvaloniaDialogService.cs`                   | ✅   |
| 4.14.3.3 | ✅ ViewModel 注入 IDialogService, 替换 View 层直接文件对话框 (OpenFile/SaveAs 使用 IDialogService + 回退) | 修改 `ViewModels/Timeline/TimelineEditorViewModel.cs` | ✅   |

### Sprint 4.14.4 — ✅ VideoPreviewControl → IVideoPlayerService

| 编号     | 任务                                                                                   | 新建/修改文件                                         | 状态 |
| -------- | -------------------------------------------------------------------------------------- | ----------------------------------------------------- | ---- |
| 4.14.4.1 | ✅ 新建 IVideoPlayerService 接口 (Load/Play/Pause/Seek/GetCurrentTime)                 | `Services/Motion/IVideoPlayerService.cs`              | ✅   |
| 4.14.4.2 | ✅ 新建 LibVlcVideoPlayerService 实现 (封装 LibVLC 生命周期)                           | `Services/Motion/LibVlcVideoPlayerService.cs`         | ✅   |
| 4.14.4.3 | ✅ VideoPreviewControl 改为绑定 IVideoPlayerService, 删除内部 LibVLC 管理 (435→310 行) | 修改 `Controls/Timeline/VideoPreviewControl.axaml.cs` | ✅   |

---

## Phase 4.15: 架构改进 — 长期 (L1-L4)

> **目标：** DI 容器 + 静态服务接口化 + 单元测试 + code-behind 完全迁移
> **周期：** Phase 7 前实施
> **前置：** Phase 4.14 完成
> **依据：** 架构审查报告 v2 (Phase 4.12) §六.长期

### Sprint 4.15.1 — ✅ 引入 DI 容器

| 编号     | 任务                                                                                | 新建/修改文件                                                   | 状态 |
| -------- | ----------------------------------------------------------------------------------- | --------------------------------------------------------------- | ---- |
| 4.15.1.1 | ✅ 选型 Microsoft.Extensions.DI + 注册所有服务                                      | `App.axaml.cs`, `IOStudio.csproj`, `Services/ServiceLocator.cs` | ✅   |
| 4.15.1.2 | ✅ ViewModel 构造函数改为依赖注入 (IDialogService/IUndoRedoService/IPlaybackEngine) | `ViewModels/Timeline/TimelineEditorViewModel.cs`                | ✅   |
| 4.15.1.3 | ✅ View 层通过 ViewLocator.CreateViewModel&lt;T&gt;() + DI 创建 ViewModel           | `ViewLocator.cs`, `ViewModels/MainWindowViewModel.cs`           | ✅   |

### Sprint 4.15.2 — ✅ 补全静态服务接口

| 编号     | 任务                                                        | 新建/修改文件                                                                       | 状态 |
| -------- | ----------------------------------------------------------- | ----------------------------------------------------------------------------------- | ---- |
| 4.15.2.1 | ✅ InterpolationEngine → IInterpolationEngine 接口 + 实例化 | `Services/Motion/IInterpolationEngine.cs`, `Services/Motion/InterpolationEngine.cs` | ✅   |
| 4.15.2.2 | ✅ MotionFileReader → IMotionFileReader 接口 + 实例化       | `Services/Motion/IMotionFileReader.cs`, `Services/Motion/MotionFileReader.cs`       | ✅   |
| 4.15.2.3 | ✅ MotionExportService → IMotionExportService 接口 + 实例化 | `Services/Motion/IMotionExportService.cs`, `Services/Motion/MotionExportService.cs` | ✅   |
| 4.15.2.4 | ✅ CurvePresetService → ICurvePresetService 接口 + 实例化   | `Services/Motion/ICurvePresetService.cs`, `Services/Motion/CurvePresetService.cs`   | ✅   |

### Sprint 4.15.3 — ✅ 单元测试框架搭建

| 编号     | 任务                                                        | 新建/修改文件                          | 状态 |
| -------- | ----------------------------------------------------------- | -------------------------------------- | ---- |
| 4.15.3.1 | ✅ 新建 IOStudio.Tests 项目 (xUnit + Moq)                   | `IOStudio.Tests/IOStudio.Tests.csproj` | ✅   |
| 4.15.3.2 | ✅ ViewModel 层测试 (Mock IPlaybackEngine/IUndoRedoService) | `IOStudio.Tests/ViewModels/`           | ✅   |
| 4.15.3.3 | ✅ Service 层测试 (Mock ITimelineContext)                   | `IOStudio.Tests/Services/`             | ✅   |
| 4.15.3.4 | ✅ Model 层序列化/反序列化测试                              | `IOStudio.Tests/Models/`               | ✅   |

### Sprint 4.15.4 — ✅ code-behind 完全迁移 (大部分完成)

| 编号     | 任务                                                                          | 新建/修改文件                                                                          | 状态 |
| -------- | ----------------------------------------------------------------------------- | -------------------------------------------------------------------------------------- | ---- |
| 4.15.4.1 | ✅ 分组管理 UI (~250 行) → 独立 GroupDialog + GroupDialogViewModel (3 种模式) | `Views/Timeline/GroupDialog.axaml(.cs)`, `ViewModels/Timeline/GroupDialogViewModel.cs` | ✅   |
| 4.15.4.2 | ✅ 上下文菜单 → AXAML 声明式 Command 绑定 + ContextMenuCommands partial       | `TimelineEditorViewModel.ContextMenuCommands.cs`, `TimelineEditorWindow.axaml`         | ✅   |
| 4.15.4.3 | ✅ 滚动/缩放同步 (~200 行) → ScrollSyncBehavior                               | `Controls/Timeline/Behaviors/ScrollSyncBehavior.cs`                                    | ✅   |
| 4.15.4.4 | ✅ 视频全屏切换 (~120 行) → FullscreenBehavior                                | `Controls/Timeline/Behaviors/FullscreenBehavior.cs`                                    | ✅   |
| 4.15.4.5 | ✅ 内联时间编辑 (~120 行) → TimeFormatConverter + ViewModel 命令              | `Converters/TimeFormatConverter.cs`                                                    | ✅   |

### Sprint 4.15.5 — ✅ Bug Fix 批量修复 (视频/关键帧/属性面板)

| 编号     | 任务                                            | 产出文件                                                             | 状态 |
| -------- | ----------------------------------------------- | -------------------------------------------------------------------- | ---- |
| 4.15.5.1 | ✅ 视频在预览面板内显示 (修复 HWND 延迟绑定)    | `VideoPreviewControl.axaml.cs`                                       | ✅   |
| 4.15.5.2 | ✅ 真全屏模式 (隐藏标题栏/视频列表/工具栏/菜单) | `VideoPreviewControl.axaml(.cs)` + `TimelineEditorWindow.axaml(.cs)` | ✅   |
| 4.15.5.3 | ✅ 修复关键帧复制粘贴 (绝对/本地时间坐标转换)   | `TimelineEditorViewModel.Keyframes.cs`                               | ✅   |
| 4.15.5.4 | ✅ 添加上一个/下一个关键帧工具栏按钮            | `TimelineEditorWindow.axaml(.cs)`                                    | ✅   |
| 4.15.5.5 | ✅ 视频预览鼠标滚轮缩放 + 适配按钮显示"适配"    | `VideoPreviewControl.axaml(.cs)`                                     | ✅   |
| 4.15.5.6 | ✅ Bool 关键帧属性面板隐藏插值选项              | `KeyframePropertyViewModel.cs`                                       | ✅   |

### Sprint 4.15.6 — ✅ 深层修复 + UI 优化 (视频/全屏/粘贴/轨道头)

| 编号     | 任务                                                    | 产出文件                               | 状态 |
| -------- | ------------------------------------------------------- | -------------------------------------- | ---- |
| 4.15.6.1 | ✅ 视频 HWND 真修复 (\_pendingHwnd 缓存，三重保障)      | `LibVlcVideoPlayerService.cs`          | ✅   |
| 4.15.6.2 | ✅ 全屏消除黑边 (Grid 列定义 Width/MinWidth/MaxWidth=0) | `VideoPreviewControl.axaml.cs`         | ✅   |
| 4.15.6.3 | ✅ 关键帧粘贴真修复 (clipVm.AddKeyframe 同步 Model+VM)  | `TimelineEditorViewModel.Keyframes.cs` | ✅   |
| 4.15.6.4 | ✅ 移除轨道上下移动按钮 + 强化 LiveValueDisplay         | `TimelineEditorWindow.axaml`           | ✅   |

---

## Phase 9: 长期功能规划 (Roadmap)

> **目标：** 记录未来版本的功能方向, 作为产品路线图参考
> **状态：** 📋 规划中 (暂不实施)
> **前置：** Phase 1-8 全部完成

### 9.1 模板系统

| 编号  | 功能描述                               | 技术方向                                      |
| ----- | -------------------------------------- | --------------------------------------------- |
| 9.1.1 | 项目模板 (预置轨道+设备配置, 一键创建) | `MotionProjectTemplate` 模型 + 模板选择对话框 |
| 9.1.2 | 关键帧模板 (保存/加载常用关键帧序列)   | 复用 EffectPreset 扩展                        |
| 9.1.3 | 设备配置模板 (导入/导出设备轨道映射)   | XML/JSON 配置导入导出                         |

### 9.2 多文件标签页

| 编号  | 功能描述                                          | 技术方向                                |
| ----- | ------------------------------------------------- | --------------------------------------- |
| 9.2.1 | TabControl 多文件编辑 (同时打开多个 .motion 文件) | Avalonia TabControl + 多 ViewModel 实例 |
| 9.2.2 | 文件间复制/粘贴轨道片段                           | 跨 ViewModel 剪贴板                     |
| 9.2.3 | 文件比较 (Diff 视图)                              | 双时间轴对比渲染                        |

### 9.3 实时设备预览

| 编号  | 功能描述                                    | 技术方向                           |
| ----- | ------------------------------------------- | ---------------------------------- |
| 9.3.1 | 编辑时实时输出到设备 (所见即所得)           | 低延迟 DeviceDispatcher 旁路输出   |
| 9.3.2 | 安全边界可视化 (超限值高亮警告)             | SafetyGuard 实时检测 + UI 红色标记 |
| 9.3.3 | 设备状态监控面板 (实时显示各通道当前输出值) | 仪表盘控件                         |

### 9.4 录制模式

| 编号  | 功能描述                                         | 技术方向                       |
| ----- | ------------------------------------------------ | ------------------------------ |
| 9.4.1 | 手动操控录制 (摇杆/滑块输入实时录制为关键帧)     | 输入采样 → 降采样 → 关键帧生成 |
| 9.4.2 | 录制回放对比 (录制轨道 vs 编辑轨道叠加显示)      | 双轨道渲染                     |
| 9.4.3 | 录制自动平滑 (Douglas-Peucker 简化 + 贝塞尔拟合) | 曲线拟合算法                   |

### 9.5 音频轨道

| 编号  | 功能描述                        | 技术方向                      |
| ----- | ------------------------------- | ----------------------------- |
| 9.5.1 | 独立音频文件导入 (mp3/wav/ogg)  | NAudio / LibVLCSharp 音频解码 |
| 9.5.2 | 音频波形显示在专用轨道          | WaveformControl 扩展          |
| 9.5.3 | 音频节拍检测 → 自动标记辅助对齐 | BeatDetector 频域分析         |
| 9.5.4 | 多音频轨道混合                  | 音频混合引擎                  |

---

## Phase 5: 媒体同步 ✅

> **目标：** 视频/音频 与时间轴联动, 音频波形辅助对齐
> **周期：** W11-W12 (2026-05-18 ~ 2026-05-29)，共 2 周
> **前置：** Phase 4 完成
> **新增 NuGet：** `LibVLCSharp` + `LibVLCSharp.Avalonia`

### Sprint 5.1 — ✅ 视频集成 (Day 1-5)

> **注：** 以下任务大部分已在 Phase 2 迭代中提前完成（Sprint 2.6/2.7/2.8）

| 编号  | 任务                                                                    | 新建文件                                                                      | 工时 |
| ----- | ----------------------------------------------------------------------- | ----------------------------------------------------------------------------- | ---- |
| 5.1.1 | ✅ 添加 LibVLCSharp NuGet 依赖 (Sprint 2.6 已完成)                      | 修改 `IOStudio.csproj`                                                        | 0.5h |
| 5.1.2 | ✅ MediaPlayerControl → VlcVideoHost 子窗口渲染 (Sprint 2.6/2.8 已完成) | `Controls/Timeline/VideoPreviewControl.axaml(.cs)`, `VlcVideoHost.cs`         | 4h   |
| 5.1.3 | ✅ 视频时钟与主时间轴同步 (Sprint 2.8 SyncTime 智能同步)                | `VideoPreviewControl.axaml.cs`                                                | 5h   |
| 5.1.4 | ✅ 视频文件列表 + 双击切换预览 (Sprint 2.7 已完成)                      | `VideoPreviewControl.axaml(.cs)`                                              | 2h   |
| 5.1.5 | ✅ 视频帧预览 (SeekAccurate + NextFrame 精确帧)                         | 修改 `IVideoPlayerService`, `LibVlcVideoPlayerService`, `VideoPreviewControl` | 3h   |

### Sprint 5.2 — ✅ 音频波形 (Day 6-10)

| 编号  | 任务                                                  | 新建文件                                                             | 工时 |
| ----- | ----------------------------------------------------- | -------------------------------------------------------------------- | ---- |
| 5.2.1 | ✅ AudioWaveformExtractor (LibVLC PCM回调提取+降采样) | `Services/Motion/AudioWaveformExtractor.cs`                          | 4h   |
| 5.2.2 | ✅ WaveformControl (Avalonia DrawingContext 波形绘制) | `Controls/Timeline/WaveformControl.cs`                               | 4h   |
| 5.2.3 | ✅ 波形显示在时间轴顶部作为参考轨 + 菜单切换          | 修改 `TimelineEditorWindow.axaml(.cs)`, `TimelineEditorViewModel.cs` | 2h   |
| 5.2.4 | ✅ 节拍检测 (能量阈值+局部平均+最小间隔过滤)          | `Services/Motion/BeatDetector.cs`                                    | 3h   |
| 5.2.5 | ✅ 波形提取性能优化 (8x加速播放+渐进式显示+低采样率)  | 修改 `AudioWaveformExtractor.cs`                                     | 1h   |
| 5.2.6 | ✅ 节拍检测默认关闭 (菜单可选开启, 即时生效)          | 修改 `TimelineEditorViewModel.cs`, `TimelineEditorWindow.axaml(.cs)` | 0.5h |
| 5.2.7 | ✅ 修复左右轨道面板垂直对齐偏移 (波形占位区)          | 修改 `TimelineEditorWindow.axaml`                                    | 0.5h |

### Sprint 5.3 — ✅ Bug Fix 第二轮 (曲线刷新/UX/多选/波形/视频缩放)

| 编号  | 任务                                                                      | 修改文件                                                                                                  | 工时 |
| ----- | ------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------- | ---- |
| 5.3.1 | ✅ 曲线视图粘贴后刷新 (RefreshAllTrackControls 联动 SyncCurveEditorData)  | `TimelineEditorWindow.axaml.cs`                                                                           | 0.5h |
| 5.3.2 | ✅ 音频波形提取修复 (--no-video → --vout=none 避免阻断音频解复用)         | `AudioWaveformExtractor.cs`                                                                               | 0.3h |
| 5.3.3 | ✅ 视频预览鼠标滚轮缩放修复 (透明输入覆层拦截原生HWND事件)                | `VideoPreviewControl.axaml(.cs)`                                                                          | 0.5h |
| 5.3.4 | ✅ 视频全屏自动适配100% (进入全屏时 SetScale(0) + \_isFitMode)            | `VideoPreviewControl.axaml.cs`                                                                            | 0.3h |
| 5.3.5 | ✅ 关键帧右键菜单UX优化 (✓当前类型标记/emoji图标/扁平化预设)              | `TrackClipControl.cs`, `CurveEditorControl.cs`                                                            | 1h   |
| 5.3.6 | ✅ 多选关键帧批量设置插值/预设 (BatchSetInterpolation + BatchApplyPreset) | `TrackClipControl.cs`, `CurveEditorControl.cs`, `ViewModel.Keyframes.cs`, `TimelineEditorWindow.axaml.cs` | 1.5h |

**时钟同步策略：**

```
MediaSyncService
  ├── 主时钟: MotionPlaybackEngine.CurrentTimeMs (Stopwatch 精度)
  ├── 从时钟: LibVLC MediaPlayer.Time
  ├── 同步策略: 每 100ms 检查漂移
  │   if |engineTime - mediaTime| > 50ms:
  │       mediaPlayer.Time = engineTime  // 强制对齐
  └── 优先级: 引擎时钟为主 (设备安全优先于媒体精度)
```

**验收标准 (Phase 5)：**

| #   | 验收项     | 通过条件                       |
| --- | ---------- | ------------------------------ |
| E1  | 导入视频   | 支持 mp4/avi/mkv, 播放正常     |
| E2  | 播放同步   | 视频与设备动作同步, 漂移 <50ms |
| E3  | 拖动时间轴 | 视频帧跟随跳转                 |
| E4  | 波形显示   | 音频波形清晰, 与时间轴对齐     |

---

## Phase 6: 效果预设

> **目标：** 预置常用效果 (颠簸/转弯/下坠等), 拖拽到时间轴快速编辑
> **周期：** W13-W14 (2026-06-01 ~ 2026-06-12)，共 2 周
> **前置：** Phase 4 完成 (可与 Phase 5 并行)

### Sprint 6.1 — 预设数据模型 + 协议 (Day 1-4)

| 编号  | 任务                                   | 新建文件                                 | 工时 |
| ----- | -------------------------------------- | ---------------------------------------- | ---- |
| 6.1.1 | EffectPreset 模型                      | `Models/Motion/EffectPreset.cs`          | 2h   |
| 6.1.2 | 预设文件格式 (.preset.json)            | `Config/Motion/Presets/`                 | 1h   |
| 6.1.3 | EffectPresetService (加载/保存/应用)   | `Services/Motion/EffectPresetService.cs` | 3h   |
| 6.1.4 | MotionServer 预设协议命令              | 修改 `MotionCommandHandler.cs`           | 2h   |
| 6.1.5 | 内置预设: 运动类 (颠簸/转弯/下坠/俯冲) | `Config/Motion/Presets/motion/`          | 3h   |
| 6.1.6 | 内置预设: 环境类 (风/烟雾/闪电)        | `Config/Motion/Presets/effects/`         | 2h   |

**预设文件格式：**

```json
{
  "name": "颠簸",
  "category": "motion",
  "description": "模拟车辆颠簸路面效果",
  "duration_ms": 3000,
  "tracks": [
    {
      "role": "pitch",
      "keyframes": [
        { "time_ms": 0, "value": 0.5 },
        { "time_ms": 300, "value": 0.7, "interpolation": "bezier" },
        { "time_ms": 600, "value": 0.3 },
        { "time_ms": 900, "value": 0.6 },
        { "time_ms": 1200, "value": 0.4 },
        { "time_ms": 3000, "value": 0.5 }
      ]
    },
    {
      "role": "heave",
      "keyframes": [
        /* ... */
      ]
    }
  ],
  "parameters": {
    "intensity": { "default": 1.0, "min": 0.1, "max": 3.0 },
    "frequency": { "default": 1.0, "min": 0.5, "max": 2.0 }
  }
}
```

### Sprint 6.2 — 预设库 UI (Day 5-10)

| 编号  | 任务                                  | 新建文件                                            | 工时 |
| ----- | ------------------------------------- | --------------------------------------------------- | ---- |
| 6.2.1 | PresetLibraryPanel (分类/搜索/缩略图) | `Controls/Timeline/PresetLibraryPanel.axaml(.cs)`   | 4h   |
| 6.2.2 | 预设拖拽到时间轴 (Drag&Drop)          | 交互逻辑                                            | 4h   |
| 6.2.3 | 预设参数调节面板 (强度/频率/时长)     | `Controls/Timeline/PresetParameterPanel.axaml(.cs)` | 3h   |
| 6.2.4 | 从选中片段保存为自定义预设            | 保存逻辑                                            | 2h   |
| 6.2.5 | PresetLibraryViewModel                | `ViewModels/Timeline/PresetLibraryViewModel.cs`     | 2h   |

**验收标准 (Phase 6)：**

| #   | 验收项     | 通过条件                             |
| --- | ---------- | ------------------------------------ |
| F1  | 预设库面板 | 显示分类列表, 搜索过滤正常           |
| F2  | 拖入轨道   | 拖拽预设到时间轴, 生成对应关键帧     |
| F3  | 参数调节   | 修改强度/频率, 关键帧实时更新        |
| F4  | 保存预设   | 选中片段→保存为预设→出现在库中       |
| F5  | 网络协议   | list_presets / apply_preset 命令工作 |

---

## Phase 7: LicHper 授权集成 + 高级功能 + 打磨

> **目标：** LicHper 授权门控集成 (Basic/Pro 分级) + 生产级完善
> **周期：** W15-W17 (2026-06-15 ~ 2026-07-04)，共 2.5 周
> **前置：** Phase 1-6 完成

### Sprint 7.1 — LicHper 授权集成 + 功能门控 (Day 1-5)

| 编号   | 任务                                                                     | 新建/修改文件                                           | 工时 | 优先级 |
| ------ | ------------------------------------------------------------------------ | ------------------------------------------------------- | ---- | ------ |
| 7.1.1  | LicHperInterface P/Invoke 封装 (Login/Validate/Logout/GetLicense)        | 新建 `Services/LicHperInterface.cs`                     | 1h   | P0     |
| 7.1.2  | LicenseService 授权门控服务 (Initialize/IsPro/HasFeature/TryActivatePro) | 新建 `Services/LicenseService.cs`                       | 3h   | P0     |
| 7.1.3  | ILicenseService 接口 + DI 注册                                           | 新建 `Services/ILicenseService.cs`, 修改 `App.axaml.cs` | 1h   | P0     |
| 7.1.4  | 授权管理 UI (激活/导入 license/状态显示)                                 | 新建 `Views/LicenseDialog.axaml(.cs)`                   | 4h   | P0     |
| 7.1.5  | LicenseViewModel (激活/注销/刷新授权状态)                                | 新建 `ViewModels/LicenseViewModel.cs`                   | 3h   | P0     |
| 7.1.6  | MainWindowViewModel Pro 功能 CanExecute 门控                             | 修改 `ViewModels/MainWindowViewModel.cs`                | 2h   | P0     |
| 7.1.7  | MainWindow.axaml Pro 菜单锁标识 + 升级提示                               | 修改 `Views/MainWindow.axaml`                           | 1.5h | P0     |
| 7.1.8  | MotionCommandHandler.list_files 区分 Basic/Pro 模式                      | 修改 `Services/Motion/MotionCommandHandler.cs`          | 0.5h | P0     |
| 7.1.9  | release_IOStudio.bat 构建脚本 (含 LicHper.dll 复制)                      | 新建 `release_IOStudio.bat`                             | 1h   | P0     |
| 7.1.10 | Obfuscar IL 混淆配置                                                     | 新建 `obfuscar_IOStudio.xml`                            | 2h   | P1     |
| 7.1.11 | Basic/Pro 端到端验证: 无授权→Basic, 导入 license→Pro, 加载 .mtn          | 手动测试                                                | 2h   | P0     |

**LicHper.dll 集成方式：**

```
构建时:
  1. 从 Authorization/LicHper 项目编译 LicHper.dll (x64)
  2. release_IOStudio.bat 复制 LicHper.dll 到 IOStudio 输出目录

运行时:
  1. App 启动 → LicenseService.Initialize()
  2. Validate("IOStudio.Pro", 1) → 返回 0 (Pro) 或 10002 (Basic)
  3. UI 绑定 LicenseService.IsPro 控制功能可见性
  4. 用户可在设置面板导入 license token → Login() → TryActivatePro()
```

### Sprint 7.2 — 高级功能 (Day 6-12)

> **注：** Undo/Redo、自动保存、键盘快捷键已提前规划到 Phase 2.5 编辑器增强阶段

| 编号   | 任务                                      | 工时 | 优先级 |
| ------ | ----------------------------------------- | ---- | ------ |
| 7.2.1  | ~~Undo/Redo~~ → 已移至 Phase 2.5A.1       | -    | -      |
| 7.2.2  | ~~自动保存~~ → 已移至 Phase 2.5A.4        | -    | -      |
| 7.2.3  | 崩溃恢复 (autosave → 启动时检测恢复)      | 2h   | P1     |
| 7.2.4  | SafetyGuard 增强: 加速度限制              | 3h   | P1     |
| 7.2.5  | SafetyGuard 增强: 通信超时急停            | 2h   | P0     |
| 7.2.6  | ~~键盘快捷键体系~~ → 已移至 Phase 2.5B.1  | -    | -      |
| 7.2.7  | OutputTest 一键录制为轨道                 | 4h   | P2     |
| 7.2.8  | UI 美化: 主题统一, 动画, 图标             | 4h   | P2     |
| 7.2.9  | 性能优化: 大文件 (>1000 关键帧) 渲染      | 3h   | P1     |
| 7.2.10 | 用户手册 (IOStudio 帮助窗口内嵌 Markdown) | 4h   | P1     |
| 7.2.11 | MotionServer API 文档 (协议参考)          | 2h   | P0     |

**快捷键体系设计：**

| 快捷键              | 功能                     |
| ------------------- | ------------------------ |
| `Space`             | 播放/暂停切换            |
| `Ctrl+Z` / `Ctrl+Y` | Undo / Redo              |
| `Ctrl+S`            | 保存                     |
| `Ctrl+Shift+S`      | 另存为                   |
| `Ctrl+N`            | 新建项目                 |
| `Ctrl+O`            | 打开项目                 |
| `Delete`            | 删除选中关键帧/片段      |
| `Ctrl+A`            | 全选 (当前轨道关键帧)    |
| `Ctrl+C/V/X`        | 复制/粘贴/剪切关键帧     |
| `Home`              | 跳到起始                 |
| `End`               | 跳到末尾                 |
| `+` / `-`           | 缩放时间轴               |
| `F`                 | 自适应缩放 (Fit to view) |
| `I`                 | 设置循环入点             |
| `O`                 | 设置循环出点             |

**验收标准 (Phase 7)：**

| #   | 验收项             | 通过条件                                                            |
| --- | ------------------ | ------------------------------------------------------------------- |
| G1  | Basic 模式默认     | 无 license 时启动 IOStudio, Pro 功能菜单显示🔒, 不可点击            |
| G2  | Basic 加载 .mtn    | Basic 模式下加载 .mtn → 播放 → 设备运动                             |
| G3  | Basic 拒绝 .motion | Basic 模式下无法加载/创建明文 .motion 文件                          |
| G4  | Basic TCP          | Basic 模式 TCP:9600 正常工作, Python/Unity/UE 可连接控制            |
| G5  | Pro 激活           | 导入 license token → Validate("IOStudio.Pro") 返回 0 → Pro 模式解锁 |
| G6  | Pro 全功能         | Pro 模式下时间轴编辑/曲线编辑/导出 .mtn 全部可用                    |
| G7  | IL 混淆            | 混淆后功能正常, ILSpy 无法轻易读取逻辑                              |
| G8  | Undo/Redo          | 添加关键帧→Ctrl+Z→关键帧消失→Ctrl+Y→关键帧恢复                      |
| G9  | 自动保存           | 编辑后 60s 自动保存, 文件更新                                       |
| G10 | 通信超时           | MotionServer 断开客户端后 30s 触发急停                              |
| G11 | 大文件             | 1000 关键帧的时间轴渲染帧率 >30fps                                  |
| G12 | 授权管理 UI        | 设置面板可查看授权状态/激活/注销, 界面完整                          |

---

## Phase 8: Unity/UE 客户端 SDK

> **目标：** 提供 Unity/UE TCP 路径（路径 B）集成 Demo 与文档；嵌入式路径（路径 A，直接 IODevice DLL）已由 Phase 0 完成
> **周期：** W18-W19 (2026-07-13 ~ 2026-07-24)，共 1.5 周
> **前置：** Phase 0（嵌入式路径 ✅）+ Phase 1-3（TCP 协议稳定）
> **里程碑：** 两条路径均有完整 Demo：路径 A（直接 DLL 嵌入，Phase 0 验证）和路径 B（TCP 控制，Phase 8 Demo）

### Sprint 8.1 — Unity SDK (Day 1-3)

| 编号  | 任务                                         | 产出                                         | 工时 |
| ----- | -------------------------------------------- | -------------------------------------------- | ---- |
| 8.1.1 | Unity MotionClient.cs (TcpClient, 帧协议)    | `SDK/Unity/MotionClient.cs` (~120 行)        | 4h   |
| 8.1.2 | Unity MainThreadDispatcher.cs (线程回调)     | `SDK/Unity/MainThreadDispatcher.cs` (~30 行) | 1h   |
| 8.1.3 | Unity Demo 场景 (VideoPlayer + MotionClient) | `SDK/Unity/Demo/CinemaController.cs`         | 3h   |
| 8.1.4 | Unity README (集成指南)                      | `SDK/Unity/README_Unity.md`                  | 1h   |

### Sprint 8.2 — UE SDK (Day 4-6)

| 编号  | 任务                               | 产出                                   | 工时 |
| ----- | ---------------------------------- | -------------------------------------- | ---- |
| 8.2.1 | UE MotionClient Actor (FSocket)    | `SDK/UE/MotionClient.h/.cpp` (~100 行) | 4h   |
| 8.2.2 | UE Demo 关卡 (蓝图触发 + 动感联动) | `SDK/UE/Demo/`                         | 2h   |
| 8.2.3 | UE README (集成指南)               | `SDK/UE/README_UE.md`                  | 1h   |
| 8.2.4 | SDK 综合文档 (协议参考 + 集成指南) | `SDK/README.md`                        | 2h   |

**SDK 产出目录：**

```
IOStudio/
  SDK/
    README.md                      # 综合文档: 协议参考 + 快速开始
    Unity/
      MotionClient.cs              # ~120 行, 拖入 Unity 即用
      MainThreadDispatcher.cs      # ~30 行, 主线程回调
      README_Unity.md
      Demo/
        CinemaController.cs        # 示例: 视频+动感联动
    UE/
      MotionClient.h               # ~100 行, AMotionClient Actor
      MotionClient.cpp
      README_UE.md
      Demo/
```

**验收标准 (Phase 8)：**

| #   | 验收项     | 通过条件                                                      |
| --- | ---------- | ------------------------------------------------------------- |
| H1  | Unity SDK  | MotionClient 连接 IOStudio/MotionPlayer, Play/Pause/Stop 正常 |
| H2  | Unity Demo | VideoPlayer + MotionClient 联动, 视频播放同时触发动感         |
| H3  | UE SDK     | AMotionClient 蓝图调用工作正常                                |
| H4  | .mtn 透明  | 客户端 LoadFile("xxx.mtn") 透明工作, 加解密对 SDK 不可见      |
| H5  | 断线重连   | SDK 断线后自动重连, 状态恢复                                  |

---

## Phase 9: 飞荇客平台协议通讯接口开发对接

> **目标：** 完成与飞荇客动感平台的通讯协议对接，实现 IOStudio/IODevice 与飞荇客平台的数据互通，支持飞荇客平台下发动感指令和状态回传
> **周期：** W15-W17 (2026-06-15 ~ 2026-07-04)，共 2.5 周
> **前置：** Phase 1 (播放引擎) + Phase 0→7 (C++ MotionPlayer)
> **里程碑：** IOStudio/IODevice 可通过飞荇客平台协议接收动感指令并驱动设备运动

### Sprint 9.1 — 协议分析与基础通讯层 (Day 1-4)

| 编号  | 任务                                    | 新建/修改文件                                | 工时 |
| ----- | --------------------------------------- | -------------------------------------------- | ---- |
| 9.1.1 | 飞荇客平台协议文档分析与规范整理        | `Docs/FeiXingKe_Protocol_Spec.md`            | 4h   |
| 9.1.2 | 协议数据模型定义 (消息帧/指令码/状态码) | `Models/FeiXingKe/FxkProtocolModels.cs`      | 3h   |
| 9.1.3 | 协议编解码器 (序列化/反序列化/校验)     | `Services/FeiXingKe/FxkProtocolCodec.cs`     | 4h   |
| 9.1.4 | 通讯客户端/服务端基础框架 (TCP/UDP)     | `Services/FeiXingKe/FxkConnectionService.cs` | 6h   |
| 9.1.5 | 连接管理 (心跳/重连/超时检测)           | `Services/FeiXingKe/FxkConnectionService.cs` | 3h   |

### Sprint 9.2 — 指令处理与设备联动 (Day 5-8)

| 编号  | 任务                                                         | 新建/修改文件                             | 工时 |
| ----- | ------------------------------------------------------------ | ----------------------------------------- | ---- |
| 9.2.1 | 指令Handler (动感指令→MotionPlaybackEngine/DeviceDispatcher) | `Services/FeiXingKe/FxkCommandHandler.cs` | 6h   |
| 9.2.2 | 状态回传服务 (设备状态/播放状态→飞荇客平台)                  | `Services/FeiXingKe/FxkStatusReporter.cs` | 4h   |
| 9.2.3 | 通道映射配置 (飞荇客通道ID ↔ IODevice 通道)                  | `Services/FeiXingKe/FxkChannelMapper.cs`  | 3h   |
| 9.2.4 | IOStudioSettings 增加飞荇客平台配置                          | 修改 `Models/IOStudioSettings.cs`         | 1h   |
| 9.2.5 | MainWindowViewModel 增加飞荇客连接状态显示                   | 修改 `ViewModels/MainWindowViewModel.cs`  | 2h   |

### Sprint 9.3 — 联调测试与优化 (Day 9-12)

| 编号  | 任务                                  | 新建/修改文件                         | 工时 |
| ----- | ------------------------------------- | ------------------------------------- | ---- |
| 9.3.1 | 飞荇客平台模拟器 (测试用)             | `Tools/fxk_simulator.py`              | 4h   |
| 9.3.2 | 端到端联调 (飞荇客平台→IOStudio→设备) | 手动验证                              | 4h   |
| 9.3.3 | 异常处理 (协议错误/连接断开/指令冲突) | 多文件                                | 3h   |
| 9.3.4 | 性能测试 (高频指令场景)               | 手动验证                              | 2h   |
| 9.3.5 | 接口文档 (供飞荇客平台侧参考)         | `Docs/FeiXingKe_Integration_Guide.md` | 2h   |

**验收标准 (Phase 9)：**

| #   | 验收项   | 通过条件                                   |
| --- | -------- | ------------------------------------------ |
| I1  | 协议连接 | IOStudio 与飞荇客平台建立连接，心跳正常    |
| I2  | 指令接收 | 接收飞荇客平台动感指令，解析无误           |
| I3  | 设备驱动 | 飞荇客指令→IODevice 设备正确运动           |
| I4  | 状态回传 | 设备状态/播放状态实时回传至飞荇客平台      |
| I5  | 断线重连 | 网络断开后自动重连，状态恢复               |
| I6  | 指令冲突 | 本地播放与飞荇客指令冲突时有明确优先级策略 |
| I7  | 通道映射 | 飞荇客通道 ID 正确映射到 IODevice 输出通道 |

---

## Phase 10: Unity 插件开发

> **目标：** 开发 Unity Editor 插件 + 运行时组件，使 Unity 项目可便捷集成动感平台功能，支持编辑器内预览和运行时播控
> **周期：** W18-W19 (2026-07-06 ~ 2026-07-17)，共 2 周
> **前置：** Phase 0→7 (C++ MotionPlayer / C# Wrapper) + Phase 1b (TCP MotionServer)
> **里程碑：** Unity Asset Store 可发布级别的插件包，含编辑器扩展 + 运行时 + Demo 场景

### Sprint 10.1 — Unity 运行时核心 (Day 1-4)

| 编号   | 任务                                         | 产出                                   | 工时 |
| ------ | -------------------------------------------- | -------------------------------------- | ---- |
| 10.1.1 | IOToolkit Unity Package 工程结构 (UPM)       | `SDK/Unity/com.iotoolkit.motion/`      | 2h   |
| 10.1.2 | MotionClient 运行时组件 (TCP 连接 + 帧协议)  | `Runtime/MotionClient.cs` (~200 行)    | 6h   |
| 10.1.3 | MotionPlayerComponent (MonoBehaviour 封装)   | `Runtime/MotionPlayerComponent.cs`     | 4h   |
| 10.1.4 | MainThreadDispatcher (异步回调→主线程)       | `Runtime/MainThreadDispatcher.cs`      | 1h   |
| 10.1.5 | 事件系统 (OnStateChanged/OnPlaybackComplete) | `Runtime/MotionEvents.cs`              | 2h   |
| 10.1.6 | 嵌入式路径支持 (直接 P/Invoke IODevice.dll)  | `Runtime/Native/MotionPlayerNative.cs` | 4h   |

### Sprint 10.2 — Unity 编辑器扩展 (Day 5-7)

| 编号   | 任务                                 | 产出                                  | 工时 |
| ------ | ------------------------------------ | ------------------------------------- | ---- |
| 10.2.1 | MotionPlayer Inspector 自定义面板    | `Editor/MotionPlayerEditor.cs`        | 3h   |
| 10.2.2 | .motion/.mtn 文件 Importer           | `Editor/MotionFileImporter.cs`        | 3h   |
| 10.2.3 | Motion Preview Window (编辑器内预览) | `Editor/MotionPreviewWindow.cs`       | 4h   |
| 10.2.4 | 设置面板 (连接配置/设备选择)         | `Editor/IOToolkitSettingsProvider.cs` | 2h   |

### Sprint 10.3 — Demo + 文档 (Day 8-10)

| 编号   | 任务                         | 产出                      | 工时 |
| ------ | ---------------------------- | ------------------------- | ---- |
| 10.3.1 | Demo 场景: 视频 + 动感联动   | `Samples~/CinemaDemo/`    | 4h   |
| 10.3.2 | Demo 场景: VR 过山车         | `Samples~/VRCoasterDemo/` | 3h   |
| 10.3.3 | 集成指南 (README + API 文档) | `Documentation~/`         | 3h   |
| 10.3.4 | UPM Package 打包验证         | `package.json` + CI       | 2h   |

**Unity 插件产出目录：**

```
SDK/Unity/com.iotoolkit.motion/
├── package.json
├── Runtime/
│   ├── MotionClient.cs              # TCP 路径
│   ├── MotionPlayerComponent.cs     # MonoBehaviour
│   ├── MotionPlayerNative.cs        # 嵌入式路径 (P/Invoke)
│   ├── MainThreadDispatcher.cs
│   ├── MotionEvents.cs
│   └── IOToolkit.Motion.Runtime.asmdef
├── Editor/
│   ├── MotionPlayerEditor.cs
│   ├── MotionFileImporter.cs
│   ├── MotionPreviewWindow.cs
│   ├── IOToolkitSettingsProvider.cs
│   └── IOToolkit.Motion.Editor.asmdef
├── Samples~/
│   ├── CinemaDemo/
│   └── VRCoasterDemo/
└── Documentation~/
    ├── index.md
    └── api-reference.md
```

**验收标准 (Phase 10)：**

| #   | 验收项     | 通过条件                                          |
| --- | ---------- | ------------------------------------------------- |
| J1  | UPM 导入   | Unity 2021+ 通过 Package Manager 导入无报错       |
| J2  | TCP 路径   | MotionClient 连接 IOStudio，Play/Pause/Stop 正常  |
| J3  | 嵌入式路径 | MotionPlayerNative 直接调用 IODevice.dll 驱动设备 |
| J4  | 编辑器预览 | Motion Preview Window 可预览 .motion 文件曲线     |
| J5  | Demo 运行  | CinemaDemo 场景: VideoPlayer + 动感联动正常       |
| J6  | 断线重连   | 网络断开后自动重连，状态恢复                      |
| J7  | 文档完整   | README + API 文档 + 集成步骤完整可用              |

---

## Phase 11: UE 插件开发

> **目标：** 开发 Unreal Engine 插件，提供蓝图节点 + C++ API，支持 UE 项目集成动感平台功能
> **周期：** W19-W20 (2026-07-13 ~ 2026-07-24)，共 2 周
> **前置：** Phase 0→7 (C++ MotionPlayer) + Phase 1b (TCP MotionServer)
> **里程碑：** UE Marketplace 可发布级别的插件，含蓝图节点 + C++ API + Demo 关卡

### Sprint 11.1 — UE 插件核心 (Day 1-5)

| 编号   | 任务                                     | 产出                                     | 工时 |
| ------ | ---------------------------------------- | ---------------------------------------- | ---- |
| 11.1.1 | IOToolkitMotion 插件工程结构             | `SDK/UE/IOToolkitMotion/`                | 2h   |
| 11.1.2 | FMotionClient (TCP 连接 + 帧协议)        | `Source/Private/MotionClient.cpp`        | 6h   |
| 11.1.3 | AMotionPlayerActor (Actor 封装)          | `Source/Public/MotionPlayerActor.h`      | 4h   |
| 11.1.4 | 蓝图函数库 (BlueprintFunctionLibrary)    | `Source/Public/MotionBlueprintLibrary.h` | 4h   |
| 11.1.5 | 嵌入式路径 (直接链接 IODevice.dll C API) | `Source/Private/MotionPlayerNative.cpp`  | 4h   |
| 11.1.6 | 蓝图事件委托 (OnStateChanged/OnComplete) | `Source/Public/MotionDelegates.h`        | 2h   |

### Sprint 11.2 — UE 编辑器扩展 + Demo (Day 6-10)

| 编号   | 任务                               | 产出                                  | 工时 |
| ------ | ---------------------------------- | ------------------------------------- | ---- |
| 11.2.1 | Motion Asset 自定义资产类型        | `Source/Public/MotionAsset.h`         | 3h   |
| 11.2.2 | Motion Asset Editor (编辑器内预览) | `Source/Editor/MotionAssetEditor.cpp` | 4h   |
| 11.2.3 | 项目设置面板 (连接配置)            | `Source/Editor/IOToolkitSettings.cpp` | 2h   |
| 11.2.4 | Demo 关卡: 蓝图触发 + 动感联动     | `Content/Demo/`                       | 4h   |
| 11.2.5 | Demo 关卡: VR 体验                 | `Content/Demo/VRDemo/`                | 3h   |
| 11.2.6 | 集成指南 (README + API 文档)       | `Docs/`                               | 3h   |
| 11.2.7 | .uplugin 打包验证 (UE 5.x)         | `IOToolkitMotion.uplugin`             | 2h   |

**UE 插件产出目录：**

```
SDK/UE/IOToolkitMotion/
├── IOToolkitMotion.uplugin
├── Source/
│   ├── IOToolkitMotion/
│   │   ├── Public/
│   │   │   ├── MotionPlayerActor.h
│   │   │   ├── MotionBlueprintLibrary.h
│   │   │   ├── MotionAsset.h
│   │   │   └── MotionDelegates.h
│   │   └── Private/
│   │       ├── MotionClient.cpp
│   │       ├── MotionPlayerNative.cpp
│   │       └── MotionModule.cpp
│   └── IOToolkitMotionEditor/
│       ├── Public/
│       │   └── MotionAssetEditor.h
│       └── Private/
│           ├── MotionAssetEditor.cpp
│           └── IOToolkitSettings.cpp
├── Content/
│   └── Demo/
│       ├── CinemaDemo/
│       └── VRDemo/
└── Docs/
    ├── README.md
    └── API_Reference.md
```

**验收标准 (Phase 11)：**

| #   | 验收项       | 通过条件                                         |
| --- | ------------ | ------------------------------------------------ |
| K1  | 插件加载     | UE 5.x 启用插件无编译错误                        |
| K2  | TCP 路径     | FMotionClient 连接 IOStudio，蓝图 Play/Stop 正常 |
| K3  | 嵌入式路径   | 直接链接 IODevice.dll，无需 IOStudio 进程        |
| K4  | 蓝图节点     | 蓝图编辑器中 MotionBlueprintLibrary 节点可用     |
| K5  | Motion Asset | .motion/.mtn 文件可作为 UE 资产导入和预览        |
| K6  | Demo 运行    | CinemaDemo 关卡: 动感联动正常                    |
| K7  | 文档完整     | README + API 文档 + 蓝图使用指南完整可用         |

---

## 甘特图

```
2026    Feb/Mar            Mar                Apr                May                  Jun                  Jul
W-1  W00  W01  W02  W03  W04  W05  W06  W07  W08  W09  W10  W11  W12  W13  W14  W15  W16  W17  W18  W19
 │    │    │    │    │    │    │    │    │    │    │    │    │    │    │    │    │    │    │    │    │
 ├────┴────┤
 │Phase 0  │  IODevice C++ MotionPlayer (MotionPlayer.h/.cpp + C Wrapper + C# Wrapper)
 │  1.5 周  │
      ├─────┼────┴────┤
            │ Phase 1 │  播放引擎 + MotionServer TCP + .motion/.mtn 格式
            │  2.5 周  │
            ├─────────┼────┴────┤
                       │ Phase 2 │  时间轴基础 UI (无 SDK)
                       │  2.5 周  │
                       ├─────────┼────┴────┤
                                  │ Phase 3 │  预览 + 导出 + .mtn 加密
                                  │  2 周   │
                                  ├────┴────┤
                                  │Phase2.5 │  编辑器增强 (Undo/多选/快捷键)
                                  │  1.5 周  │
                                  ├─────────┼────┴────┤
                                             │ Phase 3 │  导出 + .mtn 加密 (预览已完成)
                                             │  1.5 周  │
                                             ├─────────┼────┴────┤
                                                        │ Phase 4 │  曲线编辑器
                                                        │  2 周   │
                                                        ├─────────┼────┴────┤
                                                                   │ Phase 5 │  媒体增强 (基础已完成)
                                                                   │  1 周   │
                                                        ├────┴────┤
                                                        │ Phase 6 │  效果预设 (可与 P5 并行)
                                                        │  2 周   │
                                                                              ├────┴────┴────┤
                                                                               │   Phase 7    │
                                                                               │LicHper+打磨 │
                                                                               │   2 周       │
                                                                               ├──────────────┤
                                                                                        ├────┴───┤
                                                                                        │Phase 8 │
                                                                                        │SDK 1.5w│
                                                                                        ├────────┤
★M1              ★M2       ★M2.5           ★M3              ★M4       ★M5      ★M6
格式稳定        编辑器可用  编辑器专业版    链路+加密完整   编辑器全功能  C++MP可用  授权集成

M1 (W03): .motion 格式稳定，MotionPlaybackEngine 可播放预览 ✅
M2 (W06): IOStudio 时间轴编辑器可用，基础编辑功能完整 ✅ (Sprint 2.12 完成)
M2.5: 编辑器增强完成 (Undo/Redo + 多选 + 快捷键 + 自动保存)
M3: 编辑→导出 .motion/.mtn 完整链路 (含加密)
M4: 曲线编辑器 + 媒体增强 + 效果预设全功能
M5: IODevice.dll C++ MotionPlayer 可用，Unity/UE 直接播放
M6: LicHper 授权集成完成，IOStudio (Basic+Pro) 可交付
```

**实际进度说明：**

- Phase 1 (播放引擎 + MotionServer TCP) ✅ 已完成
- Phase 2 (时间轴基础 UI) ✅ 已完成 (Sprint 2.1~2.12)
- Phase 3 Sprint 3.1 (预览播放) — 大部分已在 Phase 2 迭代中提前完成
- Phase 5 Sprint 5.1 (视频集成) — 大部分已在 Phase 2 迭代中提前完成 (LibVLCSharp + VlcVideoHost)
- Phase 2.5 (编辑器增强) — **新增, 下一步重点**, 整合 Sprint 2.11 Task 6 专业改进建议

**Phase 5 & 6 可部分并行：** Phase 6 的预设数据模型和协议命令不依赖媒体功能，Sprint 6.1 可与 Sprint 5.2 并行开发，实际压缩 ~1 周。

**Phase 3 工时压缩：** 由于 Sprint 3.1 大部分任务已在 Phase 2 迭代中完成, Phase 3 实际只需 Sprint 3.2 (导出) + Sprint 3.3 (加密), 工时从 2 周压缩至 ~1.5 周。

**Phase 5 工时压缩：** 由于视频集成基础已在 Sprint 2.6~2.8 完成, Phase 5 仅需音频波形相关工作, 工时从 2 周压缩至 ~1 周。

**Phase 0 (C++ MotionPlayer) 延后执行：** 等待 IOStudio 编辑器稳定 .motion 格式后再在 C++ 层复现，避免格式变更导致双端返工。

---

## 风险清单

| ID  | 风险                                      | 影响                                                | 概率   | 缓解措施                                                                                                |
| --- | ----------------------------------------- | --------------------------------------------------- | ------ | ------------------------------------------------------------------------------------------------------- |
| R1  | SkiaSharp 曲线编辑性能瓶颈 (>500 关键帧)  | Phase 4 延期                                        | 中     | 采用 LOD 渲染: 缩小视图时减少绘制点; 使用 SKPath 批量绘制                                               |
| R2  | LibVLCSharp 与 Avalonia 11 兼容性问题     | ✅ 已解决 (VlcVideoHost 子窗口方案)                 | 已关闭 | Sprint 2.8 采用 Win32 CreateWindowEx 独立 HWND 解决                                                     |
| R3  | IODevice.dll 在 SetDO 高频调用下延迟/卡顿 | Phase 1 验收不过                                    | 低     | 批量写入 (合并同一 Tick 内同设备的多通道); 降低 Tick 频率到 30fps                                       |
| R4  | TCP 帧协议黏包/半包                       | Phase 1 延期                                        | 低     | 已设计 4 字节长度前缀, 在 ReadExactAsync 中严格处理; Python 测试脚本验证                                |
| R5  | 贝塞尔插值与 Unity/UE 内置曲线不一致      | 客户端反馈异常                                      | 中     | 提供三次贝塞尔参考实现文档; 对齐 Unity AnimationCurve 的 tangent 参数格式                               |
| R6  | 多设备并发 SetDO 线程安全问题             | 随机崩溃                                            | 中     | DeviceDispatcher 内加 lock 或使用 Channel<T> 队列化写入                                                 |
| R7  | 用户编辑大项目 (100+ 轨道) 内存/UI 卡顿   | Phase 2 体验差                                      | 低     | 虚拟化渲染 (只渲染可见轨道); 延迟加载 Clip/Keyframe                                                     |
| R8  | AES 密钥被逆向提取                        | .mtn 保护失效                                       | 中     | 多层防护: IL 混淆 + 密钥分散存储; 后续可升级到 C++ native DLL 存储密钥                                  |
| R9  | LicHper.dll 被替换/绕过                   | Pro 功能泄露                                        | 低     | IL 混淆 + LicenseService 多点校验; LicHper DXGI 水印作为额外威慑; 后续可升级 C++ native 校验            |
| R10 | **Avalonia NuGet 版本不一致** (v9 新增)   | 运行时 TypeLoadException / 布局异常                 | 高     | `ItemsRepeater` 11.1.5 需统一至 11.3.10；节点三期间集中升级                                             |
| R11 | **net6.0 已 EOL** (v9 新增)               | 安全补丁停止，SDK 工具链逐步弃用                    | 中     | 节点三评估 net8.0 升级影响，节点五执行迁移                                                              |
| R12 | **核心引擎零测试覆盖** (v9 新增)          | 回归 Bug 不可控，C++ 移植缺少对照                   | 中     | 节点三安排 6h 补齐 PlaybackEngine / Interpolation / SafetyGuard 单元测试                                |
| R13 | **节点二工期紧张** (v9 新增)              | .mtn + TCP + 影院播放器需 12 天内完成 ~52h 有效工时 | 高     | 关键路径优化：多 Slot 架构 (14h) → .mtn 加密 (6h) → TCP Server (20h) → 播放器 (16h) 串行                |
| R14 | **多 Slot 并发通道冲突** (v10 新增)       | 多文件同时写同一设备通道时输出异常                  | 中     | ChannelMixer 仲裁策略 (Priority/Blend/Max/Additive)；默认 Priority 策略最安全                           |
| R15 | **引擎求值/派发分离兼容** (v10 新增)      | 重构 EvaluateAndDispatch 可能破坏编辑器预览         | 中     | Engine 保留独立使用能力；Manager 模式下 Tick+EvaluateChannels 分离，编辑器预览使用 Manager 单 Slot 模式 |

---

## 验收标准总表

| 阶段 | 编号    | 验收项                                                      | 类型   |
| ---- | ------- | ----------------------------------------------------------- | ------ |
| P1   | A1-A15  | TCP 协议 + 引擎 + 安全                                      | 功能   |
| P2   | B1-B7   | 时间轴编辑 UI                                               | 功能   |
| P2.5 | B+1~B+6 | 编辑器增强 (Undo/Redo, 多选, 快捷键, 自动保存)              | 体验   |
| P3   | C1-C9   | 预览 + 导出 + .mtn 加密 + 完整链路                          | 集成   |
| P4   | D1-D5   | 曲线编辑                                                    | 交互   |
| P4.5 | D+1~D+8 | 优化与体验提升 (自适应刻度/自动滚动/深色主题/框选/复制粘贴) | 体验   |
| P5   | E1-E4   | 媒体同步                                                    | 功能   |
| P6   | F1-F5   | 效果预设                                                    | 功能   |
| P7   | G1-G12  | LicHper 授权集成 (Basic/Pro门控) + 高级功能 + 性能          | 质量   |
| P8   | H1-H5   | Unity/UE 客户端 SDK                                         | 集成   |
| P9   | I1-I7   | 飞荇客平台协议通讯接口对接                                  | 集成   |
| P10  | J1-J7   | Unity 插件开发 (UPM Package)                                | 产品   |
| P11  | K1-K7   | UE 插件开发 (UE Plugin)                                     | 产品   |
| P12  | —       | 长期功能规划 (模板/多文件/录制/音频)                        | 路线图 |

**端到端验收场景 (最终)：**

```
Pro 模式 (内部开发, 已授权):
1. IOStudio 启动 → LicenseService.Initialize() → Validate("IOStudio.Pro") → Pro 模式
2. 加载 IODevice.xml → 连接实体设备
3. 打开时间轴编辑器 → 新建项目 → 选择 Chair 设备的 OAxis_00~02
4. 导入参考视频 (侏罗纪.mp4)
5. 从预设库拖入 "下坠" 效果 → 调节强度参数
6. 手动编辑贝塞尔曲线 → 微调关键帧
7. 按 ▶ 预览 → 视频+座椅同步动作 → 按 ⏸ 微调 → 继续预览
8. "保存" → 侏罗纪冒险.motion (内部开发用)
9. "导出发行版" → 侏罗纪冒险.mtn (AES-256-GCM 加密, 交付买家)

Basic 模式 (买家环境, 未授权):
10. 买家安装 IOStudio.exe + LicHper.dll + IODevice.xml + 侏罗纪冒险.mtn
11. IOStudio 启动 → Validate 返回 10002 → Basic 模式 (时间轴编辑器菜单显示🔒)
12. 选择 侏罗纪冒险.mtn → [▶ 播放] → 座椅运动
13. TCP:9600 自动启动 → Unity 端 MotionClient 连接
14. Unity 发送 {"cmd":"load","file":"侏罗纪冒险.mtn"} → 成功
15. Unity 发送 {"cmd":"play"} → 座椅运动 → 接收 state 推送
16. Unity 发送 {"cmd":"stop"} → 座椅平滑回中

安全验证:
17. 买家无法打开 .mtn 文件查看/编辑关键帧数据
18. Basic 模式下时间轴编辑器/导出功能完全不可访问 (菜单灰色+锁标识)
19. 修改 .mtn 任意字节 → 加载失败 (GCM 认证拒绝)
20. 将 .authrc license 文件拷贝到另一台机器 → Validate 返回 10001 (BIOS UUID 不匹配)
```

---

## 新建文件清单 (全部阶段)

```
IOStudio/
├── Models/Motion/                          # Phase 1
│   ├── MotionTimeline.cs
│   ├── MotionTrack.cs
│   ├── MotionClip.cs
│   ├── MotionKeyframe.cs
│   ├── PlaybackState.cs
│   ├── TrackGroup.cs                      # Phase 2.5 (轨道分组)
│   └── EffectPreset.cs                    # Phase 6
│
├── Services/Motion/                        # Phase 1
│   ├── MotionPlaybackEngine.cs
│   ├── InterpolationEngine.cs
│   ├── SafetyGuard.cs
│   ├── DeviceDispatcher.cs
│   ├── MotionFileReader.cs
│   ├── MotionServerService.cs
│   ├── MotionCommandHandler.cs
│   ├── MotionProtocol.cs
│   ├── UndoRedoService.cs                 # Phase 2.5 (撤销重做)
│   ├── MotionCryptoService.cs             # Phase 3 (.mtn 加解密)
│   ├── MotionProjectService.cs            # Phase 2
│   ├── MotionExportService.cs             # Phase 3
│   ├── MediaSyncService.cs                # Phase 5
│   ├── AudioWaveformExtractor.cs          # Phase 5
│   ├── BeatDetector.cs                    # Phase 5 [可选]
│   └── EffectPresetService.cs             # Phase 6
│
├── ViewModels/
│   ├── LicenseViewModel.cs                # Phase 7 (授权管理 ViewModel)
│   └── Timeline/                           # Phase 2
│       ├── TimelineEditorViewModel.cs
│       ├── TrackViewModel.cs
│       ├── ClipViewModel.cs
│       ├── KeyframeViewModel.cs
│       ├── CurveEditorViewModel.cs        # Phase 4
│       └── PresetLibraryViewModel.cs      # Phase 6
│
├── Views/
│   ├── LicenseDialog.axaml(.cs)           # Phase 7 (授权激活/管理 UI)
│   └── Timeline/                           # Phase 2
│       ├── TimelineEditorWindow.axaml(.cs)
│       ├── AddTrackDialog.axaml(.cs)
│       ├── ExportDialog.axaml(.cs)        # Phase 3
│       └── ImportMediaDialog.axaml(.cs)   # Phase 5
│
├── Controls/Timeline/                      # Phase 2
│   ├── TimeRulerControl.cs
│   ├── TrackHeaderControl.axaml(.cs)
│   ├── TrackClipControl.axaml(.cs)
│   ├── KeyframeMarkerControl.cs
│   ├── KeyframePropertyPanel.axaml(.cs)
│   ├── CurveEditorControl.cs              # Phase 4
│   ├── MediaPlayerControl.axaml(.cs)      # Phase 5
│   ├── WaveformControl.cs                 # Phase 5
│   ├── PresetLibraryPanel.axaml(.cs)      # Phase 6
│   └── PresetParameterPanel.axaml(.cs)    # Phase 6
│
├── Config/Motion/                          # Phase 1
│   ├── motion.schema.json
│   ├── demo_chair_3dof.motion
│   └── Presets/                            # Phase 6
│       ├── motion/
│       │   ├── bump.preset.json
│       │   ├── turn.preset.json
│       │   ├── drop.preset.json
│       │   └── dive.preset.json
│       └── effects/
│           ├── wind.preset.json
│           ├── smoke.preset.json
│           └── lightning.preset.json
│
├── SDK/                                    # Phase 8 (推迟到基础功能完善后)
│   ├── README.md
│   ├── Unity/
│   │   ├── MotionClient.cs
│   │   ├── MainThreadDispatcher.cs
│   │   ├── README_Unity.md
│   │   └── Demo/CinemaController.cs
│   └── UE/
│       ├── MotionClient.h
│       ├── MotionClient.cpp
│       ├── README_UE.md
│       └── Demo/
│
├── Tools/                                  # Phase 1
│   └── motion_test_client.py
│
├── Services/LicHperInterface.cs            # Phase 7 (LicHper P/Invoke 封装)
├── Services/LicenseService.cs              # Phase 7 (授权门控服务)
├── Services/ILicenseService.cs             # Phase 7 (授权服务接口)
├── release_IOStudio.bat                    # Phase 7 (发布脚本, 含 LicHper.dll 复制)
└── obfuscar_IOStudio.xml                   # Phase 7 (IL 混淆配置)
```

**修改文件清单 (现有文件)：**

| 文件                                | 阶段     | 修改内容                                                                              |
| ----------------------------------- | -------- | ------------------------------------------------------------------------------------- |
| `Models/IOStudioSettings.cs`        | P1       | +MotionServerEnabled, +MotionServerPort                                               |
| `ViewModels/MainWindowViewModel.cs` | P1+P2+P7 | +MotionServer 启停, +打开时间轴编辑器命令, +LicenseService Pro 门控 (CanExecute 绑定) |
| `IOStudio.csproj`                   | P5       | +LibVLCSharp NuGet                                                                    |
| `Config/IOStudioSettings.xml`       | P1       | +MotionServer 配置字段                                                                |
| `Views/MainWindow.axaml`            | P2+P7    | +时间轴菜单, +Pro 功能锁标识, +授权管理入口                                           |
| `App.axaml.cs`                      | P7       | +LicenseService DI 注册, +启动时授权初始化                                            |

---

## 附录: 动感平台编辑器架构审查报告 v2 (Phase 4.12)

> 审查日期: 2026-03-12 | 对比基线: Phase 4.10.4 初次审查

### 一、代码规模总览

| 层级                 | 文件数 | 总行数      | 变化 (vs 4.10.4)      | 主要职责                                        |
| -------------------- | ------ | ----------- | --------------------- | ----------------------------------------------- |
| Models/Motion/       | 10     | ~321        | +1 文件 +32 行        | 纯数据模型 (POCO)                               |
| Services/Motion/     | 12     | ~1,834      | **+5 文件 +747 行**   | 播放引擎、插值、Undo/Redo、标记、导出、曲线预设 |
| ViewModels/Timeline/ | 6      | ~3,292      | +727 行               | 编辑器状态管理、命令、业务逻辑                  |
| Views/Timeline/      | 6      | ~4,042      | +935 行               | UI 布局、事件处理、对话框                       |
| Controls/Timeline/   | 9      | ~4,796      | +955 行               | 自定义渲染 (SkiaSharp/DrawingContext)、交互控件 |
| **总计**             | **43** | **~14,285** | **+6 文件 +3,396 行** |                                                 |

**规模增长分析：** 从 ~10,889 行增长至 ~14,285 行 (+31%), 主要来自:

- Phase 4.10~4.12 新增功能 (标记系统、导出服务、曲线预设、框选/复制粘贴、深色主题等)
- TimelineEditorViewModel 2,000→2,608 行 (+30%), TimelineEditorWindow.axaml.cs ~1,860→2,459 行 (+32%)

### 二、架构改进 (vs 4.10.4) ✅

#### 2.1 服务接口化 — **已部分解决** 🟡

上次审查指出 "7 个 Service 全部为具体类, 无接口"，现已新增 3 个接口:

| 接口                       | 实现类                         | 状态                            |
| -------------------------- | ------------------------------ | ------------------------------- |
| `IPlaybackEngine` (60 行)  | `MotionPlaybackEngine`         | ✅ 新增, ViewModel 通过接口依赖 |
| `IUndoRedoService` (38 行) | `UndoRedoService`              | ✅ 新增, 可 Mock 测试           |
| `ITimelineContext` (51 行) | `TimelineEditorViewModel` 实现 | ✅ 新增, 跨模块共享状态接口     |

> `ITimelineContext` 是关键改进 — `MarkerService` 和 `CurvePresetService` 通过此接口访问共享状态, 而非直接依赖巨型 ViewModel。

**仍缺少接口:** `DeviceDispatcher`、`SafetyGuard`、`MotionFileReader` (static)、`InterpolationEngine` (static)

#### 2.2 新增服务层模块 — **职责外移**

| 新服务                | 行数   | 从何处抽取                     | 解耦效果                                 |
| --------------------- | ------ | ------------------------------ | ---------------------------------------- |
| `MarkerService`       | 237 行 | 新功能 (标记/书签)             | 独立管理, 通过 `ITimelineContext` 交互   |
| `CurvePresetService`  | 185 行 | 原嵌入 VM 的预设逻辑           | 静态服务, 支持单选/批量应用, 走 UndoRedo |
| `MotionExportService` | 285 行 | 原嵌入 ExportDialog 的导出逻辑 | 支持 .motion/CSV/CompactJSON, 可独立测试 |
| `ITimelineContext`    | 51 行  | 新增接口                       | 子服务/子 VM 访问共享状态的桥梁          |
| `IPlaybackEngine`     | 60 行  | MotionPlaybackEngine 抽象      | 引擎可替换/Mock                          |

#### 2.3 Undo/Redo 覆盖改进 — **显著提升** ✅

| 操作                        | 4.10.4 | 当前 | 变化                                      |
| --------------------------- | ------ | ---- | ----------------------------------------- |
| 关键帧增/删                 | ✅     | ✅   | 无变化                                    |
| 关键帧粘贴                  | ✅     | ✅   | 无变化                                    |
| 轨道增/删                   | ✅     | ✅   | 无变化                                    |
| 轨道排序上/下移             | ❌     | ✅   | **新增**                                  |
| 分组删除                    | ❌     | ✅   | **新增**                                  |
| 分组设置/移出               | ❌     | ✅   | **新增**                                  |
| 标记增/删/改                | —      | ✅   | **新增** (MarkerService 全走 UndoRedo)    |
| 曲线预设应用 (单/批量)      | —      | ✅   | **新增** (CurvePresetService 走 UndoRedo) |
| 独立事件增/删               | ❌     | ✅   | **新增**                                  |
| 批量删除关键帧              | —      | ✅   | **新增**                                  |
| 分组创建/重命名             | ❌     | ❌   | 仍直接修改                                |
| 批量设插值/批量移动关键帧   | —      | ❌   | 直接修改, 无撤销                          |
| 属性面板编辑 (时间/值/切线) | ❌     | ❌   | 直接修改, 无撤销                          |
| 事件属性更新                | ❌     | ❌   | 直接修改, 无撤销                          |

**覆盖率:** 从 ~40% → ~70% (核心操作基本覆盖, 属性编辑/部分批量操作仍缺失)

#### 2.4 子 ViewModel 萌芽 — `CurveEditorViewModel`

- 182 行, 作为 `TimelineEditorViewModel` 的子 VM
- 桥接 `CurveEditorControl` ↔ 主 VM 的选中/编辑操作
- 通过 `_parent` 引用主 VM (非接口依赖, 紧耦合)

#### 2.5 模型层扩展 — 干净增长

| 新增模型           | 行数  | 用途                              |
| ------------------ | ----- | --------------------------------- |
| `TimelineEvent`    | 30 行 | 独立事件 (不绑定关键帧)           |
| `TimelineMarker`   | 27 行 | 时间轴标记/书签                   |
| `TimelineViewMode` | 14 行 | 枚举: Dopesheet / Curves 视图模式 |

模型层保持 POCO 纯净, `System.Text.Json` 序列化无泄漏, **评价不变: ⭐⭐⭐⭐⭐**

### 三、持续存在的架构风险 🔴

#### 3.1 ViewModel 巨类 — 恶化 (2,000→2,608 行)

`TimelineEditorViewModel.cs` 仍承担 9+ 个职责域, 且持续膨胀:

| 职责领域       | 行数估算 | 变化 | 说明                                                    |
| -------------- | -------- | ---- | ------------------------------------------------------- |
| 属性/状态      | ~420 行  | +160 | 50+ 响应式属性 (新增主题/吸附/工作区域/视口等)          |
| 分组管理       | ~360 行  | +60  | Create/Delete/Rename/Collapse/DisplayList (部分走 Undo) |
| 关键帧编辑     | ~600 行  | +100 | Select/Add/Delete/Move/Copy/Paste + 多选 + 框选         |
| 轨道管理       | ~310 行  | +40  | Add/Remove/MoveUp/MoveDown/LoadTimeline (全走 Undo)     |
| 独立事件管理   | ~210 行  | +20  | Add/Remove/Update/Sort (部分走 Undo)                    |
| 文件操作       | ~170 行  | +20  | New/Open/Save/SaveAs/AutoSave                           |
| 播放控制       | ~170 行  | +10  | Play/Pause/Stop/Tick/Speed + 引擎事件                   |
| 缩放/滚动/视口 | ~130 行  | +30  | Zoom/Scroll/SnapToGrid/ZoomToFit/WorkArea               |
| 视频同步       | ~70 行   | 0    | VideoFilePath/Duration                                  |
| 构造/清理      | ~168 行  | +60  | 构造函数 (ReactiveCommand 初始化) + Dispose             |

> **紧迫性: HIGH** — 2,608 行远超单个类合理上限 (~500 行), 每次新增功能都在加剧。建议拆分为:
>
> | 子 ViewModel                       | 职责                                | 估算行数 |
> | ---------------------------------- | ----------------------------------- | -------- |
> | `FileOperationsViewModel`          | New/Open/Save/SaveAs/AutoSave       | ~170     |
> | `PlaybackControlViewModel`         | Play/Pause/Stop/Tick/Speed/Loop     | ~170     |
> | `TrackGroupManagerViewModel`       | 轨道增删移/分组 CRUD/DisplayList    | ~670     |
> | `KeyframeEditorViewModel`          | 选中/增删移/多选/框选/复制粘贴/插值 | ~600     |
> | `EventManagerViewModel`            | 独立事件增删改/事件排序             | ~210     |
> | `TimelineEditorViewModel` (协调层) | 共享状态 + 子 VM 聚合 + 视口/缩放   | ~788     |

#### 3.2 代码后台 — 恶化 (~1,860→2,459 行)

`TimelineEditorWindow.axaml.cs` 是项目中**最大的架构债务**:

| 问题区域               | 行数    | 严重度  | 说明                                                                            |
| ---------------------- | ------- | ------- | ------------------------------------------------------------------------------- |
| 键盘快捷键 `OnKeyDown` | ~190 行 | 🔴 HIGH | 30+ 条 `if/else` 链, 应提取为 `KeyBindings` AXAML 或 `KeyboardShortcutBehavior` |
| 轨道拖拽排序           | ~180 行 | 🔴 HIGH | Pointer 跟踪 + VisualTree 命中测试, 应提取为 `DragReorderBehavior`              |
| 分组管理 UI            | ~250 行 | 🟡 MED  | 代码中构建 Window (新建/重命名分组对话框), 应提取为独立 Dialog                  |
| 上下文菜单处理         | ~250 行 | 🟡 MED  | 14 个右键菜单处理方法, 直接调 ViewModel                                         |
| TrackClipControl 接线  | ~180 行 | 🟡 MED  | 手动订阅控件事件→转发 ViewModel                                                 |
| 视频全屏切换           | ~120 行 | 🟡 MED  | Grid 行列操作 + WindowState 管理                                                |
| 滚动/缩放同步          | ~200 行 | 🟡 MED  | 多控件 ScrollOffset 手动同步                                                    |
| 直接 VM 调用           | ~90 处  | 🔴 HIGH | `ViewModel.Method()` 散布全文, 紧耦合                                           |
| 文件对话框             | ~55 行  | 🟡 MED  | 平台交互, 应在 `IDialogService` 中                                              |
| 关键帧导航             | ~50 行  | 🟡 MED  | 遍历轨道找上/下一帧, 应在 ViewModel                                             |
| 内联时间编辑           | ~120 行 | 🟡 MED  | mm:ss/秒/ms 格式解析, 纯逻辑应在 ViewModel                                      |

#### 3.3 对话框缺少 ViewModel

| 对话框                           | 行数   | 问题                                                                                                                |
| -------------------------------- | ------ | ------------------------------------------------------------------------------------------------------------------- |
| `AddTrackDialog.axaml.cs`        | 315 行 | **零 ViewModel**, ~65 行 XML 解析 IODevice.xml (I/O 逻辑在 UI 中), 辅助类 (DeviceInfo/ChannelInfo) 定义在 UI 文件内 |
| `ExportDialog.axaml.cs`          | 155 行 | **零 ViewModel**, 直接调用 `MotionExportService`, 选项构建通过 `FindControl` 读控件                                 |
| `KeyframePropertyPanel.axaml.cs` | 535 行 | 命令式推送更新 (`UpdateFromKeyframe()`/`UpdateFromEvent()`), `_isUpdating` 标志防反馈环, 7 个自定义事件而非数据绑定 |
| `VideoPreviewControl.axaml.cs`   | 434 行 | 完整 LibVLC 生命周期在控件内, 文件选择器在 UI 代码中, 无 ViewModel 管理状态                                         |

#### 3.4 DI 容器缺失

- `TimelineEditorViewModel` 构造函数: `_undoRedo = new UndoRedoService()` — 直接 new, 无注入
- `MotionPlaybackEngine` 通过 `new MotionPlaybackEngine()` 创建 — 虽有 `IPlaybackEngine` 接口但未通过 DI
- `MarkerService` 构造函数接收 `ITimelineContext` — ✅ 正确模式, 但手动传入而非容器注入
- `CurveEditorViewModel` 通过 `_parent` 引用主 VM — 直接引用具体类, 非接口

**整体 DI 成熟度:** Level 1 (手动构造注入, 无容器, 接口仅用于解耦而非注入)

#### 3.5 Undo/Redo 残留盲区

以下操作调用 `MarkDirty()` 但**不走** `UndoRedoService`:

| 操作                                  | 位置       | 影响                               |
| ------------------------------------- | ---------- | ---------------------------------- |
| `CreateGroup()`                       | L660~682   | 无法撤销新建分组                   |
| `RenameGroup()`                       | L728~755   | 无法撤销重命名                     |
| `SetSelectedKeyframesInterpolation()` | L2055~2076 | 批量设插值无撤销                   |
| `MoveSelectedKeyframes()`             | L2082~2105 | 批量移动关键帧无撤销               |
| `UpdateKeyframeProperties()`          | L2370~2395 | 属性面板编辑 (时间/值/切线) 无撤销 |
| `UpdateKeyframeEvent()`               | L2399~2407 | 关键帧事件编辑无撤销               |
| `UpdateEvent()`                       | L2555~2573 | 独立事件属性修改无撤销             |
| `SetManualDuration()`                 | L121~137   | 手动设置时长无撤销                 |

### 四、各层详细评审

#### 4.1 Models/Motion — ⭐⭐⭐⭐⭐ (不变)

| 指标        | 评价                                                                     |
| ----------- | ------------------------------------------------------------------------ |
| POCO 纯净度 | ✅ 10 个文件共 321 行, 零逻辑, 零依赖                                    |
| JSON 序列化 | ✅ `[JsonPropertyName]` snake_case, `[JsonIgnore(WhenWritingNull)]` 合理 |
| 向后兼容    | ✅ `MotionTrack.AxisName` setter 做旧→新字段迁移                         |
| 可扩展性    | ✅ 新增 TimelineEvent/Marker/ViewMode 均干净独立                         |

#### 4.2 Services/Motion — ⭐⭐⭐⭐ (↑ 从 3.5 升至 4)

| 指标             | 评价                                                                                                                         |
| ---------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| 职责单一         | ✅ 12 个文件, 每个服务单一职责                                                                                               |
| 接口抽象         | 🟡 3/12 有接口 (IPlaybackEngine/IUndoRedoService/ITimelineContext)                                                           |
| 静态服务         | 🟡 `InterpolationEngine`/`MotionFileReader`/`CurvePresetService`/`MotionExportService` 为 static — 纯函数合理, 但不利于 Mock |
| ITimelineContext | ✅ 关键改进: 子服务通过接口访问共享状态                                                                                      |
| UndoRedo 设计    | ✅ Command 模式 + `LambdaCommand` 通用实现, 100 层栈限制                                                                     |
| 播放引擎         | ✅ 状态机清晰 (Idle→Playing⇄Paused→Stopping→Idle), Smoothstep 回中                                                           |

#### 4.3 ViewModels/Timeline — ⭐⭐⭐ (不变, 但风险加剧)

| 指标             | 评价                                                                              |
| ---------------- | --------------------------------------------------------------------------------- |
| ReactiveUI 使用  | ✅ 13 个 ReactiveCommand, `RaiseAndSetIfChanged` 规范                             |
| 子 ViewModel     | 🟡 ClipVM/KeyframeVM/TrackVM/GroupHeaderVM/CurveEditorVM 均良好, 但主 VM 仍为巨类 |
| 主 VM 规模       | 🔴 2,608 行, 9 个职责域, SRP 严重违反                                             |
| ITimelineContext | ✅ 主 VM 实现此接口, 子服务可解耦访问                                             |
| CurveEditorVM    | 🟡 正确的子 VM 模式, 但通过 `_parent` (具体类) 而非接口引用                       |

#### 4.4 Views/Timeline — ⭐⭐ (不变)

| 指标                 | 评价                                                                  |
| -------------------- | --------------------------------------------------------------------- |
| TimelineEditorWindow | 🔴 2,459 行 God code-behind, ~56 事件处理方法, ~90 处 VM 成员直接访问 |
| AddTrackDialog       | 🔴 零 ViewModel, XML I/O 在 UI 层                                     |
| ExportDialog         | 🟡 零 ViewModel, 但逻辑量较小 (155 行)                                |
| AXAML 布局           | ✅ 809 行主窗口 AXAML, 结构清晰, 使用 Grid/DockPanel 合理             |

#### 4.5 Controls/Timeline — ⭐⭐⭐⭐ (不变)

| 指标                  | 评价                                                                     |
| --------------------- | ------------------------------------------------------------------------ |
| TrackClipControl      | ✅ 1,321 行 DrawingContext 渲染, Dopesheet+Curves 双模式, 框选/拖拽/吸附 |
| CurveEditorControl    | ✅ 1,108 行 贝塞尔曲线+切线手柄+多轨道+缩放平移                          |
| TimeRulerControl      | ✅ 911 行 自适应刻度+播放头+工作区域+标记/事件渲染                       |
| KeyframePropertyPanel | 🟡 535 行 命令式更新, 应迁移到数据绑定                                   |
| VideoPreviewControl   | 🟡 434 行 完整 LibVLC 生命周期, 应抽取 `IVideoPlayerService`             |
| PlayheadOverlay       | ✅ 70 行 轻量覆盖层                                                      |

> 三大渲染控件 (TrackClip/CurveEditor/TimeRuler = 3,340 行) 作为自定义 `Control` 使用 `DrawingContext` 渲染是正确的架构选择, 不应迁移到 ViewModel。

### 五、与 4.10.4 审查对比 — 改进追踪

| 4.10.4 建议                   | 当前状态                                       | 评估      |
| ----------------------------- | ---------------------------------------------- | --------- |
| 拆分 ViewModel 为 5 个子 VM   | ❌ 未实施, 主 VM 反而增长 30%                  | 🔴 恶化   |
| 引入 `IPlaybackEngine` 等接口 | ✅ 新增 3 个接口                               | 🟢 改善   |
| 引入 DI 容器                  | ❌ 未实施, 仍手动 new                          | 🟡 未变   |
| 代码后台 → Behavior 迁移      | ❌ 未实施, code-behind 增长 32%                | 🔴 恶化   |
| 完整 Undo/Redo 覆盖           | 🟡 从 ~40% 提升至 ~70%                         | 🟢 改善   |
| 多轨道批量操作                | ✅ 框选/批量删除/复制粘贴已实现                | 🟢 已完成 |
| 关键帧曲线预设                | ✅ CurvePresetService (10 种预设, 单/批量应用) | 🟢 已完成 |
| 时间轴标记/书签               | ✅ MarkerService + TimeRuler 渲染              | 🟢 已完成 |

### 六、推荐的架构改进路线图

#### 短期 (Phase 4.13 级别, 1~2 个 Sprint)

| #   | 改进                             | 难度 | 收益 | 说明                                                           |
| --- | -------------------------------- | ---- | ---- | -------------------------------------------------------------- |
| S1  | Undo/Redo 补全残留盲区           | ⭐⭐ | HIGH | 补全 §3.5 中 8 个缺失操作, 约 200 行改动                       |
| S2  | 提取 `KeyboardShortcutService`   | ⭐⭐ | HIGH | 从 code-behind 提取 190 行 OnKeyDown 到 ViewModel 层           |
| S3  | AddTrackDialog → MVVM 化         | ⭐⭐ | MED  | 新建 AddTrackDialogViewModel, XML 解析移到 DeviceSchemaService |
| S4  | KeyframePropertyPanel → 数据绑定 | ⭐⭐ | MED  | 消除命令式 Update 方法, 使用 SelectedKeyframe 直接绑定         |

#### 中期 (Phase 5~6 前, 2~3 个 Sprint)

| #   | 改进                                        | 难度   | 收益         | 说明                                                       |
| --- | ------------------------------------------- | ------ | ------------ | ---------------------------------------------------------- |
| M1  | **ViewModel 拆分** (主 VM → 5 子 VM)        | ⭐⭐⭐ | **CRITICAL** | 解决 SRP 违反, 降低 2,608 行到各 ~300 行, 是所有改进的前置 |
| M2  | 提取 `DragReorderBehavior`                  | ⭐⭐   | HIGH         | 从 code-behind 提取 180 行拖拽逻辑                         |
| M3  | 引入 `IDialogService`                       | ⭐⭐   | MED          | 统一文件对话框/确认对话框/输入对话框                       |
| M4  | VideoPreviewControl → `IVideoPlayerService` | ⭐⭐   | MED          | LibVLC 生命周期解耦, 支持替换播放器后端                    |

#### 长期 (Phase 7 前)

| #   | 改进                                         | 难度     | 收益 | 说明                                                      |
| --- | -------------------------------------------- | -------- | ---- | --------------------------------------------------------- |
| L1  | 引入 DI 容器 (Splat/Microsoft.Extensions.DI) | ⭐⭐⭐   | HIGH | 所有服务通过容器注册/注入, 支持测试替换                   |
| L2  | 补全静态服务接口                             | ⭐⭐     | MED  | InterpolationEngine/MotionFileReader/ExportService 接口化 |
| L3  | 单元测试框架搭建                             | ⭐⭐⭐   | HIGH | 基于已有接口建立 ViewModel/Service 层测试                 |
| L4  | code-behind 完全迁移 (目标 <300 行)          | ⭐⭐⭐⭐ | HIGH | TimelineEditorWindow.axaml.cs 从 2,459 行压缩到纯接线代码 |

### 七、架构合理性评分 (v2)

| 维度         | 4.10.4 评分 | 当前评分    | 变化  | 说明                                              |
| ------------ | ----------- | ----------- | ----- | ------------------------------------------------- |
| 模型层       | ⭐⭐⭐⭐⭐  | ⭐⭐⭐⭐⭐  | →     | 极简 POCO, 新增模型保持一致风格                   |
| 服务层       | ⭐⭐⭐½     | ⭐⭐⭐⭐    | ↑     | 新增接口 + 职责外移 (MarkerService/ExportService) |
| ViewModel 层 | ⭐⭐⭐      | ⭐⭐⭐      | →     | CurveEditorVM 子 VM 正确, 但主 VM 膨胀抵消改进    |
| View 层      | ⭐⭐        | ⭐⭐        | →     | 仍为最薄弱环节, code-behind 持续膨胀              |
| 渲染控件层   | ⭐⭐⭐⭐    | ⭐⭐⭐⭐    | →     | 三大渲染控件专业、独立、可复用                    |
| 可测试性     | ⭐⭐        | ⭐⭐½       | ↑     | 接口化改善了可测性, 但无实际测试代码              |
| 可扩展性     | ⭐⭐⭐      | ⭐⭐⭐      | →     | 模型/服务可扩展, VM 巨类仍是瓶颈                  |
| **综合**     | **⭐⭐⭐**  | **⭐⭐⭐¼** | **↑** | **服务层明显改善, 但 VM/View 层债务持续累积**     |

### 八、结论

**正面变化：** 服务层接口化 (IPlaybackEngine/IUndoRedoService/ITimelineContext) 和职责外移 (MarkerService/CurvePresetService/MotionExportService) 是正确方向, Undo/Redo 覆盖率从 ~40% 提升至 ~70%。

**核心风险：** `TimelineEditorViewModel` (2,608 行) 和 `TimelineEditorWindow.axaml.cs` (2,459 行) 两个巨类分别承担了业务逻辑和 UI 交互的几乎全部职责, **总计 5,067 行占项目总量的 35%**, 且增速高于项目平均水平。这两个文件是后续所有功能 (Phase 5~7) 的修改热点, 不拆分将显著拖慢开发速度并增加回归风险。

**优先建议：** 在进入 Phase 5 (媒体同步) 和 Phase 6 (效果预设) 之前, 优先完成 **M1 (ViewModel 拆分)** + **S1 (Undo 补全)** + **S2 (快捷键提取)**, 预计投入 3~5 天, 可将后续开发效率提升 30%+。

---

## 附录: 动感平台编辑器架构审查报告 v3.1 (Phase 4.13~4.15 全部完成)

> 审查日期: 2026-03-16 | 对比基线: v3 (基础设施创建阶段)
> 涵盖范围: Phase 4.13 (短期改进) + Phase 4.14 (中期改进) + Phase 4.15 (长期改进)
> **本次更新:** v3→v3.1 反映所有接入层任务完成 (DI 注入/VM 拆分/GroupDialog/ContextMenuCommands/VideoPreview 重构)

### 一、代码规模总览

| 层级                            | 文件数 | 总行数 (非空) | 变化 (vs v2)           | 主要变化                                                                                      |
| ------------------------------- | ------ | ------------- | ---------------------- | --------------------------------------------------------------------------------------------- |
| Models/Motion/                  | 13     | ~441          | +3 文件 +120 行        | DeviceSchema/ShortcutAction/ExecuteShortcutResult                                             |
| Services/ + Services/Motion/    | 33     | ~5,039        | **+14 文件 +3,205 行** | 10 接口 + ServiceLocator + DialogService + VideoPlayer + KeyboardShortcut                     |
| ViewModels/Timeline/            | 10     | ~4,060        | **+4 文件 +768 行**    | 6 partial files + AddTrackDialogVM + KeyframePropertyVM + GroupDialogVM + ContextMenuCommands |
| Views/Timeline/ (cs+axaml)      | 8      | ~3,189        | -853 行                | GroupDialog 独立 + AXAML Command 绑定 + code-behind 清理                                      |
| Controls/Timeline/ + Behaviors/ | 10     | ~4,176        | +3 文件 +160 行        | DragReorder/ScrollSync/Fullscreen Behaviors + VideoPreview 重构 (-90 行)                      |
| Converters/                     | 9      | ~595          | +1 文件 +80 行         | TimeFormatConverter                                                                           |
| **IOStudio 主项目**             | **83** | **~17,500**   | **+27 文件 +3,480 行** |                                                                                               |
| **IOStudio.Tests**              | **3**  | **~257**      | **全新**               | xUnit + Moq, 19 个测试                                                                        |
| **总计**                        | **86** | **~17,757**   | **+30 文件**           |                                                                                               |

**规模增长分析 (v3.1 更新)：** 从 ~14,285 行增长至 ~17,500 行 (+22%), 但结构质量大幅改善:

- **ViewModel partial class 拆分**: 原 2,608 行单文件 → 7 文件 3,132 行 (主 837 + FileOps 202 + Playback 182 + TrackGroups 658 + Keyframes 940 + Events 223 + ContextMenuCommands 90)
- **VideoPreviewControl 重构**: 435 → 345 行 (-21%), 移除直接 LibVLC 依赖
- **GroupDialog 独立化**: 新增 299 行 (VM 188 + AXAML 52 + cs 59), Window code-behind 3 个方法精简
- **上下文菜单 AXAML 化**: 6 个 Click 处理程序 → Command 绑定, 移除 ~50 行 code-behind
- **DI 构造注入**: ViewModel 接受 3 个可注入服务 (IDialogService/IUndoRedoService/IPlaybackEngine)

### 二、架构改进成果 (Phase 4.13~4.15)

#### 2.1 服务接口化 — **大幅改善** 🟢 (3/12 → 12/14)

| 接口                       | 实现类                        | Phase      | 状态                   |
| -------------------------- | ----------------------------- | ---------- | ---------------------- |
| `IPlaybackEngine`          | `MotionPlaybackEngine`        | 4.12       | ✅ 既有                |
| `IUndoRedoService`         | `UndoRedoService`             | 4.12       | ✅ 既有                |
| `ITimelineContext`         | `TimelineEditorViewModel`     | 4.12       | ✅ 既有                |
| `IKeyboardShortcutService` | `KeyboardShortcutService`     | **4.13.2** | ✅ 新增                |
| `IDeviceSchemaService`     | `DeviceSchemaService`         | **4.13.3** | ✅ 新增                |
| `IDialogService`           | `AvaloniaDialogService`       | **4.14.3** | ✅ 新增                |
| `IVideoPlayerService`      | `LibVlcVideoPlayerService`    | **4.14.4** | ✅ 新增                |
| `IInterpolationEngine`     | `InterpolationEngineInstance` | **4.15.2** | ✅ 新增 (包装静态类)   |
| `IMotionFileReader`        | `MotionFileReaderInstance`    | **4.15.2** | ✅ 新增 (包装静态类)   |
| `IMotionExportService`     | `MotionExportServiceInstance` | **4.15.2** | ✅ 新增 (包装静态类)   |
| `ICurvePresetService`      | `CurvePresetServiceInstance`  | **4.15.2** | ✅ 新增 (包装静态类)   |
| `IProtocolSender`          | 多种 Sender                   | 既有       | ✅ 既有 (非 Motion 层) |
| `DeviceDispatcher`         | —                             | —          | ❌ 仍无接口            |
| `SafetyGuard`              | —                             | —          | ❌ 仍无接口            |

**接口覆盖率:** 3/12 → 12/14 (86%), 仅 `DeviceDispatcher`/`SafetyGuard` 未接口化 (低优先级, 非核心编辑流程)

#### 2.2 DI 容器 — **从无到有** 🟢

| 组件                                             | 说明                                                           |
| ------------------------------------------------ | -------------------------------------------------------------- |
| `Microsoft.Extensions.DependencyInjection` 6.0.1 | NuGet 包引入                                                   |
| `ServiceLocator.cs` (36 行)                      | 静态访问器, `Configure()` / `Resolve<T>()` / `TryResolve<T>()` |
| `App.axaml.cs` 注册                              | Singleton: 7 服务, Transient: 2 服务                           |

**DI 成熟度:** Level 1 → **Level 3** (容器就位 + 服务注册 + VM 构造注入完成 + ViewLocator DI 创建)

#### 2.3 Undo/Redo 覆盖 — **~40% → ~95%** 🟢

| 操作                        | v2  | v3  | 变化            |
| --------------------------- | --- | --- | --------------- |
| 分组创建/重命名             | ❌  | ✅  | **4.13.1 补全** |
| 批量设插值                  | ❌  | ✅  | **4.13.1 补全** |
| 批量移动关键帧              | ❌  | ✅  | **4.13.1 补全** |
| 属性面板编辑 (时间/值/切线) | ❌  | ✅  | **4.13.1 补全** |
| 关键帧事件编辑              | ❌  | ✅  | **4.13.1 补全** |
| 独立事件属性修改            | ❌  | ✅  | **4.13.1 补全** |
| 手动设置时长                | ❌  | ✅  | **4.13.1 补全** |

**仅剩极少数边缘操作** (如轨道重命名、工作区域调整) 未走 UndoRedo, 核心编辑操作 100% 覆盖。

#### 2.4 AddTrackDialog MVVM 化 — ✅ 完成

| 指标             | 改造前           | 改造后                                |
| ---------------- | ---------------- | ------------------------------------- |
| code-behind 行数 | 315 行           | 57 行 (-82%)                          |
| ViewModel        | 无               | AddTrackDialogViewModel (209 行)      |
| XML 解析位置     | UI 层            | DeviceSchemaService (129 行)          |
| 数据模型         | 定义在 UI 文件内 | Models/Motion/DeviceSchema.cs (45 行) |
| 编译绑定         | 无               | ✅ x:DataType + CompiledBinding       |

#### 2.5 键盘快捷键提取 — ✅ 完成

| 指标                 | 改造前              | 改造后                                  |
| -------------------- | ------------------- | --------------------------------------- |
| OnKeyDown 方法       | ~190 行 30+ if/else | ~20 行: 解析 → ExecuteShortcut()        |
| 快捷键映射           | 硬编码在 View       | KeyboardShortcutService (69 行, 可配置) |
| ShortcutAction 枚举  | 无                  | 74 行, 覆盖全部操作                     |
| ExecuteShortcut 分发 | 无                  | ViewModel 层统一路由                    |

#### 2.6 单元测试 — **从零到 19 个** 🟢

| 测试文件                            | 测试数 | 覆盖层                                 |
| ----------------------------------- | ------ | -------------------------------------- |
| `KeyframePropertyViewModelTests.cs` | 7      | ViewModel (模式切换/值钳位/负时间防护) |
| `UndoRedoServiceTests.cs`           | 7      | Service (Execute/Undo/Redo/事件/Clear) |
| `MotionModelSerializationTests.cs`  | 5      | Model (序列化往返/默认值/snake_case)   |
| **合计**                            | **19** | **三层覆盖**                           |

框架: xUnit 2.5.3 + Moq 4.20.70, 全部通过 ✅

#### 2.7 可复用 Behavior / Converter 提取

| 组件                  | 行数   | 提取来源                | 模式                       |
| --------------------- | ------ | ----------------------- | -------------------------- |
| `DragReorderBehavior` | 162 行 | Window 拖拽排序 ~180 行 | Avalonia AttachedProperty  |
| `ScrollSyncBehavior`  | 95 行  | Window 滚动同步 ~200 行 | Avalonia AttachedProperty  |
| `FullscreenBehavior`  | 102 行 | Window 全屏切换 ~120 行 | 实例类 + Window 引用       |
| `TimeFormatConverter` | 80 行  | Window 时间解析 ~120 行 | IValueConverter + 静态辅助 |

**合计:** 439 行可复用组件, 对应 Window code-behind ~620 行迁移潜力。

### 三、持续存在的架构风险 (更新)

#### 3.1 ViewModel 拆分 — **已完成 (partial class 方式)** 🟢

`TimelineEditorViewModel` 从单文件 2,608 行拆分为 7 个 partial class 文件:

| 文件                      | 行数      | 职责                                  |
| ------------------------- | --------- | ------------------------------------- |
| `.cs` (主文件)            | 837       | 共享状态/属性/构造/快捷键分发/Dispose |
| `.FileOps.cs`             | 202       | New/Open/Save/SaveAs/AutoSave         |
| `.Playback.cs`            | 182       | Play/Pause/Stop/Tick/Speed/Loop       |
| `.TrackGroups.cs`         | 658       | 轨道增删移/分组 CRUD/DisplayList      |
| `.Keyframes.cs`           | 940       | 选中/增删移/多选/框选/复制粘贴/插值   |
| `.Events.cs`              | 223       | 独立事件增删改/事件排序               |
| `.ContextMenuCommands.cs` | 90        | 右键菜单 ReactiveCommand 声明         |
| **合计**                  | **3,132** | **7 文件, 职责明确**                  |

**适配说明:** 采用 partial class 方式而非独立子 VM, 优势:

- 保持类型一致性, View 层绑定路径不变
- 避免跨 VM 协调的复杂消息传递
- 每个文件 <1000 行, 可独立理解

#### 3.2 code-behind — **有所缩减** 🟡

`TimelineEditorWindow.axaml.cs`: 2,459 → **2,079 行** (-380 行, -15.5%)

- 减少来源: OnKeyDown 提取 (-170 行), 上下文菜单 Command 化 (-50 行), GroupDialog 精简 (-120 行), 死代码清理 (-40 行)
- **仍保留的复杂逻辑:** OnCtxSetColor/SetTrackHeight (需刷新 SkiaSharp 控件), OnCtxMoveToGroup (动态子菜单), KeyframePropertyPanel 接线
- Behaviors 已创建但 DragReorderBehavior **尚未接入** (接入后预计再减 ~180 行)

#### 3.3 对话框 ViewModel 覆盖 — **大幅改善** 🟢

| 对话框                | v2 ViewModel | v3.1 ViewModel                     | 变化                         |
| --------------------- | ------------ | ---------------------------------- | ---------------------------- |
| AddTrackDialog        | ❌           | ✅ AddTrackDialogViewModel         | 🟢 完成                      |
| GroupDialog           | ❌           | ✅ GroupDialogViewModel (3 种模式) | 🟢 **新增**                  |
| ExportDialog          | ❌           | ❌                                 | 未变                         |
| KeyframePropertyPanel | ❌           | ✅ KeyframePropertyViewModel       | 🟢 VM+绑定已完成             |
| VideoPreviewControl   | ❌           | ✅ IVideoPlayerService 接入        | 🟢 **重构完成** (435→345 行) |

#### 3.4 DI 接入层 — **已完成** 🟢

- ✅ DI 容器注册 9+ 服务 (Singleton/Transient)
- ✅ ViewModel 构造注入 3 个可选服务 (IDialogService/IUndoRedoService/IPlaybackEngine)
- ✅ 无参构造通过 ServiceLocator.TryResolve 回退
- ✅ ViewLocator.CreateViewModel&lt;T&gt;() 静态工厂方法, DI 优先 + Activator 回退
- ✅ MainWindowViewModel 通过 ViewLocator 创建 TimelineEditorViewModel

### 四、与 v2 审查对比 — 改进追踪

| v2 建议                          | 当前状态                                                         | 评估                                          |
| -------------------------------- | ---------------------------------------------------------------- | --------------------------------------------- |
| S1: Undo/Redo 补全 8 个操作      | ✅ 全部完成, 覆盖率 ~95%                                         | 🟢 ✅ 已完成                                  |
| S2: 提取 KeyboardShortcutService | ✅ 完成, OnKeyDown 精简 ~170 行                                  | 🟢 ✅ 已完成                                  |
| S3: AddTrackDialog MVVM 化       | ✅ 完成, code-behind -82%                                        | 🟢 ✅ 已完成                                  |
| S4: KeyframePropertyPanel 绑定化 | ✅ VM 创建 + Panel 编译绑定 + \_isUpdating 消除                  | 🟢 ✅ 已完成 (4.13.4.4 Window 接线层部分残留) |
| M1: ViewModel 拆分               | ✅ 7 个 partial class 文件 (主 837 行)                           | 🟢 ✅ **已完成** (适配为 partial class 方案)  |
| M2: DragReorderBehavior          | 🟡 Behavior 创建, 未接入 AXAML                                   | 🟡 基础层完成                                 |
| M3: IDialogService               | ✅ 接口+实现+VM 注入完成, OpenFile/SaveAs 已使用                 | 🟢 ✅ **已完成**                              |
| M4: IVideoPlayerService          | ✅ 接口+实现+VideoPreviewControl 重构完成 (移除 LibVLC 直接依赖) | 🟢 ✅ **已完成**                              |
| L1: DI 容器                      | ✅ 容器 + 注册 + VM 构造注入 + ViewLocator DI                    | 🟢 ✅ **已完成**                              |
| L2: 静态服务接口化               | ✅ 全部 4 个静态服务有接口+包装类                                | 🟢 ✅ 已完成                                  |
| L3: 单元测试                     | ✅ 19 个测试, 三层覆盖                                           | 🟢 ✅ 已完成                                  |
| L4: code-behind 迁移             | ✅ GroupDialog + ContextMenuCommands + Behaviors/Converters      | 🟢 ✅ **大部分完成** (DragReorder 接入待定)   |

### 五、架构合理性评分 (v3)

| 维度         | v2 评分     | v3.1 评分     | 变化   | 说明                                                     |
| ------------ | ----------- | ------------- | ------ | -------------------------------------------------------- |
| 模型层       | ⭐⭐⭐⭐⭐  | ⭐⭐⭐⭐⭐    | →      | POCO 纯净, DeviceSchema/ShortcutAction 规范              |
| 服务层       | ⭐⭐⭐⭐    | ⭐⭐⭐⭐⭐    | ↑↑     | 接口覆盖 86%, DI 容器 Level 3, ServiceLocator + 构造注入 |
| ViewModel 层 | ⭐⭐⭐      | ⭐⭐⭐⭐      | ↑↑     | 7 文件 partial class 拆分 + DI 注入 + 4 个对话框 VM      |
| View 层      | ⭐⭐        | ⭐⭐⭐        | ↑↑     | GroupDialog + ContextMenu AXAML化 + Behaviors 就绪       |
| 渲染控件层   | ⭐⭐⭐⭐    | ⭐⭐⭐⭐½     | ↑      | VideoPreview 解耦 LibVLC, 通过 IVideoPlayerService 抽象  |
| 可测试性     | ⭐⭐½       | ⭐⭐⭐⭐      | ↑↑     | 19 个测试 + 完整接口 + DI + Moq = 强可测基础             |
| 可扩展性     | ⭐⭐⭐      | ⭐⭐⭐⭐      | ↑      | DI 容器 + 接口 + partial class = 新功能可注入            |
| **综合**     | **⭐⭐⭐¼** | **⭐⭐⭐⭐¼** | **↑↑** | **所有基础设施完成 + 接入层完成 = 架构就绪**             |

### 六、下一阶段优先建议

#### 高优先级: M2 — DragReorderBehavior 接入

`DragReorderBehavior.cs` (162 行) 已创建, 但尚未接入 AXAML (4.14.2.2~3):

- 需要对齐 TrackClipControl.IsTrackSelected 同步
- 需要拖拽指示器 UI
- 接入后可从 Window code-behind 移除 ~180 行

#### 中优先级: 残留清理

| 任务                                                   | 收益                              |
| ------------------------------------------------------ | --------------------------------- |
| 4.13.4.4: RefreshPropertyPanel() 消除                  | 消除最后的命令式 UI 刷新 (~30 行) |
| ExportDialog ViewModel 化                              | 最后一个无 VM 的对话框            |
| 扩充单元测试 (ContextMenuCommands/FileOps/GroupDialog) | 测试覆盖率从 19 → ~40+            |

#### 完成后预期:

- `TimelineEditorWindow.axaml.cs`: 2,079 → ~1,900 行 (DragReorder 接入 -180)
- 综合评分: ⭐⭐⭐⭐¼ → ⭐⭐⭐⭐½

### 七、结论

**Phase 4.13~4.15 完整成果总结:**

- ✅ 创建 30+ 个新文件 (接口/服务/VM/Behavior/Converter/Tests/Dialog)
- ✅ ViewModel 从 1 文件 2,608 行 → 7 文件 3,132 行 (最大文件 940 行, 主文件 837 行)
- ✅ Undo/Redo 覆盖率 ~40% → ~95%
- ✅ 服务接口覆盖率 25% → 86%
- ✅ DI 容器 Level 3 (注册 + 构造注入 + ViewLocator DI)
- ✅ 单元测试从 0 → 19 个 (三层覆盖)
- ✅ AddTrackDialog code-behind -82%
- ✅ GroupDialog 独立化 (3 种模式, 编译绑定)
- ✅ 上下文菜单 AXAML Command 绑定 (6 处 Click→Command)
- ✅ VideoPreviewControl 解耦 LibVLC (-21%)
- ✅ IDialogService 注入 ViewModel (OpenFile/SaveAs)
- ✅ 键盘快捷键从 View 完全提取到 Service/VM 层
- 🟡 DragReorderBehavior 已创建, 待接入 AXAML
- 🟡 4.13.4.4 Window 接线层部分残留 (RefreshPropertyPanel)

**核心结论:** Phase 4.13~4.15 **全部主要任务已完成**。架构综合评分从 ⭐⭐⭐¼ 提升至 ⭐⭐⭐⭐¼, 12/12 条 v2 建议中 10 条已完成 (83%), 1 条大部分完成, 1 条基础层就绪。项目架构已具备进入 Phase 5 (功能扩展) 的充分条件。
