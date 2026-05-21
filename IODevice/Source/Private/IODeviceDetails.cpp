/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#include "IODeviceDetails.h"
#include "PlayerInput.h"
#include "IOStatics.h"
#include "InputSettings.h"
#include "IOLog.h"

IOToolkit::IODevice& IOToolkit::IODeviceDetails::GetDevice()
{
    return device;
}

void IOToolkit::IODeviceDetails::Initialize()
{
    if (rawIO)
    {
        rawIO->Initialize();
    }
}

void IOToolkit::IODeviceDetails::Tick(float DeltaSeconds)
{
    if (rawIO && rawIO->Valid())
    {
        rawIO->Tick(DeltaSeconds);
    }
}

void IOToolkit::IODeviceDetails::Destroy()
{
	if (rawIO)
	{
		try {
			rawIO->Destroy();
		}
		catch (...) {
			// Ignore exceptions during rawIO cleanup
		}
	}
	this->ClearBinding();
}

void IOToolkit::IODeviceDetails::ProcessFrameEnd()
{
    if (rawIO)
    {
        rawIO->OnFrameEnd();
    }
}

void IOToolkit::IODeviceDetails::ClearBinding()
{
    KeyBindings.clear();
    ActionBindings.clear();
    AxisBindings.clear();
    AxisKeyBindings.clear();
}

void IOToolkit::IODeviceDetails::BindKey(const FKey& InKey, InputEvent InEvent,InputActionHandlerSignature delegate)
{
    if (!ValidDevcie(std::string(" [BindKey] ") + InKey.GetName()))
    {
        return;
    }
    if (StaticKeys::ValidKey(InKey))
    {
        std::string msg = std::string("Bind delegate for key ") + InKey.GetName() + " succeed. device name: " + getName();
        IOLog::Instance().Log(msg);
    }
    else
    {
        std::string msg = std::string("Bind delegate for key ") + InKey.GetName() + " failed. because it is invalid. device name: " + getName();
        IOLog::Instance().Warning(msg);
        return;
    }

    FInputKeyBinding KB(FInputChord(InKey, false, false, false, false), InEvent);
   
    KB.KeyDelegate.BindDelegate(delegate);
    KeyBindings.push_back(KB);
}

void IOToolkit::IODeviceDetails::BindKey(const FKey& InKey, InputEvent InEvent, InputActionHandlerWithKeySignature delegate)
{
    if (!ValidDevcie(std::string(" [BindKey] ") + InKey.GetName()))
    {
        return;
    }
    if (StaticKeys::ValidKey(InKey))
    {
        std::string msg = std::string("Bind delegate for key ") + InKey.GetName() + " succeed. device name: " + getName();
        IOLog::Instance().Log(msg);
    }
    else
    {
        std::string msg = std::string("Bind delegate for key ") + InKey.GetName() + " failed. because it is invalid. device name: " + getName();
        IOLog::Instance().Warning(msg);
        return;
    }

    FInputKeyBinding KB(FInputChord(InKey, false, false, false, false), InEvent);
   
    KB.KeyDelegate.BindDelegate(delegate);
    KeyBindings.push_back(KB);
}

void IOToolkit::IODeviceDetails::BindAxis(const std::string axisName, FInputAxisHandlerSignature delegate)
{
    if (!ValidDevcie(std::string(" [BindAxis] ") + axisName))
    {
        return;
    }
    if (UInputSettings::Instance().HasAxis(device.GetID(), axisName))
    {
        std::string msg = std::string("Bind delegate for axis ") + axisName + " succeed. device name: " + getName();
        IOLog::Instance().Log(msg);
    }else
    {
        std::string msg = std::string("Bind delegate for axis ") + axisName + " failed, because can not find match axis name in config files. device name: "+ getName();
        IOLog::Instance().Warning(msg);
        return;
    }

    FInputAxisBinding AB(axisName);
    AB.AxisDelegate.BindDelegate(delegate);
    AxisBindings.push_back(AB);
}

