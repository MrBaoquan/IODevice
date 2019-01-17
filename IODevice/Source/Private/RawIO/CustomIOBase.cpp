/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#include "RawIO/CustomIOBase.h"

void DevelopHelper::CustomIOBase::Initlialize()
{
    for (uint8 channelIndex = 0;channelIndex < channelsState.size();++channelIndex)
    {
        ButtonState& chState = channelsState[channelIndex];
        chState.Key = GetButtonKey(channelIndex);
    }
}

void DevelopHelper::CustomIOBase::Tick(float DeltaSeconds)
{

}

void DevelopHelper::CustomIOBase::OnFrameEnd()
{

}