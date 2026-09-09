# 动感平台编辑系统 — 架构设计与实施规划 (v9)

> 基于 IODevice 生态和 IOStudio 现有架构，将动感平台时间轴编辑功能集成为 IOStudio 的核心模块，并通过 TCP 网络协议为 Unity/UE 提供播控接口。
>
> Motion 处理核心在 IODevice C++ 中实现。MotionPlayer 采用 Slot-Based Multi-Engine 架构，内部集成 ChannelMixer（通道仲裁）、InterpolationEngine（插值）、SafetyGuard（安全保护）、DeviceOutputBus（设备派发）。IOStudio C# 侧仅保留编辑器 UI 逻辑，播放控制通过 C# Wrapper (`IOToolkit.MotionPlayer`) 调用 C++ 核心。TCP MotionServer 作为远程集中管理通道，所有命令通过 `slot` 字段寻址。

---

## 〇、核心决策：集成进 IOStudio + TCP 播控协议

### 决策推导过程

**关键事实：动感平台关键帧的本质就是 IODevice 的输出控制**

通过深入分析代码发现，动感平台编辑器的核心操作链路是：

```
时间轴关键帧 { time: T, value: V }
     ↓ 插值计算
当前帧值 = Interpolate(keyframes, currentTime)
     ↓ 设备调度
IODeviceController.GetIODevice("Platform-0").SetDO("OAxis_00", value)
```

这与 IOStudio 现有功能的关系：

| 功能              | IOStudio 现有实现                                          | MotionEditor 需要 | 重复度 |
| ----------------- | ---------------------------------------------------------- | ----------------- | ------ |
| IODevice.xml 解析 | `IORoot → Device → OAction → Key` (IONodeConfig.cs 1851行) | **完全相同**      | 100%   |
| 设备生命周期      | `IODeviceController.Load/Unload/GetIODevice/Update`        | **完全相同**      | 100%   |
| 输出控制          | `device.SetDO(key, value)`                                 | **完全相同**      | 100%   |
| 通道监控          | `Device.Update()` 轮询 DI/AD/DO 实时值                     | **完全相同**      | 100%   |
| 设备配置 UI       | 设备属性编辑器、Schema Service、INI Config                 | **完全相同**      | 100%   |
| 输出测试          | `OutputTestViewModel` (模拟量/脉冲/跑马灯)                 | 是其**超集**      | 80%    |
| 事件转发          | `EventForwardingService` → `IProtocolSender`               | 可组合使用        | 50%    |
| 连接管理          | `ConnectionManager` (UDP/TCP/Serial)                       | **完全相同**      | 100%   |
| 外部设备扫描      | `ExternalDeviceService` → IOUI-Win64-\*.dll                | **完全相同**      | 100%   |

**如果拆分为独立项目，以上 90% 的代码需要复制或抽取共享库——维护成本极高。**

**✅ 结论：动感平台编辑器集成为 IOStudio 的模块 + MotionSDK 独立发布**

理由：

1. **数据模型完全共享**：关键帧操控的就是 `Device.OAction.Key(OAxis_xx)`，与 IOStudio 的 IODevice.xml 模型完全一致
2. **设备基础设施完全共享**：设备加载、配置、监控、输出控制代码 100% 相同
3. **OutputTestViewModel 就是简化版的动感控制**：模拟量输出、脉冲、跑马灯本质上就是"没有时间轴的关键帧播放"
4. **IOStudio 本名"IO聚合调试平台"**：时间轴编辑是"调试/调校"的高级模式，不违反定位
5. **用户工作流连贯**：配置设备 → 测试通道 → 编辑时间轴 → 导出动作文件，一站式完成

### 架构总览

```
┌─────────────────────────────────────────────────────────────────────────┐
│                                                                         │
│            IOStudio (IO聚合调试平台) — 单一桌面应用                       │
│                                                                         │
│  ┌─────────────────┐ ┌──────────────────┐ ┌──────────────────────┐     │
│  │  设备监控模块     │ │  事件转发模块     │ │  时间轴编辑模块 (NEW) │     │
│  │  (现有 Tab)      │ │  (现有 Window)   │ │  (新增 Tab/Window)   │     │
│  │                  │ │                  │ │                      │     │
│  │  DI/AD/DO 监控   │ │  输入→协议映射    │ │  多轨道时间轴编辑     │     │
│  │  输出测试        │ │  DirectOutput    │ │  曲线编辑器          │     │
│  │  设备配置        │ │                  │ │  媒体同步            │     │
│  │  通道管理        │ │                  │ │  效果预设            │     │
│  │                  │ │                  │ │  导出 .motion 文件   │     │
│  └──────┬───────────┘ └────────┬─────────┘ └──────────┬───────────┘     │
│         │                      │                      │                 │
│  ┌──────┴──────────────────────┴──────────────────────┴──────────┐      │
│  │                    共享基础设施 (已有)                          │      │
│  │  IONodeConfig (IORoot/Device/OAction/Key)                     │      │
│  │  IODeviceController / ConnectionManager / Senders             │      │
│  │  DeviceSchemaService / IniConfigService / ExternalDeviceService│     │
│  │  KeyNameValidator / RecordingManager                          │      │
│  └──────────────────────────────┬────────────────────────────────┘      │
│                                 │                                       │
│  ┌──────────────────────────────┴────────────────────────────────┐      │
│  │              IODevice_CSharp_Wrapper (.NET Framework 4.5.2)   │      │
│  │              IODevice_C_Wrapper (C ABI, stdcall + BSTR)       │      │
│  │              IODevice.dll (C++ 核心 + 插件 DLL)               │      │
│  │                ├── IODeviceController / IODevice / PlayerInput │      │
│  │                └── MotionPlayer (嵌入式动作播放, § 五A)        │      │
│  └───────────────────────────────────────────────────────────────┘      │
│                                                                         │
│  ┌──────────────────────────────────────────────────────────────┐       │
│  │         MotionServer (TCP, 端口 9600, 4字节长度前缀+JSON)    │       │
│  │         (IOStudio 内建播控网络服务)                           │       │
│  │  ├── 接收 JSON 命令: play / pause / stop / seek / load       │       │
│  │  ├── 推送播放状态: 50ms 间隔广播给所有连接的客户端              │       │
│  │  └── 推送安全事件: 急停/限位触发等                             │       │
│  └──────────────────────────────────────────────────────────────┘       │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
                          ↑ TCP (4字节长度前缀 + JSON)
    ┌─────────────────────┼──────────────────────────┐
    │                     │                          │
┌───┴────────────┐  ┌────┴───────────┐  ┌───────────┴──────────┐
│  Unity 客户端   │  │  UE 客户端     │  │  其他 (Python/Web)   │
│  纯 C#, 无DLL  │  │  纯 C++, 内置  │  │  标准 socket 库      │
│  ~120 行代码   │  │  ~80 行代码    │  │  任何语言             │
└────────────────┘  └────────────────┘  └──────────────────────┘

嵌入式 C++ SDK: 暂不开发, 后续需求驱动 (详见 § 5.2.3)
```

**两个构建版本和交付物的关系：**

- **IOStudio (Editor 构建)**：编辑工具 + 播控 TCP 服务器（内建 MotionServer），产出 `.motion` (JSON, 内部) 和 `.mtn` (加密, 交付买家) 文件。**仅内部使用。**
- **MotionPlayer (Player 构建)**：同源码、不同配置，排除编辑器代码。仅加载 `.mtn` 加密文件 + TCP 播控。**交付给买家。**
- **客户端 SDK**：Unity/UE 薄客户端，通过 TCP 发送 JSON 命令给 IOStudio/MotionPlayer。**推迟到基础功能完善后 (Phase 8)。**
- **`.mtn` 加密文件**：AES-256-GCM 加密，买家无法查看/编辑/还原关键帧数据。详见 § 十。

---

## 一、系统定位与目标

### 1.1 定位

| 交付物                      | 定位                                                                                                 | 用户                                       | 包含编辑能力 |
| --------------------------- | ---------------------------------------------------------------------------------------------------- | ------------------------------------------ | :----------: |
| **IOStudio Basic** (免费版) | IO聚合调试平台：设备配置、通道监控、事件转发、**MotionServer TCP 播控**、**加载/播放 .mtn 加密文件** | **所有用户** (含买家/影院/场馆)            |      ❌      |
| **IOStudio Pro** (授权版)   | Basic 全部功能 + **时间轴编辑**、**曲线编辑**、**媒体同步**、**效果预设**、**导出 .mtn**             | 内部工程师、内容制作人员 (需 LicHper 授权) |      ✅      |
| **客户端 SDK** (后期)       | 薄客户端：纯 C#/C++ 网络协议封装，通过 TCP 控制播放                                                  | Unity/UE 开发者                            |      —       |

> **单一二进制发布：** IOStudio Basic 与 Pro 为同一个 exe，通过 LicHper 授权系统在运行时区分版本。未授权 = Basic (免费播放器)，授权 = Pro (完整编辑器)。无需维护两套构建配置。

### 1.2 动感平台核心场景

4D/5D 影院需要根据影片内容，在精确的时间点控制多种体感设备协同动作：

| 设备类型       | 控制方式                          | 输出通道 (IODevice.xml)                       |
| -------------- | --------------------------------- | --------------------------------------------- |
| **动感座椅**   | 多自由度运动平台 (2DOF/3DOF/6DOF) | `OAxis_00~05` (俯仰/横滚/升降/左右/前后/偏航) |
| **灯光**       | DMX512 / 继电器                   | `OAxis_00~02` (亮度/颜色/频闪)                |
| **风机**       | PWM / 模拟量                      | `OAxis_00~01` (风速/方向)                     |
| **烟雾机**     | 继电器 / 串口                     | `OAxis_00` (开关/时长)                        |
| **水雾/喷水**  | 继电器                            | `OAxis_00` (开关)                             |
| **气味发生器** | 串口 / GPIO                       | `OAxis_00~01` (类型/浓度)                     |
| **闪电/雷声**  | 继电器 + 音频                     | `OAxis_00` (开关)                             |

所有设备的输出控制最终都是 `IODevice.SetDO("OAxis_xx", value)` — 与 IOStudio 现有的输出测试完全一致，只是增加了**时间维度**。

### 1.3 与 IOStudio 现有功能的继承关系

```
IOStudio 现有功能                              时间轴编辑模块 (新增)

设备监控: 实时显示 DI/AD/DO 值                → 时间轴编辑时同步显示设备实时值
输出测试: 手动设置 OAxis 值                    → 关键帧定义 OAxis 在某时刻的值
跑马灯:  按顺序遍历通道                        → 时间轴上多轨道按时序控制多通道
脉冲:    设置→延时→归零                        → 关键帧自动插值 0→V→0
事件转发: 输入→输出映射                        → 时间轴→输出映射 (.motion文件)
设备配置: IODevice.xml 编辑                    → .motion 文件引用同一个 IODevice.xml
```

**本质：OutputTestViewModel (684行) 就是"没有时间轴的动感控制器"。时间轴编辑模块是其在时间维度上的扩展。**

### 1.4 交付物

1. **IOStudio (单一二进制)**：设备配置 + 通道监控 + 事件转发 + MotionServer TCP 播控 + .mtn 播放 + TCP:9600 播控接口。未授权时为 **Basic 模式** (免费播放器)，LicHper 授权后为 **Pro 模式** (完整编辑器 + 导出能力)。交付给所有用户。
2. **AuthAssistant (内部工具)**：LicHper 授权管理工具，用于生成/分发 Pro 授权 license。仅内部运维使用。
3. **客户端 SDK (Unity/UE 薄客户端)**：纯语言 TCP 客户端，Unity ~120 行 / UE ~80 行，通过网络命令控制播放。**推迟到基础功能完善后实施 (Phase 8)**
4. **IODevice.dll 嵌入式 MotionPlayer**：`MotionPlayer` 类已集成至 IODevice.dll（C++ 单例，pImpl 模式）；C Wrapper 新增 `MotionLoad/MotionPlay/MotionStop` 等全局函数（无 `InDeviceName` 参数）；C# Wrapper 新增 `IOToolkit.MotionPlayer` 静态类。Unity/UE 等已依赖 IODevice.dll 的应用**无需 IOStudio 进程**即可直接播放 .motion/.mtn 文件。详见 § 五A。

**`.motion` 文件**由 IOStudio Pro 编辑和保存 (内部)；**`.mtn` 加密文件**由 IOStudio Pro 导出，IOStudio Basic 加载播放 (交付给买家)。客户端 SDK 通过 TCP 网络命令触发，不区分 Basic/Pro。

---

## 二、集成架构：IOStudio 内部的时间轴模块

### 2.1 IOStudio 模块化结构 (集成后)

```
IOStudio/
├── App.axaml(.cs) / Program.cs           # 应用入口 (不变)
│
├── Models/                                # 数据模型
│   ├── IOStudioSettings.cs               # (已有) 应用设置
│   ├── EventForwardConfigDto.cs          # (已有) 事件转发DTO
│   ├── ActionKeyMapping.cs               # (已有)
│   ├── EditorLauncher.cs                 # (已有)
│   ├── ProtocolGroup.cs                  # (已有)
│   │
│   └── Timeline/                          # 【新增】时间轴数据模型
│       ├── MotionProject.cs               # 项目根模型 (引用 IODevice.xml 的设备)
│       ├── MotionTimeline.cs              # 时间轴 (Duration, FrameRate)
│       ├── MotionTrack.cs                 # 轨道 (→ Device.OAction/Key)
│       ├── MotionClip.cs                  # 片段 (StartTime, Duration)
│       ├── MotionKeyframe.cs              # 关键帧 (Time, Value, Interpolation)
│       ├── DeviceMapping.cs               # 逻辑名→物理设备映射
│       └── EffectPreset.cs                # 效果预设
│
├── ViewModels/
│   ├── MainWindowViewModel.cs            # (已有) 增加时间轴入口
│   ├── IONodeConfig.cs                   # (已有) Device/OAction/Key - 时间轴直接复用
│   ├── OutputTestViewModel.cs            # (已有) 输出测试
│   ├── EventForwardConfigViewModel.cs    # (已有) 事件转发
│   ├── RangeMappingDialogViewModel.cs    # (已有)
│   ├── TestViewModel.cs                  # (已有)
│   │
│   └── Timeline/                          # 【新增】时间轴 ViewModels
│       ├── TimelineEditorViewModel.cs     # 时间轴编辑器主 VM
│       ├── TrackViewModel.cs              # 单轨道 VM (引用 Device.OAction)
│       ├── ClipViewModel.cs               # 片段 VM
│       ├── CurveEditorViewModel.cs        # 曲线编辑器 VM
│       ├── TimelinePlaybackViewModel.cs   # 播放控制 VM
│       ├── PresetLibraryViewModel.cs      # 预设库 VM
│       └── MediaPlayerViewModel.cs        # 媒体播放 VM (可选)
│
├── Views/
│   ├── MainWindow.axaml                  # (已有) 增加"时间轴编辑器"菜单项
│   ├── (其他已有 Views)
│   │
│   └── Timeline/                          # 【新增】时间轴 Views
│       ├── TimelineEditorWindow.axaml     # 时间轴编辑器窗口 (独立窗口)
│       ├── TimeRuler.axaml                # 时间标尺
│       ├── TrackPanel.axaml               # 轨道面板
│       ├── CurveEditor.axaml              # 曲线编辑器
│       ├── PresetLibrary.axaml            # 预设库面板
│       └── MediaPlayer.axaml              # 媒体播放 (可选)
│
├── Services/
│   ├── ConnectionManager.cs              # (已有) 直接复用
│   ├── EventForwardingService.cs         # (已有) 直接复用
│   ├── DeviceSchemaService.cs            # (已有) 直接复用
│   ├── ExternalDeviceService.cs          # (已有) 直接复用
│   ├── IniConfigService.cs               # (已有) 直接复用
│   ├── KeyNameValidator.cs               # (已有) 直接复用
│   ├── RecordingManager.cs               # (已有) 直接复用
│   ├── AnalogValueProcessor.cs           # (已有) 直接复用
│   ├── Senders/                          # (已有) 直接复用
│   │
│   └── Timeline/                          # 【新增】时间轴服务
│       ├── TimelinePlaybackService.cs     # 播放引擎 (Tick驱动, 调度 SetDO)
│       ├── InterpolationService.cs        # 插值计算 (线性/贝塞尔/阶梯/缓动)
│       ├── SafetyGuardService.cs          # 安全保护 (限位/回中/急停)
│       ├── MotionProjectService.cs        # 项目管理 (新建/打开/保存)
│       ├── MotionExportService.cs         # 导出 .motion 文件
│       └── MediaSyncService.cs            # 媒体同步 (可选)
│
├── Controls/
│   ├── (已有自定义控件)
│   │
│   └── Timeline/                          # 【新增】时间轴控件
│       ├── TimeRulerControl.cs            # 时间标尺 (SkiaSharp)
│       ├── TrackControl.cs                # 轨道渲染 (SkiaSharp)
│       ├── CurveEditorControl.cs          # 曲线编辑器 (SkiaSharp)
│       ├── KeyframeDot.cs                 # 关键帧标记
│       └── WaveformControl.cs             # 波形渲染 (可选)
│
├── Converters/                            # (已有) 直接复用
├── Extensions/                            # (已有) 直接复用
│
└── Config/
    ├── IODevice.xml                       # (已有) 时间轴编辑器直接读取
    ├── Schemas/                           # (已有) 设备 Schema
    ├── IOStudioSettings.xml               # (已有)
    │
    └── Presets/                            # 【新增】效果预设库
        ├── Motion/
        │   ├── 强烈颠簸.json
        │   └── 轻微摇晃.json
        └── Wind/
            ├── 微风.json
            └── 强风.json
```

