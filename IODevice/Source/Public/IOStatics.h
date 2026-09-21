/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once

#include <map>
#include <string>
#include <memory>
#include <mutex>
#include "IODevice.h"
#include "IODeviceDetails.h"
#include "LessKey.h"
#include "KeyDetails.h"

namespace IOToolkit
{

class IODevices
{
public:
    /**
     * 设备表的唯一同步点。
     *
     * 运行期查询（IODevice::IsValid / GetDeviceDetail）与拆除（UnInitialize 的 clear）
     * 来自不同线程：遍历 std::map 的同时 clear/erase 会直接报废迭代器，调用方随后拿到的
     * IODeviceDetails& 已指向释放内存 —— 再经 rawIO 虚调用就会跳进未映射地址。
     * 因此所有访问（含 GetDevcies() 的遍历）都必须串行化。
     *
     * recursive：设备 tick 会回调进插件，插件上行又回到 GetDeviceDetail，需要同线程重入。
     */
    static std::recursive_mutex& RegistryMutex();

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
