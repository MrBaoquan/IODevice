/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once

#include <map>
#include <string>
#include <memory>
#include "IODevice.h"
#include "IODeviceDetails.h"
#include "LessKey.h"
#include "KeyDetails.h"

namespace DevelopHelper
{

class IODevices
{
public:
    static void AddDevice(IODeviceDetails& deviceDetails);
    
    static IODevice& GetDevice(const std::string deviceName);
    static IODeviceDetails& GetDeviceDetail(const std::string deviceName);
    static IODeviceDetails& GetDeviceDetail(const IODevice& InDevice);
    static IODeviceDetails& GetDeviceDetail(uint8 deviceID);
    static const uint8 GetDevicesCount();
    static const uint8 GetDevicesCount(std::string InType);
    /** Initianlize all devices. */
    static int Initialize();
	static int UnInitialize();
    static bool HasDevice(const std::string deviceName);
    static std::map<std::string, IODeviceDetails>& GetDevcies();

private:
    static std::map<std::string,IODeviceDetails> devices;
    static IODeviceDetails Invalid;
};

struct StaticKeys
{
    static void Initialize();
    static void AddKey(const FKeyDetails& keyDetails);
    static bool ValidKey(const FKey& key);
    static bool IsExternalKey(const FKey& InKey);
    static bool IsExternalAxisKey(const FKey& InKey);
    static std::shared_ptr<FKeyDetails> GetKeyDetails(const FKey& key);
    static std::map<FKey, std::shared_ptr<FKeyDetails>,LessKey> InputKeys;
};

};
