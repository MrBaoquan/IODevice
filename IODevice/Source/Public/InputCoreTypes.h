/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include <memory>
#include <map>
#include "IOExportsAPI.h"
#include "CoreTypes.inl"

namespace IOToolkit
{

/**
* Input event
*/
enum InputEvent
{
    IE_Pressed = 0,
    IE_Released = 1,
    IE_Repeat = 2,
    IE_DoubleClick = 3,
    IE_Axis = 4,
    IE_MAX = 5,
};

/**
 * Key struct .
 */
struct IOAPI FKey
{
public:
    FKey():FKey("Name_None"){}
    FKey(const char* InKeyName);
    FKey(const FKey& key);
    ~FKey();

    const char* GetName() const { return keyName; };
    const bool IsValid() const;
    const bool IsModifierKey() const;
    bool IsFloatAxis() const;
    bool IsVectorAxis() const;
    void operator=(const FKey& rhs);
    friend bool operator==(const FKey& KeyA, const FKey& KeyB);
    friend bool operator!=(const FKey& KeyA, const FKey& KeyB);
private:
    char* keyName = nullptr;

};

/**
 * All inputable keys.
 */

struct IOAPI EKeys
{
    /** Standard Keyboard & Mouse */
    static const FKey AnyKey;

    static const FKey MouseX;
    static const FKey MouseY;
    static const FKey MouseScrollUp;
    static const FKey MouseScrollDown;
    static const FKey MouseWheelAxis;

    static const FKey LeftMouseButton;
    static const FKey RightMouseButton;
    static const FKey MiddleMouseButton;
    static const FKey ThumbMouseButton;
    static const FKey ThumbMouseButton2;

    static const FKey BackSpace;
    static const FKey Tab;
    static const FKey Enter;
    static const FKey Pause;

    static const FKey CapsLock;
    static const FKey Escape;
    static const FKey SpaceBar;
    static const FKey PageUp;
    static const FKey PageDown;
    static const FKey End;
    static const FKey Home;

    static const FKey Left;
    static const FKey Up;
    static const FKey Right;
    static const FKey Down;

    static const FKey Insert;
    static const FKey Delete;

    static const FKey Zero;
    static const FKey One;
    static const FKey Two;
    static const FKey Three;
    static const FKey Four;
    static const FKey Five;
    static const FKey Six;
    static const FKey Seven;
    static const FKey Eight;
    static const FKey Nine;

    static const FKey A;
    static const FKey B;
    static const FKey C;
    static const FKey D;
    static const FKey E;
    static const FKey F;
    static const FKey G;
    static const FKey H;
    static const FKey I;
    static const FKey J;
    static const FKey K;
    static const FKey L;
    static const FKey M;
    static const FKey N;
    static const FKey O;
    static const FKey P;
    static const FKey Q;
    static const FKey R;
    static const FKey S;
    static const FKey T;
    static const FKey U;
    static const FKey V;
    static const FKey W;
    static const FKey X;
    static const FKey Y;
    static const FKey Z;

    static const FKey NumPadZero;
    static const FKey NumPadOne;
    static const FKey NumPadTwo;
    static const FKey NumPadThree;
    static const FKey NumPadFour;
    static const FKey NumPadFive;
    static const FKey NumPadSix;
    static const FKey NumPadSeven;
    static const FKey NumPadEight;
    static const FKey NumPadNine;

    static const FKey Multiply;
    static const FKey Add;
    static const FKey Subtract;
    static const FKey Decimal;
    static const FKey Divide;

    static const FKey F1;
    static const FKey F2;
    static const FKey F3;
    static const FKey F4;
    static const FKey F5;
    static const FKey F6;
    static const FKey F7;
    static const FKey F8;
    static const FKey F9;
    static const FKey F10;
    static const FKey F11;
    static const FKey F12;

    static const FKey NumLock;

    static const FKey ScrollLock;

    static const FKey LeftShift;
    static const FKey RightShift;
    static const FKey LeftControl;
    static const FKey RightControl;
    static const FKey LeftAlt;
    static const FKey RightAlt;
    static const FKey LeftCommand;
    static const FKey RightCommand;
    static const FKey Invalid;

    /** Buttons for PCI & JoyStick  */
    static const FKey Button_00;
    static const FKey Button_01;
    static const FKey Button_02;
    static const FKey Button_03;
    static const FKey Button_04;
    static const FKey Button_05;
    static const FKey Button_06;
    static const FKey Button_07;
    static const FKey Button_08;
    static const FKey Button_09;
    static const FKey Button_10;
    static const FKey Button_11;
    static const FKey Button_12;
    static const FKey Button_13;
    static const FKey Button_14;
    static const FKey Button_15;
    static const FKey Button_16;
    static const FKey Button_17;
    static const FKey Button_18;
    static const FKey Button_19;
    static const FKey Button_20;
    static const FKey Button_21;
    static const FKey Button_22;
    static const FKey Button_23;
    static const FKey Button_24;
    static const FKey Button_25;
    static const FKey Button_26;
    static const FKey Button_27;
    static const FKey Button_28;
    static const FKey Button_29;
    static const FKey Button_30;
    static const FKey Button_31;


