/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#include "IODeviceController.h"
#include <windows.h>
#include "IOStatics.h"
#include "PlayerInput.h"
#include "IOApplication.h"
#include "IOLog.h"

using namespace DevelopHelper;

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
		PostQuitMessage(0);
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


int DevelopHelper::IODeviceController::Load()
{
	IOApplication::DyLoad();
	return 0;
}


int DevelopHelper::IODeviceController::Unload()
{
    IOApplication::DyUnload();
	return 0;
}

DevelopHelper::IODevice& DevelopHelper::IODeviceController::GetIODevice(const char* deviceName)
{
    return IODevices::GetDevice(deviceName);
}

const float DevelopHelper::IODeviceController::GetDeltaSeconds() const
{
    return deltaTime;
}

/** Application tick entry. */
void DevelopHelper::IODeviceController::Update()
{
    static float minDelta = 0.02f;
    static unsigned long lastTime = GetTickCount();
    
    float deltaSeconds = static_cast<float>((GetTickCount() - lastTime) / 1000.0f);
    deltaTime = deltaSeconds;
   
    /** Step 1.   Tick all devices . */
    std::map<std::string, IODeviceDetails>& devices = IODevices::GetDevcies();
    for (auto& deviceIt : devices)
    {
        deviceIt.second.Tick(deltaSeconds);
    }
    

    /** Step 2. Tick player input */
    PlayerInput::Instance().Tick(deltaSeconds);


    for (auto& deviceIt : devices)
    {
        deviceIt.second.ProcessFrameEnd();
    }

    lastTime = GetTickCount();
}

void DevelopHelper::IODeviceController::ClearBindings()
{
    for (auto& deviceIt : IODevices::GetDevcies())
    {
        deviceIt.second.ClearBinding();
    }
    IOLog::Instance().Log("All input bindings has been removed. \n");
}
