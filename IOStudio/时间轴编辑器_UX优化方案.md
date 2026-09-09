# 时间轴编辑器 — 多轨道场景 UX 优化方案

> **版本：** v1.2
> **最后更新：** 2026-04-24
> **状态：** Phase UX-A 已全部交付；UX-B3 独立轨已交付；其余 Phase UX-B/C/D/E 规划中
> **关联文档：** [动感平台编辑系统\_架构设计.md](动感平台编辑系统_架构设计.md) · [动感平台\_开发实施计划.md](动感平台_开发实施计划.md)
> **背景：** 用户在编排 14 轨道影院骑乘 demo (6DOF + 8 特效) 过程中暴露的操作性问题归纳整理

---

## 交付状态总览 (2026-04-24)

### Phase UX-A — 紧迫项 ✅ 全部交付

| 编号  | 描述                                   | 状态      | 备注                                                |
| ----- | -------------------------------------- | --------- | --------------------------------------------------- |
| UX-A1 | 轨道头实时预览值跟随播放头             | ✅ 已交付 | BUG-058                                             |
| UX-A2 | 轨道属性对话框 (双击轨道头 / 右键菜单) | ✅ 已交付 | 复用 AddTrackDialog 的编辑模式                      |
| UX-A3 | Alt+拖拽事件/标记 (防止误触)           | ✅ 已交付 | 单击仅选中, Alt+左键才进入拖拽                      |
| UX-A4 | Marker vs Event 视觉区分               | ✅ 已交付 | Marker=圆角矩形标签+贯穿实线; Event=旗帜标签+虚线杆 |

### Phase UX-B — 中期

| 编号  | 描述                                           | 状态                   | 备注                                                                                                     |
| ----- | ---------------------------------------------- | ---------------------- | -------------------------------------------------------------------------------------------------------- |
| UX-B1 | 曲线编辑器焦点模式 + 逐轨 Show-in-Curve 开关   | ✅ 已交付              | 轨道头新增"眼睛"切换按钮 + TrackVM.ShowInCurve 字段                                                      |
| UX-B2 | 显示过滤器下拉 (仅选中 / 仅分组 / 自定义预设)  | ✅ 已交付 (2026-04-24) | DisplayFilterMode (All/SelectedTrackOnly/ActiveGroupOnly/HasKeyframesInWorkArea) + 工具栏 ComboBox + 选中变化自动刷新 |
| UX-B3 | 独立事件轨 / 标记轨 (分带布局, 与刻度完全分离) | ✅ 已交付 (2026-04-24) | 标尺改为 3 分带 (20 刻度 + 22 标记 + 26 事件 = 68px); 标记贯穿实线, 事件贯穿虚线; 每条带内部独立避让层级 |
| UX-B4 | 分组 Solo/Mute + 折叠摘要行                    | ✅ 已交付 (2026-04-24) | TrackGroup.Muted/Soloed + GroupHeaderVM 级联到组内轨道; Solo 分组间互斥; 分组头右侧 S/M 按钮             |
| UX-B5 | 事件多 Lane 显示 (基于 LaneId 字段驱动)        | ⏸️ 已规划              | 数据已就绪 (v2.0 `EventLanes[]`); UI 暂合并显示为单一事件带                                              |
| UX-B6 | 标记多 Lane 显示 (基于 LaneId 字段驱动)        | ⏸️ 已规划              | 数据已就绪 (v2.0 `MarkerLanes[]`); UI 暂合并显示为单一标记带                                             |
| UX-B7 | 事件/标记轨右键菜单"添加 Lane / 移至 Lane"     | ⏸️ 已规划              | 配合 UX-B5/B6                                                                                            |

### Phase UX-C — 长期

| 编号  | 描述                               | 状态      | 备注                                      |
| ----- | ---------------------------------- | --------- | ----------------------------------------- |
| UX-C1 | bool 开关式关键帧编辑器            | ✅ 已存在 | KeyframePropertyPanel 已使用 ToggleSwitch |
| UX-C2 | 多轨批量操作 + Clip/整轨道复制粘贴 | ⏸️ 已规划 | 需要独立剪贴板服务                        |
| UX-C3 | 项目/轨道模板与预设                | ⏸️ 已规划 | -                                         |
| UX-C4 | Dopesheet 行内迷你曲线预览         | ⏸️ 已规划 | -                                         |
| UX-C5 | 事件/标记精确时间输入框            | ⏸️ 已规划 | 选中后在属性面板直接键入 `MM:SS.mmm`      |
| UX-C6 | 事件/标记批量吸附对齐              | ⏸️ 已规划 | 多选 + 对齐到最近关键帧 / 刻度 / 播放头   |
| UX-C7 | 从视频字幕/章节生成 Marker         | ⏸️ 已规划 | 导入 SRT / MP4 Chapter 自动创建 Markers   |

### Phase UX-D — 键盘快捷键 (新增类别)

| 编号  | 描述                                | 状态      | 备注                               |
| ----- | ----------------------------------- | --------- | ---------------------------------- |
| UX-D1 | F2 重命名选中轨道/事件/标记         | ✅ 已交付 (2026-04-24) | ShortcutAction.RenameSelected + KeyboardShortcutService F2 路由 + HandleRenameSelectedAsync (轨道→属性对话框; 事件/标记→属性面板聚焦名称输入) |
| UX-D2 | Del 删除选中项 (事件/标记/关键帧)   | ⏸️ 已规划 | 部分存在, 需要路由统一             |
| UX-D3 | ↑/↓ 切换选中轨道                    | ⏸️ 已规划 | -                                  |
| UX-D4 | ←/→ 跳转上一/下一关键帧             | ⏸️ 已规划 | 配合播放头                         |
| UX-D5 | Ctrl+D 复制选中关键帧到当前播放头   | ⏸️ 已规划 | -                                  |
| UX-D6 | 空格 播放/暂停 (已存在需确认一致性) | ✅ 已存在 | -                                  |

### Phase UX-E — 性能 / 稳定性 (新增类别)

| 编号  | 描述                                            | 状态      | 备注                                             |
| ----- | ----------------------------------------------- | --------- | ------------------------------------------------ |
| UX-E1 | 关键帧虚拟化 (>1000 keyframes 时仅渲染可视区域) | ⏸️ 已规划 | 当前全量 DrawGeometry                            |
| UX-E2 | SyncEventsToRuler / SyncMarkersToRuler 去重调用 | ⏸️ 已规划 | 每次编辑都重建 List, 可改为 ObservableCollection |
| UX-E3 | 长 duration (>30min) 场景的缩放极值优化         | ⏸️ 已规划 | 当前 minPpm=0.001 可能不足                       |
| UX-E4 | 实时值轨道头刷新节流 (高帧率播放时减少 UI 调度) | ⏸️ 已规划 | 对应 UX-A1                                       |

### 已修复的 Bug

| 编号    | 描述                                            | 修复日期   | 备注                                                       |
| ------- | ----------------------------------------------- | ---------- | ---------------------------------------------------------- |
| BUG-058 | 轨道头实时值不随播放头刷新                      | 2026-04-20 | UX-A1 交付                                                 |
| BUG-066 | 标尺点击事件标签文字无法选中 (仅旗帜背景可选中) | 2026-04-24 | HitTestEvent 改用绘制阶段缓存的精确 flagRect; 标记同步修复 |

### 同步变更

- `.motion` 文件格式升级至 **v2.0**: 新增 `TimelineEvent.lane` / `TimelineMarker.lane` / `MotionTrack.show_in_curve` 字段, 以及顶层 `event_lanes` / `marker_lanes` 数组。默认值兼容旧文件 (lane 默认 "default", show_in_curve 默认 true)。
- `cinema_ride_demo.motion` 已升级至 v2.0。
- `TimeRulerControl` 高度 28 → 68 (分带: 20 刻度 + 22 标记 + 26 事件); 左侧轨道面板标题同步调整为 68, 并加入"◆ 标记 / 🚩 事件"图例行。
- 全量单元测试 165/165 通过。

### 反馈闭环与健壮性优化 (2026-08-27)

对应 UX 7.1 (操作日志 / 状态栏反馈) 落地, 以及代码健壮性修复:

- **底部状态栏**: 窗口底部新增状态栏, 展示当前操作反馈、最近 6 条操作日志、撤销/重做栈顶描述、轨道数/时长统计。
  - 涉及文件: `Views/Timeline/TimelineEditorWindow.axaml` (状态栏布局), `ViewModels/Timeline/TimelineEditorViewModel.cs` (RecentOperations / StatusMessage / TrackCountLabel / PushStatus)
- **操作反馈注入**: 命令执行、撤销、重做、保存、另存为、自动保存均推送状态反馈 (`TimelineEditorViewModel.FileOps.cs`)。
- **Undo 栈上限生效**: `UndoRedoService` 由 Stack 改为 List 模拟栈, `MaxUndoLevels=100` 真正生效, 超限丢弃最旧命令, 消除无界内存增长 (`Services/Motion/UndoRedoService.cs`)。
- **原子写入**: `.motion` 保存改为同目录临时文件 + `File.Replace` 原子替换, 崩溃/断电不再损坏原文件 (`Services/Motion/MotionFileReader.cs`)。
- 验证方式: `dotnet build IOStudio.csproj -c Debug` 0 错误; 手动冒烟启动验证。

