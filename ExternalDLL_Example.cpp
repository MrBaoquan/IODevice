/** 
 * ============================================================================
 * EXAMPLE CODE ONLY - DO NOT COMPILE THIS FILE DIRECTLY
 * ============================================================================
 * 
 * 外部 DLL 开发示例
 * 展示如何实现 GetDeviceAD_INT 接口以支持 int32_t 数据类型
 * 
 * 编译说明：
 * 1. 老版本 DLL：只需要实现 GetDeviceAD (short*)
 * 2. 新版本 DLL：实现 GetDeviceAD_INT (int32_t*)，可选实现 GetDeviceAD 以兼容老版本
 * 
 * 注意：这是示例代码，不能直接编译！
 * 请根据实际硬件 SDK 替换示例函数（如 ReadADC24Bit、GetEncoderCount 等）
 * 
 * ============================================================================
 */

#if 0  // Set to 1 to enable example code compilation (after implementing hardware functions)

#include <cstdint>

// Forward declarations - These are example functions, replace with your actual hardware SDK
int32_t ReadADC24Bit();
int32_t GetEncoderCount();
int32_t ReadRawData();
int32_t ReadAnalogChannel(int channel);
uint16_t ReadModbusRegister(int address);
int32_t ReadModbusInt32();

// 设备信息结构
struct DeviceInfo
{
    unsigned char InputCount = 16;   // 数字输入通道数
    unsigned char OutputCount = 16;  // 数字输出通道数
    unsigned char AxisCount = 0;     // 模拟输入通道数
};

// ============================================================================
// 示例1：Modbus 设备 - 使用 int32_t 存储原始寄存器数据
// ============================================================================
extern "C" __declspec(dllexport) int GetDeviceAD_INT(uint8_t deviceIndex, int32_t* adValues)
{
    // 场景1：读取 Modbus 32-bit 整数（2个寄存器拼接）
    // 假设从 Modbus 设备读取两个 16-bit 寄存器并拼接为 32-bit
    uint16_t highRegister = 0x1234;  // 从设备读取高位寄存器
    uint16_t lowRegister = 0x5678;   // 从设备读取低位寄存器
    adValues[0] = (static_cast<int32_t>(highRegister) << 16) | lowRegister;
    
    // 场景2：读取 Modbus Float（IEEE 754）
    float temperature = 25.6f;  // 从温度传感器读取
    // 将 float 的二进制表示存入 int32_t
    adValues[1] = *reinterpret_cast<int32_t*>(&temperature);
    
    // 场景3：高精度 ADC 原始值
    adValues[2] = 123456789;  // 直接存储大范围整数
    
    return 1;  // 成功返回 1
}

// ============================================================================
// 示例2：高精度采集卡 - 24-bit ADC
// ============================================================================
extern "C" __declspec(dllexport) int GetDeviceAD_INT(uint8_t deviceIndex, int32_t* adValues)
{
    // 读取 24-bit ADC（范围：-8388608 ~ 8388607）
    int32_t rawADC = ReadADC24Bit();  // Replace with your actual hardware SDK function
    adValues[0] = rawADC;  // 直接存储，保留完整精度
    
    return 1;
}

// ============================================================================
// 示例3：编码器 - 高精度位置计数
// ============================================================================
extern "C" __declspec(dllexport) int GetDeviceAD_INT(uint8_t deviceIndex, int32_t* adValues)
{
    // 读取编码器位置（可能是很大的计数值）
    int32_t encoderPosition = GetEncoderCount();  // Replace with your actual hardware SDK function
    adValues[0] = encoderPosition;
    
    return 1;
}

// ============================================================================
// 示例4：兼容性实现 - 同时提供新旧两个接口
// ============================================================================

