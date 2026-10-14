/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#include "InputSettings.h"
#include <string>
#include <cfloat>
#include "IOStatics.h"
#include "Paths.hpp"
#include "rapidxml.hpp"
#include "rapidxml_utils.hpp"
#include "RawIOFactory.h"
#include "IOLog.h"
#include "IOApplication.h"
#include "IODiag.h"

IOToolkit::UInputSettings& IOToolkit::UInputSettings::Instance()
{
    static UInputSettings instance;
    return instance;
}

int IOToolkit::UInputSettings::Initialize()
{
    using namespace rapidxml;
    
    
    std::string configDir = Paths::Instance().GetConfigDir();

    std::string configFilePath = "";
    if (customConfigPath == "Invalid")
    {
        configFilePath = configDir + "IODevice.xml";
    }else
    {
        configFilePath = customConfigPath;
    }
    
    try
    {
        file<> fdoc(configFilePath.data());
        xml_document<> doc;
        doc.parse<0>(fdoc.data());
        xml_node<>* root = doc.first_node();
        uint8 deviceID = 0;
        IODIAG("UInputSettings::Initialize parsing configFilePath=%s", configFilePath.c_str());

        for (xml_node<>* device = root->first_node("Device");device;device = device->next_sibling())
        {
            // All devices.
            std::string IOType = device->first_attribute("Type") ? device->first_attribute("Type")->value() : IOType::Invalid;
            std::string DeviceName = device->first_attribute("Name") ? device->first_attribute("Name")->value() : IOType::Invalid;
            std::string DllName = device->first_attribute("DllName") ? device->first_attribute("DllName")->value() : IOType::Invalid;
            uint8 deviceIndex = static_cast<uint8>(GetNodeValue(device->first_attribute("Index") ? device->first_attribute("Index")->value() : "", 0));

            if (DeviceName == IOType::Invalid || IODevices::HasDevice(DeviceName))
            {
                IOLog::Instance().Error("Please make sure that all of your Device node have a correct Name attribute. ");
                continue;
            }

            // Auto add standard device if no standard xml node in config file.
            if (IOType != IOType::Standard&&deviceID == 0)
            {
                if(!AddDevice(DeviceProperties(0, IOType::Standard, IOType::Standard, "", 0)))
                {
                    // 该平台没有 Standard 设备(Android 无键盘/鼠标)。这里必须跳过而非中止:
                    // 中止会让整个设备表为空, 之后所有 IO 查询都落到 Invalid, 现象是
                    // 「IO 全部静默失效」而不是报错。槽位编号照旧递增, 与映射表下标保持一致。
                    IODIAG("Initialize Standard skipped (not available on this platform)");
                    IOLog::Instance().Warning(std::string("Create standard device skipped (not available on this platform)"));
                }
                deviceID++;
                ActionMappings.push_back(std::vector<FInputActionKeyMapping>());
                AxisMappings.push_back(std::vector<FInputAxisKeyMapping>());
                KeyProperties.push_back(std::map<FKey, FInputKeyProperties, LessKey>());
                OActionMappings.push_back(std::map<std::string, std::vector<FOutputActionKey>>());
            }

            // if standard device node not at the top position.
            if (IOType == IOType::Standard&&deviceID != 0)
            {

                IOLog::Instance().Error(std::string("You must put the standard device node at the top position in ")+configFilePath);
                return ErrorCode;
            }

            if (IOType == IOType::External)
            {
                if (DllName == IOType::Invalid)
                {
                    IOLog::Instance().Error(std::string("Please make sure the External device ") + DeviceName + " has a DllName attribute. ");
                    continue;
                }
            }

            if (!AddDevice(DeviceProperties(deviceID, IOType, DeviceName, DllName, deviceIndex)))
            {
                IODIAG("Initialize AddDevice FAILED name=%s dll=%s type=%s", DeviceName.c_str(), DllName.c_str(), IOType.c_str());
                continue;
            }
            IODIAG("Initialize AddDevice OK name=%s mapSize=%zu", DeviceName.c_str(), IODevices::GetDevcies().size());

            ActionMappings.push_back(std::vector<FInputActionKeyMapping>());
            AxisMappings.push_back(std::vector<FInputAxisKeyMapping>());
            KeyProperties.push_back(std::map<FKey, FInputKeyProperties, LessKey>());

            // All custom properties.
            xml_node<>* properties = device->first_node("Properties");
            if (properties)
            {
                for (xml_node<>* Key = properties->first_node("Key");Key;Key = Key->next_sibling("Key"))
                {
                    std::string keyName = Key->first_attribute("Name") ? Key->first_attribute("Name")->value() : IOType::Invalid;
                    if (keyName == IOType::Invalid)
                    {
                        IOLog::Instance().Warning(std::string("Please make sure that all of your Key node in ") + DeviceName + "/Properties have a correct Name attribute. ");
                        continue;
                    }
                    FInputKeyProperties keyProperty;

                    // 兼容旧版 PreOffset/PreScale 和新版 Offset/Scale
                    auto offsetAttr = Key->first_attribute("Offset") ? Key->first_attribute("Offset") : Key->first_attribute("PreOffset");
                    auto scaleAttr = Key->first_attribute("Scale") ? Key->first_attribute("Scale") : Key->first_attribute("PreScale");
					keyProperty.Offset = GetNodeValue(offsetAttr ? offsetAttr->value() : "", 0.f);
					keyProperty.Scale = GetNodeValue(scaleAttr ? scaleAttr->value() : "", 1.f);
					keyProperty.DeadZone = GetNodeValue(Key->first_attribute("DeadZone") ? Key->first_attribute("DeadZone")->value() : "", 0.f);
					keyProperty.bInvert = std::string(Key->first_attribute("Invert") ? Key->first_attribute("Invert")->value() : std::string("")) == "True" ? true : false;
                    keyProperty.bInvertEvent = std::string(Key->first_attribute("InvertEvent") ? Key->first_attribute("InvertEvent")->value() : std::string("")) == "True" ? true : false;
                    keyProperty.Exponent = GetNodeValue(Key->first_attribute("Exponent") ? Key->first_attribute("Exponent")->value() : "", 1.f);
                    keyProperty.Sensitivity = GetNodeValue(Key->first_attribute("Sensitivity") ? Key->first_attribute("Sensitivity")->value() : "", 1.f);

					keyProperty.Min = GetNodeValue(Key->first_attribute("Min") ? Key->first_attribute("Min")->value() : "", -FLT_MAX);
					keyProperty.Max = GetNodeValue(Key->first_attribute("Max") ? Key->first_attribute("Max")->value() : "", FLT_MAX);

                    // 变化速率上限 (归一化值/秒, 见 PlayerInput.h 的 FInputKeyProperties::SlewRate)。
                    // 它是<b>全局 Key 属性</b> —— 配一次, 所有引用该通道的 Action/Axis/OAction
                    // 下的 Key 都继承, 不必逐个重复。0/缺省 = 不限速, 保持既有行为不变。
                    //
                    // 兼容: 早期版本这个属性叫 MaxPerSecond (字面像"每秒的最大值", 易误读), 一并认。
                    auto slewAttr = Key->first_attribute("SlewRate")
                        ? Key->first_attribute("SlewRate")
                        : Key->first_attribute("MaxPerSecond");
                    float slewRate = GetNodeValue(slewAttr ? slewAttr->value() : "", 0.f);
                    if (slewRate > 0.f)
                    {
                        keyProperty.SlewRate = slewRate;
                        ChannelRateLimits[DeviceName + "|" + keyName] = slewRate;
                    }

                    KeyProperties[deviceID].insert(std::pair<FKey, FInputKeyProperties>(FKey(keyName.data()), keyProperty));
                }
            }

            // All actions.
            for (xml_node<>* action = device->first_node("Action");action;action = action->next_sibling("Action"))
            {
                std::string actionName = action->first_attribute("Name") ? action->first_attribute("Name")->value() : IOType::Invalid;
                if (actionName == IOType::Invalid)
                {
                    IOLog::Instance().Warning(std::string("Please make sure that all of your Action node in ") + DeviceName + " have a correct Name attribute. ");
                    continue;
                }
                for (xml_node<>* key = action->first_node("Key");key;key = key->next_sibling("Key"))
                {
                    std::string keyName = key->first_attribute("Name") ? key->first_attribute("Name")->value() : IOType::Invalid;
                    if (keyName == IOType::Invalid)
                    {
                        IOLog::Instance().Warning(std::string("Please make sure that all of your Key node in ") + DeviceName + "/" + actionName + " have a correct Name attribute. ");
                        continue;
                    }
                    FInputActionKeyMapping keyMapping(actionName, FKey(keyName.data()));
                    ActionMappings[deviceID].push_back(keyMapping);
                }
            }

            // All axis
            for (xml_node<>* axis = device->first_node("Axis");axis;axis = axis->next_sibling("Axis"))
            {
                std::string axisName = axis->first_attribute("Name") ? axis->first_attribute("Name")->value() : IOType::Invalid;
                if (axisName == IOType::Invalid)
                {
                    IOLog::Instance().Warning(std::string("Please make sure that all of your Axis node in ") + DeviceName + " have a correct Name attribute. ");
                    continue;
                }
                for (xml_node<>* key = axis->first_node("Key");key;key = key->next_sibling("Key"))
                {
                    std::string keyName = key->first_attribute("Name") ? key->first_attribute("Name")->value() : IOType::Invalid;
                    if (keyName == IOType::Invalid)
                    {
                        IOLog::Instance().Warning(std::string("Please make sure that all of your Key node in ") + DeviceName + "/" + axisName + " have a correct Name attribute. ");
                        continue;
                    }
                    float scale = GetNodeValue(key->first_attribute("Scale") ? key->first_attribute("Scale")->value() : "", 1.f);
                    FInputAxisKeyMapping axisMapping(axisName, FKey(keyName.data()), scale);
                    AxisMappings[deviceID].push_back(axisMapping);
                }
            }

			// All oactions
			std::map<std::string, std::vector<FOutputActionKey>> _oactions;
			for (xml_node<>* axis = device->first_node("OAction"); axis; axis = axis->next_sibling("OAction"))
			{
				std::string axisName = axis->first_attribute("Name") ? axis->first_attribute("Name")->value() : IOType::Invalid;
				if (axisName == IOType::Invalid)
				{
					IOLog::Instance().Warning(std::string("Please make sure that all of your Axis node in ") + DeviceName + " have a correct Name attribute. ");
					continue;
				}
				
				std::vector<FOutputActionKey> _oactionKeys;
				for (xml_node<>* key = axis->first_node("Key"); key; key = key->next_sibling("Key"))
				{
					std::string keyName = key->first_attribute("Name") ? key->first_attribute("Name")->value() : IOType::Invalid;
					if (keyName == IOType::Invalid)
					{
						IOLog::Instance().Warning(std::string("Please make sure that all of your Key node in ") + DeviceName + "/" + axisName + " have a correct Name attribute. ");
						continue;
					}
					float scale = GetNodeValue(key->first_attribute("Scale") ? key->first_attribute("Scale")->value() : "", 1.f);
					bool _invertEvent = std::string(key->first_attribute("InvertEvent") ? key->first_attribute("InvertEvent")->value() : std::string("")) == "True" ? true : false;
					_oactionKeys.push_back(FOutputActionKey(FKey(keyName.data()), scale,_invertEvent));
				}
				_oactions.insert(std::pair<std::string, std::vector<FOutputActionKey>>(axisName, _oactionKeys));
			}
			OActionMappings.push_back(_oactions);


            deviceID++;
        }
        // Initlize KeyProperties of RawIO
        {
            std::lock_guard<std::recursive_mutex> registryLock(IODevices::RegistryMutex());
            for(auto& deviceIt:IODevices::GetDevcies())
            {
                deviceIt.second.Initialize();
            }
        }
    }
    catch (const std::runtime_error& e)
    {
        IODIAG("Initialize RUNTIME_ERROR: %s", e.what());
        IOLog::Instance().Log(e.what());
        return ErrorCode;
    }
    catch (const rapidxml::parse_error& e)
    {
        IODIAG("Initialize PARSE_ERROR: %s at %s", e.what(), configFilePath.c_str());
        IOLog::Instance().Log(std::string("XML syntax error: ")+e.what()+ " at " + configFilePath);
        return ErrorCode;
    }
    catch (const std::exception& e)
    {
        IODIAG("Initialize STD_EXCEPTION: %s (file=%s)", e.what(), configFilePath.c_str());
        std::string msg = std::string("Please make sure the file ") + configFilePath + " exists.";
        IOLog::Instance().Error(msg);
        return ErrorCode;
    }
    catch (...)
    {
        IODIAG("Initialize UNKNOWN_ERROR (file=%s)", configFilePath.c_str());
        IOLog::Instance().Error("Unknow xml error.");
        return ErrorCode;
    }
    
    return SuccessCode;
}


