/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#include "IODeviceController.h"
#include "IOClock.h"
#include "IOStatics.h"
#include "PlayerInput.h"
#include "MotionPlayer.h"
#include "IOApplication.h"
#include "IOLog.h"

using namespace IOToolkit;

// Initialize static mutex
std::recursive_mutex IODeviceController::controllerMutex;

/** Application main entry. */
IODeviceController::IODeviceController()
{
	if (IOApplication::SuccessResult(IOApplication::Constructor()))
	{
		IOLog::Instance().Log("IODevice initialize successful. \n");
	}
	else
	{
		IOLog::Instance().Log("IODevice initialize failed. \n");
	}
}

IODeviceController::~IODeviceController()
{
}

IODeviceController& IODeviceController::Instance()
{

    static IODeviceController single_instance;
    return single_instance;
}


int IOToolkit::IODeviceController::Load()
{
	IOApplication::DyLoad();
	return 0;
}


int IOToolkit::IODeviceController::Unload()
{
    // Lock mutex to ensure Update is not running before unloading
    std::lock_guard<std::recursive_mutex> lock(controllerMutex);
    
    IOApplication::DyUnload();
	return 0;
}

int IOToolkit::IODeviceController::EnterSafeState()
{
    std::lock_guard<std::recursive_mutex> lock(controllerMutex);

    // 锁序约定 (全局统一): controller → Motion → Registry。
    // 必须先让 MotionPlayer 收口 (它内部持 _impl->mutex 派发时会取 Registry)，
    // 再锁 Registry 遍历设备。若先锁 Registry 再进 MotionPlayer，会与编辑器播放线程
    // (Motion → Registry) 构成 AB-BA 死锁 —— 两把 recursive_mutex 跨线程互等即永久阻塞。
    MotionPlayer::Instance().EnterSafeState();

    std::lock_guard<std::recursive_mutex> registryLock(IODevices::RegistryMutex());
    for (auto& deviceIt : IODevices::GetDevcies())
    {
        deviceIt.second.DOImmediate();
    }
    return 0;
}

IOToolkit::IODevice& IOToolkit::IODeviceController::GetIODevice(const char* deviceName)
{
    return IODevices::GetDevice(deviceName);
}

const float IOToolkit::IODeviceController::GetDeltaSeconds() const
{
    return deltaTime;
}

/** Application tick entry. */
void IOToolkit::IODeviceController::Update()
{
    // Try to acquire lock, if cannot acquire (Unload/ClearBindings is running), skip this tick
    std::unique_lock<std::recursive_mutex> lock(controllerMutex, std::try_to_lock);
    if (!lock.owns_lock()) return;
    
    // Early return if IOToolkit is not loaded (during shutdown)
    if (!IOApplication::bLoaded) return;

    static float minDelta = 0.02f;
    static std::uint64_t lastTime = IOClock::GetMilliseconds();
    
    const std::uint64_t currentTime = IOClock::GetMilliseconds();
    float deltaSeconds = static_cast<float>((currentTime - lastTime) / 1000.0f);
    deltaTime = deltaSeconds;
   
    // 锁序约定 (全局统一): controller → Motion → Registry。
    // Registry 临界区按遍历分段持有 (与 Un Initialize 的 devices.clear() 串行化)，不得跨过
    // Step 3 的 MotionPlayer::Tick —— MotionPlayer 内部持 _impl->mutex 派发时会取 Registry；
    // 若此处持 Registry 再进 MotionPlayer，会与编辑器播放线程 (native 派发开关下 Motion → Registry)
    // 构成 AB-BA 死锁：双驱动互等永久阻塞、tick/poll 计数同时冻结、CPU≈0。
    {
        /** Step 1.   Tick all devices . */
        std::lock_guard<std::recursive_mutex> registryLock(IODevices::RegistryMutex());
        std::map<std::string, IODeviceDetails>& devices = IODevices::GetDevcies();
        for (auto& deviceIt : devices)
        {
            deviceIt.second.Tick(deltaSeconds);
        }

        /** Step 2. Tick player input */
        PlayerInput::Instance().Tick(deltaSeconds);
    }

    /** Step 3. Tick motion player (output-side state machine) — 不持 Registry 进入 */
    MotionPlayer::Instance().Tick(deltaSeconds);

    {
        /** Step 4. Frame end — 重新按需持 Registry 遍历设备表 */
        std::lock_guard<std::recursive_mutex> registryLock(IODevices::RegistryMutex());
        for (auto& deviceIt : IODevices::GetDevcies())
        {
            deviceIt.second.ProcessFrameEnd();
        }
    }

    lastTime = currentTime;
}

void IOToolkit::IODeviceController::ClearBindings()
{
    // Lock mutex to ensure Update is not running
    std::lock_guard<std::recursive_mutex> lock(controllerMutex);
    std::lock_guard<std::recursive_mutex> registryLock(IODevices::RegistryMutex());
    
    for (auto& deviceIt : IODevices::GetDevcies())
    {
        deviceIt.second.ClearBinding();
    }
    IOLog::Instance().Log("All input bindings has been removed. \n");
}