### 2.2 时间轴编辑器如何复用 IOStudio 已有代码

**零拷贝复用（直接引用已有类）：**

```csharp
// TrackViewModel.cs — 轨道直接引用 IOStudio 已有的 Device 和 OAction 对象

public class TrackViewModel : ViewModelBase
{
    /// <summary>
    /// 直接引用 IONodeConfig.cs 中的 Device 对象 (IOStudio已有)
    /// </summary>
    public Device TargetDevice { get; }

    /// <summary>
    /// 直接引用 IONodeConfig.cs 中的 OAction 对象 (IOStudio已有)
    /// 一个 OAction 包含多个 Key (OAxis_xx)，每个 Key 是一个输出通道
    /// </summary>
    public OAction? TargetOAction { get; }

    /// <summary>
    /// 如果直接控制单个通道 (不通过 OAction)，引用 Key 对象
    /// </summary>
    public Key? TargetKey { get; }

    public string TrackName => TargetOAction?.DeviceLabel
        ?? TargetKey?.Name
        ?? "Unknown";

    public ObservableCollection<ClipViewModel> Clips { get; }

    // 播放时直接调用 IOStudio 已有的设备控制代码
    public void ApplyValue(float value)
    {
        // 与 OutputTestViewModel.ExecuteSendAnalog() 完全相同的调用方式
        var device = IODeviceController.GetIODevice(TargetDevice.Name);
        if (device?.IsValid() != true) return;

        if (TargetOAction != null)
        {
            // OAction 下所有 Key 按 Scale 和 InvertEvent 输出
            // 复用 IONodeConfig.cs 中 Device.SyncOActionPropsToIODevice() 的配置
            IOToolkit.Key oKey = TargetOAction.Name;
            device.SetDOAction(oKey, value);
        }
        else if (TargetKey != null)
        {
            IOToolkit.Key doKey = TargetKey.Name;
            device.SetDO(doKey, value);
        }
    }
}
```

**复用清单：**

| 已有代码                            | 时间轴模块如何复用                                               |
| ----------------------------------- | ---------------------------------------------------------------- |
| `IORoot.Instance` (IONodeConfig.cs) | 时间轴编辑器读取同一个 `IODevice.xml`，获取设备/OAction/通道列表 |
| `Device` 类                         | TrackViewModel 直接引用 `Device` 实例，不创建新模型              |
| `OAction` / `Key` 类                | 轨道绑定到 `OAction` 或 `Key`，获取 Name/Label/Scale 等属性      |
| `IODeviceController`                | 播放引擎调用 `GetIODevice(name).SetDO()` 输出值                  |
| `Device.Update()`                   | 播放时复用已有的 DI/AD/DO 值刷新逻辑                             |
| `OutputTestViewModel`               | 时间轴的"预览播放"本质就是自动化的 OutputTest                    |
| `DeviceSchemaService`               | 时间轴添加轨道时，从 Schema 读取通道信息                         |
| `ConnectionManager`                 | 如果时间轴需要通过网络协议输出，复用已有连接管理                 |
| `IProtocolSender` 系列              | 时间轴可支持"播放时同时转发到协议"，复用事件转发的发送器         |
| 设备属性编辑 UI                     | 时间轴编辑器中的设备管理直接打开已有的 `DevicePropertiesWindow`  |

### 2.3 时间轴编辑器的入口方式

时间轴编辑器作为**独立窗口**打开（而非 Tab），原因：

1. 时间轴 UI 需要大量横向空间（时间标尺 + 轨道），不适合与设备监控共享 Tab 空间
2. 可以与设备监控窗口并排使用：左边 IOStudio 监控设备状态，右边时间轴编辑

```xml
<!-- MainWindow.axaml — 菜单栏新增 -->
<MenuItem Header="工具(_T)">
    <MenuItem Header="时间轴编辑器(_T)" Command="{Binding OpenTimelineEditorCommand}" />
    <MenuItem Header="事件转发配置(_E)" Command="{Binding OpenEventForwardConfigCommand}" />
</MenuItem>
```

```csharp
// MainWindowViewModel.cs — 新增命令
public ReactiveCommand<Unit, Unit> OpenTimelineEditorCommand { get; }

// 构造函数中:
OpenTimelineEditorCommand = ReactiveCommand.CreateFromTask(async () =>
{
    var window = new TimelineEditorWindow();
    // 传入当前设备列表 — 直接复用 IORoot.Instance.Devices
    window.Initialize(IORoot.Instance.Devices.Where(d => d.Type != "Standard"));
    window.Show(); // 非模态窗口, 可与主窗口并存
});
```

### 2.4 数据模型：关键帧与 IODevice.xml 的关系

```
IODevice.xml (已有, 设备拓扑定义)
┌──────────────────────────────────────────────────┐
│ <IORoot>                                          │
│   <Device Name="Platform-0" DllName="MODBUS" ..> │
│     <OAction Name="Pitch">                        │
│       <Key Name="OAxis_00" Scale="1" />           │
│     </OAction>                                    │
│     <OAction Name="Roll">                         │
│       <Key Name="OAxis_01" Scale="1" />           │
│     </OAction>                                    │
│   </Device>                                       │
│   <Device Name="FanCtrl-0" DllName="MODBUS" ..>  │
│     <OAction Name="WindSpeed">                    │
│       <Key Name="OAxis_00" Scale="1" />           │
│     </OAction>                                    │
│   </Device>                                       │
│ </IORoot>                                         │
└──────────────┬───────────────────────────────────┘
               │
               │ 时间轴引用 (不复制, 直接引用 Device/OAction 对象)
               ▼
.motion 文件 (新增, 时间轴数据)
┌──────────────────────────────────────────────────┐
│ {                                                 │
│   "ioConfigRef": "Config/IODevice.xml",           │
│   "tracks": [                                     │
│     {                                             │
│       "device": "Platform-0",                     │  ← Device.Name
│       "oaction": "Pitch",                         │  ← OAction.Name
│       "clips": [{                                 │
│         "startTime": 5000,                        │
│         "keyframes": [                            │
│           { "t": 0,    "v": 0.0 },               │  ← 归一化值
│           { "t": 500,  "v": 0.8 },               │
│           { "t": 3000, "v": 0.0 }                │
│         ]                                         │
│       }]                                          │
│     },                                            │
│     {                                             │
│       "device": "FanCtrl-0",                      │
│       "oaction": "WindSpeed",                     │
│       "clips": [...]                              │
│     }                                             │
│   ]                                               │
│ }                                                 │
└──────────────────────────────────────────────────┘
```

**关键设计点：**

1. `.motion` 文件通过 `device` + `oaction` **名称**引用 IODevice.xml 中的设备和输出动作
2. 编辑器运行时直接操作 `IORoot.Instance` 中的 `Device` 对象实例
3. 关键帧值是**归一化值** (0~1 或 -1~1)，物理映射由 IODevice.xml 中 Key 的 `Scale/Offset/Min/Max` 处理
4. 修改 IODevice.xml 的设备配置（如换通道、改 Scale）会自动影响时间轴播放效果——与 IOStudio 其他功能完全一致

---

## 三、播放引擎：从 OutputTestViewModel 到 TimelinePlaybackService

### 3.1 演进关系

OutputTestViewModel 现有的三种模式 → 时间轴播放引擎的对应关系：

```
OutputTestViewModel (已有, 684行)          TimelinePlaybackService (新增)

ExecuteSendAnalog()
  → device.SetDO(key, AnalogValue)    ──→  PlayFrame(time)
  → 单次设置一个值                           → 对每个轨道: value = Interpolate(keyframes, time)
                                            → device.SetDO(key, value)
                                            → 区别: 值由插值引擎计算, 不是用户手动输入

ContinuousPulseAsync()
  → SetDO(1) → delay → SetDO(0) → loop ──→ 就是只有两个关键帧的时间轴:
                                            → [{t:0, v:1}, {t:duration, v:0}] + Loop

SequenceAsync()
  → 遍历选中通道, 逐个SetDO(1)/SetDO(0) ──→ 就是多轨道错开排列的关键帧:
                                            → Track0: [{t:0, v:1}, {t:interval, v:0}]
                                            → Track1: [{t:interval, v:1}, {t:interval*2, v:0}]
                                            → ...
```

**OutputTestViewModel 的三种模式本质上就是时间轴编辑器的特殊用例（极简关键帧）。**

### 3.2 TimelinePlaybackService 设计

```csharp
// Services/Timeline/TimelinePlaybackService.cs

public class TimelinePlaybackService : IDisposable
{
    private readonly Stopwatch _stopwatch = new();
    private readonly InterpolationService _interpolation = new();
    private readonly SafetyGuardService _safetyGuard = new();

    private MotionTimeline? _timeline;
    private float _currentTimeMs;
    private float _speed = 1.0f;
    private PlaybackState _state = PlaybackState.Stopped;

    public PlaybackState State => _state;
    public float CurrentTimeMs => _currentTimeMs;

    public event Action<float>? OnTimeChanged;
    public event Action? OnPlaybackComplete;

    /// <summary>
    /// 每帧更新 — 由 IOStudio 已有的 Observable.Interval 驱动
    /// 复用 MainWindowViewModel 中已有的定时轮询机制
    /// </summary>
    public void Tick(float deltaTimeMs)
    {
        if (_state != PlaybackState.Playing || _timeline == null) return;

        _currentTimeMs += deltaTimeMs * _speed;

        if (_currentTimeMs >= _timeline.DurationMs)
        {
            Stop();
            OnPlaybackComplete?.Invoke();
            return;
        }

        // 对每个轨道计算插值并输出
        foreach (var track in _timeline.Tracks)
        {
            var rawValue = _interpolation.Evaluate(track, _currentTimeMs);
            var safeValue = _safetyGuard.Clamp(track, rawValue);

            // 直接复用 IOStudio 已有的设备控制方式
            var device = IODeviceController.GetIODevice(track.DeviceName);
            if (device?.IsValid() != true) continue;

            IOToolkit.Key outputKey = track.OutputKeyName;
            device.SetDO(outputKey, safeValue);
        }

        OnTimeChanged?.Invoke(_currentTimeMs);
    }

    public void Play(MotionTimeline timeline) { ... }
    public void Pause() { ... }  // 平滑回中
    public void Stop() { ... }   // 立即回中
    public void Seek(float timeMs) { ... }
}
```

**关键：`Tick()` 的驱动方式与 IOStudio 已有的设备轮询完全一致。**

IOStudio 的 `MainWindowViewModel` 已有 100ms 定时轮询：

```csharp
// MainWindowViewModel.cs 现有代码 (约第480行)
Observable.Interval(TimeSpan.FromMilliseconds(100))
    .ObserveOn(RxApp.MainThreadScheduler)
    .Subscribe(_ => {
        Devices.ToList().ForEach(device => device.Update());
    });
```

时间轴播放引擎只需挂载到同一个定时器，或使用更高频的独立定时器：

```csharp
// 新增: 16ms 间隔 (~60fps) 用于时间轴播放
Observable.Interval(TimeSpan.FromMilliseconds(16))
    .ObserveOn(RxApp.MainThreadScheduler)
    .Subscribe(_ => {
        _timelinePlayback?.Tick(16);
    });
```

---

## 四、.motion 动作数据文件格式

### 4.1 文件格式设计

```json
{
  "version": "1.0",
  "metadata": {
    "name": "侏罗纪冒险_体感特效",
    "duration": 180000,
    "frameRate": 60,
    "author": "Studio A",
    "createdAt": "2026-03-04T10:00:00Z",
    "ioConfigRef": "Config/IODevice.xml"
  },
  "tracks": [
    {
      "name": "座椅-俯仰",
      "device": "Platform-0",
      "outputType": "OAction",
      "outputName": "Pitch",
      "keyName": "OAxis_00",
      "clips": [
        {
          "startTime": 5000,
          "duration": 3000,
          "keyframes": [
            {
              "t": 0,
              "v": 0.0,
              "interp": "bezier",
              "handles": [0.25, 0.1, 0.25, 1.0]
            },
            { "t": 500, "v": 0.8, "interp": "bezier" },
            { "t": 1200, "v": -0.6, "interp": "linear" },
            { "t": 3000, "v": 0.0, "interp": "easeOut" }
          ]
        }
      ]
    },
    {
      "name": "风机-风速",
      "device": "FanCtrl-0",
      "outputType": "Key",
      "outputName": null,
      "keyName": "OAxis_00",
      "clips": [
        {
          "startTime": 4500,
          "duration": 5000,
          "keyframes": [
            { "t": 0, "v": 0.0, "interp": "linear" },
            { "t": 1000, "v": 0.7, "interp": "easeIn" },
            { "t": 4000, "v": 0.7, "interp": "linear" },
            { "t": 5000, "v": 0.0, "interp": "easeOut" }
          ]
        }
      ]
    }
  ]
}
```

**设计要点：**

- `device` 直接对应 IODevice.xml 中 `<Device Name="Platform-0">`
- `outputType: "OAction"` + `outputName: "Pitch"` → 通过 OAction 名称控制（走 `SetDOAction`，自动处理 Scale/InvertEvent）
- `outputType: "Key"` + `keyName: "OAxis_00"` → 直接控制单个通道（走 `SetDO`）
- 值范围归一化，物理映射由 IODevice.xml 的 Key 属性（Scale/Offset/Min/Max）处理
- 文件格式 JSON，方便各语言解析；**发行版使用 .mtn 加密格式 (AES-256-GCM)，买家无法查看/编辑 (详见 § 十)**

### 4.2 编辑器添加轨道的交互流程

```
用户点击"添加轨道"
    ↓
弹出设备/通道选择器 (复用 IORoot.Instance.Devices 数据)
    ├── Device: Platform-0 (MODBUS-0)
    │     ├── OAction: Pitch → [OAxis_00]
    │     ├── OAction: Roll  → [OAxis_01]
    │     └── DO通道: OAxis_02, OAxis_03, ...
    ├── Device: FanCtrl-0 (MODBUS-1)
    │     └── OAction: WindSpeed → [OAxis_00]
    └── Device: DMX-0 (DMX-0)
          └── DO通道: OAxis_00 ~ OAxis_15
    ↓
用户选择 "Platform-0 → Pitch"
    ↓
创建 TrackViewModel { TargetDevice = Device对象, TargetOAction = OAction对象 }
    ↓
轨道显示在时间轴上, 可添加关键帧
```

设备/通道选择器的数据**完全来自 IORoot.Instance**，不需要任何新的设备发现或配置逻辑。

### 4.3 设备映射与可移植性

同一个 `.motion` 文件要在不同影院(不同硬件配置)使用时：

- `.motion` 中的 `device` 名称是逻辑名
- 每个影院的 `IODevice.xml` 定义了实际的设备驱动(MODBUS/SNAP7/...)和通道编号
- 只要 `IODevice.xml` 中的 Device Name 和 OAction Name 匹配，`.motion` 文件就能运行
- 需要换硬件时，只改 `IODevice.xml`，不改 `.motion`

---

## 五、播放引擎架构与网络控制协议

### 5.0 两条集成路径：嵌入式 MotionPlayer vs IOStudio + TCP

IODevice.dll 已内置 **C++ MotionPlayer**（详见 § 五A），提供两条互补的集成路径：

| 路径                  | 部署场景                                                     | 技术方式                                                                |
| --------------------- | ------------------------------------------------------------ | ----------------------------------------------------------------------- |
| **A. 直接嵌入**       | Unity/UE 等已使用 IODevice.dll 的应用，无需独立运行 IOStudio | `IOToolkit::MotionPlayer::Instance().Load/Play` → IODevice 内部驱动设备 |
| **B. IOStudio + TCP** | IOStudio 作为独立进程运行，集中管理设备 + 时间轴编辑 + 播放  | `MotionClient.LoadFile/Play` → TCP:9600 → IOStudio → IODevice           |

两条路径**不互斥**，可按需选择或混用：

- 小规模单机场景 → **路径 A**：Unity 工程直接 add `IODevice_CSharp_Wrapper.dll`，调用 `MotionPlayer.Play()`
- 影院管控中心场景 → **路径 B**：IOStudio 管理所有设备，Unity/UE 通过 TCP 发命令
- 开发调试 → **路径 A** 直接嵌入；生产部署 → **路径 B** 集中监控

