/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include <string>
#include "InputBinding.h"
#include "CoreTypes.inl"

namespace DevelopHelper 
{

struct FInputActionBinding :public FInputBinding
{
private:
    /** Whether the binding is part of a paired (both pressed and released events bound) action */
    uint8 bPaired : 1;

public:
    /** Key event to bind it to, e.g. pressed, released, double click */
    InputEvent KeyEvent;

    /** Friendly name of action, e.g "jump" */
    std::string ActionName;

public:
    /** The delegate bound to the action */
    InputActionUnifiedDelegate ActionDelegate;

    FInputActionBinding()
        : FInputBinding()
        , bPaired(false)
        , KeyEvent(InputEvent::IE_Pressed)
        , ActionName("NAME_None")
    { }

    FInputActionBinding(const std::string InActionName, const InputEvent InKeyEvent)
        : FInputBinding()
        , bPaired(false)
        , KeyEvent(InKeyEvent)
        , ActionName(InActionName)
    { }

    bool IsPaired() const { return bPaired; }
    friend struct IODeviceDetails;
};


};