// 老接口：为兼容老版本 IOToolkit
extern "C" __declspec(dllexport) int GetDeviceAD(uint8_t deviceIndex, short* adValues)
{
    // 读取数据并转换为 short（可能有范围限制）
    int32_t rawValue = ReadRawData();  // Replace with your actual hardware SDK function
    
    // 限制在 short 范围内
    if (rawValue > 32767) rawValue = 32767;
    if (rawValue < -32768) rawValue = -32768;
    
    adValues[0] = static_cast<short>(rawValue);
    return 1;
}

// 新接口：推荐使用
extern "C" __declspec(dllexport) int GetDeviceAD_INT(uint8_t deviceIndex, int32_t* adValues)
{
    // 直接返回完整的 int32_t 数据，无需截断
    adValues[0] = ReadRawData();  // Replace with your actual hardware SDK function
    return 1;
}

// ============================================================================
// 示例5：如何在 int32_t 和 float 之间转换
// ============================================================================

// 方式1：将 int32_t 解释为 float
void Example_IntToFloat()
{
    int32_t storedValue = 0x41CB3333;  // 存储的二进制数据
    float actualValue = *reinterpret_cast<float*>(&storedValue);  // 解释为 float: 25.4
}

// 方式2：将 float 存储为 int32_t
void Example_FloatToInt()
{
    float sensor_value = 36.5f;
    int32_t storedValue = *reinterpret_cast<int32_t*>(&sensor_value);  // 保留二进制表示
}

// ============================================================================
// 完整的 DLL 实现模板
// ============================================================================

static DeviceInfo g_deviceInfo;

extern "C" __declspec(dllexport) DeviceInfo* Initialize()
{
    g_deviceInfo.InputCount = 16;
    g_deviceInfo.OutputCount = 16;
    g_deviceInfo.AxisCount = 8;  // 8个模拟输入通道
    return &g_deviceInfo;
}

extern "C" __declspec(dllexport) int OpenDevice(uint8_t deviceIndex)
{
    // 打开设备的初始化代码
    return 1;  // 成功返回 1
}

extern "C" __declspec(dllexport) int CloseDevice(uint8_t deviceIndex)
{
    // 关闭设备的清理代码
    return 1;
}

extern "C" __declspec(dllexport) int GetDeviceDI(uint8_t deviceIndex, unsigned char* diValues)
{
    // 读取数字输入
    for (int i = 0; i < 16; i++)
    {
        diValues[i] = 0;  // 从硬件读取
    }
    return 1;
}

extern "C" __declspec(dllexport) int SetDeviceDO(uint8_t deviceIndex, short* doValues)
{
    // 设置数字输出
    for (int i = 0; i < 16; i++)
    {
        // 写入硬件
    }
    return 1;
}

extern "C" __declspec(dllexport) int GetDeviceDO(uint8_t deviceIndex, short* doValues)
{
    // 读取数字输出状态
    for (int i = 0; i < 16; i++)
    {
        doValues[i] = 0;  // 从硬件读取
    }
    return 1;
}

// 新版本接口（推荐）
extern "C" __declspec(dllexport) int GetDeviceAD_INT(uint8_t deviceIndex, int32_t* adValues)
{
    // 读取模拟输入 - 使用 int32_t 存储原始数据
    for (int i = 0; i < 8; i++)
    {
        adValues[i] = ReadAnalogChannel(i);  // Replace with your actual hardware SDK function
    }
    return 1;
}

// 老版本接口（可选，用于向后兼容）
extern "C" __declspec(dllexport) int GetDeviceAD(uint8_t deviceIndex, short* adValues)
{
    // 读取模拟输入 - 转换为 short（可能有范围限制）
    for (int i = 0; i < 8; i++)
    {
        int32_t rawValue = ReadAnalogChannel(i);  // Replace with your actual hardware SDK function
        // 限制在 short 范围内
        if (rawValue > 32767) rawValue = 32767;
        if (rawValue < -32768) rawValue = -32768;
        adValues[i] = static_cast<short>(rawValue);
    }
    return 1;
}

#endif  // End of example code