**Motion 处理核心唯一实现在 IODevice C++ 中。** IOStudio 不独立实现播放引擎逻辑（插值、安全保护、设备派发），而是通过 C# Wrapper (`IOToolkit.MotionPlayer`) 调用 C++ 核心。IOStudio 的职责限于：编辑器 UI（时间轴编辑、曲线编辑）、MotionServer TCP 网络服务协调、以及调用 C++ MotionPlayer API 驱动播放。

---

## 五A、IODevice 嵌入式 MotionPlayer

### 5.A.1 架构定位：与 PlayerInput 的对称关系

`IODeviceController::Update()` 已有三步驱动结构：

1. `Tick()` 所有设备（硬件 I/O 刷新）
2. `PlayerInput::Instance().Tick(deltaSeconds)` — 跨设备**输入侧**状态机
3. `ProcessFrameEnd()` — 帧尾处理

`MotionPlayer` 作为**输出侧**跨设备时序状态机，插入 Step 2 与 Step 3 之间：

```cpp
// IODeviceController::Update() — 修改后
void IODeviceController::Update()
{
    // Step 1: 刷新所有设备硬件 I/O
    for (auto& device : _devices) device->Tick();

    // Step 2: 输入侧状态机（跨设备输入聚合）
    PlayerInput::Instance().Tick(_deltaSeconds);

    // Step 3: 输出侧状态机（跨设备动作文件播放）★ 新增
    MotionPlayer::Instance().Tick(_deltaSeconds);

    // Step 4: 帧尾处理
    ProcessFrameEnd();
}
```

| 维度       | `PlayerInput` (已有)                    | `MotionPlayer` (新增)                         |
| ---------- | --------------------------------------- | --------------------------------------------- |
| 方向       | 输入侧（多设备 DI/AD → 归一化输入状态） | 输出侧（.motion 文件关键帧 → 多设备 DO/AO）   |
| 数据来源   | 设备实时输入值                          | .motion/.mtn 文件关键帧数据                   |
| 时序驱动   | `Tick(deltaSeconds)` by `Update()`      | `Tick(deltaSeconds)` by `Update()`            |
| 设备作用域 | 跨设备（全局输入聚合）                  | 跨设备（全局输出分发）                        |
| 对外暴露   | 内部使用（不出口到 C Wrapper）          | **公开 API**（C Wrapper + C# Wrapper 均暴露） |

### 5.A.2 C++ 类设计

```cpp
// Source/Public/MotionPlayer.h — 新建文件
#pragma once
#include <functional>
#include <memory>
#include "IODevice.h"  // IOAPI 宏

namespace IOToolkit {

    enum class MotionState { Idle, Playing, Paused, Stopping };
    enum class MotionEvent { PlaybackComplete, SafetyTriggered, StateChanged };

    class IOAPI MotionPlayer {
    public:
        static MotionPlayer& Instance();

        // 文件管理
        int  Load(const char* filePath);   // 0=成功; 负数=错误码
        void Unload();

        // 播放控制
        int  Play();
        int  PlayFrom(float timeMs);
        int  Pause();
        int  Resume();
        int  Stop();       // 触发平滑回中 → Stopping → Idle
        int  Seek(float timeMs);

        // 参数
        void SetSpeed(float speed);    // 默认 1.0
        void SetLoop(bool loop);

        // 状态查询
        MotionState GetState()       const;
        float       GetCurrentTime() const;  // ms
        float       GetDuration()    const;  // ms

        // 事件回调（可选）
        void BindMotionEvent(MotionEvent evt, std::function<void()> callback);

        // 内部调用：由 IODeviceController::Update() 驱动，外部勿直接调用
        void Tick(float deltaSeconds);

    private:
        MotionPlayer(); ~MotionPlayer();
        MotionPlayer(const MotionPlayer&) = delete;
        MotionPlayer& operator=(const MotionPlayer&) = delete;

        class Impl;
        std::unique_ptr<Impl> _impl;  // pImpl 隐藏 JSON/AES/插值内部依赖
    };

}  // namespace IOToolkit
```

**pImpl 内部模块：**

| 内部模块              | 功能                                      | 依赖                    |
| --------------------- | ----------------------------------------- | ----------------------- |
| `MotionFileLoader`    | .motion JSON 解析 / .mtn AES-256-GCM 解密 | nlohmann/json, Crypto++ |
| `InterpolationEngine` | Linear/Bezier/Step/EaseInOut 插值         | 无外部依赖              |
| `SafetyGuard`         | 限位 Clamp + 速度限制                     | 无外部依赖              |
| `DeviceOutputBus`     | 插值值 → `IODevices::GetDevice().SetDO()` | IODevice 内部           |

### 5.A.3 C Wrapper API（无设备名参数）

单设备操作（`SetDOSingle`/`SetDOAction`）需传 `BSTR InDeviceName`，而 MotionPlayer 跨多设备——**所有 MotionPlayer C Wrapper 函数均无 `InDeviceName` 参数**：

```cpp
// IODevice_CWrapper.h — 新增 MotionPlayer 函数组
IOCAPI int   __stdcall MotionLoad(BSTR InFilePath);
IOCAPI void  __stdcall MotionUnload();
IOCAPI int   __stdcall MotionPlay();
IOCAPI int   __stdcall MotionPlayFrom(float InTimeMs);
IOCAPI int   __stdcall MotionPause();
IOCAPI int   __stdcall MotionResume();
IOCAPI int   __stdcall MotionStop();
IOCAPI int   __stdcall MotionSeek(float InTimeMs);
IOCAPI void  __stdcall MotionSetSpeed(float InSpeed);
IOCAPI void  __stdcall MotionSetLoop(bool InLoop);
IOCAPI int   __stdcall MotionGetState();        // 0=Idle 1=Playing 2=Paused 3=Stopping
IOCAPI float __stdcall MotionGetCurrentTime();  // ms
IOCAPI float __stdcall MotionGetDuration();     // ms
```

### 5.A.4 C# Wrapper（IOToolkit.MotionPlayer 静态类）

```csharp
// IODevice_CSharp_Wrapper/IOToolkit/Facades/MotionPlayer.cs — 新建文件
namespace IOToolkit
{
    /// <summary>
    /// 嵌入式动作播放器。与 IODeviceController 平级——直接使用，无需实例化。
    /// </summary>
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

### 5.A.5 集成示例

**Unity（路径 A，无需 IOStudio）：**

```csharp
// CinemaController.cs — 与 IOStudio 现有初始化方式完全一致
using IOToolkit;
using UnityEngine;

public class CinemaController : MonoBehaviour
{
    public VideoPlayer videoPlayer;

    void Start()
    {
        IODeviceController.Load("Config/IODevice.xml");
        MotionPlayer.Load("Config/Motion/侏罗纪冒险.mtn");  // 平级调用
    }

    void Update()
    {
        IODeviceController.Update();  // 内部自动驱动 MotionPlayer.Tick()
    }

    public void OnPlay() { videoPlayer.Play(); MotionPlayer.Play(); }
    public void OnStop() { videoPlayer.Stop(); MotionPlayer.Stop(); }
}
```

**UE（路径 A，C++ 直接链接）：**

```cpp
// 与已有 IODevice 使用方式完全一致，新增 MotionPlayer 调用
void ACinemaActor::BeginPlay()
{
    IOToolkit::IODeviceController::Instance().Load("Config/IODevice.xml");
    IOToolkit::MotionPlayer::Instance().Load("Config/Motion/侏罗纪冒险.mtn");
}

void ACinemaActor::Tick(float DeltaTime)
{
    IOToolkit::IODeviceController::Instance().Update();  // 自动驱动 MotionPlayer Tick
}

void ACinemaActor::OnPlayClicked()
{
    IOToolkit::MotionPlayer::Instance().Play();
}
```

---

### 5.1 播放引擎放在哪里：IOStudio 内部 vs 独立库

#### 5.1.1 两种放置方式对比

| 维度                   | A: IOStudio 内部 (独立命名空间)                          | B: 独立类库项目 (MotionEngine.csproj) |
| ---------------------- | -------------------------------------------------------- | ------------------------------------- |
| **IODevice 访问**      | 直接 `IORoot.Instance.Devices` / `device.SetDO()`        | 需通过接口抽象, 注入 IODevice 依赖    |
| **项目复杂度**         | 无新项目, Solution 不变                                  | 新增 .csproj, 需管理项目引用和依赖    |
| **构建/部署**          | 编译为 IOStudio.exe 的一部分, 零额外配置                 | 多一个 DLL 输出, 需配置输出路径和拷贝 |
| **命名空间隔离**       | `IOStudio.Motion.*` 独立命名空间, 清晰分层               | 天然隔离                              |
| **后续提取为独立库**   | 仅需移动文件 + 提取依赖接口, 代价低                      | 已经是独立库                          |
| **引用 IOStudio 服务** | 直接用 `IORoot`, `ConnectionManager`, `IOStudioSettings` | 需反向依赖 IOStudio 或抽象接口        |
| **团队认知成本**       | 低: 所有代码在一个项目里                                 | 中: 需理解项目间引用关系              |

#### 5.1.2 决策：播放核心在 IODevice C++，编辑器 UI 在 IOStudio

**播放引擎核心在 IODevice C++ 中实现，IOStudio 通过 C# Wrapper 消费。**

| 层级 | 位置 | 职责 |
|------|------|------|
| **播放核心** | IODevice.dll (C++) | MotionPlayer 多 Slot 管理、ChannelMixer 通道仲裁、InterpolationEngine 插值、SafetyGuard 安全保护、DeviceOutputBus 设备派发 |
| **C ABI 导出** | IODevice_C_Wrapper.dll | `MotionLoadSlot / MotionPlaySlot / MotionGetSlotState` 等 stdcall + BSTR 导出 |
| **C# P/Invoke** | IODevice_CSharp_Wrapper | `IOToolkit.MotionPlayer` 静态类，[DllImport] 调用 |
| **编辑器 C#** | IOStudio (`Services/Motion/`) | 编辑器级协调：ViewModel ↔ MotionPlayer Wrapper 桥接、MotionServer TCP 服务、文件管理 UI、时间轴编辑状态 |

核心理由：

1. **Motion 处理与 IODevice 生态统一**
   - `MotionPlayer` 与 `PlayerInput` 对称——输出侧跨设备时序状态机
   - 由 `IODeviceController::Update()` 驱动 Tick，与设备 I/O 刷新同步
   - Unity/UE 可直接嵌入使用，无需 IOStudio 进程

2. **避免 C#/C++ 双重实现维护成本**
   - 插值算法、安全保护、通道仲裁只在 C++ 实现一次
   - C# 通过 Wrapper 调用，零逻辑重复

3. **IOStudio 聚焦编辑器职责**
   - IOStudio 不重复实现播放逻辑，仅负责 UI 和网络协调
   - 编辑器预览播放通过 `IOToolkit.MotionPlayer.PlaySlot()` 调用 C++ 核心
   - MotionServer TCP 服务在 IOStudio C# 中实现（网络层不属于 IODevice 职责）

#### 5.1.3 目录结构

```
IOStudio/
├── Services/
│   ├── ConnectionManager.cs              # (已有) TCP/UDP/Serial 连接管理
│   ├── EventForwardingService.cs         # (已有) 事件转发
│   ├── Senders/
│   │   ├── IProtocolSender.cs            # (已有) 协议发送器接口
│   │   ├── TcpServerSender.cs            # (已有) TCP 广播
│   │   └── ...
│   └── Motion/                           # 【新增】播放引擎 + 网络服务
│       ├── MotionPlayerManager.cs        #   多 Slot 编排器
│       ├── MotionSlot.cs                 #   Slot 容器
│       ├── ChannelMixer.cs               #   通道仲裁器
│       ├── MotionPlaybackEngine.cs       #   播放引擎 (纯求值器, 不直接派发)
│       ├── InterpolationEngine.cs        #   插值引擎 (Linear/Bezier/Step/Ease)
│       ├── SafetyGuard.cs                #   安全保护 (后混合阶段)
│       ├── DeviceDispatcher.cs           #   设备调度 → IODevice SetDO
│       ├── MotionFileReader.cs           #   .motion JSON 解析
│       ├── MotionServerService.cs        #   TCP 服务器 (播控网络接口)
│       ├── MotionCommandHandler.cs       #   命令解析与分发
│       └── MotionProtocol.cs             #   协议消息定义 + 帧序列化
│
├── Models/
│   └── Motion/                           # 【新增】Motion 数据模型
│       ├── MotionTimeline.cs             #   时间轴
│       ├── MotionTrack.cs                #   轨道 (device + oaction 映射)
│       ├── MotionClip.cs                 #   片段
│       ├── MotionKeyframe.cs             #   关键帧
│       └── PlaybackState.cs              #   播放状态枚举
│
├── ViewModels/
│   └── Timeline/                         # 【新增】时间轴编辑 UI 逻辑
│       ├── TimelineEditorViewModel.cs
│       ├── TrackViewModel.cs
│       └── ...
│
└── Views/
    └── Timeline/                         # 【新增】时间轴编辑 UI
```

**v9 关键分层规则：**

```
IODevice.dll (C++)
  └── MotionPlayer (核心播放逻辑：多Slot管理/插值/安全/通道仲裁/设备派发)
        ↑
IODevice_C_Wrapper.dll (C ABI)
        ↑