int IOToolkit::UInputSettings::Uninitialize()
{
	AxisMappings.clear();
	ActionMappings.clear();
	KeyProperties.clear();
	OActionMappings.clear();

	return 0;
}

const bool IOToolkit::UInputSettings::HasAxis(uint8 deviceID, std::string axisName)
{
    if (deviceID < IODevices::GetDevicesCount())
    {
        for (auto& axis : AxisMappings[deviceID])
        {
            if (axis.AxisName == axisName)
            {
                return true;
            }
        }
    }
    return false;
}

const bool IOToolkit::UInputSettings::HasOAction(uint8 deviceID, std::string oActionName)
{
    if (deviceID < IODevices::GetDevicesCount())
    {
        auto& _oactions = OActionMappings[deviceID];
        if (_oactions.find(oActionName) != _oactions.end()) return true;
    }
    return false;
}

float IOToolkit::UInputSettings::GetChannelRateLimit(const std::string& deviceName, const std::string& channelName) const
{
    auto it = ChannelRateLimits.find(deviceName + "|" + channelName);
    return it != ChannelRateLimits.end() ? it->second : 0.f;
}

int IOToolkit::UInputSettings::SetConfigPath(const char* InPath)
{
    customConfigPath = InPath;
    return 1;
}

const bool IOToolkit::UInputSettings::HasAction(uint8 deviceID, std::string actionName)
{
    if (deviceID < IODevices::GetDevicesCount())
    {
        for (auto& action : ActionMappings[deviceID])
        {
            if (action.ActionName == actionName)
            {
                return true;
            }
        }
    }
    return false;
}

bool IOToolkit::UInputSettings::AddDevice(DeviceProperties deviceProps)
{
    std::shared_ptr<RawIO> rawIO = RawIOFactory::CreateRawInput(deviceProps);
    if (!rawIO)
    {
        return false;
    }
    IODeviceDetails ID(deviceProps, rawIO);
    IODevices::AddDevice(ID);
    return true;
}

float IOToolkit::UInputSettings::GetNodeValue(const char* val, float defaultValue /*= 0.f*/)
{
    float newVal = defaultValue;
    std::string strVal(val);
    if (strVal != "")
    {
        newVal = static_cast<float>(std::atof(val));
    }
    return newVal;
}

