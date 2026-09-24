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

    /**
     * 立即把各设备"已置脏"的输出推给插件 (ExternalIO::DOImmediate)。
     *
     * 为什么需要: SetDO 只写缓冲 + 置脏, 真正下发发生在设备 Tick 里。设备 Tick 由宿主按
     * UIRefreshIntervalMs (默认 100ms) 轮询驱动 —— 若只依赖它, 60fps 的 native 派发会被
     * 降采样成 ~10fps, 表现为动作"一顿一顿"。MotionPlayer::Tick 在派发后调用本函数,
     * 使下发频率跟随派发频率。无脏数据时是空操作, 与轮询 tick 的 flush 幂等。
     */
    static int FlushOutputs();

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
