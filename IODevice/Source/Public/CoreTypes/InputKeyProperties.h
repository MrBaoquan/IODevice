/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include <cfloat>
#include "CoreTypes.inl"

namespace IOToolkit
{


enum class ESafeStopPolicy : uint8
{
    Immediate = 0,
    Controlled = 1
};

struct FInputKeyProperties
{
	/**
	 * 偏移值，用于校准零点
	 */
	float Offset;

	/**
	 * 缩放值，用于映射输入范围
	 */
	float Scale;

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

	/**
	 * 变化速率上限 (归一化值/秒; 0 = 不限速)。
	 * 限制该通道每秒最多能变多少 —— 内容里的阶跃会顶到执行机构报错,
	 * 由 MotionPlayer 派发前按通道执行。命名取控制领域的 SlewRate 而非
	 * MaxPerSecond: 后者字面像"每秒的最大值", 而这是"变化率的上限"。
	 */
	float SlewRate;

    /** Safe target in engineering units used by SafeStop. */
    float SafeValue;

    /** Ramp rate in normalized range units per second for Controlled SafeStop. 0 = use SlewRate. */
    float SafeStopRate;

    /** Immediate for discrete outputs / hard stop; Controlled for analog actuators. */
    ESafeStopPolicy SafeStopPolicy;

    FInputKeyProperties()
        : Offset(0.f)
		, Scale(1.f)
		, DeadZone(0.2f)
        , Sensitivity(1.f)
        , Exponent(1.f)
        , bInvert(false)
        , bInvertEvent(false)
		, Min(-FLT_MAX)
		, Max(FLT_MAX)
		, SlewRate(0.f)
        , SafeValue(0.f)
        , SafeStopRate(0.f)
        , SafeStopPolicy(ESafeStopPolicy::Immediate)
    {}

};



};