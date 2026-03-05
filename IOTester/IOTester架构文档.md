# IOTester 项目架构文档

## 1. 项目概述

IOTester 是一个基于 **Avalonia UI 11.3** + **ReactiveUI** 的跨平台桌面应用，用于 IO 设备的配置、测试、监控与事件转发。它是 IODevice 生态系统的核心上位机工具，面向工业控制与互动体验领域。

### 1.1 技术栈

| 层面 | 技术选型 |
|------|---------|
| **UI 框架** | Avalonia UI 11.3.10 (跨平台 WPF 替代) |
| **MVVM 框架** | ReactiveUI 11.3.8 (响应式 MVVM) |
| **数据绑定** | DynamicData (响应式集合) + Compiled Bindings |
| **运行时** | .NET 6.0, AnyCPU |
| **底层驱动** | IODevice C++ DLL → C# Wrapper (P/Invoke) |
| **配置格式** | XML (设备/事件) + INI (外部设备) + JSON (Schema) |
| **图标库** | IconPacks.Avalonia.Ionicons |
| **串口通信** | System.IO.Ports |
| **INI 解析** | ini-parser-netstandard |

### 1.2 项目依赖关系

```
IOTester (Avalonia 桌面应用)
  ├── IODevice_CSharp_Wrapper  (C# 封装层, P/Invoke)
  │     └── IODevice.dll       (C++ 核心驱动)
  └── DNHper                   (通用工具库: 单例配置、日志等)
```

---

## 2. 整体架构

### 2.1 分层架构图

```
┌──────────────────────────────────────────────────────────┐
│                     Views (AXAML)                         │
│  MainWindow, IODeviceView, IONodeView, TestControl       │
│  EventForwardConfigWindow, DevicePropertiesWindow        │
│  OutputTestWindow, IONodeEditWindow, KeyEditDialog       │
├──────────────────────────────────────────────────────────┤
│                  Controls (自定义控件)                     │
│  CollapsibleHeader, DeviceConfigPanel                    │
├──────────────────────────────────────────────────────────┤
│               Converters (值转换器)                       │
│  BoolToColor, BoolToActive, MappingTypeDisplay ...       │
├──────────────────────────────────────────────────────────┤
│                 ViewModels (MVVM)                         │
│  MainWindowVM, TestVM, EventForwardConfigVM              │
│  OutputTestVM, RangeMappingDialogVM                      │
│  IONodeConfig (Device, Action, Axis, OAction, Key ...)   │
├──────────────────────────────────────────────────────────┤
│                   Models (数据模型)                        │
│  EventForwardConfigDto, ProtocolGroup, MappingDto        │
│  IOTesterSettings, ActionKeyMapping, EditorLauncher      │
├──────────────────────────────────────────────────────────┤
│                  Services (业务服务)                       │
│  EventForwardingService, ConnectionManager               │
│  AnalogValueProcessor, RecordingManager                  │
│  DeviceSchemaService, IniConfigService                   │
│  ExternalDeviceService, KeyNameValidator                 │
│  Senders: IProtocolSender → NetIO, Modbus, TCP,          │
│           UDP, Serial, TcpServer                         │
├──────────────────────────────────────────────────────────┤
│                Extensions (扩展方法)                       │
│  ProtocolGroupExtensions (DTO ↔ Model 转换)              │
├──────────────────────────────────────────────────────────┤
│              External Libraries (底层)                    │
│  IODevice_CSharp_Wrapper → IODevice C++ DLL              │
│  DNHper (SingletonConfig, Logger ...)                    │
└──────────────────────────────────────────────────────────┘
```

### 2.2 运行时数据流

```
┌─────────────┐    C++ P/Invoke    ┌─────────────────┐
│  硬件设备     │ ─────────────────→ │  IODevice DLL    │
│  (Joystick,  │                    │  (C++ 核心驱动)   │
│   MODBUS,    │                    └────────┬────────┘
│   SNAP7...)  │                             │
└─────────────┘                     IODevice_CSharp_Wrapper
                                             │
                    ┌────────────────────────┤
                    ▼                        ▼
            ┌──────────────┐        ┌──────────────────┐
            │  IORoot       │        │  IODeviceController│
            │  (XML配置树)   │        │  (设备轮询引擎)    │
            └──────┬───────┘        └────────┬─────────┘
                   │                         │
        ┌──────────┼──────────┐              │
        ▼          ▼          ▼              ▼
   ┌────────┐ ┌────────┐ ┌────────┐   ┌──────────┐
   │ Device  │ │ Device  │ │ Device  │   │ 实时轮询  │
   │ (Tab1)  │ │ (Tab2)  │ │ (Tab3)  │   │ (定时器)  │
   └────┬───┘ └────────┘ └────────┘   └──────────┘
        │                                    │
   ┌────┼────────┬─────────┐                │
   ▼    ▼        ▼         ▼                ▼
Action  Axis   OAction  Properties    ┌──────────────┐
(DI)   (AD)    (DO)    (全局属性)     │ UI 实时刷新   │
                                      │ (100ms周期)  │
                                      └──────────────┘
```