### 预设编辑流程优化 (2026-08-27)

解决"编辑曲线没有效果"的困惑——编辑曲线从"改库模板"改为**优先编辑时间轴中当前动作实例的关键帧**：

- **交互语义**：双击预设 = 应用预设到时间轴（保持, 方案 A）；「编辑曲线」= 编辑当前动作实例的关键帧（保存立即写回时间轴, 可播放预览, Ctrl+Z 可撤销）。无匹配实例时回退为编辑预设库模板。
- 实现: `ActionInstanceService.GetInstanceClips / ReplaceInstanceKeyframes`; `TimelineEditorViewModel.BuildInstanceCurvePreset / UpdateInstanceCurve`; `TimelineEditorWindow` 编辑回调按"实例优先、模板回退"分发; `PresetCurveEditorWindow` 支持上下文标题（"动作曲线" vs "预设曲线"）。
- 提示明确化: 卡片 ToolTip "单击选中 · 双击应用预设到时间轴 · 拖拽放置到轨道"; 参数对话框标题改"应用预设到时间轴"; 编辑按钮 ToolTip 明确用途。
- 涉及文件: `Services/Motion/ActionInstanceService.cs`, `ViewModels/Timeline/TimelineEditorViewModel.PresetLibrary.cs`, `Views/Timeline/TimelineEditorWindow.axaml.cs`, `Views/Timeline/PresetCurveEditorWindow.axaml(.cs)`, `Controls/Timeline/PresetLibraryPanel.axaml`。
- 验证方式: `dotnet build IOStudio.csproj -c Debug` 0 错误; 手动冒烟。

### 预设面板交互修复 + 动作边界剪裁 (2026-08-27)

- **交互失效根因修复**: `PresetLibraryPanel` 在隧道阶段无条件 `e.Pointer.Capture(null)` 抑制了所有子按钮的 Click, 导致"弹窗取消/关闭/应用无效、单击不选中、编辑曲线无效"（DoubleTapped 不依赖 Click, 故双击仍能弹窗）。修复: 仅在实际拖拽过时才释放捕获 (`Controls/Timeline/PresetLibraryPanel.axaml.cs`)。
- **新增动作边界剪裁**: 时间轴上动作实例 clip 左右边缘可鼠标拖拽拉伸——拖左边界=改 in 点（右边界不动）, 拖右边界=改时长（左边界不动）, 内部关键帧相对时间保持、超出新边界裁剪, 带冲突检测与快照撤销。clip 边缘绘制白色拖拽手柄提示可拖。
  - 涉及文件: `Services/Motion/ActionInstanceService.cs` (ResizeInstance), `ViewModels/Timeline/TimelineEditorViewModel.PresetLibrary.cs` (ResizeActionInstance), `Controls/Timeline/TrackClipControl.cs` (事件), `TrackClipControl.Interaction.cs` (边界命中/拖拽), `TrackClipControl.Rendering.cs` (边缘手柄), `Views/Timeline/TimelineEditorWindow.TrackClipWiring.cs` (绑定)。
