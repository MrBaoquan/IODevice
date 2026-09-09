/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once

#include <mutex>
#include "IODevice.h"

namespace IOToolkit
{

class IOAPI IODeviceController
{
public:
    static IODeviceController& Instance();
	int Load();
	int Unload();
    int EnterSafeState();
    IODevice& GetIODevice(const char* deviceName);
    const float GetDeltaSeconds() const;
    void Update();
    void ClearBindings();
    
    // Mutex to protect concurrent access between Update and Unload/ClearBindings
    static std::recursive_mutex controllerMutex;

private:
    IODeviceController();
    ~IODeviceController();
    float deltaTime = 0.f;
};

};