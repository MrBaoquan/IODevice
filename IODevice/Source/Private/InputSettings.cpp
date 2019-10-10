/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#include "InputSettings.h"
#include <string>
#include "IOStatics.h"
#include "Paths.hpp"
#include "rapidxml.hpp"
#include "rapidxml_utils.hpp"
#include "RawIOFactory.h"
#include "IOLog.h"
#include "IOApplication.h"

DevelopHelper::UInputSettings& DevelopHelper::UInputSettings::Instance()
{
    static UInputSettings instance;
    return instance;
}

int DevelopHelper::UInputSettings::Initialize()
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
        for (xml_node<>* device = root->first_node("Device");device;device = device->next_sibling())
        {
            // All devices.
            std::string IOType = device->first_attribute("Type") ? device->first_attribute("Type")->value() : IOType::Invalid;
            std::string DeviceName = device->first_attribute("Name") ? device->first_attribute("Name")->value() : IOType::Invalid;
            std::string DllName = device->first_attribute("DllName") ? device->first_attribute("DllName")->value() : IOType::Invalid;
            uint8 deviceIndex = static_cast<uint8>(GetNodeValue(device->first_attribute("Index") ? device->first_attribute("Index")->value() : "", 0));

            if (DeviceName == IOType::Invalid)
            {
                IOLog::Instance().Error("Please make sure that all of your Device node have a correct Name attribute. ");
                continue;
            }

            // Auto add standard device if no standard xml node in config file.
            if (IOType != IOType::Standard&&deviceID == 0)
            {
                if(!AddDevcie(DevicePropeties(0, IOType::Standard, IOType::Standard, "", 0)))
                {
                    IOLog::Instance().Error(std::string("Create standard device failed "));
                    return ErrorCode;
                }
                deviceID++;
                ActionMappings.push_back(std::vector<FInputActionKeyMapping>());
                AxisMappings.push_back(std::vector<FInputAxisKeyMapping>());
                KeyProperties.push_back(std::map<FKey, FInputKeyProperties, LessKey>());
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

            if (!AddDevcie(DevicePropeties(deviceID, IOType, DeviceName, DllName, deviceIndex)))
            {
                continue;
            }

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

                    keyProperty.DeadZone = GetNodeValue(Key->first_attribute("DeadZone") ? Key->first_attribute("DeadZone")->value() : "", 0.f);
                    keyProperty.bInvert = std::string(Key->first_attribute("Invert") ? Key->first_attribute("Invert")->value() : std::string("")) == "True" ? true : false;
                    keyProperty.bInvertEvent = std::string(Key->first_attribute("InvertEvent") ? Key->first_attribute("InvertEvent")->value() : std::string("")) == "True" ? true : false;
                    keyProperty.Exponent = GetNodeValue(Key->first_attribute("Exponent") ? Key->first_attribute("Exponent")->value() : "", 1.f);
                    keyProperty.Sensitivity = GetNodeValue(Key->first_attribute("Sensitivity") ? Key->first_attribute("Sensitivity")->value() : "", 1.f);
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
            deviceID++;
        }
        // Initlize KeyProperties of RawIO
        for(auto& deviceIt:IODevices::GetDevcies())
        {
            deviceIt.second.Initialize();
        }
    }
    catch (const std::runtime_error& e)
    {
        IOLog::Instance().Log(e.what());
        return ErrorCode;
    }
    catch (const rapidxml::parse_error& e)
    {
        IOLog::Instance().Log(std::string("XML syntax error: ")+e.what()+ " at " + configFilePath);
        return ErrorCode;
    }
    catch (const std::exception&)
    {
        std::string msg = std::string("Please make sure the file ") + configFilePath + " exists.";
        IOLog::Instance().Error(msg);
        return ErrorCode;
    }
    catch (...)
    {
        IOLog::Instance().Error("Unknow xml error.");
        return ErrorCode;
    }
    
    return SuccessCode;
}


int DevelopHelper::UInputSettings::Uninitialize()
{
	AxisMappings.clear();
	ActionMappings.clear();
	KeyProperties.clear();

	return 0;
}

const bool DevelopHelper::UInputSettings::HasAxis(uint8 deviceID, std::string axisName)
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

int DevelopHelper::UInputSettings::SetConfigPath(const char* InPath)
{
    customConfigPath = InPath;
    return 1;
}

const bool DevelopHelper::UInputSettings::HasAction(uint8 deviceID, std::string actionName)
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

bool DevelopHelper::UInputSettings::AddDevcie(DevicePropeties deviceProps)
{
    std::shared_ptr<RawIO> rawIO = RawIOFactory::CreateRawInput(deviceProps);
    if (!rawIO)
    {
        return false;
    }
    IODeviceDetails ID(deviceProps.Name, rawIO);
    IODevices::AddDevice(ID);
    return true;
}

float DevelopHelper::UInputSettings::GetNodeValue(const char* val, float defaultValue /*= 0.f*/)
{
    float newVal = defaultValue;
    std::string strVal(val);
    if (strVal != "")
    {
        newVal = static_cast<float>(std::atof(val));
    }
    return newVal;
}