void IOToolkit::IODeviceDetails::BindAxisKey(const FKey AxisKey, FInputAxisHandlerSignature delegate)
{
    if (!ValidDevcie(std::string(" [BindAxisKey] ") + AxisKey.GetName()))
    {
        return;
    }
    if ((StaticKeys::ValidKey(AxisKey)))
    {
        std::string msg = std::string("Bind delegate for axis key ") + AxisKey.GetName() + " succeed. device name: " + getName();
        IOLog::Instance().Log(msg);
    }
    else
    {
        std::string msg = std::string("Bind delegate for axis key ") + AxisKey.GetName() + " failed. because it is invalid. device name: " + getName();
        IOLog::Instance().Warning(msg);
        return;
    }

    FInputAxisKeyBinding AB(AxisKey);
    AB.AxisDelegate.BindDelegate(delegate);
    AxisKeyBindings.push_back(AB);
}

void IOToolkit::IODeviceDetails::BindAction(std::string ActionName, const InputEvent KeyEvent, InputActionHandlerWithKeySignature delegate)
{
	if (!ValidDevcie(std::string(" [BindAction] ") + ActionName))
	{
		return;
	}
	if (UInputSettings::Instance().HasAction(device.GetID(), ActionName))
	{
		std::string msg = std::string("Bind delegate for Action ") + ActionName + " succeed. device name: " + getName();
		IOLog::Instance().Log(msg);
	}
	else
	{
		std::string msg = std::string("Bind delegate for Action ") + ActionName + " failed, because can not find match action name in config files. device name: " + getName();
		IOLog::Instance().Warning(msg);
		return;
	}
	FInputActionBinding AB(ActionName, KeyEvent);
	AB.ActionDelegate.BindDelegate(delegate);
	AddActionBinding(AB);
}

void IOToolkit::IODeviceDetails::BindAction(std::string ActionName, const InputEvent KeyEvent, InputActionHandlerSignature delegate)
{
    if (!ValidDevcie(std::string(" [BindAction] ") + ActionName))
    {
        return;
    }
    if (UInputSettings::Instance().HasAction(device.GetID(), ActionName))
    {
        std::string msg = std::string("Bind delegate for Action ") + ActionName + " succeed. device name: " + getName();
        IOLog::Instance().Log(msg);
    }
    else
    {
        std::string msg = std::string("Bind delegate for Action ") + ActionName + " failed, because can not find match action name in config files. device name: " + getName();
        IOLog::Instance().Warning(msg);
        return;
    }

    FInputActionBinding AB(ActionName, KeyEvent);
    AB.ActionDelegate.BindDelegate(delegate);
    AddActionBinding(AB);
}

int IOToolkit::IODeviceDetails::SetDO(float* InDOStatus)
{
    if (!ValidDevcie(std::string(" [SetDO] ")))
    {
        return -1;
    }
    return rawIO ? rawIO->SetDO(InDOStatus) : -1;
}


int IOToolkit::IODeviceDetails::SetDOOn(const char* InOAction)
{
    if (!ValidDevcie(std::string(" [SetDOOn] ") + InOAction))
    {
        return -1;
    }

    if (!UInputSettings::Instance().HasOAction(device.GetID(), InOAction))
    {
        std::string msg = std::string("Try to resolve [SetDOOn] ") + InOAction + " failed, because can not find matched oaction name in config files. device name: " + getName();
        IOLog::Instance().Warning(msg);
        return -1;
    }
	return rawIO ? rawIO->SetDOOn(InOAction) : -1;
}


int IOToolkit::IODeviceDetails::SetDOOff(const char* InOAction)
{
    if (!ValidDevcie(std::string(" [SetDOOff] ") + InOAction))
    {
        return -1;
    }
    if (!UInputSettings::Instance().HasOAction(device.GetID(), InOAction))
    {
        std::string msg = std::string("Try to resolve [SetDOOff] ") + InOAction + " failed, because can not find matched oaction name in config files. device name: " + getName();
        IOLog::Instance().Warning(msg);
        return -1;
    }

	return rawIO ? rawIO->SetDOOff(InOAction) : -1;
}

