/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include <string>
#include <vector>
#include <memory>
#include "RawIO/RawIO.h"
#include "CoreTypes/IOTypes.h"

namespace IOToolkit
{

class RawIOFactory
{
public:
    static std::shared_ptr<RawIO> CreateRawInput(DeviceProperties deviceProps);

};


};