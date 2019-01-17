#pragma once
#include "CoreTypes.inl"

namespace DevelopHelper
{


struct FInputKeyProperties
{

    /** What the dead zone of the axis is.  For control axes such as analog sticks. */
    float DeadZone;

    /** Scaling factor to multiply raw value by. */
    float Sensitivity;

    /** For applying curves to [0..1] axes, e.g. analog sticks */
    float Exponent;

    /** Inverts reported values for this axis */
    uint8 bInvert : 1;

    uint8 bInvertEvent : 1;

    FInputKeyProperties()
        : DeadZone(0.2f)
        , Sensitivity(1.f)
        , Exponent(1.f)
        , bInvert(false)
        , bInvertEvent(false)
    {}

};



};