int IOToolkit::IODeviceDetails::DOImmediate()
{
    if (!ValidDevcie(std::string(" [DOImmediate] ")))
    {
        return -1;
    }
	return rawIO?rawIO->DOImmediate():-1;
}

int IOToolkit::IODeviceDetails::SetDO(const char* InOAction, float InValue, bool bIngoreMassage/*bIngoreMassage=false*/)
{
    if (!ValidDevcie(std::string(" [SetDO] ") + InOAction))
    {
        return -1;
    }

    if (!UInputSettings::Instance().HasOAction(device.GetID(), InOAction))
    {
        std::string msg = std::string("Try to resolve [SetDO] ") + InOAction + " failed, because can not find matched oaction name in config files. device name: " + getName();
        IOLog::Instance().Warning(msg);
        return -1;
    }

	return rawIO ? rawIO->SetDO(InOAction, InValue,bIngoreMassage):-1;
}

int IOToolkit::IODeviceDetails::SetDO(const FKey& InKey, float InValue)
{
    if (!ValidDevcie(std::string(" [SetDO] ") + InKey.GetName()))
    {
        return -1;
    }
    return rawIO ? rawIO->SetDO(InKey, InValue) : -1;
}

int IOToolkit::IODeviceDetails::GetDO(float* OutDOStatus)
{
    if (!ValidDevcie(std::string(" [GetDO] ")))
    {
        return -1;
    }
    return rawIO ? rawIO->GetDO(OutDOStatus) : -1;
}


int IOToolkit::IODeviceDetails::RefreshStreamingData(BYTE* StreamingData, unsigned int DataSize)
{
	if (!ValidDevcie(std::string(" [RefreshStreamingData] ")))
	{
		return -1;
	}
	return rawIO ? rawIO->RefreshStreamingData(StreamingData,DataSize) : -1;
}

int IOToolkit::IODeviceDetails::WritePluginChannel(const char* channelName, const BYTE* data, unsigned int size)
{
	if (!ValidDevcie(std::string(" [WritePluginChannel] ")))
	{
		return 0;
	}
	if (!channelName) return 0;
	return rawIO ? rawIO->WritePluginChannel(channelName, data, size) : 0;
}

int IOToolkit::IODeviceDetails::BindPluginChannel(const char* channelName,
	std::function<void(const char*, const BYTE*, unsigned int)> handler)
{
	if (!channelName || !handler) return -1;

	// First handler for this channel triggers ExternalIO to register dispatcher.
	bool firstHandlerForChannel = false;
	int assignedId = -1;
	{
		std::lock_guard<std::mutex> lk(pluginChannelState->mtx);
		auto& list = pluginChannelState->handlers[channelName];
		firstHandlerForChannel = list.empty();
		assignedId = pluginChannelState->nextId++;
		list.push_back({ assignedId, std::move(handler) });
	}
	if (firstHandlerForChannel && rawIO)
	{
		rawIO->EnsurePluginChannelDispatcher(this);
	}
	return assignedId;
}

int IOToolkit::IODeviceDetails::UnbindPluginChannel(const char* channelName, int handlerId)
{
	if (!channelName) return 0;
	std::lock_guard<std::mutex> lk(pluginChannelState->mtx);
	auto it = pluginChannelState->handlers.find(channelName);
	if (it == pluginChannelState->handlers.end()) return 0;
	auto& list = it->second;
	for (auto eIt = list.begin(); eIt != list.end(); ++eIt)
	{
		if (eIt->id == handlerId)
		{
			list.erase(eIt);
			if (list.empty()) pluginChannelState->handlers.erase(it);
			return 1;
		}
	}
	return 0;
}

