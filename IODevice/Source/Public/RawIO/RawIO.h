/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include <map>
#include <vector>
#include "CoreTypes.inl"
#include "InputCoreTypes.h"
#include "CoreTypes/InputKeyProperties.h"
#include "LessKey.h"

namespace  DevelopHelper
{

const uint8 InvalidDeviceID = static_cast<uint8>(255);
const uint8 MaxIOCount = static_cast<uint8>(255);
const float MaxAxisValue = 1000.f;

class RawIO
{
public:
    RawIO():deviceID(InvalidDeviceID),IOType("Invalid"){}
    RawIO(uint8 InID,std::string InType):deviceID(InID),IOType(InType){}
    virtual ~RawIO() {}

    virtual void Tick(float DeltaSeconds);

	virtual void Destroy() {};

    virtual const bool Valid() const { return false; }

    virtual int SetDO(short* InDOStatus);

    virtual int SetDO(const FKey InKey, short val);

	virtual int SetDO(const char* InOAction, short val);
	virtual int SetDOOn(const char* InOAction);
	virtual int SetDOOff(const char* InOAction);

    virtual int GetDO(short* OutDOStatus);

    virtual short GetDO(const FKey InKey);

	virtual void Initialize() = 0;

    virtual void OnFrameEnd();

    void InputKey(FKey InKey, InputEvent keyEvent,int deviceID);

    void InputAxis(FKey Key, float Delta, float DeltaTime, uint8 InID, int32 NumSamples);

    const uint8 ID()const { return deviceID; };
    const std::string getIOType()const { return IOType; }
public:
    struct ButtonState
    {
        FKey Key;
        uint8 status : 1;

        uint8 lastStatus : 1;

        /** Whether should delay when calcuate IE_Repeat event. */
        uint8 bDelay : 1;

        double lastRepeatTime;

        ButtonState() :
            Key(EKeys::Invalid),
            status(0)
            , lastStatus(0)
            , bDelay(false)
            , lastRepeatTime(0.0) {}
    };
protected:
    void DispatchButtonEvent(std::vector<BYTE> DIStatus, std::vector<ButtonState>& channelsState);
    void DispatchAxisEvent(std::vector<short> InAxis);

    InputEvent GetChannelEvent(ButtonState& chState);

    bool IsKeyPressed(ButtonState& chState);

    FKey GetButtonKey(uint8 channelIndex);
    FKey GetAxisKey(uint8 axisIndex);
	FKey GetOAxisKey(uint8 oaxisIndex);

	float MassageKeyInput(FKey InKey, float InRawValue);

protected:
    uint8 deviceID;

    uint8 pressedValue = 1;
    float repeatDelay = 0.1f;
    uint8 repeatRate = 30;  // 20 character per second.

    std::map<FKey,struct FInputKeyProperties, LessKey> KeyProperties;

    std::string IOType;

    BYTE inputCount = 16;
    BYTE outputCount = 16;
    BYTE axisCount = 0;
};

};