IODevice_CSharp_Wrapper (C# P/Invoke: IOToolkit.MotionPlayer)
        ↑
IOStudio:
  Views/Timeline/     ──引用──→  ViewModels/Timeline/
  ViewModels/Timeline ──引用──→  IOToolkit.MotionPlayer (C# Wrapper)
                                 +  Services/Motion/ (TCP服务/文件管理)
                                 +  Models/Motion/ (数据模型)
  Services/Motion/    ──引用──→  IOToolkit.MotionPlayer (C# Wrapper)
                                 +  Models/Motion/
                                 (不引用 ViewModels / Views)
```

> `Services/Motion/` 中的播放协调（预览、TCP 命令处理）通过 `IOToolkit.MotionPlayer` C# Wrapper 调用 C++ 核心，不在 C# 中重复实现播放逻辑。

---

### 5.B 多文件并发播放架构

> § 5.B 中描述的 C# 类设计（MotionPlayerManager、ChannelMixer、MotionSlot 等）作为 C++ `MotionPlayer::Impl` 的设计蓝图，生产实现在 IODevice C++ 中完成（详见 § 5.A / § 5.B.7），IOStudio C# 侧通过 C# Wrapper 消费 C++ 核心。

#### 5.B.1 问题陈述

**现有架构（v7）的局限：**

```
TimelineEditorViewModel ─ 1:1 ─→ MotionPlaybackEngine ─ 1:1 ─→ MotionTimeline
                                          │
                                    直接调用 DeviceDispatcher.DispatchTrack()
                                          │
                                    SafetyGuard (单实例, 全局 _lastValues)
```

**缺陷：**

| 问题                        | 影响                                                                    | 根因                         |
| --------------------------- | ----------------------------------------------------------------------- | ---------------------------- |
| **单文件限制**              | `MotionPlaybackEngine` 持有唯一 `CurrentTimeline`，无法同时播放多个文件 | 1:1 绑定设计                 |
| **SafetyGuard 状态污染**    | 多引擎同时写同一通道时，速度限制历史值(`_lastValues`)被交替覆盖         | 全局单字典、无源追踪         |
| **DeviceDispatcher 无仲裁** | 多引擎对同一通道 SetDO() 结果不确定（取决于 Tick 执行顺序）             | Last-write-wins、无通道锁    |
| **回中冲突**                | Engine A 回中过程中 Engine B 启动，同通道出现撕扯                       | 回中在引擎内、每引擎独立停止 |
| **C++ 单例**                | `MotionPlayer::Instance()` 全局唯一，C Wrapper 无 Slot 概念             | 单例模式                     |

**典型业务场景：**

```
4D影院座位厅场景:

座椅动感 (侏罗纪冒险_座椅.mtn)          → Chair: OAxis_00(Pitch), OAxis_01(Roll), OAxis_02(Heave)
灯光氛围 (侏罗纪冒险_灯光.mtn)          → LightCtrl: Light_Main, Light_Side, Light_Strobe
环境特效 (侏罗纪冒险_特效.mtn)          → EffectCtrl: Wind, Smoke, Rain, Spray
震动补充 (侏罗纪冒险_震动.mtn)          → Chair: OAxis_03(Vibration) ← 与座椅共享设备!

需求: 4 个文件同时播放, 其中"座椅"和"震动"共享 Chair 设备但控制不同通道;
      运营可单独暂停/停止"特效"而不影响其他文件的播放。
```

#### 5.B.2 解决方案：Slot-Based Multi-Engine 架构

**核心思路：** 引入 `MotionPlayerManager` 编排层，管理多个命名 Slot，每个 Slot 包含独立的 `MotionPlaybackEngine`；引擎仅负责求值（不直接派发设备命令），由统一的 `ChannelMixer` 汇总各 Slot 输出、解决通道冲突后，经 `SafetyGuard` 安全检查，最终由 `DeviceDispatcher` 一次性派发。

```
┌─────────────────────────────────────────────────────────────────────────┐
│                     MotionPlayerManager (编排层)                         │
│                                                                         │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐   │
│  │  Slot "seat" │  │ Slot "light"│  │ Slot "fx"   │  │ Slot "vib"  │   │
│  │  Priority: 0 │  │ Priority: 1 │  │ Priority: 2 │  │ Priority: 0 │   │
│  │  Policy:Pri  │  │ Policy:Pri  │  │ Policy:Pri  │  │ Policy:Add  │   │
│  │  ┌─────────┐│  │  ┌─────────┐│  │  ┌─────────┐│  │  ┌─────────┐│   │
│  │  │ Engine  ││  │  │ Engine  ││  │  │ Engine  ││  │  │ Engine  ││   │
│  │  │ (求值器) ││  │  │ (求值器) ││  │  │ (求值器) ││  │  │ (求值器) ││   │
│  │  └────┬────┘│  │  └────┬────┘│  │  └────┬────┘│  │  └────┬────┘│   │
│  │       │      │  │       │      │  │       │      │  │       │      │   │
│  │  Channel Map │  │  Channel Map │  │  Channel Map │  │  Channel Map │   │
│  │  Chair.P=0.3 │  │  Light.M=0.8 │  │  FX.Wind=0.6 │  │  Chair.V=0.4 │   │
│  └───────┬─────┘  └───────┬─────┘  └───────┬─────┘  └───────┬─────┘   │
│          │                 │                 │                 │         │
│          └─────────────────┼─────────────────┼─────────────────┘         │
│                            ▼                                             │
│                    ┌───────────────┐                                     │
│                    │ ChannelMixer  │ ← 按 Priority/Blend/Max/Add 策略   │
│                    │ 通道仲裁      │    解决多 Slot 写同一通道的冲突      │
│                    └───────┬───────┘                                     │
│                            ▼                                             │
│                    ┌───────────────┐                                     │
│                    │ SafetyGuard   │ ← 混合后统一安全检查 (速度限制/限位) │
│                    │ (后混合阶段)   │    _lastValues 基于混合结果,无污染   │
│                    └───────┬───────┘                                     │
│                            ▼                                             │
│                    ┌───────────────┐                                     │
│                    │DeviceDispatcher│ ← 每通道每 Tick 仅一次 SetDO()      │
│                    │ (统一派发)     │                                     │
│                    └───────────────┘                                     │
└─────────────────────────────────────────────────────────────────────────┘
```

#### 5.B.3 通道冲突策略 (ChannelConflictPolicy)

当一个 Tick 内多个 Slot 输出同一通道值时，由 ChannelMixer 根据 Slot 上配置的策略决定最终值：

| 策略                | 行为                                                 | 典型场景                         |
| ------------------- | ---------------------------------------------------- | -------------------------------- |
| **Priority** (默认) | 数值最小的 Priority 值优先（0 最高），低优先级被忽略 | 座椅主控 vs. 补充微调            |
| **Blend**           | 所有活跃源的算术平均                                 | 多个环境灯光文件叠加             |
| **Max**             | 取最大值                                             | 风力叠加——多个特效取最强风       |
| **Additive**        | 求和并 Clamp 到 [0, 1]                               | 震动叠加——多个文件的震动效果累加 |
| **Override**        | 最后加载的 Slot 优先（LIFO）                         | 简单覆盖场景                     |

**冲突策略绑定在 Slot 上**而非通道上，因为同一个 Slot（同一个文件）内的所有通道通常遵循相同的策略。如果需要更细粒度控制，可在 `.motion` 文件的 Track 级别增加 `conflict_policy` 字段（后续扩展）。

```csharp
public enum ChannelConflictPolicy
{
    Priority = 0,   // 高优先级 Slot 赢 (默认)
    Blend    = 1,   // 算术平均
    Max      = 2,   // 取最大值
    Additive = 3,   // 求和 Clamp
    Override = 4    // 最后加载的 Slot 优先
}
```

#### 5.B.4 核心类设计

##### MotionPlayerManager (编排器)

```csharp
namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 多文件并发播放编排器。管理多个命名 Slot，每个 Slot 包含独立引擎。
    /// 统一 Tick 驱动所有引擎 → ChannelMixer 仲裁 → SafetyGuard → DeviceDispatcher.
    /// </summary>
    public class MotionPlayerManager : IDisposable
    {
        private readonly ConcurrentDictionary<string, MotionSlot> _slots = new();
        private readonly ChannelMixer _mixer = new();
        private readonly SafetyGuard _safetyGuard;
        private readonly DeviceDispatcher _dispatcher;

        // 时钟
        private readonly Stopwatch _stopwatch = new();
        private double _lastTickMs;

        public MotionPlayerManager()
            : this(new DeviceDispatcher(), new SafetyGuard()) { }

        public MotionPlayerManager(DeviceDispatcher dispatcher, SafetyGuard safetyGuard)
        {
            _dispatcher = dispatcher;
            _safetyGuard = safetyGuard;
            _stopwatch.Start();
        }

        // ── Slot 管理 ──────────────────────────────────────────────

        /// <summary>创建或获取 Slot。slotId 不存在则创建。</summary>
        public MotionSlot GetOrCreateSlot(string slotId, int priority = 0,
            ChannelConflictPolicy policy = ChannelConflictPolicy.Priority)
        {
            return _slots.GetOrAdd(slotId, id => new MotionSlot(id, priority, policy));
        }

        /// <summary>移除 Slot。自动停止播放并执行回中。</summary>
        public bool RemoveSlot(string slotId)
        {
            if (_slots.TryRemove(slotId, out var slot))
            {
                slot.Engine.ForceStop();
                slot.Dispose();
                return true;
            }
            return false;
        }

        public MotionSlot? GetSlot(string slotId) =>
            _slots.TryGetValue(slotId, out var s) ? s : null;

        public IReadOnlyCollection<MotionSlot> Slots => _slots.Values.ToList().AsReadOnly();

        // ── 批量操作 ──────────────────────────────────────────────

        public void PlayAll()  { foreach (var s in _slots.Values) s.Engine.Play(); }
        public void PauseAll() { foreach (var s in _slots.Values) s.Engine.Pause(); }
        public void StopAll()  { foreach (var s in _slots.Values) s.Engine.Stop(); }
        public void ForceStopAll() { foreach (var s in _slots.Values) s.Engine.ForceStop(); }

        // ── 核心 Tick (Manager 级) ────────────────────────────────

        /// <summary>
        /// 由外部定时器驱动 (IOStudio: Observable.Interval 60fps / C++: Update())。
        /// 1. Tick 所有 Slot 引擎 → 各引擎求值
        /// 2. 收集所有 Slot 的通道快照
        /// 3. ChannelMixer 仲裁 → SafetyGuard 检查 → DeviceDispatcher 派发
        /// </summary>
        public void Tick()
        {
            double now = _stopwatch.Elapsed.TotalMilliseconds;
            double deltaMs = Math.Min(now - _lastTickMs, 100); // 安全上限 100ms
            _lastTickMs = now;
            double deltaSec = deltaMs / 1000.0;

            // Step 1: 驱动所有引擎求值 (各引擎独立更新状态和时间)
            var slotOutputs = new List<SlotOutput>();
            foreach (var slot in _slots.Values)
            {
                var engine = slot.Engine;
                engine.Tick(); // 引擎内部更新时间、状态；不派发

                var snapshot = engine.EvaluateChannels(deltaSec);
                if (snapshot.Count > 0)
                {
                    slotOutputs.Add(new SlotOutput(
                        slot.Priority,
                        slot.ConflictPolicy,
                        snapshot
                    ));
                }
            }

            // Step 2: 通道仲裁
            var mixed = _mixer.Mix(slotOutputs);

            // Step 3: 安全检查 + 派发
            foreach (var (channelKey, value) in mixed)
            {
                float safeValue = _safetyGuard.Check(channelKey, value, deltaSec);
                _dispatcher.DispatchByChannelKey(channelKey, safeValue);
            }
        }

        // ── 事件 ──────────────────────────────────────────────────

        public event Action<string, PlaybackState, PlaybackState>? SlotStateChanged;
        public event Action<string>? SlotPlaybackCompleted;

        public void Dispose()
        {
            foreach (var slot in _slots.Values) slot.Dispose();
            _slots.Clear();
        }
    }
}
```

##### MotionSlot (Slot 容器)

```csharp
namespace IOStudio.Services.Motion
{
    /// <summary>
    /// 一个播放槽位 = 一个独立引擎 + 元数据。
    /// </summary>
    public class MotionSlot : IDisposable
    {
        public string Id { get; }
        public int Priority { get; set; }
        public ChannelConflictPolicy ConflictPolicy { get; set; }
        public IPlaybackEngine Engine { get; }

        public MotionSlot(string id, int priority = 0,
            ChannelConflictPolicy policy = ChannelConflictPolicy.Priority)
        {
            Id = id;
            Priority = priority;
            ConflictPolicy = policy;
            Engine = new MotionPlaybackEngine(); // 不再传入 Dispatcher/SafetyGuard
        }

        public void Dispose() => Engine.Dispose();
    }
}
```

##### ChannelMixer (通道仲裁器)

```csharp
namespace IOStudio.Services.Motion
{
    public readonly record struct SlotOutput(
        int Priority,
        ChannelConflictPolicy Policy,
        Dictionary<string, float> Channels
    );

    /// <summary>
    /// 汇总多个 Slot 的通道输出，按策略解决冲突。
    /// 同一通道被多个 Slot 写入时，根据写入端 Slot 的 ConflictPolicy 决定合并方式。
    /// 当多个写入端策略不一致时，以最高优先级 Slot 的策略为准。
    /// </summary>
    public class ChannelMixer
    {
        public Dictionary<string, float> Mix(List<SlotOutput> slotOutputs)
        {
            if (slotOutputs.Count == 0)
                return new Dictionary<string, float>();

            if (slotOutputs.Count == 1)
                return new Dictionary<string, float>(slotOutputs[0].Channels);

            // 按通道聚合: channelKey → List<(priority, policy, value)>
            var channelSources = new Dictionary<string, List<(int pri, ChannelConflictPolicy pol, float val)>>();
            foreach (var output in slotOutputs)
            {
                foreach (var (key, value) in output.Channels)
                {
                    if (!channelSources.TryGetValue(key, out var list))
                    {
                        list = new List<(int, ChannelConflictPolicy, float)>();
                        channelSources[key] = list;
                    }
                    list.Add((output.Priority, output.Policy, value));
                }
            }

            // 逐通道仲裁
            var result = new Dictionary<string, float>(channelSources.Count);
            foreach (var (channelKey, sources) in channelSources)
            {
                if (sources.Count == 1)
                {
                    result[channelKey] = sources[0].val;
                    continue;
                }

                // 确定策略: 以最高优先级(最小 pri 值) Slot 的策略为准
                sources.Sort((a, b) => a.pri.CompareTo(b.pri));
                var policy = sources[0].pol;

                result[channelKey] = policy switch
                {
                    ChannelConflictPolicy.Priority => sources[0].val,   // 最高优先级
                    ChannelConflictPolicy.Blend    => sources.Average(s => s.val),
                    ChannelConflictPolicy.Max      => sources.Max(s => s.val),
                    ChannelConflictPolicy.Additive => Math.Clamp(sources.Sum(s => s.val), 0f, 1f),
                    ChannelConflictPolicy.Override => sources[^1].val,  // LIFO
                    _ => sources[0].val
                };
            }
            return result;
        }
    }
}
```

#### 5.B.5 MotionPlaybackEngine 变更 (求值/派发分离)

**关键设计：** Engine 不再直接持有 `DeviceDispatcher` 和 `SafetyGuard`，`Tick()` 仅更新内部时间和状态；新增 `EvaluateChannels()` 方法返回当前帧的通道快照，由 Manager 统一后处理。

```csharp
public class MotionPlaybackEngine : IPlaybackEngine
{
    // 当前帧通道快照 (Tick 后由 Manager 读取)
    private readonly Dictionary<string, float> _channelSnapshot = new();

    /// <summary>
    /// Tick: 仅更新时间和状态 (不派发设备命令)。
    /// 当由 MotionPlayerManager 管理时，Manager 在外部统一调用 Tick() → EvaluateChannels()。
    /// </summary>
    public void Tick()
    {
        // ... 原有时间推进、状态机逻辑 (不变)
        // 移除: EvaluateAndDispatch(deltaMs) → 改为仅时间推进
    }

    /// <summary>
    /// 求值当前帧所有轨道的通道输出。
    /// 返回 channelKey → normalizedValue 字典。
    /// channelKey 格式: "{DeviceName}.oaxis.{OAxisChannel}" 或 "{DeviceName}.{OActionName}"
    /// </summary>
    public Dictionary<string, float> EvaluateChannels(double deltaTimeSec)
    {
        _channelSnapshot.Clear();
        if (CurrentTimeline is null) return _channelSnapshot;

        foreach (var track in CurrentTimeline.Tracks)
        {
            if (track.Muted) continue;

            float rawValue = EvaluateTrack(track, CurrentTimeMs);
            string channelKey = MakeChannelKey(track);

            // 如果同一 Timeline 内有多个 Track 写同一通道 (罕见但合法)
            // 后面的 Track 覆盖前面的 → 文件内顺序决定
            _channelSnapshot[channelKey] = rawValue;
        }
        return _channelSnapshot;
    }

    /// <summary>回中过程中的通道输出</summary>
    public Dictionary<string, float> EvaluateReturnChannels()
    {
        _channelSnapshot.Clear();
        if (_returnStartValues is null) return _channelSnapshot;

        float t = (float)(_returnElapsedMs / ReturnDurationMs);
        t = Math.Clamp(t, 0f, 1f);
        float eased = t * t * (3f - 2f * t); // smoothstep

        foreach (var (key, startVal) in _returnStartValues)
        {
            _channelSnapshot[key] = startVal + (0.5f - startVal) * eased;
        }
        return _channelSnapshot;
    }

    private static string MakeChannelKey(MotionTrack track)
    {
        return track.OutputType == "oaxis" && !string.IsNullOrEmpty(track.OAxisChannel)
            ? $"{track.DeviceName}.oaxis.{track.OAxisChannel}"
            : $"{track.DeviceName}.{track.OActionName}";
    }
}
```

#### 5.B.6 DeviceDispatcher 变更

新增 `DispatchByChannelKey` 方法，支持从 channelKey 字符串直接派发：

```csharp
public class DeviceDispatcher
{
    /// <summary>
    /// 按 channelKey 格式派发。
    /// channelKey: "{DeviceName}.oaxis.{Channel}" 或 "{DeviceName}.{OActionName}"
    /// </summary>
    public void DispatchByChannelKey(string channelKey, float normalizedValue)
    {
        var parts = channelKey.Split('.', 3);
        if (parts.Length < 2) return;

        string deviceName = parts[0];
        if (parts.Length == 3 && parts[1] == "oaxis")
            DispatchOAxis(deviceName, parts[2], normalizedValue);
        else
            Dispatch(deviceName, parts[1], normalizedValue);
    }

    // ... 原有 Dispatch / DispatchOAxis / DispatchTrack 保留不变
}
```

#### 5.B.7 C++ MotionPlayer 多 Slot 设计

`MotionPlayer` 为全局单例管理器 (类比 `IODeviceController`)，内部管理多个 `MotionSlot`：

```cpp
// Source/Public/MotionPlayer.h
#pragma once
#include <functional>
#include <memory>
#include <string>
#include "IODevice.h"

namespace IOToolkit {

    enum class MotionState  { Idle, Playing, Paused, Stopping };
    enum class MotionEvent  { PlaybackComplete, SafetyTriggered, StateChanged };
    enum class MixPolicy    { Priority = 0, Blend = 1, Max = 2, Additive = 3, Override = 4 };

    class IOAPI MotionPlayer {
    public:
        static MotionPlayer& Instance();

        // ── Slot 管理 ────────────────────────────────
        int  LoadSlot(const char* slotId, const char* filePath,
                      int priority = 0, MixPolicy policy = MixPolicy::Priority);
        void UnloadSlot(const char* slotId);
        void UnloadAll();

        // ── 单 Slot 播控 ─────────────────────────────
        int  PlaySlot(const char* slotId);
        int  PauseSlot(const char* slotId);
        int  ResumeSlot(const char* slotId);
        int  StopSlot(const char* slotId);      // 触发回中
        int  SeekSlot(const char* slotId, float timeMs);
        void SetSlotSpeed(const char* slotId, float speed);
        void SetSlotLoop(const char* slotId, bool loop);

        // ── 批量操作 ─────────────────────────────────
        void PlayAll();
        void PauseAll();
        void StopAll();

        // ── 状态查询 ─────────────────────────────────
        MotionState GetSlotState(const char* slotId) const;
        float       GetSlotCurrentTime(const char* slotId) const;
        float       GetSlotDuration(const char* slotId) const;
        int         GetSlotCount() const;

        // ── 事件回调 ─────────────────────────────────
        void BindSlotEvent(const char* slotId, MotionEvent evt,
                           std::function<void()> callback);

        // ── 内部: 由 IODeviceController::Update() 驱动 ──
        void Tick(float deltaSeconds);

    private:
        MotionPlayer(); ~MotionPlayer();
        MotionPlayer(const MotionPlayer&) = delete;
        class Impl;
        std::unique_ptr<Impl> _impl;
    };
}
```

#### 5.B.8 C Wrapper API (Slot 扩展)

```cpp
// IODevice_CWrapper.h — Slot 版本

IOCAPI int   __stdcall MotionLoadSlot(BSTR InSlotId, BSTR InFilePath,
                                       int InPriority, int InMixPolicy);
IOCAPI void  __stdcall MotionUnloadSlot(BSTR InSlotId);
IOCAPI void  __stdcall MotionUnloadAll();
IOCAPI int   __stdcall MotionPlaySlot(BSTR InSlotId);
IOCAPI int   __stdcall MotionPauseSlot(BSTR InSlotId);
IOCAPI int   __stdcall MotionResumeSlot(BSTR InSlotId);
IOCAPI int   __stdcall MotionStopSlot(BSTR InSlotId);
IOCAPI int   __stdcall MotionSeekSlot(BSTR InSlotId, float InTimeMs);
IOCAPI void  __stdcall MotionSetSlotSpeed(BSTR InSlotId, float InSpeed);
IOCAPI void  __stdcall MotionSetSlotLoop(BSTR InSlotId, bool InLoop);
IOCAPI int   __stdcall MotionGetSlotState(BSTR InSlotId);
IOCAPI float __stdcall MotionGetSlotCurrentTime(BSTR InSlotId);
IOCAPI float __stdcall MotionGetSlotDuration(BSTR InSlotId);
IOCAPI int   __stdcall MotionGetSlotCount();
IOCAPI void  __stdcall MotionPlayAll();
IOCAPI void  __stdcall MotionPauseAll();
IOCAPI void  __stdcall MotionStopAll();
```

#### 5.B.9 C# Wrapper (IOToolkit.MotionPlayer 扩展)

```csharp
namespace IOToolkit
{
    public static class MotionPlayer
    {
        // ── Slot API ─────────────────────────────
        public static int LoadSlot(string slotId, string filePath,
            int priority = 0, MixPolicy policy = MixPolicy.Priority)
            => NativeMethods.MotionLoadSlot(slotId, filePath, priority, (int)policy);
        public static void UnloadSlot(string slotId)
            => NativeMethods.MotionUnloadSlot(slotId);
        public static void UnloadAll()    => NativeMethods.MotionUnloadAll();
        public static int  PlaySlot(string slotId) => NativeMethods.MotionPlaySlot(slotId);
        public static int  PauseSlot(string slotId) => NativeMethods.MotionPauseSlot(slotId);
        public static int  StopSlot(string slotId) => NativeMethods.MotionStopSlot(slotId);
        public static void PlayAll()      => NativeMethods.MotionPlayAll();
        public static void PauseAll()     => NativeMethods.MotionPauseAll();
        public static void StopAll()      => NativeMethods.MotionStopAll();
        public static int  SlotCount      => NativeMethods.MotionGetSlotCount();
    }

    public enum MixPolicy { Priority = 0, Blend = 1, Max = 2, Additive = 3, Override = 4 }
}
```

#### 5.B.10 TCP 协议扩展

所有命令通过 `"slot"` 字段指定目标 Slot：

```json
// 加载到指定 Slot
{ "cmd": "load", "slot": "seat", "file": "侏罗纪_座椅.mtn", "priority": 0, "policy": "priority" }
{ "cmd": "load", "slot": "fx",   "file": "侏罗纪_特效.mtn", "priority": 2, "policy": "additive" }

// 单 Slot 控制
{ "cmd": "play",   "slot": "seat" }
{ "cmd": "pause",  "slot": "fx" }
{ "cmd": "stop",   "slot": "seat" }
{ "cmd": "seek",   "slot": "seat", "time_ms": 5000 }

// 批量操作
{ "cmd": "play_all" }
{ "cmd": "pause_all" }
{ "cmd": "stop_all" }

// 查询
{ "cmd": "list_slots" }

// list_slots 响应
{
    "type": "response", "cmd": "list_slots", "ok": true,
    "slots": [
        { "id": "seat",  "file": "侏罗纪_座椅.mtn", "state": "playing",  "time_ms": 12340, "priority": 0 },
        { "id": "fx",    "file": "侏罗纪_特效.mtn", "state": "playing",  "time_ms": 12340, "priority": 2 },
        { "id": "light", "file": "侏罗纪_灯光.mtn", "state": "paused",   "time_ms": 8000,  "priority": 1 }
    ]
}

// 状态广播 (所有活跃 Slot 合并推送)
{
    "type": "state",
    "slots": [
        { "id": "seat", "state": "playing", "time_ms": 12350, "duration_ms": 120000 },
        { "id": "fx",   "state": "playing", "time_ms": 12350, "duration_ms": 120000 }
    ]
}
```

#### 5.B.11 TimelineEditorViewModel 集成

编辑器预览使用 Manager 的单 Slot 模式，Slot ID 为 `"editor_preview"`：

```csharp
public partial class TimelineEditorViewModel
{
    private readonly MotionPlayerManager _manager;
    private const string EditorSlotId = "editor_preview";

    public TimelineEditorViewModel(MotionPlayerManager manager)
    {
        _manager = manager;
        var slot = _manager.GetOrCreateSlot(EditorSlotId, priority: -1); // 最高优先级
        slot.Engine.StateChanged += OnStateChanged;
        slot.Engine.PositionChanged += OnPositionChanged;
        slot.Engine.PlaybackCompleted += OnPlaybackCompleted;
    }

    private void PlayPreview()
    {
        var slot = _manager.GetSlot(EditorSlotId)!;
        slot.Engine.LoadTimeline(BuildCurrentTimeline());
        slot.Engine.Play();
    }
}
```

> **编辑器预览 Slot 使用 priority: -1**，确保编辑器预览时的输出始终覆盖其他 Slot——
> 开发者调试时，编辑器预览的输出应该是最终可见的效果。

#### 5.B.12 Unity/UE 多 Slot 集成示例

```csharp
// Unity — 4D影院场景控制器
using IOToolkit;
using UnityEngine;

public class CinemaController : MonoBehaviour
{
    void Start()
    {
        IODeviceController.Load("Config/IODevice.xml");

        // 加载多个动作文件到不同 Slot
        MotionPlayer.LoadSlot("seat",  "Config/Motion/侏罗纪_座椅.mtn",  priority: 0);
        MotionPlayer.LoadSlot("light", "Config/Motion/侏罗纪_灯光.mtn",  priority: 1);
        MotionPlayer.LoadSlot("fx",    "Config/Motion/侏罗纪_特效.mtn",  priority: 2,
                              policy: MixPolicy.Additive);
    }

    public void OnVideoPlay()
    {
        MotionPlayer.PlayAll(); // 全部同时开始
    }

    public void OnEmergencyStop()
    {
        MotionPlayer.StopAll(); // 全部平滑回中
    }

    // 运营需求: 热天关闭雨水特效，不影响座椅和灯光
    public void ToggleRainEffect(bool on)
    {
        if (on) MotionPlayer.PlaySlot("fx");
        else    MotionPlayer.StopSlot("fx"); // 仅停止特效 Slot
    }

    void OnDestroy() => MotionPlayer.UnloadAll();
}
```

---

### 5.3 网络控制协议设计

IOStudio 内建 TCP MotionServer，为 Unity/UE/Python 等外部程序提供网络播控接口。

#### 5.3.1 总体架构

```
┌──────────────────────────────────────────────────────┐
│                  IOStudio 进程                        │
│                                                      │
│  MotionServerService (TcpListener, 端口 9600)        │
│    ├─ ClientSession[] (每连接一线程)                  │
│    │    └─ MotionProtocol (帧编解码)                  │
│    ├─ MotionCommandHandler (命令分发)                 │
│    │    └─ MotionPlaybackEngine / MotionPlayerManager │
│    └─ 状态广播 (50ms 间隔, 推送至所有客户端)          │
└──────────────────────────────────────────────────────┘
          ↑ TCP (4字节长度前缀 + UTF-8 JSON)
┌─────────┴─────────┐
│  Unity/UE/Python   │  ← MotionClient 薄客户端
└───────────────────┘
```

#### 5.3.2 帧协议 (MotionProtocol)

采用 **4 字节大端长度前缀 + UTF-8 JSON** 的简单二进制帧格式：

```
┌──────────────┬───────────────────────┐
│ Length (4B)   │ JSON Payload (UTF-8)  │
│ Big-Endian    │ 最大 1MB              │
└──────────────┴───────────────────────┘
```

```csharp
// Services/Motion/MotionProtocol.cs
using System.Buffers.Binary;

namespace IOStudio.Services.Motion
{
    public static class MotionProtocol
    {
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

#### 5.3.3 命令处理 (MotionCommandHandler)

所有命令均通过 `"slot"` 字段指定目标 Slot（多 Slot 协议扩展见 § 5.B.10）：

| 命令           | 参数                          | 说明                     |
| -------------- | ----------------------------- | ------------------------ |
| `ping`         | —                             | 心跳检测                 |
| `load`         | `slot, file, priority, policy`| 加载动作文件到 Slot      |
| `unload`       | `slot`                        | 卸载 Slot                |
| `play`         | `slot`                        | 播放                     |
| `play_from`    | `slot, time_ms`               | 从指定时间播放           |
| `pause`        | `slot`                        | 暂停                     |
| `resume`       | `slot`                        | 恢复                     |
| `stop`         | `slot`                        | 停止（平滑回中）         |
| `seek`         | `slot, time_ms`               | 跳转                     |
| `set_speed`    | `slot, speed`                 | 设置速度                 |
| `set_loop`     | `slot, loop`                  | 设置循环                 |
| `get_state`    | —                             | 查询完整状态             |
| `list_files`   | —                             | 列出可用动作文件         |
| `list_devices` | —                             | 列出 IODevice 设备       |
| `list_slots`   | —                             | 列出所有 Slot 状态       |
| `play_all`     | —                             | 全部播放                 |
| `pause_all`    | —                             | 全部暂停                 |
| `stop_all`     | —                             | 全部停止                 |
| `set_channel`  | `device, key, value`          | 直接控制设备通道         |

#### 5.3.4 配置 (IOStudioSettings)

```csharp
[XmlAttribute("MotionServerEnabled")]
public bool MotionServerEnabled { get; set; } = false;

[XmlAttribute("MotionServerPort")]
public int MotionServerPort { get; set; } = 9600;
```

#### 5.3.5 安全机制

- **帧大小上限**：1MB，超出立即断开
- **心跳超时**：30 秒无消息触发紧急停止
- **多客户端**：支持多个客户端同时连接，所有客户端均收到状态广播

---

### 5.4 通信协议参考

#### 5.4.1 请求格式

```json
{ "cmd": "<命令名>", "slot": "<slotId>", ...参数 }
```

#### 5.4.2 响应格式

**成功：**
```json
{ "type": "response", "cmd": "<命令名>", "ok": true, ...数据字段 }
```

**失败：**
```json
{ "type": "response", "cmd": "<命令名>", "ok": false, "error": "<错误描述>" }
```

#### 5.4.3 状态广播

服务端每 50ms 向所有客户端推送当前状态：

```json
{
    "type": "state",
    "slots": [
        { "id": "seat", "state": "playing", "time_ms": 12350, "duration_ms": 120000 },
        { "id": "fx",   "state": "playing", "time_ms": 12350, "duration_ms": 120000 }
    ]
}
```

#### 5.4.4 事件推送

```json
{ "type": "event", "event": "playback_complete", "slot": "seat" }
{ "type": "event", "event": "safety_triggered",  "slot": "seat", "message": "速率超限" }
```

#### 5.4.5 常用命令示例

```json
// 加载动作文件到 Slot
{ "cmd": "load",  "slot": "seat", "file": "侏罗纪_座椅.mtn", "priority": 0, "policy": "priority" }

// 播放/暂停/停止
{ "cmd": "play",  "slot": "seat" }
{ "cmd": "pause", "slot": "seat" }
{ "cmd": "stop",  "slot": "seat" }

// 批量操作
{ "cmd": "play_all" }
{ "cmd": "stop_all" }

// 查询所有 Slot
{ "cmd": "list_slots" }
// → { "type": "response", "cmd": "list_slots", "ok": true,
//     "slots": [
//       { "id": "seat", "file": "侏罗纪_座椅.mtn", "state": "playing", "time_ms": 12340, "priority": 0 },
//       { "id": "fx",   "file": "侏罗纪_特效.mtn", "state": "paused",  "time_ms": 8000,  "priority": 2 }
//     ] }
```

#### 5.4.6 Python 测试客户端

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

    send_cmd(sock, {"cmd": "ping"})
    print("ping →", recv_msg(sock))

    send_cmd(sock, {"cmd": "load", "slot": "seat", "file": "demo_chair_3dof.motion"})
    print("load →", recv_msg(sock))

    send_cmd(sock, {"cmd": "play", "slot": "seat"})
    print("play →", recv_msg(sock))

    sock.settimeout(0.1)
    end = time.time() + 5
    while time.time() < end:
        try:
            msg = recv_msg(sock)
            if msg and msg.get("type") == "state":
                for s in msg.get("slots", []):
                    print(f"  {s['id']}: t={s['time_ms']:.0f}ms state={s['state']}")
        except socket.timeout:
            pass

    send_cmd(sock, {"cmd": "stop_all"})
    print("stop →", recv_msg(sock))
    sock.close()

if __name__ == "__main__":
    main()
```

---

### 5.5 Unity 客户端 SDK (TCP 薄客户端)

Unity 客户端通过 TCP 连接 IOStudio MotionServer，仅需一个 `MotionClient.cs` 文件。

```csharp
// MotionClient.cs — Unity TCP 薄客户端, 放入 Assets/ 即可使用
using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class MotionClient : MonoBehaviour
{
    [Header("MotionServer 连接")]
    public string serverIP = "127.0.0.1";
    public int serverPort = 9600;

    public event Action OnPlaybackComplete;
    public event Action<string> OnSafetyEvent;

    private TcpClient _tcp;
    private NetworkStream _stream;
    private Thread _recvThread;
    private CancellationTokenSource _cts;
    private readonly ConcurrentQueue<Action> _mainThreadQueue = new();

    void Start() => Connect();

    public void Connect()
    {
        _cts = new CancellationTokenSource();
        _tcp = new TcpClient();
        _tcp.Connect(serverIP, serverPort);
        _stream = _tcp.GetStream();
        _recvThread = new Thread(ReceiveLoop) { IsBackground = true };
        _recvThread.Start();
    }

    void Update()
    {
        while (_mainThreadQueue.TryDequeue(out var action)) action();
    }

    // ── 公开 API ──────────────────────────────────
    public void LoadFile(string path, string slot = "main", int priority = 0)
        => SendCmd($"{{\"cmd\":\"load\",\"slot\":\"{slot}\",\"file\":\"{path}\",\"priority\":{priority}}}");

    public void Play(string slot = "main") => SendCmd($"{{\"cmd\":\"play\",\"slot\":\"{slot}\"}}");
    public void Pause(string slot = "main") => SendCmd($"{{\"cmd\":\"pause\",\"slot\":\"{slot}\"}}");
    public void Resume(string slot = "main") => SendCmd($"{{\"cmd\":\"resume\",\"slot\":\"{slot}\"}}");
    public void Stop(string slot = "main") => SendCmd($"{{\"cmd\":\"stop\",\"slot\":\"{slot}\"}}");
    public void Seek(float ms, string slot = "main") => SendCmd($"{{\"cmd\":\"seek\",\"slot\":\"{slot}\",\"time_ms\":{ms}}}");
    public void PlayAll() => SendCmd("{\"cmd\":\"play_all\"}");
    public void StopAll() => SendCmd("{\"cmd\":\"stop_all\"}");

    // ── 内部实现 ──────────────────────────────────
    private void SendCmd(string json)
    {
        if (_stream == null || !_tcp.Connected) return;
        byte[] payload = Encoding.UTF8.GetBytes(json);
        byte[] header = new byte[4];
        header[0] = (byte)(payload.Length >> 24);
        header[1] = (byte)(payload.Length >> 16);
        header[2] = (byte)(payload.Length >> 8);
        header[3] = (byte)(payload.Length);
        _stream.Write(header, 0, 4);
        _stream.Write(payload, 0, payload.Length);
    }

    private void ReceiveLoop()
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                byte[] lenBuf = new byte[4];
                int read = 0;
                while (read < 4) read += _stream.Read(lenBuf, read, 4 - read);
                int len = (lenBuf[0] << 24) | (lenBuf[1] << 16) | (lenBuf[2] << 8) | lenBuf[3];
                byte[] data = new byte[len];
                read = 0;
                while (read < len) read += _stream.Read(data, read, len - read);
                string json = Encoding.UTF8.GetString(data);
                HandleMessage(json);
            }
        }
        catch (Exception) { /* 断线 */ }
    }

    private void HandleMessage(string json)
    {
        // 简易 JSON 解析 (生产环境建议用 JsonUtility 或 Newtonsoft)
        if (json.Contains("\"playback_complete\""))
            _mainThreadQueue.Enqueue(() => OnPlaybackComplete?.Invoke());
        if (json.Contains("\"safety_triggered\""))
            _mainThreadQueue.Enqueue(() => OnSafetyEvent?.Invoke(json));
    }

    void OnDestroy()
    {
        _cts?.Cancel();
        _stream?.Close();
        _tcp?.Close();
    }
}
```

**Unity 业务代码示例：**

```csharp
// 完整的 4D 影院控制只需几行
public class CinemaController : MonoBehaviour
{
    public MotionClient motion;       // Inspector 拖入
    public VideoPlayer videoPlayer;   // Unity VideoPlayer

    void Start()
    {
        motion.OnPlaybackComplete += () => Debug.Log("动作播放完成");
        motion.LoadFile("侏罗纪冒险.motion");
    }

    public void OnPlayButton()
    {
        videoPlayer.Play();
        motion.Play();
    }

    public void OnPauseButton()
    {
        videoPlayer.Pause();
        motion.Pause();
    }
}
```

### 5.6 UE 客户端 SDK (纯 C++, 引擎内置 FSocket)

```cpp
// MotionClient.h — UE Actor, 使用引擎内建 FSocket, 无外部依赖
#pragma once
#include "CoreMinimal.h"
#include "Sockets.h"
#include "SocketSubsystem.h"
#include "GameFramework/Actor.h"
#include "MotionClient.generated.h"

DECLARE_DYNAMIC_MULTICAST_DELEGATE(FOnPlaybackComplete);
DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FOnSafetyEvent, FString, Message);

