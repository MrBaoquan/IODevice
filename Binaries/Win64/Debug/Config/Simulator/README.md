# Simulator 配置说明

`Simulator` 是独立的 IOUI 仿真器插件，用于开发阶段不依赖真实硬件完成统一协议调试。它不修改 `DEBUGGER`，也不承载任何真实设备私有协议适配逻辑。

## 核心能力

`Simulator` 的基础层面向所有 IOUI 设备约定，先提供通用 `DO / DI / AD` 仿真数据面：

- `DO[0..255]`: 宿主输出状态，可由 IODevice 写入，也可由调试 UI/API 手动写入
- `DI[0..255]`: 仿真器输入反馈状态，可由预设状态机维护，也可由调试 UI/API 手动注入
- `AD[0..255]`: 仿真器模拟量反馈状态，可由预设状态机维护，也可由调试 UI/API 手动注入
- 反馈原则：`DI/AD` 反映状态机推进后的**实际**状态，不是 `DO` 的即时镜像。特效带 `effect_confirm_ms` 确认延迟，`DI[1]` 跟随 `DO[11]`，急停/告警/故障为独立状态
- `simulator.io.snapshot`: 返回完整 `DO / DI / AD` 数组和通道数量
- `simulator.io.set`: 按 `{ "kind": "do|di|ad", "channel": n, "value": v }` 写入单个通道

## 动感平台预设

动感平台只是 `Simulator Core` 之上的领域预设，复用通用 IO 数据面并额外解释以下语义：

- `DO[1..3]`: pitch / yaw / roll 姿态输入观察
- `DO[4..6]`: x / y / z 位移输入观察
- `DO[7]`: angle 转台角度输入观察
- `DO[8]`: speed_level 速度级别输入观察
- `DO[9]`: reset
- `DO[10]`: emergency_stop（急停，置位即冻结机构并清 `DI[1]`）
- `DO[11]`: motion_enable（运动使能，与 `DI[1]` 保持一致）
- `DO[16..22]`: water / snow / wind / fog / hot / light / vibrate 特效命令，经 `effect_confirm_ms` 确认后写入 `DI[16..22]` 与 `AD[32..38]`
- `DO[32]`: seat belt lock (兼容锁止语义, 在 Simulator 中等效为压杆下压)
- `DO[33]`: lap bar down
- `DO[34]`: lap bar up
- `DO[35]`: door open
- `DO[36]`: door close
- `DO[44]`: carrier to presentation
- `DO[45]`: carrier to parked
- `DO[46]`: carrier hold
- `DI[0]`: system ready
- `DI[1]`: motion enabled（跟随 `DO[11]`）
- `DI[2]`: effect enabled
- `DI[3]`: emergency active（跟随 `DO[10]`）
- `DI[4]`: alarm active（由告警注入维护）
- `DI[5]`: fault active（由故障注入维护）
- `DI[32]`: seat belt locked
- `DI[33]`: lap bar down done
- `DI[34]`: lap bar up done
- `DI[35..38]`: door opened / closed / opening / closing
- `DI[39]`: mechanism moving
- `DI[48..52]`: carrier at presentation / parked / moving_to_presentation / moving_to_parked / holding
- `AD[16]`: door position percent
- `AD[17]`: door angle
- `AD[18]`: lap bar position percent
- `AD[22]`: carrier position percent
- `AD[32]` water_level / `AD[33]` wind_level / `AD[34]` fog_level / `AD[35]` hot_level / `AD[36]` vibrate_level（确认过程中按比例斜坡；飘雪 / 闪电无标准强度通道）

## 三维视图约定

Web UI 的动感平台面板包含一个 three.js 三维视图，用于观察姿态与机构动作。它与宿主下发的是同一份数据，只读，不回写任何通道。

数据来源分工：

| 视图元素 | 数据来源 | 说明 |
| -------- | -------- | ---- |
| 平台姿态 | `DO[1..6]`（或 `pose_source=feedback` 时的 `AD[0..5]`） | 默认是宿主下发的**指令**；切到 feedback 后取插件仿真的实际值 |
| 转台 | `DO[7]` angle（同轴叠加 `DO[2]` yaw） | 驱动上层转台 |
| 舱门 | `AD[16]` | 插件状态机按 `door_duration_ms` 推进的**过程量** |
| 压杆 | `AD[18]` | 同上，按 `lap_bar_duration_ms` |
| 小车 | `AD[22]` | 同上，按 `carrier_duration_ms` |

机构链（自下而上）——按「主流平台位姿语义」组织：**位移在基准系、偏航在最外层**。