- **边界交互体验补强**: 边界命中热区 6→12px; 鼠标 hover 到动作剪辑边界时显示 SizeWestEast 拉伸光标, 移开后恢复默认 (TrackClipControl.Interaction.cs)。
- **边界剪裁语义修正 + 反馈**: 边界热区回退 8px（12px 误触）; 左边界拉伸改为"关键帧保持时间轴绝对位置"（右边界固定、块宽度变化、左侧被裁/留白, 非整体平移）; 右边界因冲突被拒时把原因推到底部状态栏 (ActionInstanceService.ResizeInstance + TimelineEditorViewModel.ExecuteActionMutation 失败分支)。
- **拉伸体验定稿**: ① 拖拽过程实时预览（DrawClip 用"预览边界+缩放关键帧"的临时 drawClip 渲染, 松手前不改真实数据）; ② 拉伸不再删除关键帧; ③ 关键帧按比例同步缩放为默认行为（kf.TimeMs *= newDur/oldDur）。
- **实时预览补强 (Dopesheet) + 全时间轴滚轮缩放**: ① resize 实时预览改为在 DrawDirectManipulationPreview 绘制虚线框+起止/时长标签, 覆盖 Dopesheet 与曲线两种视图（此前只走曲线模式 DrawClip, Dopesheet 下无预览）; ② TrackClipControl 普通滚轮=时间轴缩放（保持鼠标下时间不变, 与标尺一致）, 新增 ZoomRequested 事件接线到 ViewModel.PixelsPerMs/ScrollOffsetX, 全时间轴区域可缩放; Shift+滚轮仍为轨道高度。
- **移动/拉伸交互补全**: ① 中键拖拽平移覆盖整个轨道区域 (PanRequested → ScrollOffsetX); ② 移动/拉伸拖拽过程中实时冲突检测: 与同轨道其它内容重叠时预览框转红并标注"冲突"; ③ SnapTime 追加同轨道其它动作实例 clip 边界为吸附点, 支持无缝衔接吸附; ④ 无缝衔接时比较两侧边界关键帧值, 差异>0.05 在预览标签显示"⚠ 边界值 x→y"预警。
- **曲线视图缩放/平移同步**: CurveEditor 的 Ctrl+滚轮缩放与中键平移改为保持鼠标锚定并上报 ZoomRequested/PanRequested → ViewModel.PixelsPerMs/ScrollOffsetX, 消除"曲线区与标尺缩放断裂"（此前 CurveEditor 直接改自身 PixelsPerMs 不回写 VM）。
- **曲线焦点模式(方案A) + 轨道头优化**: ① 曲线视图默认只显示当前选中轨道(FocusedTrackIndex, 由 SyncCurveEditorData 与 SelectedTrack 变化驱动), 多轨查看用 Solo; ② 取消轨道头双击弹出属性对话框(易误触), 保留右键"轨道属性/F2"; ③ 打开编辑器默认选中第一条轨道; ④ 轨道头 Role 徽章由透明改为实底徽章(TlRoleBadgeBg), 与预设库/标尺一致。
- **多通道预设落轨自动绑定 (2026-08-28)**: 审查发现 DeviceTemplate.RoleMap 无数据源、落轨生成的 Role 轨为空壳。实施: ① PlacePreset 新建角色轨时, 用角色名启发式匹配已加载设备的 OAction 名/标签/OAxis 通道, 命中即创建"已绑定设备/OAction 通道"的轨道(取代空壳), 轨道属性对话框天然预填; ② 值类型一致性: bool 轨落轨前自动二值化关键帧并强制 step 插值 (ActionInstanceService.CreateUnboundRoleTrack / MatchDeviceChannelForRole / NormalizeClipToTrackValueType)。
- **多通道落轨匹配确认 + 拖放目标轨意图 (2026-08-28)**: 新增 PlanPlacement 落轨规划(不修改模型): 按"拖放目标轨→Role 匹配→设备/OAction 启发式匹配"给出建议轨道; 存在需新建/未匹配通道时弹出 TrackMatchDialog 逐通道确认(每通道下拉: 现有轨道/新建轨道), 用户取消则不落轨; PlacePreset 支持 roleToTrackOverride 显式映射。单通道拖到目标轨无歧义→直接放置(主流行为)。同时预设库卡片信息架构重构(名称/分类徽章/描述/关键帧·时长 metrics 层级优化)。
- **数值预设放任意轨 + 编辑立即生效 + 轨道多选 (2026-08-28)**: ① 无目标轨时数值/单通道预设自动用上下文轨道(选中轨→播放头所在轨→首轨), 放任意轨合理不打扰; ② "编辑曲线"优先编辑时间轴中"来自该预设的任意实例"(FindInstanceByPreset), 保存立即写回时间轴可预览, 无实例才编辑模板并提示; ③ 轨道多选: Ctrl+点击轨道头切换多选(MultiSelectedTracks/ToggleTrackMultiSelect), 多选后曲线视图同时显示多条选中轨(CurveEditor 焦点集合 SetFocusedTrackIndexes/UpdateCurveFocusIndexes)。
- **数值线断裂修复 (单关键帧)**: 曲线视图 (CurveEditorControl.DrawTrackCurve) 与 Dopesheet 迷你曲线 (TrackClipControl.DrawClip) 对"仅 1 个关键帧的 clip"不再跳过, 改为绘制满整个 clip 时长的常值水平线, 消除单点位置无曲线的断裂感。
- **连续数值线 + 多选保留 + 横向滚动 (2026-08-28)**: ① 轨道多选首次 Ctrl 点击保留当前激活轨道(不取消, ToggleTrackMultiSelect); ② 曲线视图无数据区间绘制"中性 0.5"虚线基线(DrawNeutralBaseline), 单轨道任意时刻有数值线(与引擎空值语义一致); ③ 时间轴横向滚动条(时间长度>可视区时拖动查看, HScrollSlider/UpdateHScrollRange/OnHScrollChanged 同步 ScrollOffsetX)。
- **工作区 In/Out 条带可视化 + 拖拽调整 (2026-08-28)**: 靠齐视频剪辑软件。① 标尺刻度带内绘制醒目 In/Out 条带(蓝条+上下边线+绿/红边界把手), 保留全高线/I/O 标签/淡蓝区域; ② 条带可拖拽: 左边界=调 In, 右边界=调 Out, 中间=整体移动(保持时长, 自动夹在时间轴范围); ③ 拖动实时同步 VM(WorkAreaChanged→WorkAreaInMs/OutMs), In/Out 加入 AffectsRender 自动重绘; ④ VM 新增 SetWorkAreaInAt/SetWorkAreaOutAt/MoveWorkArea 支持任意时间; ⑤ WorkAreaInMs/OutMs 变化时 SyncWorkAreaToRuler 保证标尺与 VM 双向一致。
- **轨道片段空白修复 + 底部滚动条重构 (2026-08-29)**: ① 根因: TimelineCanvas 在右侧 DockPanel 未设 DockPanel.Dock, Avalonia 默认 Dock=Left → 宽度按内容收缩为 188, 轨道片段不可见; 修复: 把横向滚动条移到它之前, 使 TimelineCanvas 成为 DockPanel LastChild(LastChildFill) 自动撑满(882), 并保留 code-behind 显式宽度同步(itemsCtrl.Width=canvas.Width) 防测量时序回归; ② 底部横向滚动条从 Slider 改为标准 ScrollBar(细轨道+thumb+箭头, 右侧区 Dock=Bottom, Minimum/ViewportSize/Value 与 ScrollOffsetX 同步)。
- **"任一时刻轨道值"求值统一 (2026-08-29)**: 审查确认: 运行时权威为 C++ IODevice MotionPlayer (clip 内插值 linear/step/bezier/ease, 空=0.5, 单帧=值, 首前/末后=首/末值, **无 clip 覆盖=0.5 回中位**); C# IOStudio 为编排工具, 预览求值放 C# 合理但须镜像 C++ 语义。修复: 新增 TrackValueEvaluator (统一"无覆盖=中性 0.5/Bool 0"语义, 提取自引擎), NativeMotionPlaybackEngine.EvaluateTrackAtTime 委托之 (消除原"最近 clip 边界值"与 C++ 运行时的语义偏差); 曲线编辑器 DrawTrackCurve 改为整轨连续采样 TrackValueEvaluator (clip 内插值 + 无覆盖 0.5, 所见即所播), 删除单独 DrawNeutralBaseline 虚线基线。
- **曲线编辑器审查修复 (2026-08-29)**: 审查评分 6.5/10 (手感层扎实, 数据回写层有缺口)。已修复: ① **关键帧移动 Undo** (单选/多选, 新增 KeyframeEditCommitted → CurveEditorViewModel.CommitKeyframeEdit 注册 LambdaCommand); ② **贝塞尔切线修改 Undo** (TangentEditCommitted → CommitTangentEdit); ③ **多选拖拽值域 clamp** 0~1 (不再写出域外数据); ⑤ **bool 轨强制 step** (SetKeyframeInterpolation 忽略非 step, 与运行时/导出一致)。
- **选择同步 + 批量写回收口 (2026-08-30)**: 收口 #2/#4 遗留项。**#2 选择同步**: 曲线多选集 `_multiSelectedKfs` 变化点 (Ctrl/Shift 点击、单选清空、框选结束、Delete 后) 触发 `MultiSelectionChanged` → 接线到 `CurveEditorViewModel.OnMultiSelectionChanged` (曲线 Model 层索引按 `MotionKeyframe` 引用映射为 VM 层 `KeyframeViewModel` 索引后 `AddToSelection`), 使属性面板多选摘要/Delete/批量操作与曲线框选统一; Delete 也复用同一映射, 修复"曲线框选后 Delete 删错帧/失效"隐患。**#4 批量插值写回**: 多选右键"批量设置插值/批量预设"由"直接改 Model + 发 `InterpolationChanged(ti,-1,-1)` (被 VM 丢弃)"改为触发 `BatchInterpolationRequested`/`BatchPresetApplyRequested` → 接线到 `ViewModel.BatchSetInterpolation`/`BatchApplyPreset` (均带 Undo/MarkDirty/NotifyTrackDataChanged 刷新); 右键命中已在多选中的关键帧时保持多选不清空 (修正原无条件 `KeyframeSelected` 清空 VM 选择)。涉及: `CurveEditorControl.Interaction.cs`, `CurveEditorControl.cs`, `CurveEditorViewModel.cs`, `TimelineEditorWindow.TrackManagement.cs`。
- **Idle 循环 (空窗待机, 2026-08-30)**: 解决"整个游戏/影片除预设片段外全程需 idle 循环动作"场景。**架构**: 延续"C++ 权威 + C# 镜像 + 契约测试"。运行时权威 `IODevice C++ MotionPlayer`: `MotionTrack` 新增可选 `idle` (enabled/period_ms/blend_ms/phase_mode/keyframes, 永不覆盖真实 clip 的最低优先级 gap 填充) + 求值三态 (clip 覆盖→插值; 空窗→环形关键帧采样, `continuous` 全局相位跨空窗连贯 / `restart` 每 gap 从头; 无 idle→中性值) + `blend_ms` 与相邻 clip 交叉淡化。C# 镜像: 新增 `Models/Motion/IdleLoop.cs` (JSON 对齐 C++ schema), `MotionTrack.IdleLoop` 可空属性, `TrackValueEvaluator` 新增 5 参重载镜像三态求值 (旧 4 参重载委托保留兼容), `NativeMotionPlaybackEngine.EvaluateTrackAtTime` 传 idle。**体验**: 曲线视图空窗区以虚线半透明"幽灵 idle 曲线"实时预览 (区别于 clip 实线), 轨道属性对话框新增 "🔄 Idle 循环 (空窗待机)" 配置区 (启用开关/周期秒/淡化毫秒/相位模式下拉/单周期关键帧表格增删+插值下拉)。涉及: `MotionPlayer.cpp`, `IdleLoop.cs`, `MotionTrack.cs`, `TrackValueEvaluator.cs`, `NativeMotionPlaybackEngine.cs`, `CurveEditorControl.cs/.Rendering.cs`, `CurveEditorViewModel.cs`, `TrackViewModel.cs`, `AddTrackDialogViewModel.cs/.axaml`, `TimelineEditorViewModel.TrackGroups.cs`。**验证**: C++ 编译部署 + C# 0 错误 + 契约测试 `TrackValueEvaluatorIdleTests` 11 项全过 (空窗=idle、相位、clip 优先、blend、bool step、兼容性)。
- **Overlay 覆盖关键帧 + 融合成熟度 (2026-08-30)**: 响应审查——"多轨 + 空窗某时间点自定义数值"与"数据融合灵活"。**① bool 轨 blend/overlay 输出量化到 0/1**: C++ `QuantizeTrackValue` 与 C# `Evaluate` 内 Quantize 统一 (修复 blend lerp 输出 0.5 中间值导致 SetDO 异常的隐患, 与 C# 渲染量化一致)。**② Overlay 覆盖关键帧层**: `MotionTrack.override_keyframes` (绝对时间点列表, 两点间插值, 首点前/末点后回落 idle/中性, 单点=脉冲), 优先级高于 idle、低于 clip——空窗打点无需建 clip; C++/C# JSON 对齐 + 求值镜像 (6 参重载, 旧重载委托兼容)。**③ 编辑器**: 曲线视图空窗双击 → 添加 Overlay 关键帧 (圆形标记区别于 clip 菱形), 曲线空窗区实线绘制 overlay (覆盖 idle 幽灵); `AddOverrideKeyframeAtTime` 带 Undo (新增/改值)。涉及: `MotionPlayer.cpp`, `MotionTrack.cs`, `TrackValueEvaluator.cs`, `NativeMotionPlaybackEngine.cs`, `CurveEditorControl.cs/.Rendering.cs/.Interaction.cs`, `CurveEditorViewModel.cs`, `TrackViewModel.cs`, `TimelineEditorViewModel.Keyframes.cs`, `TimelineEditorWindow.TrackManagement.cs`。**验证**: 契约测试 +7 (overlay 插值/回落/单点/优先级, bool 量化), 全套 18 项全过; C++ 编译 + C# 0 错误; IOStudio 启动正常。
- **曲线视图直接编辑 idle/overlay (P-A, 2026-08-30)**: 响应审查"配置在表单层、感知在曲线层两层割裂"。**① Overlay 关键帧直接操纵**: 新增 `DragMode.OverrideKeyframe`——圆形标记可点击选中、拖拽改值/时间 (bool 量化+排序), 释放走 `OverrideKeyframeMoved` 同步 + `OverrideKeyframeEditCommitted` → `CommitOverrideKeyframeEdit` 带 Undo; Delete 删除带 Undo (`OnOverrideKeyframeDeleteRequested`)。**② idle 幽灵曲线关键帧直接操纵**: 新增 `DragMode.IdleKeyframe` + `HitTestIdleKeyframe`——方形标记按相位 `phase + k*period` 跨周期重复显示, 点击任一周期实例选中、拖拽改相位/值 (绝对时间映射回 `[0, period)`), 释放走 `IdleKeyframeEditCommitted` → `CommitIdleKeyframeEdit` 带 Undo (相位 clamp); Delete 删除带 Undo (`OnIdleKeyframeDeleteRequested`)。**③ 选择互斥**: 点击 clip/override/idle 任一类关键帧自动清除其他两类选中; 空白框选清除全部。**④ 标记视觉区分**: clip=菱形, override=圆形, idle=方形。涉及: `CurveEditorControl.cs/.Interaction.cs/.Rendering.cs`, `CurveEditorViewModel.cs`, `TimelineEditorWindow.TrackManagement.cs`。**验证**: C# 0 错误; 契约测试 18 项全过 (求值语义未破坏); IOStudio 启动正常。
- **idle 可视化 + 配置体验 (P-B/P-C, 2026-08-31)**: 响应审查 P-B/P-C 项。**P-B 可视化**: ① 曲线视图空窗区叠画 **idle 周期边界竖虚线** (`k*period`), 在无 clip 空窗处显示; 首尾关键帧值不闭合 (差>0.05, bool>0.01) 时边界变**黄色警示虚线** + 顶部双竖线跳变标记——直观确认"循环周期边界在哪""无缝性是否成立" (`DrawIdleCycleBoundaries`); ② `blend_ms>0` 时每个 clip 边界绘制**半透明淡化带**: `[startMs-blend, startMs]` idle→clip 淡入 + `[endMs, endMs+blend]` clip→idle 淡出, 用户可看到过渡位置与时长 (`DrawIdleBlendZones`)。**P-C 配置体验**: ① 轨道属性对话框 idle 区新增**迷你循环预览条** `IdlePreviewStrip` (56px, 网格+曲线+首尾闭合警示点, 绑定 IdleKeyframes/PeriodMs/ValueType, 随表格/模板实时刷新)——填表同时看到曲线形态; ② 新增**待机模板一键填充**: 🌬呼吸/🫨摇摆/〰️微幅 三档 (`ApplyIdleTemplateCommand` → `ApplyIdleTemplate`, 覆盖现有关键帧, 周期自适应)。涉及: `CurveEditorControl.Rendering.cs`, `AddTrackDialog.axaml`, `AddTrackDialogViewModel.cs`, `IdlePreviewStrip.cs` (新)。**验证**: C# 0 错误; 契约测试 18 项全过; IOStudio 启动正常。
- **多轨节奏同步 (GroupId + 相位偏移, 2026-08-31)**: 响应审查"多轨无协同"。**① 相位偏移 `phase_offset_ms`**: idle 求值相位叠加偏移——同组多轨设不同偏移即可**错相编排** (如四轴依次起伏); C++ `MotionIdleLoop.phaseOffsetMs` + `ComputeIdlePhase` 叠加, C# `IdleLoop.PhaseOffsetMs` + `TrackValueEvaluator.ComputeIdlePhase` 镜像。**② 节奏组 `group_id`**: 同组多轨在 continuous 下**共享全局时钟天然同步** (空=独立); 语义: group 为元数据标识, 运行时 continuous 本就共用全局绝对时间, 同周期同组即同步, 相位偏移负责组内错相。C++ `groupId` + C# `IdleLoop.GroupId` JSON 对齐。**③ UI**: 轨道属性对话框 idle 区新增**相位偏移(毫秒)+节奏组输入**, 相位模式下拉图标化 (∞连续 / ▶重置)。涉及: `MotionPlayer.cpp`, `IdleLoop.cs`, `TrackValueEvaluator.cs`, `AddTrackDialogViewModel.cs/.axaml`。**验证**: 契约测试 +3 (continuous/restart 相位偏移、groupId 不影响单轨求值) → 全套 21 项全过; C++ 编译 + C# 0 错误; IOStudio 启动正常。
- **预设库资产生命周期闭环 (P-1, 2026-08-31)**: 响应审查 Biz-1"能存不能管"。**① 卡片右键菜单**: ⭐收藏/✏️重命名/🏷️修改分类/📋导出JSON/🗑️删除预设 (`PresetLibraryPanel.axaml` ContextMenu + code-behind Click 转发)。**② 收藏持久化**: `EffectPreset.IsFavorite` (运行时 UI 状态, 非序列化) + `EffectPresetLibrary` 收藏管理 (`favorites.json` 持久化, `LoadFavorites/SaveFavorites/ToggleFavorite/IsFavorite/SetFavorite`), `Initialize` 时加载, `RefreshPresets` 同步卡片 ⭐ 标记。**③ 重命名/改分类**: 走 `_dialogService.InputAsync` 输入框, `Upsert + SaveAll` 持久化 (内置预设通过 `IsBuiltInPreset` 阻止重命名/删除)。**④ 删除**: `ConfirmAsync` 确认 → `Delete` (内置禁删) → `SaveAll` 刷新; **⑤ 导出**: `TopLevel.Clipboard.SetTextAsync` 复制 JSON 到剪贴板。涉及: `EffectPreset.cs`, `EffectPresetLibrary.cs`, `PresetLibraryPanelViewModel.cs`, `PresetLibraryPanel.axaml/.cs`, `TimelineEditorViewModel.PresetLibrary.cs`。附带修复: `IODeviceView.axaml` 两处 `ToggleButton.ToolTip.Tip` 错误语法改为 `<ToolTip.Tip>` (外部改动引入, 阻塞编译)。**验证**: C# 0 错误; IOStudio 启动正常。
- **idle 入口上移 + 多轨拖拽同步反馈 (P-3a/P-1a/P-1b, 2026-08-31)**: 响应审查三大痛点。**P-3a 跨轨道共享拖拽预览**: 动作实例跨多轨道, 原拖动预览是控件局部 (`_isDraggingAction`), 其他轨道同实例 clip 无反馈。新增静态广播 `TrackClipControl.PublishSharedDragPreview/ClearSharedDragPreview` + `ContainsSharedDragPreviewInstance`, 拖动中每帧广播实例ID/预览时间, 所有绑定该实例的轨道 `DrawDirectManipulationPreview` 画同步预览框 (Teal 虚线), 拖放结束清除; `SharedDragPreviewChanged` 事件驱动其他轨道重绘。**P-1a 轨道头 idle 菜单 + 徽章**: ① 轨道头右键菜单新增 "🔄 待机循环" 子菜单 (启用/停用 + 编辑待机曲线 + 🌬呼吸/🫨摇摆/〰️微幅模板 + 待机参数…), 命令在 `ContextMenuCommands.cs` (`ToggleTrackIdleCommand`/`EditTrackIdleCommand`/`ApplyIdleTemplateToTrack`); ② 轨道头新增 🔄 待机徽章 (启用时显示, 绑定 `HasIdleLoop`); ③ "编辑待机曲线…" 经 `IdleCurveEditRequested` 事件切到曲线视图并聚焦该轨 (`SetupViewModelInteractions` 接线)。**P-1b 属性面板待机区**: 轨道检查器模式新增 "🔄 待机循环" 区段 (状态徽章 + 状态文本 `IdleStatusText` + 启用/停用/编辑曲线按钮 + 模板快捷按钮), `ShowTrackState` 增加 `trackVm` 参数供面板引用。涉及: `TrackClipControl.cs/.Interaction.cs/.Rendering.cs`, `TimelineEditorWindow.axaml/.axaml.cs/.TrackManagement.cs`, `TimelineEditorViewModel.ContextMenuCommands.cs`, `KeyframePropertyViewModel.cs`, `KeyframePropertyPanel.axaml`。**验证**: C# 0 错误; IOStudio 启动正常。
- **idle 编辑体验收敛 (移除对话框表格, 2026-08-31)**: 响应审查"待机循环仍不成熟, 无法方便编辑控制" + 用户明确"移除属性对话框的编辑"。**① 移除 AddTrackDialog idle 表格编辑**: 轨道属性对话框 (AddTrackDialog) 中整个 "🔄 Idle 循环" 编辑区 (启用/周期/淡化/相位/关键帧表格/模板) 移除, 替换为轻量引导卡片 + "在时间轴编辑待机…" 按钮 (`OnEditIdleInTimelineClick` → `RequestEditIdleInTimeline`), 窗口 `ShowTrackPropertiesDialogAsync` 收到该标志即 `EditTrackIdleCommand` 打开独立编辑器。**② 属性更新不再触碰 idle**: `UpdateTrackProperties` 移除 `IdleLoop` 写入 (old/Apply), 避免保存轨道属性时误清空已有待机配置——idle 由时间轴专项编辑器独占管理。**③ 曲线视图 idle 关键帧可添加 (断点1)**: 空窗双击分流——轨道已启用 idle → `IdleKeyframeAddRequested` (相位 = timeMs mod period) 添加待机关键帧 (带 Undo, `AddIdleKeyframeAtTime` 惰性创建 idle 循环); 未启用 → 仍添加 overlay 覆盖点。涉及: `AddTrackDialog.axaml/.axaml.cs`, `TimelineEditorWindow.TrackManagement.cs`, `CurveEditorControl.cs/.Interaction.cs`, `CurveEditorViewModel.cs`, `TimelineEditorViewModel.Keyframes.cs`, `TimelineEditorViewModel.TrackGroups.cs`。**验证**: C# 0 错误; IOStudio 启动正常。
- **独立待机编辑器 (复用关键帧编辑, 2026-08-31)**: 响应用户"待机循环应独立编辑, 核心关注单个片段, 复用关键帧编辑而非填字段"。**① 新建 `IdleCurveEditorControl`**: 单周期曲线编辑器 (水平轴 0→period, 值域 0~1), 复用关键帧编辑交互——**双击空白添加关键帧 / 拖拽改相位与值 (bool 量化) / Delete 删除 / 选中数值标签 / 环形闭合采样曲线 (含跨环闭合)** + 首尾不闭合黄色警示; 预览播放头 (`PreviewPhase` 驱动动画相位点)。**② 新建 `IdleEditorWindow`**: 独立工具窗口 (单例聚焦, `OpenIdleEditor`), 顶部参数工具条 (周期s/淡化ms/相位偏移ms/相位模式下拉 + 🌬呼吸/🫨摇摆/〰️微幅模板 + ✓启用/✕停用 + ▶预览动画), 中部单周期曲线编辑器, 底部交互提示。**③ 入口统一重定向**: `IdleCurveEditRequested` 由"切曲线视图聚焦"改为"打开独立 IdleEditorWindow"——轨道头右键"编辑待机曲线"、属性面板"编辑曲线…"、属性对话框"在时间轴编辑待机…"全部打开同一编辑器窗口 (已开则聚焦+重载目标轨)。**④ 数据流复用 VM**: 添加走 `AddIdleKeyframeAtTime`, 拖拽提交走 `CommitIdleKeyframeEdit`, 删除走 `OnIdleKeyframeDeleteRequested`, 模板走 `ApplyIdleTemplateToTrack`——全部带 Undo。涉及: `IdleCurveEditorControl.cs` (新), `IdleEditorWindow.axaml(.cs)` (新), `TimelineEditorWindow.axaml.cs` (入口重定向)。**验证**: C# 0 错误; IOStudio 启动正常。
- **曲线视图减负 (idle 装饰独立化, 2026-08-31)**: 独立待机编辑器上线后, 动画曲线视图不再叠加 idle 装饰, 解决"曲线视图信息混乱, 太多关键帧与曲线"。`CurveEditorControl` 新增 `ShowIdleOverlaysInCurve` (默认 false): 为 false 时动画曲线视图**不画 idle 幽灵曲线/周期边界虚线/淡化带/idle 方形标记**, 仅显示动画 clip 曲线 + clip 关键帧 + overlay 点——动画编排视图回归纯净。待机细节全部收敛到独立 IdleEditorWindow。涉及: `CurveEditorControl.cs/.Rendering.cs`。**验证**: C# 0 错误; IOStudio 启动正常。
- **中性值可配置 + C++ 侧落地 (2026-08-30)**: ① 审查结论: "回中/空闲值固定 0.5 太绝对", 引入可配置中性值——`MotionTrack.NeutralValue` (float?, json `neutral_value`, WhenWritingNull), `TrackValueEvaluator.ResolveNeutral(valueType, neutralValue)` (显式配置优先, null 按类型 bool=0/float=0.5)。② **C++ 运行时侧实施** (IODevice MotionPlayer, 运行时权威): `MotionTrack` 加 `valueType` + `neutralValue=0.5f`; `ParseMotionJson` 解析 `neutral_value` (显式优先, 否则按 value_type bool=0/float=0.5); `EvaluateClip` 空关键帧返回 neutral; `EvaluateTrack` 无覆盖返回 `track.neutralValue` 并下传 (涉及 `IODevice/IODevice/Source/Private/MotionPlayer.cpp`)。编译: 裸 `MSBuild IODevice.vcxproj` 因 `$(SolutionDir)` 为空导致 include 失效 (C1083 找不到 RawIO/*.h), 改用 `MSBuild IODevice.sln /p:Configuration=Debug /p:Platform=x64` 成功, 产出 `Binaries/Win64/Debug/IODevice.dll` 并复制到 IOStudio 运行目录。③ **C# 回中逻辑改用可配置中性值**: `NativeMotionPlaybackEngine` 完成时写入与平滑回中目标均由硬编码 `isBool?0:0.5f` 改为 `TrackValueEvaluator.ResolveNeutral(valueType, neutralValue)`。④ 架构判断: **不把 C# 求值下沉到 C++**——C++ MotionPlayer 为运行时权威 (UNIPlayer 实播), C# TrackValueEvaluator 为编辑器预览镜像 (编排工具须在 C# 侧高频求值、undo/redo、多选, 下沉会引入 P/Invoke 每帧求值与编辑器依赖 DLL 耦合); 正确姿势是"单点语义权威 + 镜像 + 契约测试防漂移"。
- **demo 文件合法性重写 + 启动进入动作编排 (2026-08-31)**: 用户要求"Motion 文件要合法，不能出现异常"。**① 合法性规则 (权威)**: 值域 `[0,1]`、关键帧按 `time_ms` 升序、clip 内关键帧时间 ∈ `[0, duration]`、clip 非空、插值取值合法、idle 关键帧升序且 ∈ `[0, period]`、动作实例 `role_track_ids` 引用的轨道存在。**② 现有 demo 违规**: 严检发现 9 处——`clip_dive01_heave` 关键帧乱序、俯仰轨多处 `value>1` (1.24/1.35/1.13 等)、`0b4beb62`/`c630bb72` 空关键帧 clip、idle 乱序。**③ 重写 `demo_action_sequence.motion`**: 时长 60s、5 轨 (升降/俯仰/横滚/冲击/急停)、5 个动作实例、5 个内嵌预设、事件/标记/lane 齐备, 全部通过合法校验 (脚本校验 0 问题)。**④ 启动自动进入动作编排面板**: `MainWindowViewModel` 新增 `OpenTimelineEditor(filePath)` + `OpenDefaultActionEditor()`, 构造函数末尾 `Dispatcher.Post(OpenDefaultActionEditor)` 定位输出目录 `Config/Motion/demo_action_sequence.motion` 加载并打开 `TimelineEditorWindow`; csproj 新增 `Config\Motion\*.motion` 复制到输出。**⑤ C++ ease_in_out 兼容 (架构边界)**: C++ `InterpolateKeyframes` 原先只识别 `"ease"`, C# 编辑器写出 `"ease_in_out"` 时 C++ 运行时退化为 linear (编辑器预览与实播不一致的隐性异常), 补识别 `ease_in_out` 等价 `ease`。涉及: `demo_action_sequence.motion`, `IOStudio.csproj`, `MainWindowViewModel.cs`, `MotionPlayer.cpp`。**验证**: Node 脚本严格校验 0 违规; C++ 编译 + C# 0 错误; 新 DLL 复制到 IOStudio 运行目录; IOStudio 启动自动进入动作编排面板。
- **待机编辑器窗口原生行为 + 曲线可控查看 (2026-08-31)**: 响应审查"待机曲线编辑窗口缺原生行为、时间轴曲线不体现待机"。**① P0 窗口原生行为**: `SystemDecorations=BorderOnly` 下系统标题栏被去掉导致无法关闭/移动; 保留 BorderOnly (深色边框) 但新增**自定义可拖拽标题栏** (`PointerPressed → BeginMoveDrag`) + **最小化/最大化/关闭** 三个窗口控制按钮 (`winctl` 样式, 关闭 hover 红色), 标题栏 `✕ 关闭` 保留。**② P1 预览状态反馈**: 新增 `IsPreviewing` + `PreviewToggleText`, 预览按钮文本在 `▶ 预览动画` ⇄ `⏸ 停止预览` 间切换 (`OnPreviewClick` 设置/清除)。**③ P2 曲线可控查看待机**: `CurveEditorControl.ShowIdleOverlaysInCurve` 由普通属性改为 `StyledProperty<bool>` (依赖属性, `AffectsRender` 加入), `TimelineEditorViewModel` 新增 `ShowIdleOverlaysInCurve` 可绑定属性, `TimelineEditorWindow` 曲线编辑器绑定之并在右上角加「显示待机曲线」`ToggleButton` (默认 false 保持减负, 用户可随时开启"可控查看" idle 幽灵曲线/周期边界/淡化带)。涉及: `IdleEditorWindow.axaml(.cs)`, `CurveEditorControl.cs`, `TimelineEditorViewModel.cs`, `TimelineEditorWindow.axaml`。**验证**: C# 0 错误; IOStudio 启动正常。
- **待机曲线只读查看 + 窗口重设计 (2026-08-31)**: 用户反馈三点。**① 时间轴曲线编辑器 idle 只读查看**: 动画曲线视图中的 idle 由"可编辑"改为"仅查看参考"——`DrawTrackKeyframes` 移除 idle 方形关键帧标记 (仅保留 `DrawTrackCurve` 的幽灵虚线曲线); `CurveEditorControl.Interaction.cs` 移除 `HitTestIdleKeyframe` 命中、`DragMode.IdleKeyframe` 拖拽/释放、Delete 删除 idle 分支, 空窗双击不再添加 idle 关键帧 (仅添加 Overlay)。待机关键帧编辑收敛到独立 `IdleEditorWindow`。**② 显示待机按钮仅曲线视图可见**: `TimelineEditorWindow` 曲线区「显示待机曲线」`ToggleButton` 增加 `IsVisible="{Binding IsCurvesMode}"` (Dopesheet 模式隐藏)。**③ 待机编辑窗口 UI 深度优化 + 恢复原生标题**: 去掉 `SystemDecorations="BorderOnly"` (恢复系统原生标题栏/关闭/最小化/最大化, 满足"保持原生窗口标题"), 移除自定义可拖拽标题栏与 `winctl` 按钮; 重排 UI 为三段式 (工具条: 标题+预览主按钮 `primary`; 参数区: 周期/淡化/相位偏移三列分组带单位标签; 模式+模板+启用/停用操作行), 统一深色卡片样式 (`Button.tpl` 深底浅字, `NumericUpDown` 深底), 窗口尺寸增至 680x480。涉及: `CurveEditorControl.Rendering.cs`, `CurveEditorControl.Interaction.cs`, `CurveEditorControl.cs`, `TimelineEditorWindow.axaml`, `IdleEditorWindow.axaml`。**验证**: C# 0 错误; IOStudio 启动正常。
- **待机循环首尾自动闭合 (2026-08-31)**: 用户指出"待机循环编辑器首位帧应保持同步, 不应存在首尾值不闭合提示"。**① 移除"首尾值不闭合(跳变)"警示**: 待机循环为闭合周期曲线 (首帧相位0 与末帧相位period 为同一相位点), 故删除 `IdleCurveEditorControl.Render` 中的首尾闭合警告渲染及 `WarnPen`/`WarnBrush`/`_warnText` 死代码。**② 强制首尾同步**: 新增 `EnforceLoopClosure(kfs)` (首帧相位≈0 与末帧相位≈period 存在时, 强制末帧值=首帧值, 1% 相位容差) + 公开 `SyncLoopClosure()`; 在拖拽 `OnPointerMoved`、添加关键帧 `OnCurveAddKeyframe`、拖拽提交 `OnCurveKeyframeCommitted`、装载 `LoadTrack` 四处调用, 保证待机曲线任何时候首尾闭合无缝。涉及: `IdleCurveEditorControl.cs`, `IdleEditorWindow.axaml.cs`。**验证**: C# 0 错误; IOStudio 启动正常。
- **待机编辑器体验收敛 (2026-08-31)**: 用户四点反馈。**① 首尾帧固定时间点 + 仅 Y 移动**: 待机循环首帧(相位0)/末帧(相位period)为闭合相位点, `OnPointerMoved` 拖拽时对首尾帧锁定 X (phase 固定 0/period), 只允许 Y 轴调整值, 防止破坏闭合时序; 中段关键帧仍可 X/Y 自由移动。**② NumericUpDown 输入修复 + 样式**: 移除上一轮覆盖的暗色 `<Style Selector="NumericUpDown">`, 改为与动作编排一致的浅色输入样式 (`TlInputBg`/`TlBorderLight`/`TlTextPrimary`, MinHeight 28) ——原暗背景+无前景色导致数值不可见无法输入; 同步给 `TextBox`/`ComboBox` 加同款浅色样式。**③ 窗口改浅色系**: `IdleEditorWindow` 设 `RequestedThemeVariant="Light"`, 全部硬编码暗色 (#1e1e2e/#16161f 等) 改为 `TimelineThemeResources` 的 `Tl*` DynamicResource (TlWindowBg/TlPanelBg/TlToolbarBg/TlAccent/TlBorderBrush 等), 与动作编排窗口配色一致; 曲线绘图区沿用深色背景 (#15151f, 与动作编排曲线区 #1e1e2e 同为深色绘图区风格)。**④ 移除窗口内启用/停用**: 待机循环启用/停用不再在窗口内设置, 改由轨道头右键「启用/停用待机」菜单控制 (`ToggleTrackIdleCommand`), 窗口内删除 ✓启用/✕停用按钮, `StatusText` 仅在停用时提示"请在轨道头右键菜单启用"。涉及: `IdleEditorWindow.axaml(.cs)`, `IdleCurveEditorControl.cs`。**验证**: C# 0 错误; IOStudio 启动正常。
- **待机编辑器用词与体验打磨 (2026-08-31)**: 基于用词/UI/体验三线审查结论。**P1 用词**: ① 顶部标题 🔴 → 🔄 (红点易误解为录制/警示, 且与轨道头 🔄 徽章统一); ② 「淡化(ms)」→「混合过渡(ms)」+ ToolTip "待机循环与相邻动作片段的过渡时长"; ③ 底部提示与曲线区 hint 文案统一为「关键帧」。**P2 体验**: ① 模板按钮 (呼吸/摇摆/微幅) 补 ToolTip 说明效果与"覆盖当前关键帧"行为; ② 底部提示拆两行 (交互行 + "Ctrl+Z 撤销 · Esc 关闭窗口 · ▶ 预览单个周期效果" 快捷键行); ③ 新增 Esc 关闭窗口 (`OnKeyDown` 重写)。**P3 层级**: 顶部工具条 Grid 改三列 (标题区 | 1px 分隔线 | 预览按钮), 预览主按钮与标题区视觉分离。涉及: `IdleEditorWindow.axaml(.cs)`。**验证**: C# 0 错误; IOStudio 启动正常。

- **工作区 In/Out 视觉反馈增强 (2026-08-31)**: 用户指出"时间轴出入点与持续时长没有直观视觉反馈"。原工作区 In/Out 仅在标尺(68px)内显示, 轨道主体区不可见且无时长数字。**① 标尺时长徽章**: `TimeRulerControl.DrawWorkArea` 在区域条带中央补白底时长文本 (`FormatTime(out-in)`, 如 `3.5s`/`1:30`), 直观显示 In→Out 持续时长。**② 新增 `WorkAreaOverlay` 控件**: 在轨道主体区 (`TimelineCanvas`) 叠加显示 In/Out 竖线(绿/红)、半透明蓝色区域高亮、顶部细条带 + 居中时长徽章(白底蓝字), 覆盖整个时间轴高度且始终可见 (Dopesheet/Curves 均显示); 复用 `PixelsPerMs`/`ScrollOffsetX` 绑定, In/Out 值由 `SyncWorkAreaToRuler` 统一从 VM 同步 (拖动标尺/VM 变化均触发重绘)。涉及: `WorkAreaOverlay.cs` (新), `TimeRulerControl.cs`, `TimelineEditorWindow.axaml`, `TimelineEditorWindow.NavigationAndSync.cs`。**验证**: C# 0 错误; IOStudio 启动正常。

---

## 〇、问题诊断清单

当轨道数达到 10+ 时，现有编辑器出现 **5 类主要可用性问题**：

| 编号  | 分类         | 现象                                                             | 严重程度 | 用户直接反馈 |
| ----- | ------------ | ---------------------------------------------------------------- | -------- | ------------ |
| UX-01 | 事件语义     | 事件既承担"触发器"又被用作"时间点备注"，语义混用                 | 🟡 中    | ✅           |
| UX-02 | 标尺交互     | 拖动播放头容易误触事件拖拽                                       | 🔴 高    | ✅           |
| UX-03 | 轨道编辑     | 轨道创建后无法改设备映射、值类型、OAction 绑定                   | 🔴 高    | ✅           |
| UX-04 | 曲线编辑器   | 多轨道同屏时曲线互相遮挡，无法专注                               | 🔴 高    | ✅           |
| UX-05 | 轨道头预览值 | 检查器预览值跟随播放头刷新，轨道头实时值不刷新 (已修复, BUG-058) | 🟡 中    | ✅           |
| UX-06 | 值类型一致性 | bool 类型特效仍以 float 关键帧存储，编辑体验不统一               | 🟡 中    | —            |
| UX-07 | 轨道可见性   | 14 轨全部可见时滚动疲劳；Solo/分组折叠不够强                     | 🟡 中    | —            |
| UX-08 | 关键帧密度   | 姿态轨每轨 18 帧，全选/框选在多轨同屏易误操作                    | 🟡 中    | —            |
| UX-09 | 轨道高度管理 | 独立高度 + 全局级别 + Shift+滚轮三种方式并存，心智模型复杂       | 🟢 低    | —            |
| UX-10 | 模板/复用    | 相似轨道 (Wind clip1/clip2) 无复制整条曲线功能                   | 🟡 中    | —            |

---

## 一、UX-01 事件 vs 备注 — 语义拆分

### 1.1 现状

`TimelineEvent` 同时承担两类职责：

- **触发器语义**：运行时回调 Unity/UE，带 `EventData` 载荷 (JSON / 数字 / 字符串)
- **注释语义**：用户在关键时刻 (如 "暴风雨开始") 打文字备注，不关心载荷

混用导致：

- 备注性事件也产生运行时回调（冗余触发）
- 检查器面板显示不相关的 DataType/Data 字段
- 视觉上无法一眼区分"这是要触发的"还是"这是给人看的"

### 1.2 方案：引入 Marker（标记）作为一等公民

**`TimelineMarker` 已存在但被轻度使用**。建议明确区分：

| 类型             | 用途               | 运行时行为               | 检查器字段                  | 视觉样式        |
| ---------------- | ------------------ | ------------------------ | --------------------------- | --------------- |
| `TimelineMarker` | 注释 / 章节划分    | **不触发回调**，仅视觉   | name, note, color           | 旗帜形/方框     |
| `TimelineEvent`  | 运行时可消费的事件 | 回调 Unity/UE + TCP 广播 | event_name, data_type, data | 三角标记 (现有) |

**落地动作：**

1. 标尺区域右键菜单已有「在此处添加标记」(BUG-051)，**将其作为备注场景首选项**，说明文档中明示
2. Marker 检查器面板独立实现（目前可能复用 Event 面板），去除 DataType 字段，新增 "note" 多行文本
3. Marker 视觉改用**矩形标签+文本**（事件是三角），一眼可辨
4. 规划：Marker 支持"章节"语义（占用一段时间区间），点击章节可跳到其起止点

---

## 二、UX-02 标尺播放头 vs 事件 拖拽冲突

### 2.1 现状

已修复 BUG-057：事件命中区缩小到 10px + 仅标尺上 60% 区域。但仍存在：

- 事件密集时 (如 1s 内多个雷电事件) 播放头几乎无法点在事件之间
- 长按拖拽与单击跳转无法明确区分

### 2.2 方案：三层交互优先级 + 事件独立通道

**短期 (代价小，立即收益)：**

1. **事件拖拽需要修饰键**：默认左键按下 + 拖动 = 播放头；`Alt + 左键拖拽` = 拖动事件
2. **单击事件 = 选中**（填充检查器），**不进入拖拽模式**；拖拽必须明确持续 >4px
3. 标尺事件区域增加"事件锁"按钮（全局），锁定后事件不可拖动

**中期 (架构改进)：**

1. 将事件显示从标尺中剥离，**单独一条事件轨** (Event Track) 独占高度 20-28px，位于时间标尺下方
2. 事件轨可折叠/展开；折叠时仅保留窄色带
3. 标记 (Marker) 也独占一条，与事件轨并列

**长期：**

- 支持事件分组 / 筛选（例如按 event_name 前缀过滤显示）

---

## 三、UX-03 轨道创建后编辑 — 轨道属性面板

### 3.1 现状

`添加轨道` 对话框选择 device + oaction + value_type，之后**只能删除不能改**：

- 想把 "Light" 改绑到 "LegSweep" 必须删 + 重建 + 复制所有关键帧
- float ↔ bool 无切换路径，误选了只能重建
- 缺少通道路由查看（此轨道实际输出到哪个 OAxis）

### 3.2 方案：轨道属性对话框 (Track Properties Dialog)

**触发方式：**

- 轨道头双击（名称区域）
- 轨道头右键菜单「属性 / 编辑映射...」
- 快捷键 `F2` (焦点在轨道头时)

**可编辑字段：**

| 字段               | 编辑方式             | 约束                                     |
| ------------------ | -------------------- | ---------------------------------------- |
| Label              | TextBox              | 非空                                     |
| Color              | 颜色选择器           | 色盘 + 预设 12 色                        |
| Device             | ComboBox (从 XML)    | 切换时同步刷新 OAction 列表              |
| OAction            | ComboBox (按 Device) | "无绑定" 选项保留无设备模式              |
| OutputType         | ComboBox             | oaction / oaxis                          |
| OAxis 通道         | ComboBox             | OutputType=oaxis 时可见                  |
| ValueType          | ComboBox             | float / bool；切换时提示"关键帧将二值化" |
| Group              | ComboBox (已有组)    | 可新建                                   |
| Enabled/Muted/Lock | 已有，移入本对话框   |                                          |

**切换 ValueType 的副作用约定：**

- float → bool：所有关键帧 value ≥0.5 记为 1.0 否则 0.0，interpolation 全部改 step
- bool → float：保持数值 (0/1)，interpolation 保持 step，用户可后续编辑
- 需要 UndoRedo 记录完整前后状态

---

## 四、UX-04 曲线编辑器混乱 — 多轨可视化

### 4.1 现状

14 轨同屏时：

- 姿态轨 6 条曲线相互穿插，色彩拥挤
- 特效 bool 轨渲染为矩形条带插入曲线之间，视觉噪声
- 放大看细节 → 越叠越乱
- 某轨选中高亮不够突出（所有轨都是同类色彩饱和度）

### 4.2 方案：业界通用的多曲线管理范式

参考 DaVinci Resolve / After Effects / Blender Graph Editor 的实践：

**【方案 A — 焦点模式 (推荐默认行为)】**

- 曲线编辑器默认**只显示选中的一条或多条轨道**，未选中的灰化或隐藏
- 轨道头提供 `👁 Show in Curve Editor` 开关（每轨独立）
- 新增 `曲线焦点` 模式开关：开启时仅选中轨可见 + 全高度渲染

**【方案 B — 曲线池 + 独立值域 (业界做法)】**

- 为每轨引入**独立 Y 轴归一化**：当前所有轨道共享 0-1 Y 轴，但视觉上曲线会互相参照
- 改为：每轨在自己的行内独立渲染（类似 Dopesheet 但显示曲线），避免跨轨叠加
- 即 **Dopesheet 模式 + 迷你曲线预览**：保留 Dopesheet 的行分离 + 每行内画出曲线形状

**【方案 C — 显式可见性过滤器】**

- 时间轴顶部工具条新增「显示过滤」下拉菜单：
  - 仅选中轨
  - 仅当前分组
  - 仅有关键帧范围覆盖播放头的轨
  - 自定义（保存过滤预设）

**【方案 D — 曲线折叠到分组】**

- 分组折叠时，组级显示一条"聚合曲线"（组内所有轨的平均或最大值）
- 展开时才显示各轨详细曲线

### 4.3 落地优先级

1. **P0 (本周)**：方案 A 焦点模式默认开启 + 每轨 `Show in Curve Editor` 开关
2. **P1 (下个迭代)**：方案 C 可见性过滤器
3. **P2**：方案 B 独立值域行内曲线 (Dopesheet 升级)
4. **P3**：方案 D 分组聚合预览

---

## 五、UX-05 轨道头实时预览值 — 已修复

### 5.1 根因

`TrackViewModel.LiveValue` 仅由 `PlaybackNotification.LiveValues` (设备引擎推送) 更新，未播放时拖动播放头无法刷新。检查器通过 `CurrentTimeMs` 变更 → `RefreshPropertyPanel()` → `InterpolationEngine.Evaluate()` 独立采样，故能跟随。

### 5.2 修复 (2026-04-20, BUG-058)

1. 新增 `TimelineEditorWindow.RefreshAllTrackLiveValues()`：遍历所有轨对当前播放头进行插值采样 → 写入 `TrackViewModel.LiveValue`
2. 在 `OnViewModelPropertyChanged` 中监听 `CurrentTimeMs`，未播放时调用刷新（播放中由引擎推送覆盖）
3. 文件加载后 + `Tracks.CollectionChanged` 时也采样一次，确保初始正确显示 t=0 值

**涉及文件**：`TimelineEditorWindow.axaml.cs`, `TimelineEditorWindow.TrackClipWiring.cs`

---

## 六、UX-06 ~ UX-10 其他识别的问题

### 6.1 UX-06 bool 值的关键帧编辑体验

- 当前 bool 轨仍用 float 关键帧 + step 插值表示
- 建议：bool 轨的关键帧编辑器提供 **开关按钮** 替代数值滑块
- 连续相同值的关键帧可自动合并（清理冗余）

### 6.2 UX-07 轨道大量可见时的效率

- 分组折叠应可"仅显示一行摘要"，显示组内轨道数和激活状态
- 新增 **Solo Group** (仅播放本分组) 和 **Mute Group**
- 轨道搜索框（按 label / oaction 名过滤可见轨）

### 6.3 UX-08 关键帧密集时的操作

- 多选框选限定在**当前轨道**（Shift+框选 = 跨轨道）
- 放大到一定缩放级别后自动显示**关键帧吸附网格**
- 关键帧对齐功能（对齐到其他轨关键帧时间、对齐到播放头、对齐到 fps 帧边界）

### 6.4 UX-09 轨道高度心智模型

- 当前：全局级别 (0/1/2) × 独立高度覆盖 × Shift+滚轮局部调整，三者交互规则用户不透明
- 建议：**移除独立高度**，只保留全局级别 + 轨道级别的"突出/正常/折叠"三态
- Shift+滚轮改为"在三态之间切换"而非连续变化

### 6.5 UX-10 复用与模板

- **Clip 复制粘贴**：右键 clip → 复制 clip → 在别处粘贴
- **整轨道复制粘贴**：右键轨道头 → 复制为新轨 (换 OAction 绑定)
- **关键帧模板**：保存当前选中关键帧序列为"模式" (如"快速抖动")，可在别处应用
- **项目模板 / 预设**：保存当前项目结构 (轨道配置) 为预设，下次新建直接载入

---

## 七、额外建议（架构层面）

### 7.1 操作日志 / 状态栏反馈

- 大量操作时用户无法确认是否生效（例如"为 6 轨批量加关键帧"是否成功）
- 状态栏展示最近 3 条操作摘要 + 成功/失败/撤销图标

### 7.2 快捷键发现性

- 大量功能隐藏在右键菜单和快捷键中，新用户难以发现
- 建议：F1 面板显示当前上下文的所有快捷键 + 说明（按焦点区域动态变化）
- 每个菜单项后显示其快捷键

### 7.3 多轨批量操作

- Ctrl 多选轨道 → 批量改 Enable / Mute / 分组 / 颜色 / 高度
- 批量删除关键帧（跨多轨）
- 批量缩放 / 偏移所有选中轨道的关键帧时间

### 7.4 关键帧 / Clip 语义清晰化

- 当前 Clip 存在但其必要性用户不明（为何不能直接是"轨道+关键帧"）
- 建议文档 + 工具提示明确 Clip 的作用：定义"该段时间内有输出，外部区域静默"
- 单轨道单 Clip 时，视觉上可以弱化 Clip 边框

---

## 八、实施路线图

### Phase UX-A (紧迫，≤1 周)

| #   | 任务                                           | 文件                                    | 预期工时 |
| --- | ---------------------------------------------- | --------------------------------------- | -------- |
| 1   | ✅ 轨道头实时预览值跟随播放头 (UX-05, BUG-058) | TimelineEditorWindow.TrackClipWiring.cs | 完成     |
| 2   | 轨道属性对话框 (UX-03)                         | 新建 TrackPropertiesDialog.axaml        | 中       |
| 3   | 事件拖拽改为 Alt+拖拽，单击只选中 (UX-02 短期) | TimeRulerControl.cs                     | 小       |
| 4   | Marker/Event 视觉区分 (UX-01)                  | TimeRulerControl.cs + MarkerInspector   | 小       |

### Phase UX-B (中期，2-3 周)

| #   | 任务                                               | 文件                                     |
| --- | -------------------------------------------------- | ---------------------------------------- |
| 5   | 曲线编辑器焦点模式 + Show-in-Curve 开关 (UX-04 P0) | CurveEditorControl.cs, TrackViewModel.cs |
| 6   | 显示过滤器下拉 (UX-04 P1)                          | TimelineEditorWindow.axaml               |
| 7   | 独立事件轨 (UX-02 中期)                            | 新建 EventLaneControl.cs                 |
| 8   | 分组 Solo/Mute + 组折叠摘要 (UX-07)                | TrackGroup.cs + AXAML                    |

### Phase UX-C (长期)

| #   | 任务                             | 备注                  |
| --- | -------------------------------- | --------------------- |
| 9   | bool 关键帧开关式编辑器 (UX-06)  | KeyframePropertyPanel |
| 10  | 多轨批量操作 + Clip/轨道复制粘贴 | 命令层扩展            |
| 11  | 项目模板 / 预设                  | 文件服务              |
| 12  | Dopesheet 行内曲线 (UX-04 P2)    | TrackClipControl 重绘 |

---

## 九、验收标准

- **多轨 (≥10 轨) 场景下连续编排 1 小时，无误操作引发的返工**
- **新用户在无培训情况下 10 分钟内完成轨道映射修改 + 关键帧编辑基本流程**
- 曲线编辑器在焦点模式下仅显示用户当前关注的轨道，其余不干扰视觉
- 事件与标记在视觉上明确区分，用户能立即识别备注性标签
- 所有 UX-\* 功能均经过单元/集成测试覆盖，且具备 UndoRedo 支持

---

## 十、架构成熟度优化实施记录 (2026-09-02)

> 基于"动作编排架构/体验成熟度审查" (评分: 架构分层 9、文档同步 4、可维护性 6、测试 7、功能 9、交互 8.5) 的整改落地。

### P0 — 跨语言语义契约测试 (修复真实缺陷)

- **新增 `IOStudio.Tests/Services/CrossLanguageContractTests.cs`**：8 个契约测试，锁定 C# `TrackValueEvaluator` 与 C++ `MotionPlayer` 同一 JSON 语义的黄金值——值域 `[0,1]` / clip 优先 idle / ease_in_out≡ease / idle 环形闭合相位不变性 / blend 淡入淡出 / overlay 优先级 / bool 量化 / 无覆盖中性值。
- **发现并修复跨语言差异（真实缺陷）**：`TrackValueEvaluator.Evaluate` 的 float 路径 (clip 内 `InterpolationEngine.Evaluate`、idle、blend `Linear` 输出) 原先**不 clamp 到 [0,1]**，越界 idle 关键帧 (如 -0.5/1.6) 会直接输出域外值，而 C++ SafetyGuard 在 Phase 4 输出钳位 → **编辑器预览与实际实播不一致**。修复：`Quantize` → `ClampQuantize` (float 也 clamp [0,1])，float clip 分支显式 `Math.Clamp`。
- **验证**：8 个契约测试全过；全量 253 个测试 0 失败。涉及 `TrackValueEvaluator.cs`, `CrossLanguageContractTests.cs` (新)。

### P1 — 架构文档补档 (.motion v3.0 演进字段)

- **`动感平台编辑系统_架构设计.md` v22 → v23**：§5.4 新增"v3.0 演进字段"完整字段表 (`version`/`fps`/`id`/`label`/`color`/`value_type`/`neutral_value`/`role`/`group`/`show_in_curve`/`solo`/`locked`/`idle` 对象含 `phase_mode`/`phase_offset_ms`/`group_id`/`override_keyframes[]`/`action_instances[]`/`role_track_bindings`/`embedded_presets[]`/`groups[]`/`events[]`/`markers[]`) + **求值优先级**说明 (clip > override > idle > neutral; blend 淡化; bool 量化)。
- §3 行数声明同步 (~1180 行, 含 idle/overlay/phase-group 演进)。
- **消除文档滞后**：审查指出文档 §5.2 缺 idle/override 等已上线核心字段，本补档对齐代码现状。

### P2a — TimeRulerControl 拆 partial (可维护性)

- `TimeRulerControl.cs` 1370 行 → **718 行主文件**（依赖属性/字段/交互/坐标换算）+ **新建 `TimeRulerControl.Rendering.cs`**（Render/DrawTicks/DrawPlayhead/DrawWorkArea/DrawVideoReference/DrawEventMarkers/DrawTimelineMarkers/CalculateTickInterval/FormatTime）。
- 零行为变更 (纯搬运)，编译 0 错误。
- **说明 P2b (TimelineEditorWindow.axaml 93KB → UserControl 拆分) 未本次执行**：该文件含大量 `x:Name`/事件/模板交叉绑定，拆分需 GUI 可视化回归，判定为高风险重构，保留为后续专项（需有截图验证手段时进行）。

### P3 — 体验微调 (循环边界 + 输出状态可见)

- **播放头工作区外降透明**：`PlayheadOverlay` 新增 `WorkAreaInMs/OutMs` 依赖属性，播放头超出 In/Out 时由实红 (`#ef4444`) 降为半透明红 (`#46ef4444`)，直观提示"已超出循环范围"；XAML 绑定 `WorkAreaInMs/OutMs` 到 VM。
- **LiveOutput 状态灯**：传输栏"设备实时输出"开关旁新增 8px 双圆点状态灯 (灰=仅预览 `TlTransportSeparator` / 绿=输出到设备 `TlTransportAccent`)，绑定 `IsLiveOutputEnabled`，让"预览是否真实输出到物理设备"显式可见。
- 涉及 `PlayheadOverlay.cs`, `TimelineEditorWindow.axaml`。

### 验证

C# 编译 0 错误；全量 253 个测试 0 失败；IOStudio 启动正常。
