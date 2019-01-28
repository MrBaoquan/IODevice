/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#include "RawIOFactory.h"
#include <windows.h>
#include <algorithm>
#include <filesystem>
#include "RawIO/StandardIO.h"
#include "RawIO/ExternalIO.h"
#include "RawIO/Joystick.h"
#include "Paths.hpp"
#include "IOLog.h"
#include "IOStatics.h"

namespace fs = std::experimental::filesystem;



std::shared_ptr<DevelopHelper::RawIO> DevelopHelper::RawIOFactory::CreateRawInput(DevicePropeties deviceProps)
{
    if (deviceProps.Type == IOType::Standard)
    {
        uint8 standardDeviceCount = IODevices::GetDevicesCount(IOType::Standard);
        if (standardDeviceCount > 1)
        {
            IOLog::Instance().Warning("The count of <Standard> device can not more than one. ");
            return nullptr;
        }
        IOLog::Instance().Log(std::string("Create Standard IO <") + deviceProps.Name + "> succeed.");
        return std::make_shared<StandardIO>(deviceProps.DeviceID);
    }
    else if (deviceProps.Type == IOType::Joystick)
    {
        std::shared_ptr<Joystick> joystick = std::make_shared<DevelopHelper::Joystick>(deviceProps.DeviceID, deviceProps.DeviceIndex);
        if (joystick->Valid())
        {
            IOLog::Instance().Log(std::string("Create Joystick IO <") + deviceProps.Name + "> succeed.");
            return joystick;
        }
        IOLog::Instance().Warning(std::string("Create Joystick IO <") + deviceProps.Name + "> failed.");
        return joystick;
    }
    else if(deviceProps.Type == IOType::External)
    {
        std::string fullDllName = "";
#ifdef WIN_64
        fullDllName.append("IOUI-Win64-").append(deviceProps.DllName).append(".dll");
#else
        fullDllName.append("IOUI-Win32-").append(deviceProps.DllName).append(".dll");
#endif // WIN_64
        fullDllName = Paths::Instance().GetModuleDir()+ "ExternalLibraries\\" + fullDllName;    
        if(fs::exists(fullDllName))
        {
            std::shared_ptr<ExternalIO> externalIO = std::make_shared<ExternalIO>(deviceProps.DeviceID, deviceProps.DeviceIndex, fullDllName);
            if (externalIO->Valid())
            {
                IOLog::Instance().Log(std::string("Create External IO <") + deviceProps.DllName + "> succeed. index is:" + std::to_string(deviceProps.DeviceIndex));
            }else
            {
                IOLog::Instance().Warning(std::string("Create External IO <") + deviceProps.DllName + "> failed. index is:" + std::to_string(deviceProps.DeviceIndex));
            }
            return externalIO;
            
        }else
        {
            IOLog::Instance().Error(std::string("Please make sure that the file ") + fullDllName + " exists.");
            IOLog::Instance().Error(std::string("Create External IO <") + deviceProps.DllName + "> failed. index is:" + std::to_string(deviceProps.DeviceIndex));
            return nullptr;
        }
    }
    else 
    {
        IOLog::Instance().Warning(std::string("Failed to create raw IO, invalid device type : ") + deviceProps.Type);
        return nullptr;
    }
}