void IOToolkit::IODeviceDetails::DispatchPluginChannel(const char* channelName, const BYTE* data, unsigned int size)
{
	if (!channelName) return;
	std::vector<std::function<void(const char*, const BYTE*, unsigned int)>> snapshot;
	{
		std::lock_guard<std::mutex> lk(pluginChannelState->mtx);
		auto it = pluginChannelState->handlers.find(channelName);
		if (it == pluginChannelState->handlers.end()) return;
		snapshot.reserve(it->second.size());
		for (auto& e : it->second) snapshot.push_back(e.handler);
	}
	for (auto& h : snapshot)
	{
		try { h(channelName, data, size); }
		catch (...) { /* isolate callback exceptions */ }
	}
}

float IOToolkit::IODeviceDetails::GetDO(const char* InOAction)
{
    if (!ValidDevcie(std::string(" [GetDO] ") + InOAction))
    {
        return -1;
    }

    if (!UInputSettings::Instance().HasOAction(device.GetID(), InOAction))
    {
        std::string msg = std::string("Try to resolve [GetDO] ") + InOAction + " failed, because can not find matched oaction name in config files. device name: " + getName();
        IOLog::Instance().Warning(msg);
        return -1;
    }

	return rawIO ? rawIO->GetDO(InOAction) : -1;
}

bool IOToolkit::IODeviceDetails::GetKey(const FKey& InKey)
{
    if (!ValidDevcie(std::string(" [GetKey] ") + InKey.GetName()))
    {
        return false;
    }
    return PlayerInput::Instance().GetKey(InKey, device.GetID());
}

bool IOToolkit::IODeviceDetails::GetKeyDown(const FKey& InKey)
{
    if (!ValidDevcie(std::string(" [GetKeyDown] ") + InKey.GetName()))
    {
        return false;
    }
    return PlayerInput::Instance().GetKeyDown(InKey, device.GetID());
}

bool IOToolkit::IODeviceDetails::GetKeyUp(const FKey& InKey)
{
    if (!ValidDevcie(std::string(" [GetKeyUp] ") + InKey.GetName()))
    {
        return false;
    }
    return PlayerInput::Instance().GetKeyUp(InKey, device.GetID());
}

float IOToolkit::IODeviceDetails::GetAxis(const char* AxisName)
{
    if (!ValidDevcie(std::string(" [GetAxis] ") + AxisName))
    {
        return -1;
    }

    return PlayerInput::Instance().GetAxis(AxisName, device.GetID());
}

float IOToolkit::IODeviceDetails::GetAxisKey(const FKey& InKey)
{
    if (!ValidDevcie(std::string(" [GetAxisKey] ") + InKey.GetName()))
    {
        return -1;
    }

    return PlayerInput::Instance().GetAxisKey(InKey, device.GetID());
}

float IOToolkit::IODeviceDetails::GetRawKeyValue(const FKey& InKey)
{
    if (!ValidDevcie(std::string(" [GetRawKeyValue] ") + InKey.GetName()))
    {
        return -1;
    }

    return PlayerInput::Instance().GetRawKeyValue(InKey, device.GetID());
}

float IOToolkit::IODeviceDetails::GetKeyDownDuration(const FKey& InKey)
{
    if (!ValidDevcie(std::string(" [GetKeyDownDuration] ") + InKey.GetName()))
    {
        return -1;
    }
    return PlayerInput::Instance().GetKeyDownTime(InKey, device.GetID());
}

float IOToolkit::IODeviceDetails::GetDO(const FKey& InKey)
{
    if (!ValidDevcie(std::string(" [GetDO] ") + InKey.GetName()))
    {
        return -1;
    }
    return rawIO ? rawIO->GetDO(InKey) : -1;
}


IOToolkit::FInputActionBinding& IOToolkit::IODeviceDetails::GetActionBinding(const int32 BindingIndex)
{
    return ActionBindings[BindingIndex];
}

const std::string& IOToolkit::IODeviceDetails::getName()
{
    return name;
}
const std::string& INVALID_DEVICE = "Invalid";
const std::string& IOToolkit::IODeviceDetails::getIOType()
{
    if(rawIO)
    {
       return rawIO->getIOType();
    }
    return INVALID_DEVICE;
}

