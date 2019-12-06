/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#include "RawIO/StandardIO.h"
#include <windowsx.h>
#include "IOApplication.h"
#include "InputKeyManager.h"
#include "PlayerInput.h"
#include "IODeviceController.h"
#include "IOStatics.h"

#pragma comment(lib,"Imm32.lib")

LRESULT CALLBACK DevelopHelper::StandardIO::OnMessageProc(int code, WPARAM wParam, LPARAM lParam)
{
    PMSG pMsg = nullptr;
    if (code < 0)
    {
        return CallNextHookEx(IOApplication::hhks[0], code, wParam, lParam);
    }
    pMsg = (PMSG)lParam;
    UINT msg = pMsg->message;

    switch (msg)
    {

    /** Key down */
    case WM_SYSKEYDOWN:
    case WM_KEYDOWN:
        {
            WPARAM Win32Key = pMsg->wParam;
            
            if (Win32Key == VK_PROCESSKEY)
            {
                Win32Key = ImmGetVirtualKey(pMsg->hwnd);
            }

            // LPARAM bit 30 will be ZERO for new presses, or ONE if this is a repeat
            bool bIsRepeat = (pMsg->lParam & 0x40000000) != 0;

            WPARAM ActualKey = Win32Key;
            uint32 charCode = ::MapVirtualKey(static_cast<UINT>(ActualKey), MAPVK_VK_TO_CHAR);
            StandardIO::OnKeyDown(static_cast<uint32>(ActualKey), charCode, bIsRepeat);
        }
        break;

    /** Key up */
    case WM_SYSKEYUP:
    case WM_KEYUP:
        {
            WPARAM Win32Key = pMsg->wParam;
            if (Win32Key == VK_PROCESSKEY)
            {
                Win32Key = ImmGetVirtualKey(pMsg->hwnd);
            }
            WPARAM ActualKey = Win32Key;
            uint32 charCode = ::MapVirtualKey(static_cast<UINT>(ActualKey), MAPVK_VK_TO_CHAR);
            StandardIO::OnKeyUp(static_cast<uint32>(ActualKey), charCode);
        }
        break;

    /** Mouse Button Down  */ 
    case WM_LBUTTONDBLCLK:
    case WM_LBUTTONDOWN:
    case WM_MBUTTONDBLCLK:
    case WM_MBUTTONDOWN:
    case WM_RBUTTONDBLCLK:
    case WM_RBUTTONDOWN:
    case WM_XBUTTONDBLCLK:
    case WM_XBUTTONDOWN:
    case WM_LBUTTONUP:
    case WM_MBUTTONUP:
    case WM_RBUTTONUP:
    case WM_XBUTTONUP:
    {
        POINT CursorPoint;
        CursorPoint.x = GET_X_LPARAM(lParam);
        CursorPoint.y = GET_Y_LPARAM(lParam);

        ClientToScreen(pMsg->hwnd, &CursorPoint);
        const Vector2D cursorPos(static_cast<float>(CursorPoint.x),static_cast<float>( CursorPoint.y));

        EMouseButtons::Type MouseButton = EMouseButtons::Invalid;
        bool bDoubleClick = false;
        bool bMouseUp = false;
        switch (msg)
        {
        case WM_LBUTTONDBLCLK:
            bDoubleClick = true;
            MouseButton = EMouseButtons::Left;
            break;
        case WM_LBUTTONUP:
            bMouseUp = true;
            MouseButton = EMouseButtons::Left;
            break;
        case WM_LBUTTONDOWN:
            MouseButton = EMouseButtons::Left;
            break;
        case WM_MBUTTONDBLCLK:
            bDoubleClick = true;
            MouseButton = EMouseButtons::Middle;
            break;
        case WM_MBUTTONUP:
            bMouseUp = true;
            MouseButton = EMouseButtons::Middle;
            break;
        case WM_MBUTTONDOWN:
            MouseButton = EMouseButtons::Middle;
            break;
        case WM_RBUTTONDBLCLK:
            bDoubleClick = true;
            MouseButton = EMouseButtons::Right;
            break;
        case WM_RBUTTONUP:
            bMouseUp = true;
            MouseButton = EMouseButtons::Right;
            break;
        case WM_RBUTTONDOWN:
            MouseButton = EMouseButtons::Right;
            break;
        case WM_XBUTTONDBLCLK:
            bDoubleClick = true;
            MouseButton = (HIWORD(wParam) & XBUTTON1) ? EMouseButtons::Thumb01 : EMouseButtons::Thumb02;
            break;
        case WM_XBUTTONUP:
            bMouseUp = true;
            MouseButton = (HIWORD(wParam) & XBUTTON1) ? EMouseButtons::Thumb01 : EMouseButtons::Thumb02;
            break;
        case WM_XBUTTONDOWN:
            MouseButton = (HIWORD(wParam) & XBUTTON1) ? EMouseButtons::Thumb01 : EMouseButtons::Thumb02;
            break;
        default:
           break;
        }

        if (bMouseUp)
        {
            StandardIO::OnMouseUp(MouseButton, cursorPos);
        }
        else if (bDoubleClick)
        {
            StandardIO::OnMouseDoubleClick(MouseButton);
        }
        else
        {
            StandardIO::OnMouseDown(MouseButton, cursorPos);
        }
        return CallNextHookEx(IOApplication::hhks[0], code, wParam, lParam);
    }
    break;

    /** Mouse move */
    case WM_INPUT:
        {
            uint32 Size = 0;
            ::GetRawInputData((HRAWINPUT)pMsg->lParam, RID_INPUT, NULL, &Size, sizeof(RAWINPUTHEADER));
            std::unique_ptr<uint8[]> RawData = std::make_unique<uint8[]>(Size);
            if (::GetRawInputData((HRAWINPUT)pMsg->lParam, RID_INPUT, RawData.get(), &Size, sizeof(RAWINPUTHEADER)) == Size) 
            {
                const RAWINPUT* const Raw = (const RAWINPUT* const)RawData.get();
                if (Raw->header.dwType == RIM_TYPEMOUSE)
                {
                    const bool IsAbsoluteInput = (Raw->data.mouse.usFlags&MOUSE_MOVE_ABSOLUTE) == MOUSE_MOVE_ABSOLUTE;
                    if (IsAbsoluteInput)
                    {
                        return CallNextHookEx(IOApplication::hhks[0], code, wParam, lParam);
                    }
                    const int xPosRelative = Raw->data.mouse.lLastX;
                    const int yPosRelative = Raw->data.mouse.lLastY;
                    StandardIO::OnRawMouseMove(xPosRelative, yPosRelative);
                }
                else if (Raw->header.dwType == RIM_TYPEKEYBOARD)
                {
                    OutputDebugStringA("TEST");
                }
            }
        }
        break;
    case WM_NCMOUSEMOVE:
    case WM_MOUSEMOVE:
        StandardIO::OnMouseMove();
        break;
    default:
        break;
    }
    return CallNextHookEx(IOApplication::hhks[0], code, wParam, lParam);
}