---

## 3. 核心模块详解

### 3.1 域模型层 (IONodeConfig.cs — 1851 行核心文件)

这是整个系统的领域模型核心，定义了完整的设备-通道-按键层级结构：

```
IORoot (SingletonConfig - XML根节点)
  └── Device[] (设备)
        ├── Properties (全局属性配置)
        │     └── Key[] (属性Key: Offset, Scale, Min, Max, DeadZone...)
        ├── Action[] (数字量输入通道 - DI)
        │     └── Key[] (绑定的按键: Button_00, Button_01...)
        ├── Axis[] (模拟量输入通道 - AD)
        │     └── Key[] (轴绑定: Axis_00, Axis_01...)
        └── OAction[] (输出通道 - DO)
              └── Key[] (输出绑定: OAxis_00, OAxis_01...)
```

#### 3.1.1 类继承体系

```
ReactiveObject (ReactiveUI)
  └── ViewModelBase
        └── Device
              ├── Name, Type, DllName, Index (XML属性)
              ├── Actions[], Axes[], OActions[] (子通道集合)
              ├── Properties (全局属性)
              ├── DI/AD/DO Keys (原始通道视图)
              ├── Update() — 实时轮询更新值
              └── AfterDeserialization() — XML反序列化后初始化

  └── IONodeBase (通道基类)
        ├── Name, Label, Keys[], Value, Active
        ├── AddKey/DeleteKey/EditKey/RecordKey 命令
        ├── Clone()/CopyFrom() — 编辑时的深拷贝机制
        ├── RebuildActiveSubscription() — 响应式活跃状态
        │
        ├── Action (数字量输入: DI)
        ├── Axis (模拟量输入: AD)
        ├── OAction (输出: DO)
        └── Properties (全局属性)

  └── Key (按键/通道映射)
        ├── Name, Value, Active
        ├── Offset, Scale, Min, Max
        ├── DeadZone, Sensitivity, Exponent
        ├── Invert, InvertEvent
        └── ToggleDOCommand (输出切换)
```

### 3.2 ViewModel 层

| ViewModel | 职责 |
|-----------|------|
| **MainWindowViewModel** | 应用主入口。管理设备列表、启停设备、编辑模式切换、录制订阅、定时轮询(100ms)更新UI |
| **EventForwardConfigViewModel** | 事件转发配置窗口。管理协议组(ProtocolGroup)与映射(Mapping)的CRUD、保存/加载XML |
| **OutputTestViewModel** | DO输出测试窗口。支持模拟量输出、脉冲测试、跑马灯测试三种模式 |
| **RangeMappingDialogViewModel** | 批量范围映射对话框。快速批量生成 SourceKey→TargetKey 映射 |
| **TestViewModel** | 简单的测试视图模型（开发调试用） |
| **ViewModelBase** | 所有 VM 的基类，继承 ReactiveObject |

### 3.3 Service 层

#### 3.3.1 EventForwardingService (事件转发引擎)

**核心职责：** 将IO设备的输入事件通过多种协议转发到外部系统。

```
输入设备 → [事件绑定] → [协议发送器] → 外部系统
              │               │
         BindKey()     IProtocolSender
         BindAxisKey()       │
                    ┌────────┼────────┐
                    ▼        ▼        ▼
               NetIO    Modbus-RTU  Custom
               (UDP)    (串口)      ├── TCP-Client
                                    ├── TCP-Server
                                    ├── UDP
                                    └── Serial
```

**协议发送器架构 (策略模式)：**

```csharp
interface IProtocolSender : IDisposable {
    void SendDigital(group, mapping, eventType);  // 数字量
    void SendAnalog(group, values);               // 模拟量
}
```

实现类：
- `NetIOSender` — UDP协议发送 (自定义NetIO协议)
- `ModbusSender` — Modbus-RTU串口协议
- `TcpClientSender` / `TcpServerSender` / `UdpSender` / `SerialSender` — 自定义协议

