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


int IOToolkit::RawIO::RefreshStreamingData(BYTE* StreamingData, unsigned int DataSize)
{
    return 0;
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
        InputAxis(GetAxisKey(index), InAxis[index], 0.f, deviceID, 1);
    }
}

void IOToolkit::RawIO::DispatchAxisEvent(std::vector<int32_t> InAxis)
{
    for (uint8 index = 0;index < InAxis.size();++index)
    {
        // 使用 int32_t，根据实际需求进行处理
        // 可以直接使用整数值，或者按需转换
        InputAxis(GetAxisKey(index), static_cast<float>(InAxis[index]), 0.f, deviceID, 1);
    }
}

IOToolkit::InputEvent IOToolkit::RawIO::GetChannelEvent(ButtonState& chState)
{
    InputEvent FinalInputEvent = IE_MAX;
    bool bPressed = IsKeyPressed(chState);
    if (bPressed)
    {
        double currentTime = GetTickCount() / 1000.0;
        if (chState.status != chState.lastStatus)   // �����¼�
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
    else if (chState.status != chState.lastStatus) // �����¼�
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
		NewVal += KeyProps->Offset;
		NewVal *= KeyProps->Scale;

		
		float deadZoneDenom = 1.f - KeyProps->DeadZone;
		if (deadZoneDenom > 0.001f)
		{
			if (NewVal > 0)
			{
				NewVal = max(0.f, NewVal - KeyProps->DeadZone) / deadZoneDenom;
			}
			else
			{
				NewVal = -max(0.f, -NewVal - KeyProps->DeadZone) / deadZoneDenom;
			}
		}
		else
		{
			NewVal = 0.f; // 死区过大，直接输出0
		}

		// 指数曲线处理，修正公式：sign(x) * pow(|x|, exponent)
		if (KeyProps->Exponent != 1.f)
		{
			float sign = NewVal >= 0.f ? 1.f : -1.f;
			NewVal = sign * std::powf(std::abs(NewVal), KeyProps->Exponent);
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

int IOToolkit::RawIO::SetOKProps(const char* oactionName, const char* keyName, float scale, bool invertEvent)
{
	// 默认实现返回 0，由子类重写
	return 0;
}
