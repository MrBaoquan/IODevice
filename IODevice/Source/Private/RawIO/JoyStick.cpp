/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#include "RawIO/Joystick.h"
#include <algorithm>
#include "PlayerInput.h"
#include "InputCoreTypes.h"
#include "IOLog.h"
#include "CoreTypes/IOTypes.h"


DevelopHelper::Joystick::Joystick(uint8 InID, uint8 InIndex) :CustomIOBase(InID,InIndex,IOType::Joystick)
{
    JoyStickServer::Instance().Initialize();
	Constructor();
}

void DevelopHelper::Joystick::Tick(float DeltaSeconds)
{
    if (jsEntry)
    {
        jsEntry->Update();
        const DIJOYSTATE2* js = &jsEntry->joystate;
        for (uint8 index = 0;index < inputCount;++index)
        {
            DIStatus[index] = js->rgbButtons[index] > 0 ? 1 : 0;
        }

        DispatchButtonEvent(DIStatus, channelsState);

        static float valMax = static_cast<float>(JoyStickServer::Instance().GetMaxVal());
        InputAxis(EKeys::JS_X, static_cast<float>(js->lX / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_Y, static_cast<float>(js->lY / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_Z, static_cast<float>(js->lZ / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_Rx, static_cast<float>(js->lRx / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_Ry, static_cast<float>(js->lRy / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_Rz, static_cast<float>(js->lRz / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_VX, static_cast<float>(js->lVX / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_VY, static_cast<float>(js->lVY / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_VZ, static_cast<float>(js->lVZ / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_VRx, static_cast<float>(js->lVRx / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_VRy, static_cast<float>(js->lVRy / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_VRz, static_cast<float>(js->lVRz / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_AX, static_cast<float>(js->lAX / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_AY, static_cast<float>(js->lAY / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_AZ, static_cast<float>(js->lAZ / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_ARx, static_cast<float>(js->lARx / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_ARy, static_cast<float>(js->lARy / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_ARz, static_cast<float>(js->lARz / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_FX, static_cast<float>(js->lFX / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_FY, static_cast<float>(js->lFY / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_FZ, static_cast<float>(js->lFZ / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_FRx, static_cast<float>(js->lFRx / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_FRy, static_cast<float>(js->lFRy / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_FRz, static_cast<float>(js->lFRz / valMax), 0.f, deviceID, 1);

        InputAxis(EKeys::JS_Slider_00, static_cast<float>(js->rglSlider[0] / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_Slider_01, static_cast<float>(js->rglSlider[1] / valMax), 0.f, deviceID, 1);

        InputAxis(EKeys::JS_VSlider_00, static_cast<float>(js->rglVSlider[0] / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_VSlider_01, static_cast<float>(js->rglVSlider[1] / valMax), 0.f, deviceID, 1);

        InputAxis(EKeys::JS_ASlider_00, static_cast<float>(js->rglASlider[0] / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_ASlider_01, static_cast<float>(js->rglASlider[1] / valMax), 0.f, deviceID, 1);

        InputAxis(EKeys::JS_FSlider_00, static_cast<float>(js->rglFSlider[0] / valMax), 0.f, deviceID, 1);
        InputAxis(EKeys::JS_FSlider_01, static_cast<float>(js->rglFSlider[1] / valMax), 0.f, deviceID, 1);

        static float perDirection = 4500.f;
        float pov_00 = js->rgdwPOV[0] / perDirection;
        pov_00 = pov_00 < 10 ? pov_00 : -1;
        InputAxis(EKeys::JS_POV_00, pov_00, 0.f, deviceID, 1);
        
        float pov_01 = js->rgdwPOV[1] / perDirection;
        pov_01 = pov_01 < 10 ? pov_01 : -1;
        InputAxis(EKeys::JS_POV_01, pov_01, 0.f, deviceID, 1);
        
        float pov_02 = js->rgdwPOV[2] / perDirection;
        pov_02 = pov_02 < 10 ? pov_02 : -1;
        InputAxis(EKeys::JS_POV_02, pov_02, 0.f, deviceID, 1);
        
        float pov_03 = js->rgdwPOV[3] / perDirection;
        pov_03 = pov_03 < 10 ? pov_03 : -1;
        InputAxis(EKeys::JS_POV_03, pov_03, 0.f, deviceID, 1);
    }
    
}


void DevelopHelper::Joystick::Initialize()
{
	__super::Initialize();
}

void DevelopHelper::Joystick::Constructor()
{
    jsEntry = JoyStickServer::Instance().GetEntry(deviceIndex);
    if (!jsEntry)
    {
        return;
    }

    inputCount = static_cast<uint8>(jsEntry->diDevCaps.dwButtons);
    channelsState = std::vector<ButtonState>(inputCount);
    DIStatus = std::vector<BYTE>(inputCount, 0);
	__super::Constructor();
}

DevelopHelper::JoyStickServer& DevelopHelper::JoyStickServer::Instance()
{
    static JoyStickServer instance;
    return instance;
}

/** JoyStickServer implemention */

void DevelopHelper::JoyStickServer::Initialize()
{
    if (SUCCEEDED(DirectInput8Create(GetModuleHandle(0), DIRECTINPUT_VERSION, IID_IDirectInput8, (void**)&lpDi, 0)))
    {
        djs.enumerate(lpDi, DI8DEVCLASS_GAMECTRL);
    }
}

DIJoyStick::Entry* DevelopHelper::JoyStickServer::GetEntry(int entryIndex)
{
    return djs.getEntry(entryIndex);
}

DevelopHelper::JoyStickServer::JoyStickServer()
{
    Initialize();
}
