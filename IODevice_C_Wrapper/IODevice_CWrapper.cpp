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