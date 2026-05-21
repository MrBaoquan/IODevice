/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#include "RawIOFactory.h"
#include "IOPlatform.h"
#if IODEVICE_PLATFORM_WINDOWS
#include <windows.h>
#endif
#include <algorithm>
#include <cctype>
#include <filesystem>
#if IODEVICE_PLATFORM_WINDOWS
#include "RawIO/StandardIO.h"
#include "RawIO/Joystick.h"
#endif
#include "RawIO/ExternalIO.h"
#include "Paths.hpp"
#include "IOLog.h"
#include "IOStatics.h"

namespace fs = std::filesystem;

namespace
{
    class PlatformPluginNaming
    {
    public:
        static std::string Resolve(const std::string& pluginName)
        {
#if IODEVICE_PLATFORM_WINDOWS
#if defined(WIN_64) || IODEVICE_PLATFORM_64BIT
            return std::string("IOUI-Win64-").append(pluginName).append(".dll");
#else
            return std::string("IOUI-Win32-").append(pluginName).append(".dll");
#endif
#else
            std::string normalizedName = pluginName;
            std::transform(normalizedName.begin(), normalizedName.end(), normalizedName.begin(), [](unsigned char ch) {
                return static_cast<char>(std::toupper(ch));
            });
            return std::string("IOUI-ANDROID-").append(normalizedName).append(".so");
#endif
        }
    };

    std::string ResolveExternalPluginPath(const std::string& pluginName)
    {
        fs::path pluginPath = fs::path(IOToolkit::Paths::Instance().GetExternalLibrariesDir()) / PlatformPluginNaming::Resolve(pluginName);
        return pluginPath.string();
    }
}


std::shared_ptr<IOToolkit::RawIO> IOToolkit::RawIOFactory::CreateRawInput(DeviceProperties deviceProps)
{
    if (deviceProps.Type == IOType::Standard)
    {
#if IODEVICE_PLATFORM_WINDOWS
        uint8 standardDeviceCount = IODevices::GetDevicesCount(IOType::Standard);
        if (standardDeviceCount > 1)
        {
            IOLog::Instance().Warning("The count of <Standard> device can not more than one. ");
            return nullptr;
        }
        IOLog::Instance().Log(std::string("Create Standard IO <") + deviceProps.Name + "> succeed.");
        return std::make_shared<StandardIO>(deviceProps.DeviceID);
    #else
        IOLog::Instance().Warning("Standard IO is not supported on this platform.");
        return nullptr;
    #endif
    }
    else if (deviceProps.Type == IOType::Joystick)
    {
    #if IODEVICE_PLATFORM_WINDOWS
        std::shared_ptr<Joystick> joystick = std::make_shared<IOToolkit::Joystick>(deviceProps.DeviceID, deviceProps.DeviceIndex);
        if (joystick->Valid())
        {
            IOLog::Instance().Log(std::string("Create Joystick IO <") + deviceProps.Name + "> succeed.");
            return joystick;
        }
        IOLog::Instance().Warning(std::string("Create Joystick IO <") + deviceProps.Name + "> failed.");
        return joystick;
    #else
        IOLog::Instance().Warning("Joystick IO is not supported on this platform.");
        return nullptr;
    #endif
    }
    else if(deviceProps.Type == IOType::External)
    {
        std::string fullDllName = ResolveExternalPluginPath(deviceProps.DllName);
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
