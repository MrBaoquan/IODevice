#include <windows.h>
#include <combaseapi.h>
#include <iostream>

#include "IODeviceController.h"
#include "IOSettings.h"
#include "MotionPlayer.h"
#include "StringUtils.hpp"
#include "Paths.hpp"
#include <filesystem>
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

IOCAPI int __stdcall Unload()
{
	return dh::IODeviceController::Instance().Unload();
}

IOCAPI int __stdcall SetIOConfigPath(BSTR InFilePath)
{
	return dh::IOSettings::Instance().SetIOConfigPath(std::filesystem::path(std::wstring(InFilePath)).string().data());
}

IOCAPI int __stdcall SetIOLogDir(BSTR InLogDir)
{
	return dh::IOSettings::Instance().SetIOLogDir(std::filesystem::path(std::wstring(InLogDir)).string().data());
}


IOCAPI int __stdcall BindKeyWithKey(BSTR InDeviceName, BSTR InKeyName, int InKeyEvent, InputActionWithKeySignature InHandler)
{
	int _result = -1;
	std::string _keyName = BSTR2String(InKeyName);
	dh::IODevice& _device = getIODevice(InDeviceName);
	_device.BindKey(_keyName.c_str(), (dh::InputEvent)InKeyEvent, [InHandler](dh::FKey InKey)
	{
		InHandler(string2BSTR(InKey.GetName()));
	});
	return _result;
}

IOCAPI int __stdcall BindKey(BSTR InDeviceName, BSTR InKeyName, int InKeyEvent, InputActionSignature InHandler)
{
	int _result = -1;
	std::string _keyName = BSTR2String(InKeyName);
	dh::IODevice& _device = getIODevice(InDeviceName);
	_device.BindKey(_keyName.c_str(), (dh::InputEvent)InKeyEvent, InHandler);
	return _result;
}

IOCAPI int __stdcall BindAxisKey(BSTR InDeviceName, BSTR InAxisName, InputAxisSignature InHandler)
{
	int _result = -1;
	std::string _axisKeyName = BSTR2String(InAxisName);
	dh::IODevice& _device = getIODevice(InDeviceName);
	_device.BindAxisKey(_axisKeyName.c_str(), InHandler);
	return _result;
}


