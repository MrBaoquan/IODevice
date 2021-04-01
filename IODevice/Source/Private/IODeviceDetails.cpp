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
    if (rawIO)
    {
        rawIO->Tick(DeltaSeconds);
    }
}

void IOToolkit::IODeviceDetails::Destroy()
{
	if (rawIO)
	{
		rawIO->Destroy();
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
    if (!ValidDevcie(std::string(" Key ") + InKey.GetName()))
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
    if (!ValidDevcie(std::string(" Axis ") + axisName))
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
    if (!ValidDevcie(std::string(" AxisKey ") + AxisKey.GetName()))
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
	if (!ValidDevcie(std::string(" Action ") + ActionName))
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
    if (!ValidDevcie(std::string(" Action ") + ActionName))
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
    return rawIO ? rawIO->SetDO(InDOStatus) : -1;
}


int IOToolkit::IODeviceDetails::SetDOOn(const char* InOAction)
{
	return rawIO ? rawIO->SetDOOn(InOAction) : -1;
}


int IOToolkit::IODeviceDetails::SetDOOff(const char* InOAction)
{
	return rawIO ? rawIO->SetDOOff(InOAction) : -1;
}

int IOToolkit::IODeviceDetails::DOImmediate()
{
	return rawIO?rawIO->DOImmediate():-1;
}

int IOToolkit::IODeviceDetails::SetDO(const char* InOAction, float InValue, bool bIngoreMassage/*bIngoreMassage=false*/)
{
	return rawIO ? rawIO->SetDO(InOAction, InValue,bIngoreMassage):-1;
}

int IOToolkit::IODeviceDetails::SetDO(const FKey& InKey, float InValue)
{
    return rawIO ? rawIO->SetDO(InKey, InValue) : -1;
}

int IOToolkit::IODeviceDetails::GetDO(float* OutDOStatus)
{
    return rawIO ? rawIO->GetDO(OutDOStatus) : -1;
}


float IOToolkit::IODeviceDetails::GetDO(const char* InOAction)
{
	return rawIO ? rawIO->GetDO(InOAction) : -1;
}

bool IOToolkit::IODeviceDetails::GetKey(const FKey& InKey)
{
    return PlayerInput::Instance().GetKey(InKey, device.GetID());
}

bool IOToolkit::IODeviceDetails::GetKeyDown(const FKey& InKey)
{
    return PlayerInput::Instance().GetKeyDown(InKey, device.GetID());
}

bool IOToolkit::IODeviceDetails::GetKeyUp(const FKey& InKey)
{
    return PlayerInput::Instance().GetKeyUp(InKey, device.GetID());
}

float IOToolkit::IODeviceDetails::GetAxis(const char* AxisName)
{
    return PlayerInput::Instance().GetAxis(AxisName, device.GetID());
}

float IOToolkit::IODeviceDetails::GetAxisKey(const FKey& InKey)
{
    return PlayerInput::Instance().GetAxisKey(InKey, device.GetID());
}

float IOToolkit::IODeviceDetails::GetKeyDownDuration(const FKey& InKey)
{
    return PlayerInput::Instance().GetKeyDownTime(InKey, device.GetID());
}

float IOToolkit::IODeviceDetails::GetDO(const FKey& InKey)
{
    return rawIO ? rawIO->GetDO(InKey) : -1;
}


IOToolkit::FInputActionBinding& IOToolkit::IODeviceDetails::GetActionBinding(const int32 BindingIndex)
{
    return ActionBindings[BindingIndex];
}

std::string IOToolkit::IODeviceDetails::getName()
{
    return name;
}

std::string IOToolkit::IODeviceDetails::getIOType()
{
    if(rawIO)
    {
       return rawIO->getIOType();
    }
    return "Invalid";
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
    std::string msg = std::string("Trying to bind") + customMsg + " delegate with an invalid device";
    IOLog::Instance().Warning(msg);
    return false;
}
