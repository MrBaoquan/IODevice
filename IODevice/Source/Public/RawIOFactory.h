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

struct DevicePropeties
{
    uint8 DeviceID;

    /** Raw Input&Output Type */
    std::string Type;

    /** Name of device. use to get devcie. */
    std::string Name;

    /** For external raw IO */
    std::string DllName;

    /** Device index */
    uint8 DeviceIndex;

    DevicePropeties() :DeviceID(InvalidDeviceID) ,Type(IOType::Invalid),DeviceIndex(0),DllName(IOType::Invalid){}
    DevicePropeties(uint8 InDeviceID,std::string InType,std::string InName, std::string InDllName, uint8 InDeviceIndex):
        DeviceID(InDeviceID)
        ,Type(InType)
        ,Name(InName)
        ,DllName(InDllName)
        ,DeviceIndex(InDeviceIndex){}
};


class RawIOFactory
{
public:
    static std::shared_ptr<RawIO> CreateRawInput(DevicePropeties deviceProps);

};


};