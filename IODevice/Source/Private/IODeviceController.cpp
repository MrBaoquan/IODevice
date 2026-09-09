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
    MotionPlayer::Instance().EnterSafeState();
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
   
    /** Step 1.   Tick all devices . */
    std::map<std::string, IODeviceDetails>& devices = IODevices::GetDevcies();
    for (auto& deviceIt : devices)
    {
        deviceIt.second.Tick(deltaSeconds);
    }
    

    /** Step 2. Tick player input */
    PlayerInput::Instance().Tick(deltaSeconds);

    /** Step 3. Tick motion player (output-side state machine) */
    MotionPlayer::Instance().Tick(deltaSeconds);

    for (auto& deviceIt : devices)
    {
        deviceIt.second.ProcessFrameEnd();
    }

    lastTime = currentTime;
}

void IOToolkit::IODeviceController::ClearBindings()
{
    // Lock mutex to ensure Update is not running
    std::lock_guard<std::recursive_mutex> lock(controllerMutex);
    
    for (auto& deviceIt : IODevices::GetDevcies())
    {
        deviceIt.second.ClearBinding();
    }
    IOLog::Instance().Log("All input bindings has been removed. \n");
}