    static const FKey Axis_00;
    static const FKey Axis_01;
    static const FKey Axis_02;
    static const FKey Axis_03;
    static const FKey Axis_04;
    static const FKey Axis_05;
    static const FKey Axis_06;
    static const FKey Axis_07;
    static const FKey Axis_08;
    static const FKey Axis_09;
    static const FKey Axis_10;
    static const FKey Axis_11;
    static const FKey Axis_12;
    static const FKey Axis_13;
    static const FKey Axis_14;
    static const FKey Axis_15;
    static const FKey Axis_16;
    static const FKey Axis_17;
    static const FKey Axis_18;
    static const FKey Axis_19;
    static const FKey Axis_20;
    static const FKey Axis_21;
    static const FKey Axis_22;
    static const FKey Axis_23;
    static const FKey Axis_24;
    static const FKey Axis_25;
    static const FKey Axis_26;
    static const FKey Axis_27;
    static const FKey Axis_28;
    static const FKey Axis_29;
    static const FKey Axis_30;
    static const FKey Axis_31;

	static const FKey OAxis_00;
	static const FKey OAxis_01;
	static const FKey OAxis_02;
	static const FKey OAxis_03;
	static const FKey OAxis_04;
	static const FKey OAxis_05;
	static const FKey OAxis_06;
	static const FKey OAxis_07;
	static const FKey OAxis_08;
	static const FKey OAxis_09;
	static const FKey OAxis_10;
	static const FKey OAxis_11;
	static const FKey OAxis_12;
	static const FKey OAxis_13;
	static const FKey OAxis_14;
	static const FKey OAxis_15;
	static const FKey OAxis_16;
	static const FKey OAxis_17;
	static const FKey OAxis_18;
	static const FKey OAxis_19;
	static const FKey OAxis_20;
	static const FKey OAxis_21;
	static const FKey OAxis_22;
	static const FKey OAxis_23;
	static const FKey OAxis_24;
	static const FKey OAxis_25;
	static const FKey OAxis_26;
	static const FKey OAxis_27;
	static const FKey OAxis_28;
	static const FKey OAxis_29;
	static const FKey OAxis_30;
	static const FKey OAxis_31;


    /** Joystick axes */
    static const FKey JS_X;                     /* x-axis position              */
    static const FKey JS_Y;                     /* y-axis position              */
    static const FKey JS_Z;                     /* z-axis position              */
    static const FKey JS_Rx;                    /* x-axis rotation              */
    static const FKey JS_Ry;                    /* y-axis rotation              */
    static const FKey JS_Rz;                    /* z-axis rotation              */
    static const FKey JS_VX;                    /* x-axis velocity              */
    static const FKey JS_VY;                    /* y-axis velocity              */
    static const FKey JS_VZ;                    /* z-axis velocity              */
    static const FKey JS_VRx;                   /* x-axis angular velocity      */
    static const FKey JS_VRy;                   /* y-axis angular velocity      */
    static const FKey JS_VRz;                   /* z-axis angular velocity      */
    static const FKey JS_AX;                    /* x-axis acceleration          */
    static const FKey JS_AY;                    /* y-axis acceleration          */
    static const FKey JS_AZ;                    /* z-axis acceleration          */
    static const FKey JS_ARx;                   /* x-axis angular acceleration  */
    static const FKey JS_ARy;                   /* y-axis angular acceleration  */
    static const FKey JS_ARz;                   /* z-axis angular acceleration  */
    static const FKey JS_FX;                    /* x-axis force                 */
    static const FKey JS_FY;                    /* y-axis force                 */
    static const FKey JS_FZ;                    /* z-axis force                 */
    static const FKey JS_FRx;                   /* x-axis torque                */
    static const FKey JS_FRy;                   /* y-axis torque                */
    static const FKey JS_FRz;                   /* z-axis torque                */

    static const FKey JS_Slider_00;           /* extra axes positions         */
    static const FKey JS_Slider_01;           /* extra axes positions         */
    
    static const FKey JS_VSlider_00;          /* extra axes velocities        */
    static const FKey JS_VSlider_01;          /* extra axes velocities        */

    static const FKey JS_ASlider_00;          /* extra axes accelerations     */
    static const FKey JS_ASlider_01;          /* extra axes accelerations     */

    static const FKey JS_FSlider_00;          /* extra axes forces            */
    static const FKey JS_FSlider_01;          /* extra axes forces            */

    static const FKey JS_POV_00;             /* POV directions               */
    static const FKey JS_POV_01;             /* POV directions               */
    static const FKey JS_POV_02;             /* POV directions               */
    static const FKey JS_POV_03;             /* POV directions               */
};

};