**特殊模式：DirectOutput (设备直出)**
- 直接将源设备的输入映射到目标设备的输出
- 绕过协议层，设备间直通

#### 3.3.2 ConnectionManager (连接管理器)

统一管理所有网络与串口连接的生命周期：

| 连接类型 | 管理能力 |
|---------|---------|
| UDP Client | 创建/复用/发送 |
| TCP Client | 创建/连接/发送/断线重连 |
| TCP Server | 监听/接受连接/广播 |
| Serial Port | 创建/配置/发送 |

所有连接通过 key-value 字典管理，支持并发安全（多锁机制）。

#### 3.3.3 AnalogValueProcessor (模拟量处理器)

```
设备轴值回调 → UpdateValue(groupId, key, value)
                         │
                    [值去重缓存]
                         │
              定时轮询(100ms) → ProcessAllGroups()
                         │
                    [变化检测]
                         │
               sender.SendAnalog(group, changedValues)
```

核心优化：
- 使用 ConcurrentDictionary 实现无锁读写
- 只发送变化的值（帧间差分）
- 可配置轮询间隔

#### 3.3.4 RecordingManager (按键录制)

- 单例模式，全局唯一录制状态
- 支持为 Action 节点录制绑定新按键
- 自动过滤鼠标事件
- Subject 发布录制完成事件

#### 3.3.5 DeviceSchemaService & IniConfigService

**双层配置架构：**

```
Schema (JSON)                    INI Config
┌──────────────────┐            ┌──────────────────┐
│ 定义UI结构/字段类型 │            │ 存储实际配置值     │
│ displayName       │            │ [default]         │
│ sections[]        │            │ port=COM1         │
│   fields[]        │            │ baud=9600         │
│     key, label    │            │ [device_0]        │
│     type, options │            │ port=COM3         │
└──────────────────┘            └──────────────────┘
         │                               │
         └──────── 驱动 UI 渲染 ──────────┘
```

- Schema 从 INI 自动生成，支持手动编辑增强
- 支持 Section 级别的配置隔离 (default / device_N)
- 新配置项自动发现并合并到 "其他配置" Section

#### 3.3.6 ExternalDeviceService (外部设备扫描)

自动扫描 `ExternalLibraries/` 目录下的 `IOUI-Win64-*.dll`，发现可用设备驱动并建立设备清单。

### 3.4 View 层

| 视图 | 功能 |
|------|------|
| **MainWindow** | 主窗口，Tab 页切换设备，菜单栏 |
| **IODeviceView** | 设备详情页：DI/AD/DO 三列布局，原始通道 + 用户通道 |
| **IONodeView** | 单个通道节点视图：名称、绑定的Key列表、活跃状态指示 |
| **TestControl** | 测试控件 |
| **DeviceConfigPanel** | 设备 INI 配置面板（基于 Schema 动态渲染） |
| **CollapsibleHeader** | 可折叠分组标题栏 |
| **EventForwardConfigWindow** | 事件转发配置（树形协议组+映射列表） |
| **OutputTestWindow** | DO/OAction 输出测试窗口 |
| **IONodeEditWindow** | 通道节点编辑窗口 |
| **KeyEditDialog** | Key 属性编辑对话框 |
| **PropertyKeyEditDialog** | 全局属性 Key 编辑对话框 |
| **DevicePropertiesWindow** | 设备属性编辑 (Name/Type/DllName/Index) |
| **RangeMappingDialog** | 批量范围映射对话框 |
| **HelpWindow** | 帮助窗口 |

### 3.5 配置文件体系

```
Config/
  ├── IODevice.xml          — 主设备配置 (设备列表、通道定义、Key绑定)
  ├── IOTesterSettings.xml  — 应用设置 (刷新率等)
  ├── EvtMapping.xml        — 事件转发映射配置
  └── Schemas/
        ├── modbus.schema.json   — MODBUS 设备UI配置模式
        ├── snap7.schema.json    — SNAP7 设备UI配置模式
        └── *.schema.json        — 其他设备模式

ExternalLibraries/
  ├── IOUI-Win64-MODBUS.dll   — MODBUS 驱动DLL
  ├── IOUI-Win64-SNAP7.dll    — SNAP7 驱动DLL
  └── Config/
        ├── MODBUS/config.ini   — MODBUS 运行时配置
        └── SNAP7/config.ini    — SNAP7 运行时配置
```

---

## 4. 关键设计模式

### 4.1 响应式 MVVM (Reactive Extensions)

