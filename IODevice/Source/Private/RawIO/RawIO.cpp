/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#include "RawIO/RawIO.h"
#include <windows.h>
#include "PlayerInput.h"
#include "InputSettings.h"

#define CLAMP(x, low, high)  (((x) > (high)) ? (high) : (((x) < (low)) ? (low) : (x)))

void IOToolkit::RawIO::Tick(float DeltaSeconds)
{
    
}

int IOToolkit::RawIO::SetDO(float* InDOStatus)
{
    return -1;
}


int IOToolkit::RawIO::SetDOOn(const char* InOAction)
{
	return -1;
}


int IOToolkit::RawIO::SetDOOff(const char* InOAction)
{
	return -1;
}

int IOToolkit::RawIO::DOImmediate()
{
	return 0;
}

int IOToolkit::RawIO::SetDO(const char* InOAction, float val, bool bIgnoreMassage/*=false*/)
{
	return -1;
}
int IOToolkit::RawIO::SetDO(const FKey& InKey, float val)
{
    return -1;
}

int IOToolkit::RawIO::GetDO(float* OutDOStatus)
{
    return -1;
}


float IOToolkit::RawIO::GetDO(const char* InOAction)
{
	return 0;
}

float IOToolkit::RawIO::GetDO(const FKey InKey)
{
    return 0;
}

void IOToolkit::RawIO::OnFrameEnd()
{

}


void IOToolkit::RawIO::InputKey(FKey InKey, InputEvent keyEvent,int deviceID)
{
    PlayerInput::Instance().InputKey(InKey, keyEvent, deviceID);
}

void IOToolkit::RawIO::InputAxis(FKey Key, float Delta, float DeltaTime, uint8 InID, int32 NumSamples)
{
    PlayerInput::Instance().InputAxis(Key, Delta, 0.f, InID, NumSamples);
}

void IOToolkit::RawIO::Initialize()
{
    KeyProperties = UInputSettings::Instance().KeyProperties[deviceID];
}

void IOToolkit::RawIO::DispatchButtonEvent(std::vector<BYTE> DIStatus, std::vector<ButtonState>& channelsState)
{
    for (uint8 channelIndex = 0;channelIndex < DIStatus.size();channelIndex++)
    {
        ButtonState& chState = channelsState[channelIndex];
        chState.lastStatus = chState.status;
        chState.status = DIStatus[channelIndex];

        InputEvent IEEvent = GetChannelEvent(chState);
        if (IEEvent != IE_MAX)
        {
            InputKey(chState.Key, IEEvent, deviceID);
        }
    }
}

void IOToolkit::RawIO::DispatchAxisEvent(std::vector<short> InAxis)
{
    for (uint8 index = 0;index < InAxis.size();++index)
    {
        InputAxis(GetAxisKey(index), InAxis[index]/MaxAxisValue, 0.f, deviceID, 1);
    }
}

IOToolkit::InputEvent IOToolkit::RawIO::GetChannelEvent(ButtonState& chState)
{
    InputEvent FinalInputEvent = IE_MAX;
    bool bPressed = IsKeyPressed(chState);
    if (bPressed)
    {
        double currentTime = GetTickCount() / 1000.0;
        if (chState.status != chState.lastStatus)   // 按下事件
        {
            FinalInputEvent = IE_Pressed;
            chState.lastRepeatTime = currentTime;
            chState.bDelay = true;
        }
        else
        {
            static float rate = 1.0f / repeatRate;

            double delayTime = currentTime - chState.lastRepeatTime;
            if (chState.bDelay && delayTime < repeatDelay)
            {
                chState.bDelay = false;
                chState.lastRepeatTime = currentTime;
                return IE_MAX;
            }
            if (delayTime >= rate)
            {
                FinalInputEvent = IE_Repeat;
                chState.lastRepeatTime = currentTime;
            }
        }
    }
    else if (chState.status != chState.lastStatus) // 弹起事件
    {
        FinalInputEvent = IE_Released;
    }
    return FinalInputEvent;
}

bool IOToolkit::RawIO::IsKeyPressed(struct ButtonState& chState)
{
    bool bPressed = chState.status == pressedValue ? true : false;
    if (KeyProperties.count(chState.Key))
    {
        FInputKeyProperties keyProps = KeyProperties[chState.Key];
        if (keyProps.bInvertEvent)
        {
            bPressed = !bPressed;
        }
    }
    return bPressed;
}

IOToolkit::FKey IOToolkit::RawIO::GetButtonKey(uint8 channelIndex)
{
    std::string channelKeyPrefix("Button_");
    std::string fullKeyName = "";
    if (channelIndex < 0) { return EKeys::Invalid; }
    if (channelIndex < 10)
    {
        fullKeyName = channelKeyPrefix + "0" + std::to_string(channelIndex);
    }
    else
    {
        fullKeyName = channelKeyPrefix + std::to_string(channelIndex);
    }
    return FKey(fullKeyName.data());
}

IOToolkit::FKey IOToolkit::RawIO::GetAxisKey(uint8 axisIndex)
{
    std::string channelKeyPrefix("Axis_");
    std::string fullKeyName = "";
    if (axisIndex < 0) { return EKeys::Invalid; }
    if (axisIndex < 10)
    {
        fullKeyName = channelKeyPrefix + "0" + std::to_string(axisIndex);
    }
    else
    {
        fullKeyName = channelKeyPrefix + std::to_string(axisIndex);
    }
    return FKey(fullKeyName.data());
}


IOToolkit::FKey IOToolkit::RawIO::GetOAxisKey(uint8 oaxisIndex)
{
	std::string channelKeyPrefix("OAxis_");
	std::string fullKeyName = "";
	if (oaxisIndex < 0) { return EKeys::Invalid; }
	if (oaxisIndex < 10)
	{
		fullKeyName = channelKeyPrefix + "0" + std::to_string(oaxisIndex);
	}
	else
	{
		fullKeyName = channelKeyPrefix + std::to_string(oaxisIndex);
	}
	return FKey(fullKeyName.data());
}

float IOToolkit::RawIO::MassageKeyInput(FKey InKey, float InRawValue)
{
	float NewVal = InRawValue;
	if (KeyProperties.count(InKey))
	{
		FInputKeyProperties const* const KeyProps = &KeyProperties.at(InKey);
		NewVal += KeyProps->PreOffset;
		NewVal *= KeyProps->PreScale;
		if (NewVal > 0)
		{
			NewVal = max(0.f, NewVal - KeyProps->DeadZone) / (1.f - KeyProps->DeadZone);
		}
		else
		{
			NewVal = -max(0.f, -NewVal - KeyProps->DeadZone) / (1.f - KeyProps->DeadZone);
		}
		if (KeyProps->Exponent != 1.f)
		{
			NewVal = std::sin(NewVal)*std::powf(std::abs(NewVal), KeyProps->Exponent);
		}
		NewVal *= KeyProps->Sensitivity;

		NewVal = CLAMP(NewVal, KeyProps->Min, KeyProps->Max);

		if (KeyProps->bInvert)
		{
			NewVal *= -1.f;
		}
	}
	return NewVal;
}