/**
* ��Action
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
	return _device.GetDO(IOToolkit::FKey(BSTR2String(InKeyName).data()));
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
* loop ���
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

IOCAPI float __stdcall GetRawKeyValue(BSTR InDeviceName, BSTR InKey)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return _device.GetRawKeyValue(BSTR2String(InKey).c_str());
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

IOCAPI BSTR __stdcall DeviceIOType(BSTR InDeviceName)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return string2BSTR(_device.IOType());
}

IOCAPI BSTR __stdcall DeviceDllName(BSTR InDeviceName)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return string2BSTR(_device.DllName());
}

IOCAPI int __stdcall DeviceIndex(BSTR InDeviceName)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return _device.Index();
}

IOCAPI bool __stdcall IsValid(BSTR InDeviceName)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return _device.IsValid();
}

IOCAPI int __stdcall SetAKProps(BSTR InDeviceName, BSTR InAxisName, BSTR InKeyName, float InScale)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return _device.SetAKProps(BSTR2String(InAxisName).c_str(), BSTR2String(InKeyName).c_str(), InScale);
}

IOCAPI int __stdcall SetOKProps(BSTR InDeviceName, BSTR InOActionName, BSTR InKeyName, float InScale, bool InInvertEvent)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return _device.SetOKProps(BSTR2String(InOActionName).c_str(), BSTR2String(InKeyName).c_str(), InScale, InInvertEvent);
}

IOCAPI int __stdcall SetPKProps(BSTR InDeviceName, BSTR InKeyName, float InOffset, float InScale, float InMinValue, float InMaxValue, float InDeadZone, float InSensitivity, float InExponent, bool InInvert, bool InInvertEvent)
{
	dh::IODevice& _device = getIODevice(InDeviceName);
	return _device.SetPKProps(BSTR2String(InKeyName).c_str(), InOffset, InScale, InMinValue, InMaxValue, InDeadZone, InSensitivity, InExponent, InInvert, InInvertEvent);
}

// ── MotionPlayer C Wrapper ──────────────────────────────────────

IOCAPI int __stdcall MotionLoadSlot(BSTR InSlotId, BSTR InFilePath, int InPriority, int InMixPolicy)
{
	return dh::MotionPlayer::Instance().LoadSlot(
		BSTR2String(InSlotId).c_str(),
		std::filesystem::path(std::wstring(InFilePath)).string().c_str(),
		InPriority,
		static_cast<dh::MixPolicy>(InMixPolicy));
}

IOCAPI void __stdcall MotionUnloadSlot(BSTR InSlotId)
{
	dh::MotionPlayer::Instance().UnloadSlot(BSTR2String(InSlotId).c_str());
}

IOCAPI void __stdcall MotionUnloadAll()
{
	dh::MotionPlayer::Instance().UnloadAll();
}

IOCAPI int __stdcall MotionPlaySlot(BSTR InSlotId)
{
	return dh::MotionPlayer::Instance().PlaySlot(BSTR2String(InSlotId).c_str());
}

IOCAPI int __stdcall MotionPlaySlotFrom(BSTR InSlotId, float InTimeMs)
{
	return dh::MotionPlayer::Instance().PlaySlotFrom(BSTR2String(InSlotId).c_str(), InTimeMs);
}

IOCAPI int __stdcall MotionPauseSlot(BSTR InSlotId)
{
	return dh::MotionPlayer::Instance().PauseSlot(BSTR2String(InSlotId).c_str());
}

IOCAPI int __stdcall MotionResumeSlot(BSTR InSlotId)
{
	return dh::MotionPlayer::Instance().ResumeSlot(BSTR2String(InSlotId).c_str());
}

IOCAPI int __stdcall MotionStopSlot(BSTR InSlotId)
{
	return dh::MotionPlayer::Instance().StopSlot(BSTR2String(InSlotId).c_str());
}

IOCAPI int __stdcall MotionSeekSlot(BSTR InSlotId, float InTimeMs)
{
	return dh::MotionPlayer::Instance().SeekSlot(BSTR2String(InSlotId).c_str(), InTimeMs);
}

IOCAPI void __stdcall MotionSetSlotSpeed(BSTR InSlotId, float InSpeed)
{
	dh::MotionPlayer::Instance().SetSlotSpeed(BSTR2String(InSlotId).c_str(), InSpeed);
}

IOCAPI void __stdcall MotionSetSlotLoop(BSTR InSlotId, bool InLoop)
{
	dh::MotionPlayer::Instance().SetSlotLoop(BSTR2String(InSlotId).c_str(), InLoop);
}

IOCAPI void __stdcall MotionSetSlotClockMode(BSTR InSlotId, int InMode)
{
	dh::MotionPlayer::Instance().SetSlotClockMode(
		BSTR2String(InSlotId).c_str(),
		static_cast<dh::ClockMode>(InMode));
}

IOCAPI void __stdcall MotionSetSlotExternalTime(BSTR InSlotId, float InTimeMs)
{
	dh::MotionPlayer::Instance().SetSlotExternalTime(BSTR2String(InSlotId).c_str(), InTimeMs);
}

IOCAPI int __stdcall MotionGetSlotState(BSTR InSlotId)
{
	return static_cast<int>(dh::MotionPlayer::Instance().GetSlotState(BSTR2String(InSlotId).c_str()));
}

IOCAPI float __stdcall MotionGetSlotCurrentTime(BSTR InSlotId)
{
	return dh::MotionPlayer::Instance().GetSlotCurrentTime(BSTR2String(InSlotId).c_str());
}

IOCAPI float __stdcall MotionGetSlotDuration(BSTR InSlotId)
{
	return dh::MotionPlayer::Instance().GetSlotDuration(BSTR2String(InSlotId).c_str());
}

IOCAPI int __stdcall MotionGetSlotCount()
{
	return dh::MotionPlayer::Instance().GetSlotCount();
}

IOCAPI void __stdcall MotionPlayAll()
{
	dh::MotionPlayer::Instance().PlayAll();
}

IOCAPI void __stdcall MotionPauseAll()
{
	dh::MotionPlayer::Instance().PauseAll();
}

IOCAPI void __stdcall MotionStopAll()
{
	dh::MotionPlayer::Instance().StopAll();
}

// ── Phase 3 new APIs ────────────────────────────────────

IOCAPI int __stdcall MotionLoadSlotFromJson(BSTR InSlotId, BSTR InJsonContent, int InPriority, int InMixPolicy)
{
	return dh::MotionPlayer::Instance().LoadSlotFromJson(
		BSTR2String(InSlotId).c_str(),
		BSTR2String(InJsonContent).c_str(),
		InPriority,
		static_cast<dh::MixPolicy>(InMixPolicy));
}

IOCAPI int __stdcall MotionEvaluateSlotAt(BSTR InSlotId, float InTimeMs, float* OutValues, int InMaxChannels)
{
	return dh::MotionPlayer::Instance().EvaluateSlotAt(
		BSTR2String(InSlotId).c_str(),
		InTimeMs,
		OutValues,
		InMaxChannels);
}

// Global managed callback holder
static MotionEventCallbackManaged g_managedEventCallback = nullptr;
static MotionEventDataCallbackManaged g_managedEventDataCallback = nullptr;

// C ABI bridge: native cdecl -> managed stdcall + BSTR
static void NativeEventBridge(const char* slotId, int eventType)
{
	if (g_managedEventCallback) {
		BSTR bstrSlotId = string2BSTR(slotId);
		g_managedEventCallback(bstrSlotId, eventType);
		SysFreeString(bstrSlotId);
	}
}

static void NativeEventDataBridge(const char* slotId, int eventType, const char* eventData)
{
	if (g_managedEventDataCallback) {
		BSTR bstrSlotId = string2BSTR(slotId);
		BSTR bstrEventData = string2BSTR(eventData ? eventData : "");
		g_managedEventDataCallback(bstrSlotId, eventType, bstrEventData);
		SysFreeString(bstrEventData);
		SysFreeString(bstrSlotId);
	}
}

IOCAPI void __stdcall MotionSetEventCallback(MotionEventCallbackManaged InCallback)
{
	g_managedEventCallback = InCallback;
	if (InCallback) {
		dh::MotionPlayer::Instance().SetEventCallback(NativeEventBridge);
	} else {
		dh::MotionPlayer::Instance().SetEventCallback(nullptr);
	}
}

IOCAPI void __stdcall MotionSetEventDataCallback(MotionEventDataCallbackManaged InCallback)
{
	g_managedEventDataCallback = InCallback;
	if (InCallback) {
		dh::MotionPlayer::Instance().SetEventDataCallback(NativeEventDataBridge);
	} else {
		dh::MotionPlayer::Instance().SetEventDataCallback(nullptr);
	}
}

IOCAPI void __stdcall MotionSetSafetyConfig(float InMaxRatePerSecond)
{
	dh::MotionPlayer::Instance().SetSafetyConfig(InMaxRatePerSecond);
}