#include <bitset>
LRESULT CALLBACK DevelopHelper::StandardIO::OnKeyboardProc(int code, WPARAM wParam, LPARAM lParam)
{
    if (code < 0) { return CallNextHookEx(IOApplication::hhks[0], code, wParam, lParam); }
    std::bitset<32> rawBits(lParam);
    std::bitset<16> repeatCount(rawBits.to_string(),16);
    long val = repeatCount.to_ulong();
    bool bPressed = !rawBits[31];
    OutputDebugStringA(std::to_string(val).data());
    OutputDebugStringA("\n");
    /*if (!bPressed)
    {
        OutputDebugStringA("Released");
    }*/
    uint32 charCode = ::MapVirtualKey(static_cast<UINT>(wParam), MAPVK_VK_TO_CHAR);

   // StandardIO::OnKeyDown(static_cast<uint32>(wParam), charCode, bIsRepeat);
    return CallNextHookEx(IOApplication::hhks[0], code, wParam, lParam);
}

LRESULT CALLBACK DevelopHelper::StandardIO::CallWndRetProc(_In_ int nCode, _In_ WPARAM wParam, _In_ LPARAM lParam)
{
    PCWPRETSTRUCT pMsg = nullptr;
    if (nCode != HC_ACTION)
    {
        return CallNextHookEx(IOApplication::hhks[1], nCode, wParam, lParam);
    }
    pMsg = (PCWPRETSTRUCT)lParam;
    UINT msg = pMsg->message;
    switch (msg)
    {
        // Window focus and activation
    case WM_MOUSEACTIVATE:
    {
        // If the mouse activate isn't in the client area we'll force the WM_ACTIVATE to be EWindowActivation::ActivateByMouse
        // This ensures that clicking menu buttons on the header doesn't generate a WM_ACTIVATE with EWindowActivation::Activate
        // which may cause mouse capture to be taken because is not differentiable from Alt-Tabbing back to the application.
        StandardIO::bForceActivateByMouse = !(LOWORD(pMsg->lParam) & HTCLIENT);
        return CallNextHookEx(IOApplication::hhks[1], nCode, wParam, lParam);;
    }
    break;
    case WM_ACTIVATE:
    {
        EWindowActivation ActivationType;

        if (LOWORD(pMsg->wParam) & WA_ACTIVE)
        {
            ActivationType = StandardIO::bForceActivateByMouse ? EWindowActivation::ActivateByMouse : EWindowActivation::Activate;
        }
        else if (LOWORD(pMsg->wParam) & WA_CLICKACTIVE)
        {
            ActivationType = EWindowActivation::ActivateByMouse;
        }
        else
        {
            ActivationType = EWindowActivation::Deactivate;
        }
        StandardIO::bForceActivateByMouse = false;
        StandardIO::OnWindowActivationChanged(ActivationType);
    }
    default:
        break;
    }
    return CallNextHookEx(IOApplication::hhks[1], nCode, wParam, lParam);
}


