/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#include "RawIO/CustomIOBase.h"

void IOToolkit::CustomIOBase::Constructor()
{
    for (uint8 channelIndex = 0;channelIndex < channelsState.size();++channelIndex)
    {
        ButtonState& chState = channelsState[channelIndex];
        chState.Key = GetButtonKey(channelIndex);
    }
}

void IOToolkit::CustomIOBase::Tick(float DeltaSeconds)
{

}

void IOToolkit::CustomIOBase::OnFrameEnd()
{

}

void IOToolkit::CustomIOBase::Initialize()
{
	__super::Initialize();
}
