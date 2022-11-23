#include <windows.h>
#include <combaseapi.h>
#include <iostream>

#include "IODeviceController.h"
#include "IOSettings.h"
#include "StringUtils.hpp"
#include "Paths.hpp"

#include "IODevice_CWrapper.h"

namespace dh = IOToolkit;

#pragma comment(lib,"IODevice.lib")

// PreDeclare
dh::IODevice& getIODevice(BSTR);


BOOL WINAPI DllMain(
	_In_ HINSTANCE hinstDLL,
	_In_ DWORD     fdwReason,
	_In_ LPVOID    lpvReserved
)
{
	DevelopHelper::Paths::Instance().SetModule(hinstDLL);
	switch (fdwReason)
	{
	case DLL_PROCESS_ATTACH:
	{
		std::string dllPath = DevelopHelper::Paths::Instance().GetModuleDir() + "\\";
		SetDllDirectoryA(dllPath.data());
		std::string _path = DevelopHelper::Paths::Instance().GetModuleDir() + "IODevice.dll";
		auto _module = LoadLibraryA(_path.data());
		OutputDebugStringA("============== Attched IODevice_CWrapper.dll ... ================ \n");
	}
	break;
	case DLL_PROCESS_DETACH:
		OutputDebugStringA("============== Detached IODevice_CWrapper.dll ... ================ \n");
		break;
	case DLL_THREAD_ATTACH:
		break;
	case DLL_THREAD_DETACH:
		break;
	default:
		break;
	}
	return TRUE;
}

IOCAPI int __stdcall Load()
{
	return dh::IODeviceController::Instance().Load();
}

IOCAPI int __stdcall UnLoad()
{
	return dh::IODeviceController::Instance().Unload();
}


IOCAPI int __stdcall SetIOConfigPath(BSTR InFilePath)
{
	return dh::IOSettings::Instance().SetIOConfigPath(BSTR2String(InFilePath).c_str());
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


IOCAPI float __stdcall GetDOSingle(BSTR InDeviceName, BSTR InKeyName)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return _device.GetDO(BSTR2String(InKeyName).c_str());
}

IOCAPI float __stdcall GetDOAction(BSTR InDeviceName, BSTR InOAction)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return _device.GetDO(BSTR2String(InOAction).data());
}

IOCAPI int __stdcall GetDOAll(BSTR InDeviceName, float* InStatus)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return _device.GetDO(InStatus);
}

IOCAPI int RefreshStreamingData(BSTR InDeviceName, BYTE* StreamingData, unsigned int DataSize)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return _device.RefreshStreamingData(StreamingData, DataSize);
}

IOCAPI int __stdcall SetDOSingle(BSTR InDeviceName, BSTR InKeyName, float InVal)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return _device.SetDO(IOToolkit::FKey(BSTR2String(InKeyName).data()),InVal);
}

IOCAPI int __stdcall SetDOAll(BSTR InDeviceName, float* InStatus)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return _device.SetDO(InStatus);
}


IOCAPI int __stdcall SetDOAction(BSTR InDeviceName, BSTR InOAction, float InVal, bool bIngoreMassage/*=false*/)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return _device.SetDO(BSTR2String(InOAction).data(), InVal, bIngoreMassage);
}

IOCAPI int _stdcall SetDOOn(BSTR InDeviceName, BSTR InOAction)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return _device.SetDOOn(BSTR2String(InOAction).data());
}

IOCAPI int _stdcall SetDOOff(BSTR InDeviceName, BSTR InOAction)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return _device.SetDOOff(BSTR2String(InOAction).data());
}


IOCAPI int __stdcall DOImmediate(BSTR InDeviceName)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return _device.DOImmediate();
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
	return _device.GetKeyDown(BSTR2String(InKey).c_str());;
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

IOCAPI void __stdcall ClearBindings(BSTR InDeviceName)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	_device.ClearBindings();
}