UCLASS()
class AMotionClient : public AActor
{
    GENERATED_BODY()

    FSocket* Socket = nullptr;

public:
    UPROPERTY(EditAnywhere) FString ServerIP = TEXT("127.0.0.1");
    UPROPERTY(EditAnywhere) int32 ServerPort = 9600;
    UPROPERTY(BlueprintAssignable) FOnPlaybackComplete OnPlaybackComplete;
    UPROPERTY(BlueprintAssignable) FOnSafetyEvent OnSafetyEvent;

    void BeginPlay() override
    {
        Super::BeginPlay();
        auto Subsystem = ISocketSubsystem::Get(PLATFORM_SOCKETSUBSYSTEM);
        Socket = Subsystem->CreateSocket(NAME_Stream, TEXT("MotionClient"), false);
        auto Addr = Subsystem->CreateInternetAddr();
        bool bValid; Addr->SetIp(*ServerIP, bValid); Addr->SetPort(ServerPort);
        Socket->Connect(*Addr);
    }

    UFUNCTION(BlueprintCallable) void LoadFile(FString Path)
        { SendCmd(FString::Printf(TEXT("{\"cmd\":\"load\",\"file\":\"%s\"}"), *Path)); }
    UFUNCTION(BlueprintCallable) void Play()  { SendCmd(TEXT("{\"cmd\":\"play\"}")); }
    UFUNCTION(BlueprintCallable) void Pause() { SendCmd(TEXT("{\"cmd\":\"pause\"}")); }
    UFUNCTION(BlueprintCallable) void Stop()  { SendCmd(TEXT("{\"cmd\":\"stop\"}")); }
    UFUNCTION(BlueprintCallable) void Seek(float Ms)
        { SendCmd(FString::Printf(TEXT("{\"cmd\":\"seek\",\"time_ms\":%.1f}"), Ms)); }

