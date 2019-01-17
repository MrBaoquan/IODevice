#pragma once
#include "InputBinding.h"
#include "InputChord.h"

namespace DevelopHelper 
{

/**
 * ��������ṹ
 */
struct FInputKeyBinding : public FInputBinding
{
    InputEvent KeyEvent;

    /** Input Chord to bind to */
    FInputChord Chord;

    FInputKeyBinding()
        : FInputBinding()
        , KeyEvent(InputEvent::IE_Pressed)
    { }

    FInputKeyBinding(const FInputChord InChord, const InputEvent InKeyEvent)
        : FInputBinding()
        , KeyEvent(InKeyEvent)
        , Chord(InChord)
    { }
    InputActionUnifiedDelegate KeyDelegate;

};

};