/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include <string>
#include "CoreTypes.inl"

namespace IOToolkit
{

struct IOType
{
    static const std::string Standard;
    static const std::string Joystick;
    static const std::string External;
    static const std::string Invalid;
};



const uint8 InvalidDeviceID = static_cast<uint8>(255);
const uint8 MaxIOCount = static_cast<uint8>(255);
const float MaxAxisValue = 1000.f;

struct DeviceProperties
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

	DeviceProperties() :DeviceID(InvalidDeviceID), Type(IOType::Invalid), DeviceIndex(0), DllName(IOType::Invalid) {}
	DeviceProperties(uint8 InDeviceID, std::string InType, std::string InName, std::string InDllName, uint8 InDeviceIndex) :
		DeviceID(InDeviceID)
		, Type(InType)
		, Name(InName)
		, DllName(InDllName)
		, DeviceIndex(InDeviceIndex) {}
};



};