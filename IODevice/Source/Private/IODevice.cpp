/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#include "IODevice.h"
#include "IOStatics.h"
#include "CoreTypes.inl"

void IOToolkit::IODevice::BindKey(const FKey& key, InputEvent KeyEvent, std::function<void(void)> keyDelegate)
{
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    deviceDetails.BindKey(key, KeyEvent, keyDelegate);
}

void IOToolkit::IODevice::BindAxisKey(const FKey& key, std::function<void(float)> axisDelegate)
{
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    deviceDetails.BindAxisKey(key, axisDelegate);
}

void IOToolkit::IODevice::BindAxis(const char* axisName, std::function<void(float)> axisDelegate)
{
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    deviceDetails.BindAxis(axisName, axisDelegate);
}

void IOToolkit::IODevice::BindAction(const char* actionName, InputEvent KeyEvent, std::function<void()> actionDelegate)
{
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    deviceDetails.BindAction(actionName, KeyEvent, actionDelegate);
}


//void IOToolkit::IODevice::BindAction(const char* actionName, InputEvent KeyEvent, void(*Method)(FKey))
//{
//	IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
//	deviceDetails.BindAction(actionName, KeyEvent, Method);
//}

int IOToolkit::IODevice::SetDO(float* InDOStatus)
{
    if (!this->IsValid()) { return -1; }
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.SetDO(InDOStatus);
}

int IOToolkit::IODevice::SetDO(const char* InOAction, float InValue, bool bIgnoreMassage/*=false*/)
{
	if (!this->IsValid()) { return -1; }
	IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
	return deviceDetails.SetDO(InOAction, InValue, bIgnoreMassage);
}

int IOToolkit::IODevice::SetDOOn(const char* InOAction)
{
	if (!this->IsValid()) { return -1; }
	IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
	return deviceDetails.SetDOOn(InOAction);
}


int IOToolkit::IODevice::SetDOOff(const char* InOAction)
{
	if (!this->IsValid()) { return -1; }
	IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
	return deviceDetails.SetDOOff(InOAction);
}

int IOToolkit::IODevice::DOImmediate()
{
	if (!this->IsValid()) { return -1; }
	IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
	return deviceDetails.DOImmediate();
}

int IOToolkit::IODevice::SetDO(const FKey& InKey, float InValue)
{
    if (!this->IsValid()) { return -1; }
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.SetDO(InKey, InValue);
}

int IOToolkit::IODevice::GetDO(float* OutDOStatus)
{
    if (!this->IsValid()) { return -1; }
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.GetDO(OutDOStatus);
}


float IOToolkit::IODevice::GetDO(const char* InOAction)
{
	if (!this->IsValid()) { return -1; }
	IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
	return deviceDetails.GetDO(InOAction);
}

float IOToolkit::IODevice::GetDO(const FKey& InKey)
{
	if (!this->IsValid()) { return -1; }
	IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
	return deviceDetails.GetDO(InKey);
}

bool IOToolkit::IODevice::GetKey(const FKey& InKey)
{
    if (!this->IsValid()) { return false; }
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.GetKey(InKey);
}

bool IOToolkit::IODevice::GetKeyDown(const FKey& InKey)
{
    if (!this->IsValid()) { return false; }
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.GetKeyDown(InKey);
}

bool IOToolkit::IODevice::GetKeyUp(const FKey& InKey)
{
    if (!this->IsValid()) { return false; }
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.GetKeyUp(InKey);
}

float IOToolkit::IODevice::GetAxis(const char* AxisName)
{
    if (!this->IsValid()) { return 0.f; }
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.GetAxis(AxisName);
}

float IOToolkit::IODevice::GetAxisKey(const FKey& InKey)
{
    if (!this->IsValid()) { return 0.f; }
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.GetAxisKey(InKey);
}

float IOToolkit::IODevice::GetKeyDownDuration(const FKey& InKey)
{
    if (!this->IsValid()) { return -1; }
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.GetKeyDownDuration(InKey);
}

void IOToolkit::IODevice::ClearBindings()
{
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    deviceDetails.ClearBinding();
}

const bool IOToolkit::IODevice::IsValid() const
{
    return deviceID != InvalidDeviceID;
}

const IOToolkit::uint8 IOToolkit::IODevice::GetID() const
{
    return deviceID;
}

const bool IOToolkit::IODevice::operator==(const IODevice& rhs)
{
    return deviceID == rhs.deviceID;
}

void IOToolkit::IODevice::BindAction(const char* actionName, InputEvent KeyEvent, std::function<void(FKey)> actionDelegate)
{
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    deviceDetails.BindAction(actionName, KeyEvent, actionDelegate);
}
