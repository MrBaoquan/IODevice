# GetDeviceAD 接口升级说明

## 修改概述

为了支持更大范围的数据（超出 `short` 的 -32,768 ~ 32,767 范围），新增了 `GetDeviceAD_INT` 接口，使用 `int32_t` 类型（范围：-2,147,483,648 ~ 2,147,483,647），同时保持对老版本 DLL 的完全兼容。

## 修改文件列表

### 1. 头文件修改

#### `ExternalIO.h`
- ✅ 新增 `DECLARE_FUNCTION2(int, GetDeviceAD_INT, uint8, int32_t*)`
- ✅ 新增 `bool HasGetDeviceAD_INT()` 版本检测函数
- ✅ 新增 `int GetDeviceAD(std::vector<int32_t>& OutADStatus)` 重载函数
- ✅ 新增 `std::vector<int32_t> ADStatusInt` 成员变量

#### `RawIO.h`
- ✅ 新增 `void DispatchAxisEvent(std::vector<int32_t> InAxis)` 重载函数

### 2. 实现文件修改

#### `ExternalIO.cpp`
- ✅ 修改 `Tick()` 函数：自动检测 DLL 版本，优先使用 `GetDeviceAD_INT`
- ✅ 新增 `GetDeviceAD(std::vector<int32_t>&)` 实现
- ✅ 修改 `Constructor()`：初始化 `ADStatusInt` 向量

#### `RawIO.cpp`
- ✅ 新增 `DispatchAxisEvent(std::vector<int32_t>)` 实现

## 兼容性设计

### 版本检测机制

```cpp
// 在 Tick() 中自动检测 DLL 版本
if (externalDll.HasGetDeviceAD_INT())
{
    // V2: 使用新版本 int32_t 接口
    if (GetDeviceAD(ADStatusInt)) 
    {
        DispatchAxisEvent(ADStatusInt);
    }
}
else
{
    // V1: 使用老版本 short 接口
    if (GetDeviceAD(ADStatus)) 
    {
        DispatchAxisEvent(ADStatus);
    }
}
```

### 三种使用场景

| DLL 类型 | 实现接口 | IOToolkit 行为 |
|---------|---------|----------------|
| **老版本 DLL** | 仅实现 `GetDeviceAD(short*)` | 自动使用 short 接口 |
| **新版本 DLL** | 仅实现 `GetDeviceAD_INT(int32_t*)` | 自动使用 int32_t 接口 |
| **兼容版本 DLL** | 同时实现两个接口 | 优先使用 int32_t 接口 |

## 数据类型选择理由

### 为什么选择 `int32_t` 而不是 `float`？

| 特性 | int32_t | float | short |
|------|---------|-------|-------|
| **范围** | -2,147,483,648 ~ 2,147,483,647 | ±3.4×10³⁸ | -32,768 ~ 32,767 |
| **精度** | 整数精确 | ~7位有效数字 | 整数精确 |
| **原始数据保留** | ✅ 完整保留 | ❌ 浮点转换 | ⚠️ 范围不足 |
| **Modbus 兼容** | ✅ 2个寄存器 | ✅ IEEE 754 | ✅ 1个寄存器 |
| **位操作** | ✅ 方便 | ❌ 不直观 | ✅ 方便 |
| **灵活转换** | ✅ 可转 float | - | ⚠️ 范围受限 |

### 适用场景

✅ **推荐使用 int32_t 的场景：**
- Modbus RTU/TCP 设备（32-bit 寄存器）
- 高精度采集卡（24-bit ADC）
- 编码器计数器（大范围计数）
- 需要保留原始二进制数据
- 需要进行位操作

⚠️ **可以继续使用 short 的场景：**
- 老版本设备
- 数据范围在 ±32,767 以内
- 不需要升级的老项目

## 外部 DLL 开发指南

### 1. 新版本 DLL（推荐）

