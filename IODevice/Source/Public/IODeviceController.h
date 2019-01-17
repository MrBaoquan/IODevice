/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once

#include "IODevice.h"

namespace DevelopHelper
{

class IOAPI IODeviceController
{
public:
    static IODeviceController& Instance();
    IODevice& GetIODevice(const char* deviceName);
    const float GetDeltaSeconds() const;
    void Update();
    void ClearBindings();

private:
    IODeviceController();
    ~IODeviceController();
    float deltaTime = 0.f;
};

};