/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include "CustomIOBase.h"
#include "DIJoyStick.hpp"

namespace DevelopHelper
{

class JoyStickServer
{
public:
    static JoyStickServer& Instance();
    void Initialize();
    DIJoyStick::Entry* GetEntry(int entryIndex);
    short GetMaxVal()const
    {
        return djs.getMaxValue();
    }
private:
    JoyStickServer();
    DIJoyStick djs;
    LPDIRECTINPUT lpDi = 0;
};

class Joystick :public CustomIOBase
{
public:
    Joystick(uint8 InID, uint8 InIndex);

    virtual void Tick(float DeltaSeconds) override;

	virtual void Initialize() override;
    void Constructor();

    virtual const bool Valid() const { return jsEntry!=nullptr; }

private:
    DIJoyStick::Entry* jsEntry = nullptr;
};


};