- `carrier` 轨道小车（沿 Z 进/退，`AD[22]`；轨道属房间。**待机位在 +Z 近端**，观众由此上下平台；**动作位在 −Z 远端**，面向银幕。业务上是「车前进到动作位」，而车头为 `-Z`，故前进即 −Z）
- `slide` XY 平移台：承担基准系水平位移 x（`DO[4]`）/ y（`DO[5]`）
- `turntable` 转台：承担 yaw（`DO[2]`）与 angle（`DO[7]`，同轴叠加），绕基准竖直轴，**不影响杆长**
- `cylinders` 4 根竖直电缸：承担 heave / pitch / roll（`DO[6]/DO[1]/DO[3]`），四根杆件据此解算
- 座舱（座椅 / 压杆 / 灯柱 / 速度表）作为**一个刚体**挂在 `cylinders` 下整体运动

> **舱门不是舱体的一部分**：它是**房间里的站台门**（见 `roomLayer.ts`），位于待机位与
> 动作位之间，由 `AD[16]` 驱动开合。座舱顶棚与 4 根外围立柱已移除——顶棚会从上方遮住
> 座椅/压杆状态，立柱在去掉顶棚后也无结构意义。

由此得到主流平台位姿定义：

```
p_world = t + R · p_body
t = (x, z, -y) / mm_per_unit                ← 位移表达在基准（固定）坐标系
R = Ry(yaw+angle) · Rx(pitch) · Rz(-roll)   ← 欧拉序 YXZ，偏航在最外层
```

约定（协议只定义了工程量与单位，以下几条是 Simulator 三维视图自行定死的，改动会直接改变模型动作方向）：

- **旋转顺序**：偏航为最外层（等价于先横滚、再俯仰、最后偏航）。three.js 侧为 `Euler` order `YXZ`；**偏航节点必须位于倾斜机构之外**，否则 `eulerOrder` 对偏航不生效
- **姿态正方向**：`+pitch` 抬头、`+yaw` 左转、`+roll` 右侧下沉
- **位移正方向（基准系）**：`+x` 向右、`+y` 向前、`+z` 上升
- **车头方向**：平台局部 `-Z`（座舱靠背在 `+Z` 侧，乘员面向车头）

> 位姿语义集中在 `src/motion3d/poseSolver.ts`，并由 `poseSolver.test.ts` 断言
> 「场景图整链合成 == 主流定义」。层级顺序、参考系、正方向任何一处改错都会在单测中失败。
> 座椅与压杆同挂在 `cylinders` 下，因此大偏航/大俯仰时不会互相穿透。

### 机构几何与比例尺

模型按一台 6 座 4D 动感平台的真实尺度建模（1 场景单位 = `mm_per_unit` 毫米）：

| 部件 | 尺寸（米） | 说明 |
| ---- | ---------- | ---- |
| 轨道 / 小车 | 8 / 3.2×2.2 | 待机位 +2.6 m、动作位 −2.6 m（前进方向为 `-Z`） |
| 底座 | 2.8×2.0 | 顶面 0.54 m |
| 电缸 ×4 | 缸体 0.30 | 顶面 0.84 m，位于底座四角 ±1.0 / ±0.68 |
| 动平台板 | 2.2×1.5×0.12 | 静止中心 1.56 m，杆件铰接面为板底 |
| 站台门（房间） | 门洞 3.8 m 宽 × 3.6 m 高 | 位于 `z = 0`（待机位与动作位之间）；两扇半透明门叶由 `AD[16]` 驱动左右滑开，关门时仍可透视看到门后平台 |
| 登乘平台（房间） | 2.5 × 2.4 m，台面 1.50 m | `+Z` 端、−X 侧，观众入口位；四条支腿 + 外侧三级踏步 + 扶手 |
| 座椅 | 6 座（3 列 × 2 排） | 靠背在 `+Z` 侧 |

静止态电缸长度 ≈ **0.66 m**，落在配置行程 `leg_stroke_min_mm..max` 内，并留出
`motion_travel_z_mm` 的升降余量；面板底部实时显示四杆实际长度与配置区间，越界会告警。

> `mm_per_unit` 必须与几何一致。若误设为 100，几百毫米的行程会被放大成与平台同量级的
> 位移（±300 mm 会变成 ±3 单位，超过平台板宽），机构明显失真。单元测试
> `机构尺度自洽性` 已把这条约束固化。

### 放映厅环境与「不遮挡」原则

环境特效在真实影院里都装在房间里，不是座椅上，因此三维视图额外搭了一个放映厅
（**11 m × 13 m × 4.4 m**，纵深沿 Z）：地坪 + 顶棚桁架（仅外圈边梁）+ 四角立柱
+ 墙面安装梁 + 后墙银幕 + 站台门。`EffectsLayer` 挂在场景根节点（**不随平台移动**），
每类特效都是「房间设备 + 粒子」：