    void EndPlay(const EEndPlayReason::Type R) override
    {
        if (Socket) { Socket->Close(); ISocketSubsystem::Get()->DestroySocket(Socket); }
        Super::EndPlay(R);
    }

private:
    void SendCmd(const FString& Json)
    {
        if (!Socket) return;
        auto Utf8 = StringCast<ANSICHAR>(*Json);
        uint32 Len = Utf8.Length();
        uint8 Header[4];
        Header[0] = (Len >> 24) & 0xFF; Header[1] = (Len >> 16) & 0xFF;
        Header[2] = (Len >> 8)  & 0xFF; Header[3] = Len & 0xFF;
        int32 Sent;
        Socket->Send(Header, 4, Sent);
        Socket->Send((const uint8*)Utf8.Get(), Len, Sent);
    }
};
```

**UE 使用：** 拖入关卡 → 设置 IP → 蓝图调用 Play/Stop/Seek → 绑定 OnPlaybackComplete 委托。

---

### 5.7 各端集成清单

| 目标                | 需要部署                                   | 集成文件                          | 代码量  | 路径       |
| ------------------- | ------------------------------------------ | --------------------------------- | ------- | ---------- |
| **Unity（嵌入式）** | 无需 IOStudio，IODevice DLL 直接打包进游戏 | `IODevice_CSharp_Wrapper.dll`     | ~5 行   | **路径 A** |
| **UE（嵌入式）**    | 无需 IOStudio，IODevice DLL 直接链接       | `IODevice.lib/.dll`               | ~5 行   | **路径 A** |
| **Unity（TCP）**    | IOStudio 在控制主机运行                    | `MotionClient.cs` (1 个文件)      | ~120 行 | **路径 B** |
| **UE（TCP）**       | IOStudio 在控制主机运行                    | `MotionClient.h/cpp` (1-2 个文件) | ~80 行  | **路径 B** |
| **Python**          | IOStudio 在控制主机运行                    | 标准 socket 库                    | ~50 行  | **路径 B** |
| **Web/浏览器**      | 需加 WebSocket 网关 (或直接用 TCP→WS 桥)   | 原生 JS                           | ~40 行  | **路径 B** |
| **IOStudio 自身**   | 已内建                                     | `Services/Motion/*`               | 引擎层  | 内部       |

---

## 六、关键技术难点

### 6.1 高精度时间同步

```
MotionPlaybackEngine (IOStudio 内, Services/Motion/)
  ├── 使用 Stopwatch 精确计时 (QueryPerformanceCounter 级)
  ├── 预读缓冲: 提前 N 帧计算并缓存输出值
  ├── 延迟补偿: 每设备可配 latencyOffset (ms)
  │     → 实际发送时间 = 目标时间 - latencyOffset
  └── 帧率: 内部 60fps Tick → InterpolationEngine → SafetyGuard → DeviceDispatcher

MotionServerService (TCP 播控)
  └── 50ms 间隔广播 state 消息 (20Hz, 与 AnalogValueProcessor 一致)
      → 客户端用于 UI 同步 (如进度条), 不用于精确时序控制
```

### 6.2 曲线编辑器 (SkiaSharp)

- 贝塞尔曲线编辑 (拖拽控制点)
- 关键帧菱形/圆形标记
- 缩放/平移 (滚轮 + 中键拖拽)
- 框选/多选关键帧
- 网格吸附

### 6.3 安全保护

```
SafetyGuard (Services/Motion/SafetyGuard.cs)
  ├── 限位保护: 值不超过 IODevice.xml Key 的 Min/Max (读取 IORoot)
  ├── 速度限制: 相邻帧值变化 ≤ maxVelocity × dt
  ├── 急停: 异常时平滑归零
  ├── 回中保护: Stop 命令 → Stopping 状态 → 平滑回初始位置 → Idle
  └── 通信超时: MotionServer 30秒无心跳 → 自动急停
```

---

## 七、阶段性工作拆分

> **v9 实施进展追踪 (2026-04-13 代码级审查)**
>
> | Phase               | 状态      | 说明                                                                                                               |
> | ------------------- | --------- | ------------------------------------------------------------------------------------------------------------------ |
> | Phase 1             | ✅ 已完成 | 13 个数据模型 + PlaybackEngine + Interpolation + SafetyGuard + DeviceDispatcher + MotionFileReader + ExportService |
> | Phase 2             | ✅ 已完成 | 16 个 ViewModel + 10 个自定义控件 + 4 个对话框/窗口 + MotionProjectService                                         |
> | Phase 3             | 🔧 90%    | 预览播放 ✅ / .motion 导出 ✅ / ⚠️ .mtn AES-256-GCM 加密未实现 (仅 stub)                                           |
> | Phase 4             | ✅ 已完成 | CurveEditorControl + CurveEditorViewModel + 10 种内置曲线预设                                                      |
> | Phase 4.5-4.15      | ✅ 已完成 | UndoRedoService + KeyboardShortcutService + MarkerService + 架构改进                                               |
> | Phase 5             | ✅ 已完成 | LibVlcVideoPlayerService + AudioWaveformExtractor + BeatDetector + MediaSyncService                                |
> | Phase 6             | ⏸️ 未开始 | 效果预设系统 — CurvePresetService 已有，但整体效果模板库未建                                                       |
> | Phase 1b (TCP)      | ⏸️ 未开始 | MotionServerService / MotionCommandHandler 零代码                                                                  |
> | Phase 0→7 (C++)     | ⏸️ 未开始 | IODevice 项目无 Motion 相关代码                                                                                    |
> | Phase 7→9 (LicHper) | ⏸️ 未开始 | 无 LicHper 相关文件                                                                                                |
> | Phase 8→10 (SDK)    | ⏸️ 未开始 | 依赖 TCP MotionServer                                                                                              |

### Phase 1: 播放引擎 + MotionServer 网络协议 (2.5 周) ✅ 已完成

> **目标：** 在 IOStudio 中实现 `Services/Motion/` 播放引擎 + TCP 播控服务，Unity/UE 通过网络即可控制播放

| #    | 任务                                                | 产出                    |
| ---- | --------------------------------------------------- | ----------------------- |
| 1.1  | 定义 .motion 文件格式 (JSON Schema) + C# 数据模型   | Models/Motion/\*.cs     |
| 1.2  | 实现 MotionFileReader (.motion JSON 解析)           | MotionFileReader.cs     |
| 1.3  | 实现 InterpolationEngine (Linear/Bezier/Step/Ease)  | InterpolationEngine.cs  |
| 1.4  | 实现 MotionPlaybackEngine (状态机 + Tick 驱动)      | MotionPlaybackEngine.cs |
| 1.5  | 实现 DeviceDispatcher (归一化值 → IODevice SetDO)   | DeviceDispatcher.cs     |
| 1.6  | 实现 SafetyGuard (限位 + 速度限制 + 回中)           | SafetyGuard.cs          |
| 1.7  | 实现 MotionProtocol (4字节长度前缀帧 + JSON 编解码) | MotionProtocol.cs       |
| 1.8  | 实现 MotionServerService (TcpListener, 端口 9600)   | TCP Server              |
| 1.9  | 实现 MotionCommandHandler (JSON 命令分发)           | 协议完整实现            |
| 1.10 | 实现 StatePublishLoop (50ms 状态广播)               | 状态推送                |
| 1.11 | IOStudio 设置页增加 MotionServer 端口配置 + 开关    | Settings UI             |
| 1.12 | 编写 TCP 测试客户端 (Python 脚本, ~50 行)           | 验证协议可用            |

**里程碑：** IOStudio 启动 → TCP 端口 9600 监听 → 发送 `{"cmd":"play"}` 即可驱动座椅运动。

---

### Phase 2: 时间轴基础 UI (2.5 周) ✅ 已完成

> **目标：** IOStudio 时间轴编辑窗口，完整的轨道和关键帧编辑能力
>
> _(Unity/UE 客户端 SDK 已移至 Phase 8，基础功能完善后再实施)_

| #   | 任务                                               | 产出             |
| --- | -------------------------------------------------- | ---------------- |
| 2.1 | MainWindowViewModel 增加 OpenTimelineEditorCommand | 菜单入口         |
| 2.2 | TimelineEditorWindow 独立窗口骨架 (Dock布局)       | 窗口可打开       |
| 2.3 | 设备/通道选择器 (从 IORoot.Instance.Devices 读取)  | 添加轨道 UI      |
| 2.4 | TimeRulerControl 时间标尺 (刻度/缩放/拖拽)         | SkiaSharp 控件   |
| 2.5 | TrackPanel 轨道面板 (添加/删除/颜色/静音/锁定)     | TrackPanel.axaml |
| 2.6 | ClipView 片段 (创建/拖拽/缩放/删除)                | ClipControl      |
| 2.7 | 关键帧添加/删除/移动                               | 基础编辑能力     |
| 2.8 | MotionProjectService 项目管理 (新建/打开/保存)     | 项目文件 CRUD    |

**里程碑：** IOStudio 中可打开时间轴编辑器，添加轨道和关键帧。Python 测试脚本 (Phase 1 产出) 可验证编辑→播放链路。

---

### Phase 3: 播放预览 + 导出 + 加密 (2 周) 🔧 90%

> **目标：** 编辑器内预览 + 导出 .motion/.mtn 文件 + 加密发行

| #   | 任务                                                     | 产出                 |
| --- | -------------------------------------------------------- | -------------------- |
| 3.1 | TimelineEditorViewModel 集成 MotionPlaybackEngine 做预览 | 预览播放             |
| 3.2 | 播放控制 UI (播放/暂停/停止/循环/速度)                   | 工具栏按钮           |
| 3.3 | 时间轴光标跟随播放位置                                   | 光标动画             |
| 3.4 | 导出/保存 .motion 文件 (明文, 内部使用)                  | MotionExportService  |
| 3.5 | MotionCryptoService (AES-256-GCM 加解密)                 | .mtn 加密引擎        |
| 3.6 | "导出发行版" → 生成 .mtn 加密文件                        | 加密导出流程         |
| 3.7 | MotionFileReader 支持 .motion + .mtn 双格式加载          | 双格式兼容           |
| 3.8 | 播放时设备监控同步 (主窗口 DO 值同步变化)                | 联动效果             |
| 3.9 | MotionServer 自动通知客户端文件变更                      | file_loaded 事件推送 |

**里程碑：** 编辑 → 预览 → 导出 .motion (内部) / .mtn (买家) → TCP 触发播放。加密链路打通。

---

### Phase 4: 曲线编辑器 (2 周) ✅ 已完成

> **目标：** 精细的关键帧曲线编辑

| #   | 任务                                        | 产出                  |
| --- | ------------------------------------------- | --------------------- |
| 4.1 | CurveEditor 曲线渲染 (SkiaSharp)            | CurveEditorControl.cs |
| 4.2 | 关键帧拖拽 (值 + 贝塞尔控制手柄)            | 交互逻辑              |
| 4.3 | 多选/框选/批量编辑                          | CurveEditorViewModel  |
| 4.4 | 缩放/平移/网格吸附                          | 交互优化              |
| 4.5 | 插值类型切换 (Linear/Bezier/Step/EaseInOut) | UI 交互               |

---

### Phase 5: 媒体同步 (2 周) ✅ 已完成

| #   | 任务                         | 产出                    |
| --- | ---------------------------- | ----------------------- |
| 5.1 | 集成视频播放器 (LibVLCSharp) | MediaPlayer.axaml       |
| 5.2 | 音频波形提取与渲染           | WaveformControl.cs      |
| 5.3 | 播放时钟同步 (视频 ↔ 时间轴) | MediaSyncService.cs     |
| 5.4 | 音频节拍检测 (辅助对齐)      | AudioAnalysisService.cs |

---

### Phase 6: 效果预设 (2 周) ⏸️ 未开始

| #   | 任务                                 | 产出                        |
| --- | ------------------------------------ | --------------------------- |
| 6.1 | 效果预设数据模型与存储               | EffectPreset 模型           |
| 6.2 | MotionServer 增加预设相关协议命令    | list_presets / apply_preset |
| 6.3 | 预设库面板 UI (分类/搜索/拖拽到轨道) | PresetLibrary.axaml         |
| 6.4 | 内置预设库 (运动/灯光/风/烟雾)       | Config/Presets/             |

---

### Phase 7: 成果保护 + 高级功能 + 打磨 (2.5 周) ⏸️ 未开始

| #    | 任务                                                                |
| ---- | ------------------------------------------------------------------- |
| 7.1  | IOStudio.csproj 双构建配置 (Release-Editor / Release-Player)        |
| 7.2  | Player 模式条件编译 (#if PLAYER_MODE) 排除编辑器代码                |
| 7.3  | PlayerControlPanel 播放器控制面板 (Player 专属 UI)                  |
| 7.4  | PlayerControlViewModel 播放控制逻辑                                 |
| 7.5  | release_motion_player.bat 构建脚本                                  |
| 7.6  | .NET IL 混淆配置 (Obfuscar)                                         |
| 7.7  | Undo/Redo                                                           |
| 7.8  | 自动保存 + 崩溃恢复                                                 |
| 7.9  | SafetyGuard 增强 (速度/加速度限制/通信超时)                         |
| 7.10 | 键盘快捷键体系                                                      |
| 7.11 | 从 OutputTestViewModel 一键录制为时间轴轨道 (输出测试 → 时间轴联动) |
| 7.12 | UI 美化 / 性能优化                                                  |
| 7.13 | 用户手册                                                            |

---

### Phase 8: Unity/UE 客户端 SDK (1.5 周) ⏸️ 未开始

> **目标：** 基础功能完善后，提供 Unity/UE 客户端集成
> **前置：** Phase 1~3 完成 (TCP 协议稳定)

| #   | 任务                                              | 产出                      |
| --- | ------------------------------------------------- | ------------------------- |
| 8.1 | Unity 客户端 SDK (纯 C# TcpClient, 无 native DLL) | MotionClient.cs (~120 行) |
| 8.2 | Unity MainThreadDispatcher.cs (线程回调)          | MainThreadDispatcher.cs   |
| 8.3 | Unity Demo 场景 (视频播放 + 动感联动)             | Unity 集成验证            |
| 8.4 | UE 客户端 SDK (纯 C++ FSocket, 引擎内置)          | AMotionClient (~80 行)    |
| 8.5 | UE Demo 关卡 (蓝图触发 + 动感联动)                | UE 集成验证               |
| 8.6 | SDK README (集成指南) + MotionServer API 文档     | SDK/README.md             |

**里程碑：** Unity/UE 通过 TCP 连接 IOStudio/MotionPlayer 控制播放。SDK 可独立交付。

---

## 八、工期总结

> **v9 进度追踪 (2026-04-13)**：Phase 1-5 + Phase 4.5-4.15 已全部代码验证通过，Phase 3 仅 .mtn 加密为 stub。
> 编辑器核心完成度 ~95%，剩余工作集中在：.mtn 加密 → TCP MotionServer → C++ MotionPlayer → LicHper → Unity/UE SDK。

| 阶段     | 内容                                                  |                   预估工期 | 状态                           |
| -------- | ----------------------------------------------------- | -------------------------: | ------------------------------ |
| Phase 0  | IODevice C++ MotionPlayer（C++/C Wrapper/C# Wrapper） |                     1.5 周 | ⏸️ 未开始                      |
| Phase 1  | 播放引擎 + MotionServer TCP 协议                      |                     2.5 周 | ✅ 已完成 (引擎部分，TCP 拆出) |
| Phase 2  | 时间轴基础 UI                                         |                     2.5 周 | ✅ 已完成                      |
| Phase 3  | 播放预览 + 导出 + 加密                                |                       2 周 | 🔧 90% (.mtn 加密待实现)       |
| Phase 4  | 曲线编辑器                                            |                       2 周 | ✅ 已完成                      |
| Phase 5  | 媒体同步                                              |                       2 周 | ✅ 已完成                      |
| Phase 6  | 效果预设                                              |                       2 周 | ⏸️ 未开始                      |
| Phase 7  | LicHper 授权集成 + 高级功能 + 打磨                    |                     2.5 周 | ⏸️ 未开始                      |
| Phase 8  | Unity/UE 客户端 SDK（TCP Demo + 综合文档）            |                     1.5 周 | ⏸️ 未开始                      |
| **总计** |                                                       | **~18.5 周 (约 4.5 个月)** | **~11 周已完成**               |

> **关键路径：** Phase 1 完成后 (2.5 周)，Python 测试脚本即可通过 TCP 连接 IOStudio 的 9600 端口验证播放。
>
> Phase 1→2 完成后 (5 周)，编辑器全部可用。Phase 1→3 完成后 (7 周)，加密 .mtn 文件可交付。
>
> Phase 7 完成后，IOStudio (单一二进制) + LicHper.dll + .mtn 加密文件即可交付买家 (Basic 模式免费播放)。内部通过 AuthAssistant 颁发 Pro 授权。
>
> **Unity/UE SDK (Phase 8)：** 基础功能完善后实施，仅需 TCP 协议稳定 (Phase 1~3 前置)。
>
> **嵌入式 C++ SDK**: 列入后续需求驱动计划，不在主线阶段中。当出现独立 Kiosk / 无 IOStudio 环境需求时启动 (预估 ~2.5 周)。

---

## 九、技术选型

| 需求              | 方案                                                                 | 说明                                                           |
| ----------------- | -------------------------------------------------------------------- | -------------------------------------------------------------- |
| 编辑器 UI         | **Avalonia UI 11**                                                   | IOStudio 已有                                                  |
| MVVM              | **ReactiveUI**                                                       | IOStudio 已有                                                  |
| 播放引擎          | **C# (.NET 6.0)**                                                    | `IOStudio.Services.Motion` 命名空间, 直接调用 IODevice         |
| 网络协议          | **TCP + 4字节长度前缀 + JSON**                                       | `System.Net.Sockets` 内建, **零第三方依赖**                    |
| 协议端口          | **9600** (默认, 可配置)                                              | `<IOStudio-IP>:9600`, 与 ConnectionManager TCP Server 模式一致 |
| Unity 客户端      | **纯 C# TcpClient**                                                  | .NET Standard 内建, 无 native DLL, IL2CPP 100% 兼容            |
| UE 客户端         | **FSocket (引擎内置)**                                               | 无外部依赖, 全平台支持                                         |
| 嵌入式 SDK (后续) | **C++ → C ABI → C# P/Invoke**                                        | 需求驱动, 不在当前主线                                         |
| 视频播放          | **LibVLCSharp**                                                      | 跨平台, 格式广                                                 |
| 曲线/波形渲染     | **SkiaSharp**                                                        | Avalonia 原生支持                                              |
| 高精度计时        | **Stopwatch**                                                        | IOStudio 内部 Tick 驱动                                        |
| 动作文件          | **JSON** (开发期 .motion) / **AES-256-GCM 加密** (发行版 .mtn)       | .motion 仅内部使用; .mtn 交付买家 (详见 § 十)                  |
| 授权系统          | **LicHper** (C++ native DLL) + **AuthAssistant** (Avalonia 管理工具) | AES-128-CBC 加密, BIOS UUID 机器绑定, 按 AppID 授权            |
| 成果保护          | **LicHper 授权门控 (Basic/Pro)** + **AES-256-GCM** + **IL 混淆**     | 三层保护, 详见 § 十                                            |
| 日志              | **DNHper.Logger (NLog)**                                             | 已有                                                           |

---

## 十、成果保护与授权架构

### 10.1 保护目标与策略总览

**业务需求：** 动感平台编辑系统的编排能力仅供授权用户使用，交付给买家的产品为同一个 IOStudio 二进制：

1. **IOStudio Basic (免费)** — 设备配置、通道监控、事件转发、加载/播放 .mtn 文件、MotionServer TCP 接口
2. **IOStudio Pro (授权)** — Basic 全部功能 + 时间轴编辑、曲线编辑、媒体同步、效果预设、导出 .mtn
3. **加密动作文件 (.mtn)** — 买家无法查看、编辑或逆向还原关键帧数据

**保护层次：**

```
┌─────────────────────────────────────────────────────────────────────────┐
│                      三层保护体系 (v6)                                   │
│                                                                         │
│  Layer 1: LicHper 运行时授权门控 (Runtime License Gating)               │
│    ├── 单一二进制发布: IOStudio.exe (同时包含 Basic + Pro 功能)          │
│    ├── 启动时调用 LicHper.Validate("IOStudio.Pro") 检测授权             │
│    ├── 返回 0 = Pro 模式 (全功能); 返回 10002 = Basic 模式 (免费)       │
│    ├── LicenseService 统一管理, UI/Service 层通过 HasFeature() 门控     │
│    └── 机器绑定 (BIOS UUID) + 时间校验 (防回拨) → 无法拷贝 license     │
│                                                                         │
│  Layer 2: 动作文件加密 (.mtn)                                           │
│    ├── AES-256-GCM 加密 + HMAC 签名                                    │
│    ├── Pro 模式导出 .mtn (加密) 给买家, .motion (JSON) 仅内部使用       │
│    └── Basic 模式仅可加载 .mtn, 无法读取 .motion                        │
│                                                                         │
│  Layer 3: 分发加固                                                      │
│    ├── .NET IL 混淆 (Obfuscar / ConfuserEx)                            │
│    ├── 代码签名 (Authenticode)                                          │
│    └── LicHper.dll 自带 DXGI 水印 (未授权时叠加半透明水印)              │
└─────────────────────────────────────────────────────────────────────────┘
```

### 10.2 产品分级策略 (Basic / Pro)

#### 10.2.1 单一二进制 + 运行时授权

**废弃旧方案：** 不再使用 `PLAYER_MODE` 条件编译和 `Release-Editor / Release-Player` 双构建配置。

**新方案：** 所有功能编译进同一个 `IOStudio.exe`，通过 `LicenseService` 在运行时根据授权状态启用/禁用功能模块：

```csharp
// Services/LicenseService.cs — 授权门控服务
public class LicenseService : ILicenseService
{
    private const string PRO_APP_ID = "IOStudio.Pro";
    private bool _isProLicensed = false;

    /// <summary>
    /// 应用启动时调用, 检测 Pro 授权状态
    /// uiFlag=1 为静默检测 (不显示 DXGI 水印)
    /// </summary>
    public void Initialize()
    {
        int result = LicHperInterface.Validate(PRO_APP_ID, 1);
        _isProLicensed = (result == 0);
        Log.Info($"License check: Pro={_isProLicensed} (code={result})");
    }

    /// <summary>是否为 Pro 授权</summary>
    public bool IsPro => _isProLicensed;

    /// <summary>检查指定功能是否可用</summary>
    public bool HasFeature(string feature) => feature switch
    {
        "TimelineEditor"    => _isProLicensed,
        "CurveEditor"       => _isProLicensed,
        "MediaSync"         => _isProLicensed,
        "EffectPresets"     => _isProLicensed,
        "ExportMtn"         => _isProLicensed,
        "ExportMotion"      => _isProLicensed,
        "BatchRangeMapping" => _isProLicensed,
        "OutputRecording"   => _isProLicensed,
        _                   => true  // 未注册的功能默认可用 (Basic)
    };

    /// <summary>尝试激活 Pro 授权 (导入 license 文件后调用)</summary>
    public bool TryActivatePro()
    {
        int result = LicHperInterface.Validate(PRO_APP_ID, 1);
        _isProLicensed = (result == 0);
        return _isProLicensed;
    }
}
```

#### 10.2.2 LicHper P/Invoke 接口

复用 DaemonApps/AuthAssistant 中已验证的 P/Invoke 封装：

```csharp
// Services/LicHperInterface.cs — LicHper.dll P/Invoke 封装
using System.Runtime.InteropServices;

public static class LicHperInterface
{
    private const string DLL = "LicHper.dll";

    /// <summary>使用 license token 登录</summary>
    [DllImport(DLL)] public static extern IntPtr Login(
        [MarshalAs(UnmanagedType.BStr)] string licenseToken);

    /// <summary>注销当前 license</summary>
    [DllImport(DLL)] public static extern int Logout();

    /// <summary>获取当前 license 信息 (JSON)</summary>
    [DllImport(DLL)] public static extern IntPtr GetLicense();

    /// <summary>
    /// 验证指定 AppID 的授权
    /// 返回: 0=授权有效, 10001=机器不匹配, 10002=未授权/已过期
    /// uiFlag: 0=显示DXGI水印(失败时), 1=静默检测
    /// </summary>
    [DllImport(DLL)] public static extern int Validate(
        [MarshalAs(UnmanagedType.BStr)] string appId,
        int uiFlag = 0);

    /// <summary>续期指定 AppID</summary>
    [DllImport(DLL)] public static extern int Renew(
        [MarshalAs(UnmanagedType.BStr)] string appId,
        [MarshalAs(UnmanagedType.BStr)] string expiredAt);

    /// <summary>退订指定 AppID</summary>
    [DllImport(DLL)] public static extern int Unsubscribe(
        [MarshalAs(UnmanagedType.BStr)] string appId);

    /// <summary>清除本地 license 缓存</summary>
    [DllImport(DLL)] public static extern int ClearLicense();
}
```

#### 10.2.3 LicHper 授权系统架构

```
┌─────────────────────────────────────────────────────────────────────────┐
│                      LicHper 授权系统 (已有, 复用)                       │
│                                                                         │
│  LicHper.dll (C++ native)                                               │
│    ├── AES-128-CBC 加密 (Key="mrbaoquan1231231", IV=Key)               │
│    ├── BIOS UUID 机器绑定 → license 不可跨机器拷贝                      │
│    ├── 时间回拨检测 (last_verified_at > system_time → 拒绝)             │
│    ├── 每个 AppID 独立授权, 支持 SuperAdmin (AppID="*")                 │
│    ├── License 存储: %USERPROFILE%\.authrc (AES 加密 JSON)              │
│    └── 失败时 DXGI overlay 半透明水印 (uiFlag=0)                        │
│                                                                         │
│  AuthAssistant (Avalonia UI, 内部管理工具)                               │
│    ├── SuperAdmin 登录 → 管理所有 license                               │
│    ├── 生成 license token → AES 加密的 Base64 字符串                    │
│    ├── 导出 .lic 文件 → 可邮件/U盘 发给用户                            │
│    ├── 设置授权期限 (expiredAt) + 用户信息 (username, phone)            │
│    └── 支持按 AppID 独立授权 (IOStudio.Pro)                             │
│                                                                         │
│  IOStudio 集成:                                                         │
│    ├── 启动时: LicHperInterface.Validate("IOStudio.Pro", 1)             │
│    │   ├── 返回 0 → _isProLicensed = true → Pro 模式                   │
│    │   └── 返回 10002 → _isProLicensed = false → Basic 模式            │
│    ├── UI 门控: CanExecute 绑定 LicenseService.HasFeature()             │
│    ├── 激活入口: 设置面板 → "导入授权" → 粘贴 token → Login()           │
│    └── LicHper.dll 随 IOStudio.exe 一起分发                             │
└─────────────────────────────────────────────────────────────────────────┘
```

#### 10.2.4 授权工作流

```
内部运维 (AuthAssistant)                      买家 (IOStudio)
  │                                              │
  │ 1. SuperAdmin 登录                           │
  │ 2. 创建 license:                             │
  │    AppID = "IOStudio.Pro"                     │
  │    username = "XXX影院"                       │
  │    expiredAt = "2027-03-04"                   │
  │    phone = "138xxxx"                          │
  │ 3. 导出 license token / .lic 文件            │
  │                                              │
  │─── 通过邮件/U盘发送 ──────────────────────→ │
  │                                              │ 4. IOStudio 设置面板 → "导入授权"
  │                                              │ 5. 粘贴 token → LicHperInterface.Login()
  │                                              │ 6. 重启 / TryActivatePro() → Pro 模式解锁
  │                                              │
  │ 7. [可选] Renew() 续期                       │
  │ 8. [可选] Unsubscribe() 收回授权             │
```

#### 10.2.5 功能分级矩阵

| 功能模块                    | Basic (免费) | Pro (授权) | 门控方式                             |
| --------------------------- | :----------: | :--------: | ------------------------------------ |
| **设备监控 (DI/AD/DO)**     |      ✅      |     ✅     | 无门控                               |
| **设备配置**                |      ✅      |     ✅     | 无门控                               |
| **输出测试 (单通道)**       |      ✅      |     ✅     | 无门控                               |
| **事件转发**                |      ✅      |     ✅     | 无门控                               |
| **MotionServer (TCP 播控)** |      ✅      |     ✅     | 无门控                               |
| **播放引擎 (加载/播放)**    |      ✅      |     ✅     | 无门控                               |
| **加载 .mtn (加密文件)**    |      ✅      |     ✅     | 无门控                               |
| **连接管理 (UDP/TCP/串口)** |      ✅      |     ✅     | 无门控                               |
| **外部设备扫描**            |      ✅      |     ✅     | 无门控                               |
| **源设备配置**              |      ✅      |     ✅     | 无门控                               |
| **批量范围映射**            |      ❌      |     ✅     | 🔒 `HasFeature("BatchRangeMapping")` |
| **时间轴编辑器**            |      ❌      |     ✅     | 🔒 `HasFeature("TimelineEditor")`    |
| **曲线编辑器**              |      ❌      |     ✅     | 🔒 `HasFeature("CurveEditor")`       |
| **媒体同步**                |      ❌      |     ✅     | 🔒 `HasFeature("MediaSync")`         |
| **效果预设编辑**            |      ❌      |     ✅     | 🔒 `HasFeature("EffectPresets")`     |
| **导出 .motion (JSON)**     |      ❌      |     ✅     | 🔒 `HasFeature("ExportMotion")`      |
| **导出 .mtn (加密)**        |      ❌      |     ✅     | 🔒 `HasFeature("ExportMtn")`         |
| **加载 .motion (JSON)**     |      ❌      |     ✅     | 🔒 `HasFeature("ExportMotion")`      |
| **OutputTest 一键录制**     |      ❌      |     ✅     | 🔒 `HasFeature("OutputRecording")`   |

#### 10.2.6 UI 门控实现

编辑器入口通过 `LicenseService.HasFeature()` 控制 CanExecute 和可见性：

```csharp
// MainWindowViewModel.cs — Pro 功能入口门控
public MainWindowViewModel(ILicenseService licenseService)
{
    _licenseService = licenseService;

    // 时间轴编辑器入口 — 仅 Pro 可用
    OpenTimelineEditorCommand = ReactiveCommand.CreateFromTask(
        async () =>
        {
            var window = new TimelineEditorWindow();
            window.Initialize(IORoot.Instance.Devices);
            window.Show();
        },
        this.WhenAnyValue(x => x.IsProLicensed) // CanExecute 绑定授权状态
    );

    // 导出 .mtn 入口 — 仅 Pro 可用
    ExportMtnCommand = ReactiveCommand.CreateFromTask(
        async () => { /* 导出逻辑 */ },
        this.WhenAnyValue(x => x.IsProLicensed)
    );

    // Basic 模式下显示升级提示
    ShowUpgradePromptCommand = ReactiveCommand.Create(() =>
    {
        // 弹出对话框: "此功能需要 Pro 授权, 请联系管理员获取 license"
    });
}