- **DynamicData SourceList** → ReadOnlyObservableCollection 驱动 UI 列表
- **WhenAnyValue** → 属性变化的声明式订阅
- **WhenActivated** → ViewModel 激活/销毁生命周期管理
- **ReactiveCommand** → 异步命令绑定
- **ObservableAsPropertyHelper** → 派生属性自动计算 (如 Key.Active)

### 4.2 策略模式 (Protocol Senders)

`IProtocolSender` 接口统一所有协议发送行为，`EventForwardingService` 通过字典查找动态选择发送器。

### 4.3 单例模式

- `IORoot` / `IOTesterSettings` — SingletonConfig 模式（DNHper 库提供）
- `EventForwardingService` / `RecordingManager` / `DeviceSchemaService` / `IniConfigService` / `ExternalDeviceService` — Lazy 单例

### 4.4 观察者模式

- `RecordingManager.OnRecordingComplete` — Subject 发布录制事件
- `IONodeBase.OnToggleDO` / `Key.OnToggleDO` — Subject 发布 DO 切换事件
- `IONodeBase.OnEditKey` — Subject 发布编辑事件

### 4.5 ViewLocator 约定

基于命名约定的 ViewModel → View 自动定位：将 `ViewModel` 替换为 `View` 查找对应控件。

---

## 5. 核心运行流程

### 5.1 应用启动流程

```
Program.Main()
  → BuildAvaloniaApp().StartWithClassicDesktopLifetime()
    → App.OnFrameworkInitializationCompleted()
      → new MainWindow { DataContext = new MainWindowViewModel() }
        → MainWindowViewModel.WhenActivated()
          → Load()  // 加载IODevice.xml、初始化C++设备
            → IORoot.SetConfig().Load()       // XML反序列化
            → IORoot.AfterDeserialization()    // 初始化所有子节点
            → IODeviceController.Load()        // 启动C++设备轮询
          → EventForwardingService.LoadConfig() + Start()  // 加载事件转发
          → SetupKeyBindings()                // 注册按键监听
          → SetupRecordingSubscriptions()     // 注册录制监听
          → Observable.Interval(100ms)        // 启动UI刷新定时器
            → IODeviceController.Update()     // 后台线程: C++设备轮询
            → SelectedDevice.Update()         // 主线程: UI值刷新
```

### 5.2 事件转发流程

```
硬件按键按下
  → IODevice C++ DLL 检测到变化
    → C# Wrapper: BindKey(keyName, IE_Pressed, callback)
      → EventForwardingService.BindGroup()
        → sender.SendDigital(group, mapping, "Pressed")
          → ConnectionManager.SendViaUdp/Tcp/Serial(...)
            → 网络/串口数据包发出
```

### 5.3 设备配置编辑流程

```
用户点击"编辑模式"
  → IsEditMode = true → 同步到所有 Device/IONode
    → 用户添加 Action/Axis/OAction
      → 弹出 IONodeEditWindow (Clone模式编辑)
        → 确认 → CopyFrom() 同步回原对象
          → IORoot.Instance.Save() → 持久化到 XML
```

---

## 6. 项目统计

| 维度 | 数量 |
|------|------|
| Views (AXAML) | 9 个窗口 + 5 个控件 |
| ViewModels | 7 个 |
| Models | 5 个 |
| Services | 8 个 (含 Senders) |
| Converters | 8 个 |
| 核心代码行 | IONodeConfig.cs ≈ 1851 行 (域模型核心) |
| 配置格式 | XML + INI + JSON (三层配置) |
| 通信协议 | NetIO, Modbus-RTU, TCP, UDP, Serial |

---

## 7. 架构优势与待改进点

### 优势
1. **响应式架构**：基于 ReactiveUI + DynamicData，数据流清晰可追踪
2. **多协议扩展性**：IProtocolSender 策略模式，新增协议只需实现接口
3. **Schema 驱动配置 UI**：设备配置面板基于 JSON Schema 动态渲染，新设备零代码适配
4. **编辑安全**：Clone/CopyFrom 机制确保编辑不影响运行时状态
5. **性能优化**：后台线程设备轮询 + 主线程UI更新分离；模拟量帧间差分

### 待改进
1. **IONodeConfig.cs 过大**：1851 行包含 Device/IONodeBase/Key/Action/Axis/OAction 所有类，可拆分
2. **单例过多**：7+ 个 Singleton，可考虑引入 DI 容器
3. **缺少单元测试**：Service 层无测试覆盖
4. **事件转发与设备轮询耦合**：可进一步解耦为独立进程/线程