| 特效 | 安装位置 | 设备 |
| ---- | -------- | ---- |
| 喷水 | 左右墙面安装梁（y=1.85） | 各 4 个喷嘴，向场内斜下方喷射水雾 |
| 飘雪 | 顶棚**边梁**（四角 + 各边中点） | 8 个出雪口，雪片覆盖全厅 |
| 风效 | 左墙壁挂（y=1.55） | 2 台风机（四叶转子按强度旋转） |
| 烟雾 | 地坪四周、环绕**动作位**（±2.9, −1.2 / −4.0） | 4 个雾口，贴地上浮扩散，正好罩住演出中的平台 |
| 热风 | 两侧壁挂（y=1.1） | 4 台加热器，热风沿墙上升 |
| 闪电 | 顶棚**边梁** + 厅内点光源 | 灯带整厅照亮 |

### 方向语义与观看视野

房间原本前后近似对称，很难分辨哪边是"前"。现在用三类线索把方向钉死：

| 线索 | 位置 | 含义 |
| ---- | ---- | ---- |
| **银幕 + 台口**（青色立柱 + 门楣） | `-Z` 端 | **前端**：演出/观影方向，也是小车"前进"的方向 |
| **登乘平台**（橙色，台面 1.50 m ≈ 动平台板高度） | `+Z` 端、−X 侧 | **后端**：观众入口位，由三级踏步上台 |
| **地坪行进箭头**（橙色人字，3 组） | 轨道两侧 | 从待机位指向动作位（−Z），最直观的前后线索 |
| **车头箭头**（橙色 V 形） | 动平台板前端 | 标出平台自身的朝向 |

观看视野：默认机位是后端偏右的 3/4 俯视，**取景以平台为主体**（房间作背景）。
相机沿 Z **跟随小车**（系数 0.8）：平台在待机位与动作位之间前后移动时始终居中构图，
相机与 target 同步平移，因此不会改变用户当前的环绕角度与距离。

设备外壳会在对应特效动作时点亮，因此一眼能看出「是哪台设备在动作」。

**刻意移除了几处遮挡物**，保证机构与状态始终可见：

- 顶棚只保留**外圈边梁**，不放厅中央横梁——中央横梁正好横在画面上方压住平台；
- 座舱**不做顶棚**（见上表），俯视时座椅/压杆不再被顶板挡住；
- 灯带、出雪口一律装在**边梁/边界**上，不放在厅中央。

### 站台门与业务流程

**舱门不是舱体的一部分**，而是房间里的**站台门**，位于待机位与动作位之间。完整业务流程是：

1. 平台停在**待机位**（`+2.6 m`，近端），观众上下平台；
2. 落下**压杆**（`AD[18]`）；
3. **开门**（`AD[16]` 驱动两扇门叶左右滑开）；
4. 小车**前进**到**动作位**（`-2.6 m`，远端，面向银幕）；
5. **关门**（门叶合拢封住门洞）；
6. 平台动作体验。

门洞 3.8 m 宽 × 3.6 m 高，两扇门叶半透明——**关门时仍能透视看到门后的平台状态**，
不会因为关门而看不见机构。三维视图只呈现这条链路的状态（门开合 / 小车位置 / 压杆），
流程调度本身属于业务层。

### 值域契约：归一化内容 ↔ 工程量通道（重要）

动作文件与设备通道的**值域语义不同**，这是整条链路上最容易出错的地方：

| 层 | 值域 | 说明 |
| -- | ---- | ---- |
| `.motion` 内容 | **归一化 0..1**，中位 = `neutral_value`（float 默认 `0.5`，bool 默认 `0`） | 编辑器求值器按此 clamp；预置动作库（`embedded_presets` 的 `PresetChannel.scale`）也据此设计，目的是**一套内容可复用到行程不同的平台** |
| 通道 ABI（`DO[1..7]`） | **工程量**（度 / 毫米） | 见 `docs/动感平台统一通道协议约定.md` §3.1，本插件严格按此实现 |

换算发生在 **native 派发到设备的唯一收口点**（C++ `MotionPlayer::DispatchChannel`），
解析优先级三级：

| 优先级 | 来源 | 形式 | 用途 |
| ------ | ---- | ---- | ---- |
| ① | 轨道 `engineering_range` | 绕中位的对称行程：`(normalized - neutral) * range` | 显式覆盖；代表"该轨道的工程含义与设备声明不同"，属于需要复核的偏离 |
| ② | **设备声明量程** | 绝对区间：`min + normalized * (max - min)`（支持非对称） | **唯一真相源**。内容保持设备无关，换平台只改设备配置 |
| ③ | 直通 | `normalized` | 两者都未声明；保持既有动作文件行为不变 |