void DevelopHelper::StandardIO::Initialize()
{

}

void DevelopHelper::StandardIO::Tick(float DeltaSeconds)
{
    RawIO::Tick(DeltaSeconds);
    PlayerInput::Instance().InputAxis(EKeys::MouseX, MouseDelta.X, IODeviceController::Instance().GetDeltaSeconds(), StandardDeviceID, NumMouseSamplesX);
    PlayerInput::Instance().InputAxis(EKeys::MouseY, MouseDelta.Y, IODeviceController::Instance().GetDeltaSeconds(), StandardDeviceID, NumMouseSamplesY);
    MouseDelta = Vector2D(0, 0);
    NumMouseSamplesX = 0;
    NumMouseSamplesY = 0;
}

void DevelopHelper::StandardIO::OnFrameEnd()
{

}


DevelopHelper::FKey DevelopHelper::StandardIO::TranslateMouseButtonToKey(const EMouseButtons::Type Button)
{
    FKey Key = EKeys::Invalid;

    switch (Button)
    {
    case EMouseButtons::Left:
        Key = EKeys::LeftMouseButton;
        break;
    case EMouseButtons::Middle:
        Key = EKeys::MiddleMouseButton;
        break;
    case EMouseButtons::Right:
        Key = EKeys::RightMouseButton;
        break;
    case EMouseButtons::Thumb01:
        Key = EKeys::ThumbMouseButton;
        break;
    case EMouseButtons::Thumb02:
        Key = EKeys::ThumbMouseButton2;
        break;
    }

    return Key;
}

void DevelopHelper::StandardIO::Build()
{
    ImmDisableIME(0);
}

void DevelopHelper::StandardIO::OnKeyDown(uint32 keyCode, uint32 charCode, bool isRepeat/*=false*/)
{
    FKey& key = InputKeyManager::Instance().GetKeyFromCodes(keyCode, charCode);
    //if (!isRepeat)
    //{
    //    OutputDebugStringA(std::string(key.GetName()).append("keydown \n").data());
    //}
    
    if (key == EKeys::Invalid)
    {
        return;
    }
    PlayerInput::Instance().InputKey(key, isRepeat ? IE_Repeat : IE_Pressed, StandardDeviceID);

}

void DevelopHelper::StandardIO::OnKeyUp(uint32 keyCode, uint32 charCode, bool isRepeat /*= false*/)
{
    FKey& key = InputKeyManager::Instance().GetKeyFromCodes(keyCode, charCode);
   // OutputDebugStringA(std::string(key.GetName()).append("keyup \n").data());
    if (key == EKeys::Invalid)
    {
        return;
    }
    PlayerInput::Instance().InputKey(key, IE_Released, StandardDeviceID);
}

void DevelopHelper::StandardIO::OnMouseUp(const EMouseButtons::Type Button, const Vector2D CursorPos)
{
    FKey key = TranslateMouseButtonToKey(Button);
    PlayerInput::Instance().InputKey(key, IE_Released, StandardDeviceID);
}

void DevelopHelper::StandardIO::OnMouseDown(const EMouseButtons::Type Button, const Vector2D CursorPos)
{
    FKey key = TranslateMouseButtonToKey(Button);
    PlayerInput::Instance().InputKey(key, IE_Pressed, StandardDeviceID);
}

void DevelopHelper::StandardIO::OnMouseDoubleClick(const EMouseButtons::Type Button)
{
    FKey key = TranslateMouseButtonToKey(Button);
    PlayerInput::Instance().InputKey(key, IE_DoubleClick, StandardDeviceID);
}

void DevelopHelper::StandardIO::OnRawMouseMove(const int32 X, const int32 Y)
{

    const Vector2D CursorDelta(static_cast<float>(X), static_cast<float>(Y));
    MouseDelta.X += CursorDelta.X;
    ++NumMouseSamplesX;

    MouseDelta.Y -= CursorDelta.Y;
    ++NumMouseSamplesY;
}


void DevelopHelper::StandardIO::OnWindowActivationChanged(EWindowActivation ActivationType)
{
    if (ActivationType==EWindowActivation::Deactivate)
    {
        PlayerInput::Instance().FlushPressedKeys();
    }
}

bool DevelopHelper::StandardIO::bForceActivateByMouse;

DevelopHelper::Vector2D DevelopHelper::StandardIO::MouseDelta;

DevelopHelper::int32 DevelopHelper::StandardIO::NumMouseSamplesX;

DevelopHelper::int32 DevelopHelper::StandardIO::NumMouseSamplesY;

DevelopHelper::uint8 DevelopHelper::StandardIO::StandardDeviceID = InvalidDeviceID;

void DevelopHelper::StandardIO::OnMouseMove()
{

    // PlayerInput::Instance().InputAxis(Keys::MouseX, MouseX, IODeviceController::Instance().GetDeltaSeconds(), 0, false);
}