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
| 时间轴双击加帧 | 不可 | 可以 | 不可 |
| 关键帧点选/拖拽 | 不可 | 可以 | 不可 |
| VM / 绑定逻辑 | — | — | 可以 |
