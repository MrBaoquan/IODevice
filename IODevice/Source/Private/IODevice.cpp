/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#include "IODevice.h"
#include "IOStatics.h"
#include "CoreTypes.inl"

void DevelopHelper::IODevice::BindKey(const FKey& key, InputEvent KeyEvent, std::function<void(void)> keyDelegate)
{
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    deviceDetails.BindKey(key, KeyEvent, keyDelegate);
}

void DevelopHelper::IODevice::BindAxisKey(const FKey& key, std::function<void(float)> axisDelegate)
{
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    deviceDetails.BindAxisKey(key, axisDelegate);
}

void DevelopHelper::IODevice::BindAxis(const char* axisName, std::function<void(float)> axisDelegate)
{
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    deviceDetails.BindAxis(axisName, axisDelegate);
}

void DevelopHelper::IODevice::BindAction(const char* actionName, InputEvent KeyEvent, std::function<void()> actionDelegate)
{
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    deviceDetails.BindAction(actionName, KeyEvent, actionDelegate);
}


//void DevelopHelper::IODevice::BindAction(const char* actionName, InputEvent KeyEvent, void(*Method)(FKey))
//{
//	IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
//	deviceDetails.BindAction(actionName, KeyEvent, Method);
//}

int DevelopHelper::IODevice::SetDO(short* InDOStatus)
{
    if (!this->IsValid()) { return -1; }
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.SetDO(InDOStatus);
}

int DevelopHelper::IODevice::SetDO(const char* InOAction, short InValue)
{
	if (!this->IsValid()) { return -1; }
	IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
	return deviceDetails.SetDO(InOAction, InValue);
}

int DevelopHelper::IODevice::SetDOOn(const char* InOAction)
{
	if (!this->IsValid()) { return -1; }
	IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
	return deviceDetails.SetDOOn(InOAction);
}


int DevelopHelper::IODevice::SetDOOff(const char* InOAction)
{
	if (!this->IsValid()) { return -1; }
	IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
	return deviceDetails.SetDOOff(InOAction);
}

int DevelopHelper::IODevice::SetDO(const FKey& InKey, short InValue)
{
    if (!this->IsValid()) { return -1; }
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.SetDO(InKey, InValue);
}

int DevelopHelper::IODevice::GetDO(short* OutDOStatus)
{
    if (!this->IsValid()) { return -1; }
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.GetDO(OutDOStatus);
}

bool DevelopHelper::IODevice::GetKey(const FKey& InKey)
{
    if (!this->IsValid()) { return false; }
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.GetKey(InKey);
}

bool DevelopHelper::IODevice::GetKeyDown(const FKey& InKey)
{
    if (!this->IsValid()) { return false; }
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.GetKeyDown(InKey);
}

bool DevelopHelper::IODevice::GetKeyUp(const FKey& InKey)
{
    if (!this->IsValid()) { return false; }
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.GetKeyUp(InKey);
}

float DevelopHelper::IODevice::GetAxis(const char* AxisName)
{
    if (!this->IsValid()) { return 0.f; }
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.GetAxis(AxisName);
}

float DevelopHelper::IODevice::GetAxisKey(const FKey& InKey)
{
    if (!this->IsValid()) { return 0.f; }
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.GetAxisKey(InKey);
}

float DevelopHelper::IODevice::GetKeyDownDuration(const FKey& InKey)
{
    if (!this->IsValid()) { return -1; }
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.GetKeyDownDuration(InKey);
}

short DevelopHelper::IODevice::GetDO(const FKey& InKey)
{
    if (!this->IsValid()) { return -1; }
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.GetDO(InKey);
}

void DevelopHelper::IODevice::ClearBindings()
{
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    deviceDetails.ClearBinding();
}

const bool DevelopHelper::IODevice::IsValid() const
{
    return deviceID != InvalidDeviceID;
}

const DevelopHelper::uint8 DevelopHelper::IODevice::GetID() const
{
    return deviceID;
}

const bool DevelopHelper::IODevice::operator==(const IODevice& rhs)
{
    return deviceID == rhs.deviceID;
}

void DevelopHelper::IODevice::BindAction(const char* actionName, InputEvent KeyEvent, std::function<void(FKey)> actionDelegate)
{
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    deviceDetails.BindAction(actionName, KeyEvent, actionDelegate);
}