[Reactive] public bool IsProLicensed { get; set; }
```

```xml
<!-- MainWindow.axaml — Pro 功能菜单项带锁标识 -->
<MenuItem Header="时间轴编辑器" Command="{Binding OpenTimelineEditorCommand}">
    <MenuItem.Icon>
        <Panel>
            <PathIcon Data="{StaticResource TimelineIcon}" />
            <!-- Basic 模式下显示锁图标 -->
            <PathIcon Data="{StaticResource LockIcon}"
                      IsVisible="{Binding !IsProLicensed}"
                      HorizontalAlignment="Right" VerticalAlignment="Bottom" />
        </Panel>
    </MenuItem.Icon>
</MenuItem>
```

#### 10.2.7 授权激活 UI

IOStudio 设置面板新增"授权管理"页签：

```
┌──────────────────────────────────────────────────────────────────┐
│  IOStudio — 设置 — 授权管理                                      │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌─ 当前授权状态 ──────────────────────────────────────────────┐│
│  │  版本: IOStudio Basic (免费版)                               ││
│  │  状态: 🟡 未授权                                             ││
│  │  功能: 设备管理 + 播放器 (编辑功能已锁定)                     ││
│  └──────────────────────────────────────────────────────────────┘│
│                                                                  │
│  ┌─ 激活 Pro 授权 ─────────────────────────────────────────────┐│
│  │  授权码:  [______________________________________] [粘贴]   ││
│  │                                                              ││
│  │  [▶ 激活授权]                [📄 导入 .lic 文件]              ││
│  └──────────────────────────────────────────────────────────────┘│
│                                                                  │
│  ── 或已授权时 ──                                                │
│  ┌─ 当前授权状态 ──────────────────────────────────────────────┐│
│  │  版本: IOStudio Pro                                          ││
│  │  状态: 🟢 已授权                                             ││
│  │  用户: XXX影院                                               ││
│  │  有效期: 2027-03-04                                          ││
│  │  功能: 全部功能已解锁                                        ││
│  │                                                              ││
│  │  [🔄 刷新状态]    [❌ 注销授权]                               ││
│  └──────────────────────────────────────────────────────────────┘│
└──────────────────────────────────────────────────────────────────┘
```

### 10.3 加密动作文件格式 (.mtn)

#### 10.3.1 .mtn 文件结构

```
┌──────────────────────────────────────────────────────────────┐
│                    .mtn 文件格式 (v1)                        │
├──────┬──────┬──────────┬─────────┬────────────┬──────────────┤
│  4B  │  1B  │   16B    │  12B    │    NB      │     16B      │
│Magic │ Ver  │  Salt    │  IV     │ Ciphertext │  Auth Tag    │
│"MTN" │ 0x01 │(随机)    │(随机)   │(AES-GCM)  │ (GCM认证)    │
│+0x00 │      │          │         │            │              │
├──────┴──────┴──────────┴─────────┴────────────┴──────────────┤
│                                                              │
│  Magic:      "MTN\x00" (4 字节, 文件类型标识)                │
│  Version:    0x01 (文件格式版本)                              │
│  Salt:       16 字节随机盐 (每次导出不同)                     │
│  IV:         12 字节随机初始化向量 (GCM 标准)                 │
│  Ciphertext: AES-256-GCM 加密的 JSON 载荷 (UTF-8)           │
│  Auth Tag:   16 字节 GCM 认证标签 (防篡改)                   │
│                                                              │
│  密钥派生: Key = HMAC-SHA256(AppSecret, Salt)                │
│  AppSecret: 编译到二进制中的 32 字节密钥 (混淆存储)           │
│                                                              │
│  解密后的载荷 = 标准 .motion JSON 格式                       │
└──────────────────────────────────────────────────────────────┘
```

#### 10.3.2 加密/解密实现

```csharp
namespace IOStudio.Services.Motion
{
    /// <summary>
    /// .mtn 文件加解密服务。
    /// 使用 AES-256-GCM, 密钥由 AppSecret + 随机 Salt 派生。
    /// </summary>
    public static class MotionCryptoService
    {
        private static readonly byte[] Magic = { 0x4D, 0x54, 0x4E, 0x00 }; // "MTN\0"
        private const byte FormatVersion = 0x01;

