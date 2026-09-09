/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include <windows.h>
#include "RawIO.h"
#include "Math/Vector2D.h"
#include "CoreTypes/IOTypes.h"

namespace IOToolkit
{

    namespace EMouseButtons
    {
        enum Type
        {
            Left = 0,
            Middle,
            Right,
            Thumb01,
            Thumb02,

            Invalid,
        };
    }

    enum class EWindowActivation : uint8
    {
        Activate,
        ActivateByMouse,
        Deactivate
    };


class StandardIO : public RawIO
{
public:
    static LRESULT CALLBACK OnMessageProc(int code, WPARAM wParam, LPARAM lParam);
    static LRESULT CALLBACK OnKeyboardProc(int code, WPARAM wParam, LPARAM lParam);
    static LRESULT CALLBACK CallWndRetProc(_In_ int nCode, _In_ WPARAM wParam, _In_ LPARAM lParam);
    StandardIO(int InID) :RawIO(InID,IOType::Standard) 
    {
        StandardDeviceID = deviceID;
        Build(); 
    }
    virtual ~StandardIO() override {}

	virtual void Initialize() override;

    virtual void Tick(float DeltaSeconds) override;
    virtual void OnFrameEnd() override;
    static FKey TranslateMouseButtonToKey(const EMouseButtons::Type Button);

    static void Build();
    static void OnKeyDown(uint32 keyCode, uint32 charCode, bool isRepeat=false);
    static void OnKeyUp(uint32 keyCode, uint32 charCode, bool isRepeat = false);

    static void OnMouseUp(const EMouseButtons::Type Button, const Vector2D CursorPos);

    static void OnMouseDown(const EMouseButtons::Type Button, const Vector2D CursorPos);
    static void OnMouseDoubleClick(const EMouseButtons::Type Button);

    static void OnMouseMove();
    static void OnRawMouseMove(const int32 X, const int32 Y);
    static void OnWindowActivationChanged(EWindowActivation ActivationType);
    
    static bool bForceActivateByMouse;
private: 
    static Vector2D MouseDelta;
    
    /** The number of input samples in X since input was was last processed */
    static int32 NumMouseSamplesX;

    /** The number of input samples in Y since input was was last processed */
    static int32 NumMouseSamplesY;

    /** ID of standard device */
    static uint8 StandardDeviceID;
};


};