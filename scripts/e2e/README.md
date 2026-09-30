# IOStudio E2E 自动化

三条互补的通道, 各有明确覆盖范围。**先读各自边界, 别用错工具。**

## 1. UIA (pywinauto) — 窗口级流程 + 检查器状态

依赖: Python 3.11 + `pip install pywinauto pytest pytest-html Pillow pytest-timeout`

```powershell
# 先启动应用
Binaries\Win64\Debug\IOStudio.exe --motion Binaries\Win64\Debug\Config\Motion\showcase_10min.motion

# 可见性报告 (人工查看)
C:\Python311\python.exe scripts\e2e\probe_uia.py

# 断言 (回归保护, 15 项)
C:\Python311\python.exe -m pytest scripts\e2e\probe_uia.py -v
```

**实测 (2026-09-30)**: Avalonia 暴露了 **348 个 UIA 后代元素, 48 个带 AutomationId**。

可见且可定位 (`AutomationId`):
`KfPropertyPanel` `ValueInput` `ValueSlider` `MarkerNameInput`
`PlayheadEventDataInput` `CurrentTimeDisplayText` `DurationDisplayText`
`SpeedComboBox` `ZoomPercentText` `AutoKeyToggle` `MenuBar` `MainMenu`

**盲区 (不在 UIA 树里)**: `TrackClipControl` `CurveEditorControl` `TimeRulerControl`
—— 时间轴/曲线是自绘控件, 内部的关键帧、拖拽、双击一律不可见。

因此 **UIA 能覆盖**:
- 菜单 / 对话框 / 窗口流程
- 检查器控件定位与**状态读取** (如 `ValueInput.is_enabled()`)
- 走带读数、缩放、速度

**UIA 不能覆盖**: 时间轴上的双击加帧、关键帧点选/拖拽 —— 走下面的通道。

## 2. 窗口消息注入 — 时间轴自绘交互

时间轴交互依赖 `e.ClickCount==2` 与自绘命中测试, UIA 看不到, 需注入真实输入。

```powershell
# 关键点: 窗口可能处于最小化, 必须先 ShowWindow(hWnd, 9)
# 本机交互桌面不渲染 (截图为白屏), 但 PostMessage 仍可达:
#   WM_MOUSEMOVE=0x200 WM_LBUTTONDOWN=0x201 WM_LBUTTONUP=0x202
#   lParam = (y << 16) | (x & 0xFFFF)   —— 用客户区坐标
```

行为断言依托应用日志 (`Binaries\Win64\Debug\Logs\IOStudio-*.log`):
`[Action] 双击添加关键帧 | <轨道> @ <ms>` 与 `[Action] 执行 | 添加关键帧`
成对出现即表示手势送达且已生效。

## 3. Avalonia.Headless — 无桌面 VM/绑定验证

见 `IOStudio.Tests/E2E/HeadlessFeasibilityTests.cs`。**能力边界 (实测)**:
- 能: 会话启动、真实布局、属性绑定 / VM 状态断言。可在 CI 无桌面跑。
- **不能: 合成鼠标事件送不到控件**, 故 `ClickCount==2` 的双击语义无法验证。
- 陷阱: `Dispatch(Action, CancellationToken)` 是 fire-and-forget, 会产生假绿;
  必须用会 await 的 `Dispatch<T>(Func<T>, CancellationToken)`。

## 覆盖矩阵

| 被测对象 | UIA | 窗口消息 | Headless |
|---|---|---|---|
| 菜单 / 对话框 / 窗口 | 可以 | — | — |
| 检查器控件存在性/状态 | 可以 | — | 可以 |
| 走带时间框 / 关键帧列表项 | 可以 | — | — |
| 时间轴双击加帧 | 不可 | 可以 | 不可 |
| 时间轴点选关键帧 / 拖拽 | **不可** | 可以 | 不可 |
| VM / 绑定逻辑 | — | — | 可以 |

> **UIA 的重要边界**: `TrackClipControl` / `CurveEditorControl` 是自绘控件,
> **UIA 的合成点击也送不到它们** (实测: 在时间轴画布上 `click_input` 后播放头不动)。
> 所以"在时间轴上点关键帧"这条链路 UIA 覆盖不了, 仍需窗口消息注入
> (见第 2 节); 用 UIA 能做的是**间接**覆盖: 通过走带时间框 + 检查器关键帧列表,
> 驱动并断言检查器状态。

## 4. 已实现的 UIA 断言套件

`scripts\e2e\test_inspector_editable.py` —— "选中关键帧后检查器可编辑" 回归套件 (7 项)。

```powershell
# 先启动应用 (需含关键帧的影片), 再:
C:\Python311\python.exe -m pytest scripts\e2e\test_inspector_editable.py -v
```

覆盖 (含负面基线, 保证断言有判别力):
- 检查器 `ValueInput`/`ValueSlider` 可定位;
- 播放头不在关键帧上 → 数值控件**必须禁用**;
- 播放头落到关键帧 → **必须可编辑**;
- 播放头在片段内(不在关键帧上) → 检查器**仍须认片段** (关键帧列表非空);
- 点击关键帧列表项 → 播放头跳到该帧且变为可编辑;
- 可编辑后**确实能写入**并读回;
- 走带时间框输入**精确时间不被吸附改写**。

可配置项 (换影片时):
`IOSTUDIO_WINDOW_KEYWORD` / `IOSTUDIO_FIRST_CLIP_MS` / `IOSTUDIO_GAP_MS` / `IOSTUDIO_MID_CLIP_MS`

## 5. 由本套件发现并修复的产品缺陷

| 缺陷 | 症状 | 修复 |
|---|---|---|
| 检查器片段识别 | 播放头在片段中间(不在关键帧上)时误报"播放头不在片段内", 关键帧列表不显示 | `clipIdx` 改为"进入片段即赋值", 不再依赖是否命中关键帧 |
| 时间框被吸附改写 | 输入 `33.500` 被吸到 `32.600` (吸附半径 = 10px 换算, 缩小时 >1s), 无法定位片段中段 | 键盘输入走 `SeekToExact` (不吸附); 拖拽仍保留吸附 |
| 时间框重复双击失效 | 第二次双击时间不进入编辑 | `IsVisible` 守卫 + `LostFocus` 仅在编辑态提交, 消除重入 |
| 走带图标按钮不可寻址 | 图标按钮 Name 是 `PackIconIonicons`, UIA 无法定位 | 补 `AutomationProperties.AutomationId/Name` (上一/下一关键帧、跳转、播放/暂停/停止、时间显示与输入) |

