/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include <vector>
#include "InputCoreTypes.h"
#include "PlayerInput.h"

namespace DevelopHelper
{

class UInputSettings
{
public:

    static UInputSettings& Instance();
    int Initialize();

public:
    /** List of Action Mappings */
    std::vector<std::vector<struct FInputActionKeyMapping>> ActionMappings;
    
    /** List of Action Mappings */
    std::vector<std::vector<struct FInputAxisKeyMapping>> AxisMappings;

    /** Internal structure for storing axis config data. */
    std::vector<std::map<FKey, FInputKeyProperties, LessKey>> KeyProperties;

    const bool HasAction(uint8 deviceID, std::string actionName);
    const bool HasAxis(uint8 deviceID, std::string axisName);

    int SetConfigPath(const char* InPath);

private:
    UInputSettings() {}
    ~UInputSettings(){}

    bool AddDevcie(struct DevicePropeties deviceProps);
    float GetNodeValue(const char* val,float defaultValue = 0.f);

    std::string customConfigPath = "Invalid";
};



};