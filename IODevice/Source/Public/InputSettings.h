/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include <vector>
#include "InputCoreTypes.h"
#include "PlayerInput.h"

namespace IOToolkit
{

class UInputSettings
{
public:

    static UInputSettings& Instance();
    int Initialize();
	int Uninitialize();

public:
    /** List of Action Mappings */
    std::vector<std::vector<struct FInputActionKeyMapping>> ActionMappings;
    
    /** List of Action Mappings */
    std::vector<std::vector<struct FInputAxisKeyMapping>> AxisMappings;

	/**
	 * List of OAction Mappings
	 */
	std::vector<std::map<std::string,std::vector<struct FOutputActionKey>>> OActionMappings;

	/**
	 * 通道速率上限 (归一化值/秒), 按 "设备名|通道名" 索引。
	 *
	 * 来自设备表 <Properties><Key Name="OAxis_06" SlewRate="0.5" /> 里的 SlewRate
	 * (旧写法 <ChannelRate Name="OAxis_6" MaxPerSecond="0.5" /> 仍兼容)。
	 * 由 MotionPlayer 派发前查表限速 —— 按**通道**而非按设备: 同一台设备上
	 * 模拟量通道 (电缸) 需要限速, 开关量通道 (喷水/风扇) 必须瞬时开合。
	 * 缺省 (无该属性) = 不限速, 保持既有动作文件行为不变。
	 *
	 * 除物理 Key 名外, 解析时还会把速率补挂到引用该 Key 的 **OAction 名**下 ——
	 * 按 OAction 下发的轨道 (output_type="oaction") 用 OAction 名作派发键, 不补挂就查不到。
	 */
	std::map<std::string, float> ChannelRateLimits;

	/** 取某设备某通道的速率上限; 未配置返回 0 (= 不限速)。 */
	float GetChannelRateLimit(const std::string& deviceName, const std::string& channelName) const;

	/**
	 * 速率表的键: "设备名|规范化通道名"。
	 *
	 * <para><b>为什么必须规范化</b>: 通道语义是按**编号**解析的
	 * (<c>OAxis_6</c> 与 <c>OAxis_06</c> 都是通道 6), 但速率表是按**字符串**查的。
	 * 两边口径不一致时, 表里写 <c>OAxis_6</c>、轨道写 <c>OAxis_06</c>, 就会查不到
	 * → 限速被静默跳过 (不报错、只是阶跃一步跳到底)。
	 * 注册与查询都必须走本函数, 保证只有一个口径。</para>
	 */
	static std::string MakeChannelRateKey(const std::string& deviceName, const std::string& channelName);

    /** Internal structure for storing axis config data. */
    std::vector<std::map<FKey, FInputKeyProperties, LessKey>> KeyProperties;

    const bool HasAction(uint8 deviceID, std::string actionName);
    const bool HasAxis(uint8 deviceID, std::string axisName);
    const bool HasOAction(uint8 deviceID, std::string oactionName);

    int SetConfigPath(const char* InPath);

private:
    UInputSettings() {}
    ~UInputSettings(){}

    bool AddDevice(struct DeviceProperties deviceProps);
    float GetNodeValue(const char* val,float defaultValue = 0.f);

    std::string customConfigPath = "Invalid";
};



};