#include "RawIO/RawIO.h"
#include <windows.h>
#include "PlayerInput.h"
#include "InputSettings.h"

void DevelopHelper::RawIO::Tick(float DeltaSeconds)
{
    
}

int DevelopHelper::RawIO::SetDeviceDO(BYTE* InDOStatus)
{
    return -1;
}

int DevelopHelper::RawIO::SetDeviceDO(const FKey InKey, BYTE val)
{
    return -1;
}

int DevelopHelper::RawIO::GetDeviceDO(BYTE* OutDOStatus)
{
    return -1;
}

BYTE DevelopHelper::RawIO::GetDeviceDO(const FKey InKey)
{
    return -1;
}

void DevelopHelper::RawIO::OnFrameEnd()
{

}


void DevelopHelper::RawIO::InputKey(FKey InKey, InputEvent keyEvent,int deviceID)
{
    PlayerInput::Instance().InputKey(InKey, keyEvent, deviceID);
}

void DevelopHelper::RawIO::InputAxis(FKey Key, float Delta, float DeltaTime, uint8 InID, int32 NumSamples)
{
    PlayerInput::Instance().InputAxis(Key, Delta, 0.f, InID, NumSamples);
}

void DevelopHelper::RawIO::Initialize()
{
    KeyProperties = UInputSettings::Instance().KeyProperties[deviceID];
}

void DevelopHelper::RawIO::DispatchButtonEvent(std::vector<BYTE> DIStatus, std::vector<ButtonState>& channelsState)
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

void DevelopHelper::RawIO::DispatchAxisEvent(std::vector<short> InAxis)
{
    for (uint8 index = 0;index < InAxis.size();++index)
    {
        InputAxis(GetAxisKey(index), InAxis[index]/MaxAxisValue, 0.f, deviceID, 1);
    }
}

DevelopHelper::InputEvent DevelopHelper::RawIO::GetChannelEvent(ButtonState& chState)
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

bool DevelopHelper::RawIO::IsKeyPressed(struct ButtonState& chState)
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

DevelopHelper::FKey DevelopHelper::RawIO::GetButtonKey(uint8 channelIndex)
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

DevelopHelper::FKey DevelopHelper::RawIO::GetAxisKey(uint8 axisIndex)
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

