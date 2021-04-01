/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include <cfloat>
#include "CoreTypes.inl"

namespace IOToolkit
{


struct FInputKeyProperties
{
	/**
	 * 预偏移值 在死区前
	 */
	float PreOffset;

	/**
	 * 预缩放值 在死区前
	 */
	float PreScale;

    /** What the dead zone of the axis is.  For control axes such as analog sticks. */
    float DeadZone;

    /** Scaling factor to multiply raw value by. */
    float Sensitivity;

    /** For applying curves to [0..1] axes, e.g. analog sticks */
    float Exponent;

    /** Inverts reported values for this axis */
    uint8 bInvert : 1;

    uint8 bInvertEvent : 1;

	float Min;
	float Max;

    FInputKeyProperties()
        : PreScale(1.f)
		, PreOffset(0.f)
		, DeadZone(0.2f)
        , Sensitivity(1.f)
        , Exponent(1.f)
        , bInvert(false)
        , bInvertEvent(false)
		, Min(-FLT_MAX)
		, Max(FLT_MAX)
    {}

};



};