        // AppSecret: 32 字节密钥, 编译时嵌入 (混淆存储)
        // 实际部署时应通过 C++ native 库或混淆工具保护此常量
        private static byte[] GetAppSecret()
        {
            // 分散存储 + 运行时组装, 增加逆向难度
            byte[] part1 = { /* 16 bytes */ };
            byte[] part2 = { /* 16 bytes */ };
            var key = new byte[32];
            Buffer.BlockCopy(part1, 0, key, 0, 16);
            Buffer.BlockCopy(part2, 0, key, 16, 16);
            return key;
        }

        /// <summary>
        /// 将 .motion JSON 加密为 .mtn 文件
        /// </summary>
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

            // 组装: Magic(4) + Ver(1) + Salt(16) + IV(12) + Cipher(N) + Tag(16)
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

        /// <summary>
        /// 解密 .mtn 文件为 .motion JSON
        /// </summary>
        public static string? Decrypt(byte[] mtnData)
        {
            if (mtnData.Length < 49) return null; // 最小: 4+1+16+12+0+16
            if (!mtnData[..4].SequenceEqual(Magic)) return null;
            if (mtnData[4] != FormatVersion) return null;

            byte[] salt = mtnData[5..21];
            byte[] iv = mtnData[21..33];
            byte[] tag = mtnData[^16..];
            byte[] ciphertext = mtnData[33..^16];
            byte[] key = DeriveKey(GetAppSecret(), salt);

            byte[] plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(key, 16);
            try
            {
                aes.Decrypt(iv, ciphertext, tag, plaintext);
                return Encoding.UTF8.GetString(plaintext);
            }
            catch (AuthenticationTagMismatchException)
            {
                return null; // 文件被篡改或密钥错误
            }
        }

        private static byte[] DeriveKey(byte[] secret, byte[] salt)
        {
            using var hmac = new HMACSHA256(secret);
            return hmac.ComputeHash(salt);
        }
    }
}
```

#### 10.3.3 MotionFileReader 支持双格式

```csharp
public class MotionFileReader
{
    private readonly ILicenseService _license;

    public MotionFileReader(ILicenseService license) => _license = license;

    public MotionTimeline? Load(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLower();
        string? json;

        switch (ext)
        {
            case ".motion":
                if (!_license.HasFeature("ExportMotion"))
                    return null; // Basic 模式不读明文文件
                json = File.ReadAllText(filePath);
                break;

            case ".mtn":
                byte[] encrypted = File.ReadAllBytes(filePath);
                json = MotionCryptoService.Decrypt(encrypted);
                if (json == null) return null; // 解密失败
                break;

            default:
                return null;
        }

        return JsonSerializer.Deserialize<MotionTimeline>(json);
    }
}
```

#### 10.3.4 导出加密文件的流程

```
IOStudio Pro (已授权)
  ├── "保存项目" → 保存为 .motion (JSON, 仅内部使用)
  └── "导出发行版" → 生成 .mtn (加密, 交付买家)
       ↓
MotionExportService.ExportEncrypted(timeline, path)
  ├── timeline → JSON 序列化 → string
  ├── MotionCryptoService.Encrypt(json) → byte[]
  └── File.WriteAllBytes(path.Replace(".motion", ".mtn"), encrypted)
       ↓
交付给买家: xxx.mtn + IOStudio.exe + LicHper.dll + IODevice.xml

IOStudio Basic (未授权 / 买家)
  └── MotionFileReader.Load("xxx.mtn")
       ├── 检测 .mtn 扩展名
       ├── MotionCryptoService.Decrypt(bytes) → json
       └── JsonDeserialize → MotionTimeline → 播放
```

### 10.4 密钥管理策略

| 层级                | 方案                                                           | 安全性 | 复杂度 |
| ------------------- | -------------------------------------------------------------- | ------ | ------ |
| **基础 (推荐起步)** | AppSecret 编译嵌入 + IL 混淆 + LicHper 机器绑定                | ★★★★   | 低     |
| **增强**            | AppSecret 存储在 C++ native DLL (IODevice_C_Wrapper) + LicHper | ★★★★★  | 中     |
| **高级**            | AppSecret in C++ + LicHper + 在线激活服务                      | ★★★★★  | 高     |

**推荐起步方案 (基础层)：**

- .mtn 的 AppSecret 以分散常量形式存储在 C# 代码中, 使用 Obfuscar 做 IL 混淆
- Pro 功能通过 LicHper 的 BIOS UUID 机器绑定 + AES 加密 license 保护
- AES-256-GCM 的认证特性确保 .mtn 文件不可篡改
- LicHper 自带的 DXGI 水印在 Validate 失败时叠加 (uiFlag=0)
- 对于 4D 影院这一垂直市场，此方案已提供足够的商业保护

**后续增强路径 (需求驱动)：**

- 如有批量盗版风险 → 升级到 C++ native 存储 .mtn 密钥 (利用已有 IODevice_C_Wrapper 体系)
- 如需在线激活 → LicHper 可扩展在线验证端点
- LicHper 已有安全特性 (时间回拨检测, 加密存储) 满足离线场景

### 10.5 分发包结构

```
IOStudio 发行版 (交付给所有用户, 单一包)
├── IOStudio.exe                  # 单一二进制 (Basic + Pro 功能都在)
├── LicHper.dll                   # 授权验证引擎 (C++ native)
├── IODevice.dll                  # C++ 核心
├── IODevice_C_Wrapper.dll        # C ABI 层
├── IODevice_CSharp_Wrapper.dll   # C# P/Invoke 层
├── *.dll                         # Avalonia + ReactiveUI 运行时
├── Config/
│   ├── IODevice.xml              # 设备配置 (用户根据硬件修改)
│   ├── IOStudioSettings.xml      # 应用设置
│   └── Motion/
│       ├── 侏罗纪冒险.mtn       # 加密动作文件 (买家场景)
│       ├── 海底世界.mtn
│       └── 太空漫游.mtn
└── Logs/

  未授权时: Basic 模式 — 设备管理 + 播放器 (编辑菜单显示🔒)
  导入 Pro license 后: Pro 模式 — 全部功能解锁
```

```
开发环境 (内部使用, 同一二进制 + Pro 授权)
├── IOStudio.exe                  # 同一二进制 (Pro 已授权)
├── LicHper.dll
├── (同上 DLL)
├── Config/
│   ├── IODevice.xml
│   ├── IOStudioSettings.xml
│   └── Motion/
│       ├── 侏罗纪冒险.motion    # 明文 JSON (Pro 可编辑)
│       ├── 侏罗纪冒险.mtn       # 加密版 (导出的)
│       ├── demo_chair_3dof.motion
│       └── Presets/
│           └── (效果预设)
├── SDK/                          # 客户端 SDK (开发用)
└── Tools/
    └── motion_test_client.py
```

```
内部运维工具 (不对外发布)
├── AuthAssistant.exe             # 授权管理工具 (生成/颁发 license)
└── LicHper.dll
```

### 10.6 构建与发布脚本

```batch
@echo off
REM release_IOStudio.bat — 构建 IOStudio 发行版 (单一二进制)

echo [1/5] 构建 IOStudio (Release)...
dotnet publish IOStudio/IOStudio.csproj -c Release -r win-x64 --self-contained false -o dist/IOStudio

echo [2/5] 复制 IODevice DLLs...
copy Binaries\Win64\IODevice.dll dist\IOStudio\
copy Binaries\Win64\IODevice_C_Wrapper.dll dist\IOStudio\

echo [3/5] 复制 LicHper.dll...
copy Binaries\Win64\LicHper.dll dist\IOStudio\

echo [4/5] 混淆 .NET 程序集...
obfuscar obfuscar_IOStudio.xml

echo [5/5] 签名...
signtool sign /sha1 %CERT_HASH% dist\IOStudio\IOStudio.exe

echo Done. Output: dist\IOStudio\
echo   未授权 → Basic 模式 (免费播放器)
echo   授权后 → Pro 模式 (完整编辑器)
```

### 10.7 与 MotionServer TCP 协议的关系

Basic/Pro 模式完整保留 MotionServer TCP 协议，但 `list_files` 命令根据授权状态返回不同文件类型：

```csharp
// MotionCommandHandler.cs
private string HandleListFiles()
{
    string pattern;
    if (_licenseService.IsPro)
    {
        // Pro: 列出明文 + 加密文件
        var motionFiles = Directory.GetFiles("Config/Motion", "*.motion");
        var mtnFiles = Directory.GetFiles("Config/Motion", "*.mtn");
        var files = motionFiles.Concat(mtnFiles)
            .Select(Path.GetFileName).ToList();
        return JsonSerializer.Serialize(new {
            type = "response", cmd = "list_files", ok = true, files
        });
    }
    else
    {
        // Basic: 仅列出加密文件
        pattern = "*.mtn";
        var files = Directory.GetFiles("Config/Motion", pattern)
            .Select(Path.GetFileName).ToList();
        return JsonSerializer.Serialize(new {
            type = "response", cmd = "list_files", ok = true, files
        });
    }
}
```

Unity/UE 客户端代码完全不变 — 客户端不关心 IOStudio 是 Basic 还是 Pro，TCP 协议接口完全相同。买家的 Unity/UE 项目中：

```csharp
// 买家的 Unity 代码 (与开发时完全一致)
motionClient.LoadFile("侏罗纪冒险.mtn");  // 传文件名即可, 加解密对客户端透明
motionClient.Play();
```

### 10.8 工程集成说明

LicHper 和 AuthAssistant 项目已集成至 IODevice.sln，位于 `Authorization` Solution Folder 下：

```
IODevice.sln
├── IODevice (C++ vcxproj)                # IO设备核心
├── IODevice_C_Wrapper (C++ vcxproj)      # C ABI 包装层
├── IODevice_CSharp_Wrapper (C# csproj)   # C# P/Invoke 层
├── IOStudio (C# csproj)                  # 主应用 (Basic + Pro)
├── DNHper (C# csproj)                    # 工具库
└── Authorization/                        # 授权子系统 (Solution Folder)
    ├── LicHper (C++ vcxproj)             # 授权验证 DLL (AES + UUID)
    └── AuthAssistant (C# csproj)         # 授权管理工具 (颁发 license)
```

**项目来源：** `C:\Users\Administrator\source\repos\DaemonApps\` 仓库 (GitHub: MrBaoquan/DaemonApps)

**构建依赖：**

- LicHper → Crypto++ (已随项目配置, 绝对路径)
- AuthAssistant → Avalonia 11 + Material.Avalonia + Costura.Fody (嵌入 LicHper.dll)
- IOStudio → 运行时依赖 LicHper.dll (P/Invoke, 不需要项目引用)

**集成方式：**

- IOStudio **不** 直接项目引用 LicHper/AuthAssistant
- IOStudio 通过 P/Invoke 调用 LicHper.dll (`Services/LicHperInterface.cs`)
- LicHper.dll 由构建脚本复制到 IOStudio 输出目录
- AuthAssistant 独立构建、独立运行，用于内部颁发 license

---

## 附：为什么不拆分而是集成

| 拆分方案的问题                             | 集成方案的优势                        |
| ------------------------------------------ | ------------------------------------- |
| IONodeConfig.cs (1851行) 需要复制或抽取    | 直接 `using IOStudio.ViewModels`      |
| 设备管理 UI 需要重写                       | 直接打开 `DevicePropertiesWindow`     |
| IODevice.xml 加载逻辑需要重写              | 直接 `IORoot.Instance.Devices`        |
| ConnectionManager 需要复制                 | 直接引用同一个 Service                |
| OutputTest 功能重复                        | 可以联动：输出测试 → 一键录制为时间轴 |
| 两个应用同时运行时 IODevice.dll 互斥       | 同一进程，无冲突                      |
| 需要维护 IODevice.Shared 抽取库            | 无需抽取，自然共享                    |
| .motion 文件引用 IODevice.xml 路径需要协调 | 同一 Config 目录，路径天然一致        |
