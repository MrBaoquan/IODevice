#include <windows.h>
#include <combaseapi.h>
#include <iostream>

#include "IODeviceController.h"
#include "StringUtils.hpp"

#include "IODevice_CWrapper.h"

namespace dh = DevelopHelper;

#pragma comment(lib,"IODevice.lib")

// PreDeclare
dh::IODevice& getIODevice(BSTR);


IOCAPI int __stdcall Load()
{
	return dh::IODeviceController::Instance().Load();
}

IOCAPI int __stdcall UnLoad()
{
	return dh::IODeviceController::Instance().Unload();
}


IOCAPI int __stdcall BindKey(BSTR InDeviceName, BSTR InKeyName, int InKeyEvent, InputActionSignature InHandler)
{
	int _result = -1;
	std::string _keyName = BSTR2String(InKeyName);
	dh::IODevice& _device = getIODevice(InDeviceName);
	_device.BindKey(_keyName.c_str(), (dh::InputEvent)InKeyEvent, InHandler);
	return _result;
}


/**
* °ó¶¨Action
*/
IOCAPI int __stdcall BindAction(BSTR InDeviceName, BSTR InActionName, int InKeyEvent, InputActionWithKeySignature InHandler)
{
	int _result = -1;
	std::string _actionName = BSTR2String(InActionName);

	dh::IODevice& _device = getIODevice(InDeviceName);
	_device.BindAction(_actionName.c_str(), (dh::InputEvent)InKeyEvent, [InHandler](dh::FKey InKey)
	{
		InHandler(string2BSTR(InKey.GetName()));
	});
	return _result;
}

IOCAPI int __stdcall BindAxis(BSTR InDeviceName, BSTR InAxisName, InputAxisSignature InHandler)
{
	int _result = -1;
	dh::IODevice& _device = getIODevice(InDeviceName);
	std::string _axisName = BSTR2String(InAxisName);
	_device.BindAxis(_axisName.c_str(), InHandler);
	return  _result;
}


IOCAPI BYTE __stdcall GetDOSingle(BSTR InDeviceName, BSTR InKeyName)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return _device.GetDeviceDO(BSTR2String(InKeyName).c_str());
}

IOCAPI int __stdcall GetDOAll(BSTR InDeviceName, BYTE* InStatus)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return _device.GetDeviceDO(InStatus);
}

IOCAPI int __stdcall SetDOSingle(BSTR InDeviceName, BSTR InKeyName, BYTE InStatus)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return _device.SetDeviceDO(BSTR2String(InKeyName).c_str(),InStatus);
}

IOCAPI int __stdcall SetDOAll(BSTR InDeviceName, BYTE* InStatus)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return _device.SetDeviceDO(InStatus);
}


/**
* loop ¼ì²â
*/
IOCAPI void __stdcall Query()
{
	dh::IODeviceController::Instance().Update();
}


// -----------------------Private Utils-----------------------------------
dh::IODevice& getIODevice(BSTR InDeviceName)
{
	return dh::IODeviceController::Instance().GetIODevice(std::string(BSTR2String(InDeviceName)).c_str());
}


IOCAPI void __stdcall ClearAllBindings()
{
	dh::IODeviceController::Instance().ClearBindings();
}

IOCAPI bool __stdcall GetKey(BSTR InDeviceName, BSTR InKey)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return _device.GetKey(BSTR2String(InKey).c_str());
}

IOCAPI bool __stdcall GetKeyDown(BSTR InDeviceName, BSTR InKey)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return _device.GetKeyDown(BSTR2String(InKey).c_str());
}

IOCAPI bool __stdcall GetKeyUp(BSTR InDeviceName, BSTR InKey)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return _device.GetKeyUp(BSTR2String(InKey).c_str());
}

IOCAPI float __stdcall GetAxis(BSTR InDeviceName, BSTR InAxisName)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return _device.GetAxis(BSTR2String(InAxisName).c_str());
}

IOCAPI float __stdcall GetAxisKey(BSTR InDeviceName, BSTR InKey)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return _device.GetAxisKey(BSTR2String(InKey).c_str());
}

IOCAPI float __stdcall GetKeyDownDuration(BSTR InDeviceName, BSTR InKey)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return _device.GetKeyDownDuration(BSTR2String(InKey).c_str());
}