const std::string& IOToolkit::IODeviceDetails::getDllName()
{
    return props.DllName;
}


IOToolkit::uint8 IOToolkit::IODeviceDetails::getIndex()
{
    return props.DeviceIndex;
}

bool IOToolkit::IODeviceDetails::isValid()
{
    if (rawIO)
    {
        return rawIO->Valid();
    }
    return false;
}

void IOToolkit::IODeviceDetails::AddActionBinding(const FInputActionBinding & InBinding)
{
    ActionBindings.push_back(FInputActionBinding(InBinding));
    FInputActionBinding& Binding = ActionBindings.back();

    if (Binding.KeyEvent == IE_Pressed || Binding.KeyEvent == IE_Released)
    {
        const InputEvent PairedEvent = (Binding.KeyEvent == IE_Pressed ? IE_Released : IE_Pressed);
        for (int32 BindingIndex = static_cast<int32>(ActionBindings.size() - 2); BindingIndex >= 0; --BindingIndex)
        {
            FInputActionBinding& ActionBinding = ActionBindings[BindingIndex];
            if (ActionBinding.ActionName == Binding.ActionName)
            {
                // If we find a matching event that is already paired we know this is paired so mark it off and we're done
                if (ActionBinding.bPaired)
                {
                    Binding.bPaired = true;
                    break;
                }
                // Otherwise if this is a pair to the new one mark them both as paired
                // Don't break as there could be two bound paired events
                else if (ActionBinding.KeyEvent == PairedEvent)
                {
                    ActionBinding.bPaired = true;
                    Binding.bPaired = true;
                }
            }
        }
    }
}

bool IOToolkit::IODeviceDetails::ValidDevcie(std::string customMsg)
{
    IODeviceDetails& s = *this;
    if (GetDevice().IsValid())
    {
        return true;
    }
    std::string msg = std::string("Trying to resolve") + customMsg + " with an invalid device, make sure that device exists!";
    IOLog::Instance().Warning(msg);
    return false;
}

int IOToolkit::IODeviceDetails::SetAKProps(const char* axisName, const char* keyName, float scale)
{
    if (!ValidDevcie(std::string(" [SetAKProps] ") + axisName))
    {
        return -1;
    }

    if (!UInputSettings::Instance().HasAxis(device.GetID(), axisName))
    {
        std::string msg = std::string("Try to resolve [SetAKProps] ") + axisName + " failed, because can not find matched axis name in config files. device name: " + getName();
        IOLog::Instance().Warning(msg);
        return -1;
    }

    return PlayerInput::Instance().SetAKProps(axisName, keyName, scale, device.GetID());
}

int IOToolkit::IODeviceDetails::SetOKProps(const char* oactionName, const char* keyName, float scale, bool invertEvent)
{
    if (!ValidDevcie(std::string(" [SetOKProps] ") + oactionName))
    {
        return -1;
    }

    if (!UInputSettings::Instance().HasOAction(device.GetID(), oactionName))
    {
        std::string msg = std::string("Try to resolve [SetOKProps] ") + oactionName + " failed, because can not find matched oaction name in config files. device name: " + getName();
        IOLog::Instance().Warning(msg);
        return -1;
    }

    return rawIO ? rawIO->SetOKProps(oactionName, keyName, scale, invertEvent) : -1;
}

int IOToolkit::IODeviceDetails::SetPKProps(const char* keyName, float offset, float scale, float minValue, float maxValue, float deadZone, float sensitivity, float exponent, bool invert, bool invertEvent)
{
    if (!ValidDevcie(std::string(" [SetPKProps] ") + keyName))
    {
        return -1;
    }

    return PlayerInput::Instance().SetPKProps(keyName, offset, scale, minValue, maxValue, deadZone, sensitivity, exponent, invert, invertEvent, device.GetID());
}

