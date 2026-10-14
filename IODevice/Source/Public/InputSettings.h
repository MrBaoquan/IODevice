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
	 * 来自设备表的 <ChannelRate Name="OAxis_6" MaxPerSecond="0.5" />。
	 * 由 MotionPlayer 派发前查表限速 —— 按**通道**而非按设备: 同一台设备上
	 * 模拟量通道 (电缸) 需要限速, 开关量通道 (喷水/风扇) 必须瞬时开合。
	 * 缺省 (无该节点) = 不限速, 保持既有动作文件行为不变。
	 */
	std::map<std::string, float> ChannelRateLimits;

	/** 取某设备某通道的速率上限; 未配置返回 0 (= 不限速)。 */
	float GetChannelRateLimit(const std::string& deviceName, const std::string& channelName) const;

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