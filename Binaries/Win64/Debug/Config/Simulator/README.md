# Simulator 配置说明

`Simulator` 是独立的 IOUI 仿真器插件，用于开发阶段不依赖真实硬件完成统一协议调试。它不修改 `DEBUGGER`，也不承载任何真实设备私有协议适配逻辑。

## 核心能力

`Simulator` 的基础层面向所有 IOUI 设备约定，先提供通用 `DO / DI / AD` 仿真数据面：

- `DO[0..63]`: 宿主输出状态，可由 IODevice 写入，也可由调试 UI/API 手动写入
- `DI[0..63]`: 仿真器输入反馈状态，可由预设状态机维护，也可由调试 UI/API 手动注入
- `AD[0..63]`: 仿真器模拟量反馈状态，可由预设状态机维护，也可由调试 UI/API 手动注入
- `simulator.io.snapshot`: 返回完整 `DO / DI / AD` 数组和通道数量
- `simulator.io.set`: 按 `{ "kind": "do|di|ad", "channel": n, "value": v }` 写入单个通道

## 动感平台预设

动感平台只是 `Simulator Core` 之上的领域预设，复用通用 IO 数据面并额外解释以下语义：

- `DO[1..3]`: pitch / yaw / roll 姿态输入观察
- `DO[9]`: reset
- `DO[16..22]`: water / snow / wind / fog / hot / light / vibrate 特效状态镜像到 `DI[16..22]`
- `DO[32]`: door open
- `DO[33]`: door close
- `DI[0]`: system ready
- `DI[1]`: motion enabled
- `DI[2]`: effect enabled
- `DI[35..38]`: door opened / closed / opening / closing
- `AD[16]`: door position percent
- `AD[17]`: door angle

## PluginChannel

### `_capabilities`

宿主写入 `_capabilities` 后，插件通过 `_capabilities` 上行返回能力声明 JSON。

### `_rpc.req`

支持的 `topic`:

- `plugin.capabilities`
- `simulator.capabilities`
- `simulator.snapshot`
- `simulator.io.snapshot`
- `io.snapshot`
- `simulator.io.set`
- `io.set`
- `simulator.reset`
- `motion.reset`
- `motion.door.open`
- `motion.door.close`
- `simulator.door.open`
- `simulator.door.close`

### `simulator.command`

支持的文本命令:

- `reset`
- `door.open`
- `door.close`
- `snapshot`
- `io.snapshot`

## UI / Web

当前实现已落地插件自托管的本地 `HTTP` 调试入口。默认监听 `127.0.0.1:18080`，静态资源来自 `Config/Simulator/web/`。

### HTTP API

- `GET /api/instances`: 获取已打开的 Simulator 实例列表与快照
- `GET /api/instances/{index}/snapshot`: 获取指定实例快照
- `GET /api/instances/{index}/io`: 获取指定实例完整 `DO / DI / AD` 快照
- `GET /api/instances/{index}/capabilities`: 获取指定实例能力声明
- `POST /api/instances/{index}/io/{do|di|ad}/{channel}/{value}`: 写入单个通用 IO 通道
- `POST /api/instances/{index}/commands/open`: 开门
- `POST /api/instances/{index}/commands/close`: 关门
- `POST /api/instances/{index}/commands/reset`: 复位

`web_enabled` 控制是否启动本地 HTTP 服务，`web_port` 控制监听端口。当前服务只监听本机回环地址，不对局域网开放。

当前网页优先使用 `WebSocket` 双向连接 `GET /api/instances/{index}/ws`，失败时回退到 `SSE` 实时订阅 `GET /api/instances/{index}/events`，最后再回退到 HTTP 轮询，避免整页高频轮询带来的明显延迟。

### WebSocket API

- `GET /api/instances/{index}/ws`: 建立指定实例的双向调试通道
- 服务端消息 `capabilities`: `{ "type": "capabilities", "device_index": n, "payload": {...} }`
- 服务端消息 `snapshot`: `{ "type": "snapshot", "device_index": n, "payload": {...} }`
- 客户端命令 `io.set`: `{ "type": "io.set", "kind": "do|di|ad", "channel": n, "value": v }`
- 客户端命令 `command`: `{ "type": "command", "name": "open|close|reset" }`

前端源码位于 `IOUI/SimulatorWeb/`，技术路线为 `Vue 3 + TypeScript + Vite + Pinia`，构建产物直接发布到 `IOUI/Config/Simulator/web/`；Windows / Android 包装层统一采用 `Tauri 2` 脚手架，复用同一份 Web 前端。

内置 UI、浏览器会话复用仍属于后续增量。

浏览器自动打开策略默认使用 `resume`，后续实现时必须通过会话心跳复用已有调试页，避免每次宿主启动都新建标签页。