**设备声明量程的来源（按优先级）**：

| 优先级 | 来源 | 形式 |
| ------ | ---- | ---- |
| ① | 设备自述 **capabilities**：RPC topic `plugin.capabilities` → `motion.channel_range[]` | `{"channel":"OAxis_01","min":-30,"max":30,"unit":"deg"}` |
| ② | 本插件 `config.ini` 的 `[channel_range]` 段（capabilities 不可用时兜底） | `OAxis_01 = -30, 30, deg` |

```ini
[channel_range]
OAxis_01 = -30, 30, deg     ; 归一化 0 → -30，1 → +30（中位 0.5 → 0）
OAxis_04 = -300, 300, mm
OAxis_07 = -360, 360, deg
OAxis_16 = 0, 1, bool       ; 特效等二元通道
```

- 语义：归一化 `0 → min`、`1 → max`
- `motion.channel_range[]` 由插件从 `motion_travel_*` **派生**，因此与三维视图软限位**同源**，
  不必再维护一份量程表 —— 这是首选来源
- 本插件**自身不解析** `[channel_range]` 段（mINI 忽略未知段），它只是对宿主的声明
- IOStudio 侧由 `Services/Motion/DeviceRangeCatalog` 读取：首选 capabilities，其次
  `Config/IODevice.xml` → `Config/<DllName>/config.ini` 的该段；都读不到即直通
- **时序约束**：读 capabilities 要走 native RPC，而设备派发本身跑在 native tick 内，
  在派发路径发 RPC 不安全。因此拆成 `Resolve`（派发路径只查缓存）+
  `Prime`（时间轴加载 / Play 等安全时机预热一次）

> 已落地并验证：探针文件内不写量程、并临时移除 `[channel_range]` 段后，仅靠 capabilities
> 即得到正确的工程量（pitch 18 → −12，z 180 → −120）。

> 判定原则：**量程属于设备，不属于内容**。把 ±30° 写进 `.motion` 会让同一套节目无法复用到
> 另一台行程不同的平台；而 `IODevice.xml` 在 UNIPlayer 侧是**编译产物**，手工往里加量程会被
> 下次编译覆盖，因此也不适合作为量程载体。

### 基础信号控制类的三维表达

协议 §3.2 / §3.5.1 的基础信号与控制在场景中都有对应的物理指示，而不是只高亮按钮：

| 信号 | 通道 | 三维表达 |
| ---- | ---- | -------- |
| system_ready / motion_enabled | DI[0]/DI[1] | 座舱立柱顶端灯柱**绿灯** |
| alarm_active | DI[4] | 灯柱**黄灯** |
| fault_active / emergency_stop | DI[5]/DO[10] | 灯柱**红灯**（急停闪烁）+ 地面红色脉冲环 |
| homed | DI[6] | 底座地面**绿色回零环** |
| busy | DI[7] | 转台上方**旋转环** |
| speed_level | DO[8] | 座舱立柱上的**速度条形表**（>75% 转琥珀色） |
| reset | DO[9] | 姿态回零（模型随 DO[1..7] 归零） |
| water / snow / wind / fog / hot | DO-DI[16..22] | **房间设备 + 粒子**（见上节「放映厅环境与不遮挡原则」） |
| light | DO-DI[21] | **顶棚边梁**灯带（双闪 + 停顿）+ 厅内点光源，整厅照亮 |
| vibrate | DO-DI[22] | 机构上的**高频微抖**（唯一真正属于座椅的特效） |

特效由 `DI[16..22]`（实际状态）与 `AD[32..36]`（强度）驱动，因此天然反映插件的
`effect_confirm_ms` 确认延迟，不会出现"命令一下画面就动"的伪实时。

比例尺与行程上限（超出即钳制显示并提示"超出配置行程"）：

- 比例尺默认 1 场景单位 = 1000 毫米（`motion.mm_per_unit`，1 单位 = 1 米），必须与机构几何一致
- 行程上限、轴序、机构几何、比例尺由**插件**在 `config.ini` 中维护，并经 capabilities 的 `motion` 块下发（`motion_travel_*` / `leg_stroke_*` / `mm_per_unit` / `euler_order` / `pose_source`）
- 前端 `IOUI/SimulatorWeb/src/motionChannels.ts`、`motionConfig.ts` 中的常量仅作为 capabilities 缺失时的**兜底默认值**，不再是单一来源

