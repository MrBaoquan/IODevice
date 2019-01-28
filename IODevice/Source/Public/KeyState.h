/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include <vector>
#include "CoreTypes.inl"
#include "Math/Vector.h"

namespace DevelopHelper
{

struct FKeyState
{
    FKeyState():RawValue(0.f,0.f,0.f),
        Value(0.f,0.f,0.f),
        bDown(0),
        bDownPrevious(0),
        bConsumed(false),
        LastUpDownTransitionTime(0.f),
        SampleCountAccumulator(0),
        RawValueAccumulator(0.f,0.f,0.f){}
  
    Vector RawValue;
    Vector Value;
    uint8 bDown : 1;
    uint8 bDownPrevious : 1;

    bool IsKeyDown = false;
    bool IsKeyUp = false;

    /** Global time of last up->down or down->up transition. */
    float LastUpDownTransitionTime;
 
    /** How many samples contributed to RawValueAccumulator. Used for smoothing operations, e.g. mouse */
    uint8 SampleCountAccumulator;

    /** True if this key has been "consumed" by an InputComponent and should be ignored for further components during this update. */
    uint8 bConsumed : 1;

    /** How many of each event type had been received when input was last processed. */
    std::vector<uint32> EventCounts[IE_MAX];

    /** Used to accumulate events during the frame and flushed when processed. */
    std::vector<uint32> EventAccumulator[IE_MAX];

    /** Used to accumulate input values during the frame and flushed after processing. */
    Vector RawValueAccumulator;
};


};