```cpp
// 实现 int32_t 接口
extern "C" __declspec(dllexport) int GetDeviceAD_INT(uint8_t deviceIndex, int32_t* adValues)
{
    // 场景1：Modbus 整数
    adValues[0] = ReadModbusInt32();
    
    // 场景2：Modbus Float（保留二进制）
    float temp = 25.6f;
    adValues[1] = *reinterpret_cast<int32_t*>(&temp);
    
    // 场景3：高精度 ADC
    adValues[2] = ReadADC24Bit();
    
    return 1;
}
```

### 2. 兼容版本 DLL

```cpp
// 老接口（向后兼容）
extern "C" __declspec(dllexport) int GetDeviceAD(uint8_t deviceIndex, short* adValues)
{
    int32_t value = ReadDevice();
    // 限制范围
    if (value > 32767) value = 32767;
    if (value < -32768) value = -32768;
    adValues[0] = static_cast<short>(value);
    return 1;
}

// 新接口（推荐）
extern "C" __declspec(dllexport) int GetDeviceAD_INT(uint8_t deviceIndex, int32_t* adValues)
{
    adValues[0] = ReadDevice();  // 完整数据
    return 1;
}
```

### 3. 老版本 DLL

```cpp
// 仅实现 short 接口（无需修改）
extern "C" __declspec(dllexport) int GetDeviceAD(uint8_t deviceIndex, short* adValues)
{
    adValues[0] = ReadDevice();
    return 1;
}
```

## 数据转换示例

### int32_t ↔ float 转换

```cpp
// int32_t 解释为 float
int32_t stored = 0x41C80000;
float value = *reinterpret_cast<float*>(&stored);  // 25.0f

// float 存储为 int32_t
float temp = 36.5f;
int32_t stored = *reinterpret_cast<int32_t*>(&temp);
```

### int32_t 归一化

```cpp
// 如果需要归一化到 [-1.0, 1.0]
int32_t rawValue = 1073741824;  // int32_t 值
float normalized = rawValue / 2147483647.0f;  // 归一化

// 或者自定义范围
float scaled = rawValue / 1000000.0f;  // 自定义缩放
```

## 迁移路径

### 对于现有项目

1. **不需要立即升级**：老版本 DLL 仍然完全兼容
2. **渐进式升级**：可以逐步将外部 DLL 升级到 `GetDeviceAD_INT`
3. **无需修改配置**：IOToolkit 会自动检测并使用正确的接口

### 对于新项目

1. **推荐使用 `GetDeviceAD_INT`**：获得更大的数据范围和灵活性
2. **保留原始数据**：使用 `int32_t` 存储，按需转换
3. **参考示例**：查看 `ExternalDLL_Example.cpp`

## 注意事项

1. **内存安全**：确保 `adValues` 数组有足够空间（至少 `AxisCount` 个元素）
2. **字节序**：Modbus 数据注意大端/小端字节序
3. **返回值**：成功返回 1，失败返回 0 或负数
4. **线程安全**：如果 DLL 在多线程环境中使用，注意加锁

## 常见问题

### Q1: 老版本 DLL 还能用吗？
**A:** 可以！IOToolkit 会自动检测并使用老版本接口，无需任何修改。

### Q2: 必须升级到 int32_t 吗？
**A:** 不必须。如果你的数据范围在 ±32,767 以内，可以继续使用 short。

### Q3: 如何知道我的 DLL 用的是哪个版本？
**A:** IOToolkit 会在运行时自动检测。你也可以查看 DLL 是否导出了 `GetDeviceAD_INT` 函数。

### Q4: 如果同时实现两个接口会怎样？
**A:** IOToolkit 会优先使用 `GetDeviceAD_INT`（新版本），但仍能兼容老版本客户端。

### Q5: 如何处理 Modbus Float？
**A:** 将 float 的二进制表示存入 int32_t：
```cpp
float value = 25.6f;
adValues[0] = *reinterpret_cast<int32_t*>(&value);
```

## 示例代码

完整的外部 DLL 开发示例请参考：`ExternalDLL_Example.cpp`

## 版本历史

- **v2.x.y**: 新增 `GetDeviceAD_INT` 接口，支持 int32_t 数据类型
- **v1.x**: 原始版本，使用 short 类型