滑杆允许超出上述行程，用于调试与故障注入；超出的部分只在三维视图被钳制，`DO` 实际值不受影响。

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
- `motion.lap_bar.down`
- `motion.lap_bar.up`
- `motion.door.open`
- `motion.door.close`
- `carrier.position.to_presentation`
- `carrier.position.to_parked`
- `carrier.position.hold`
- `simulator.lap_bar.down`
- `simulator.lap_bar.up`
- `simulator.door.open`
- `simulator.door.close`
- `simulator.carrier.to_presentation`
- `simulator.carrier.to_parked`
- `simulator.carrier.hold`
- `motion.alarm.set` / `simulator.alarm.set`
- `motion.fault.set` / `simulator.fault.set`

### `simulator.command`

支持的文本命令:

- `reset`
- `door.open`
- `door.close`
- `lapbar.down`
- `lapbar.up`
- `carrier.presentation`
- `carrier.parked`
- `carrier.hold`
- `alarm.raise` / `alarm.clear`
- `fault.raise` / `fault.clear`
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
- `POST /api/instances/{index}/commands/lapbar-down`: 压杆下压
- `POST /api/instances/{index}/commands/lapbar-up`: 压杆抬起
- `POST /api/instances/{index}/commands/carrier-presentation`: 小车前进到演示位
- `POST /api/instances/{index}/commands/carrier-parked`: 小车回退到初始位
- `POST /api/instances/{index}/commands/carrier-hold`: 小车保持当前位置
- `POST /api/instances/{index}/commands/alarm-raise` / `alarm-clear`: 注入/清除告警（`DI[4]`）
- `POST /api/instances/{index}/commands/fault-raise` / `fault-clear`: 注入/清除故障（`DI[5]`）
- `POST /api/instances/{index}/commands/reset`: 复位

`web_enabled` 控制是否启动本地 HTTP 服务，`web_port` 控制监听端口。Windows 只监听 `127.0.0.1`；其它平台（Android）监听 `0.0.0.0`，便于通过局域网在 PC 浏览器上打开真机调试页。按设计不提供鉴权，属开发期工具：**Android 上监听 `0.0.0.0` 时同一局域网内任何人都可写入 DO/DI/AD 与执行机构命令，请仅在受控调试网络中启用，用完及时关闭 `web_enabled`。**

当前网页优先使用 `WebSocket` 双向连接 `GET /api/instances/{index}/ws`，失败时回退到 `SSE` 实时订阅 `GET /api/instances/{index}/events`，最后再回退到 HTTP 轮询，避免整页高频轮询带来的明显延迟。

### WebSocket API

- `GET /api/instances/{index}/ws`: 建立指定实例的双向调试通道
- 服务端消息 `capabilities`: `{ "type": "capabilities", "device_index": n, "payload": {...} }`
- 服务端消息 `snapshot`: `{ "type": "snapshot", "device_index": n, "payload": {...} }`
- 客户端命令 `io.set`: `{ "type": "io.set", "kind": "do|di|ad", "channel": n, "value": v }`
- 客户端命令 `command`: `{ "type": "command", "name": "open|close|lapbar_down|lapbar_up|carrier_presentation|carrier_parked|carrier_hold|alarm_raise|alarm_clear|fault_raise|fault_clear|reset" }`
- 命令名同时接受 snake_case / kebab-case（HTTP 路由会归一化），避免两条链路命名不一致

前端源码位于 `IOUI/SimulatorWeb/`，技术路线为 `Vue 3 + TypeScript + Vite + Pinia`，构建产物直接发布到 `IOUI/Config/Simulator/web/`；Windows / Android 包装层统一采用 `Tauri 2` 脚手架，复用同一份 Web 前端。

### 前端交互约定

- 三大布局：`自定义`（信号 / 蓝图）、`标准 IO`、`动感平台`，各自职责单一
- `高级 ▾` 入口收纳低频操作：联动规则、录制/回放、模板库、场景导入导出、诊断快照，主工具栏只保留高频动作
- 场景带作用域：`共享` / `当前预设` / `当前实例`，便于为不同实例或预设维护独立画布
- 自定义工作区支持撤销/重做（`Ctrl+Z` / `Ctrl+Y`），覆盖信号、布局与联动规则
- 所有面板的手动写入统一走同一路径，来源标记与变化高亮行为一致
- 动感平台面板新增 `安全状态注入`（告警/故障），并展示运动使能/急停/故障状态徽标

内置 UI、浏览器会话复用仍属于后续增量。

浏览器自动打开策略默认使用 `resume`，后续实现时必须通过会话心跳复用已有调试页，避免每次宿主启动都新建标签页。
