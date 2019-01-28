/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#include "IOApplication.h"
#include <string>
#include "IOStatics.h"
#include "PlayerInput.h"
#include "RawIO/StandardIO.h"
#include "RawIOFactory.h"
#include "Paths.hpp"
#include "IOLog.h"

#pragma comment(lib,"Winmm.lib")

BOOL CALLBACK EnumWindowsProc(HWND hwnd, LPARAM lParam)
{
    DWORD dwCurProcessId = *((DWORD*)lParam);
    DWORD dwProcessId = 0;
    
    GetWindowThreadProcessId(hwnd, &dwProcessId);
    
    if (dwProcessId == dwCurProcessId && GetParent(hwnd) == NULL)
    {
        *((HWND *)lParam) = hwnd;
        return FALSE;
    }
    return TRUE;
}

std::string HWNDToString(HWND input)
{
    std::string output = "";
    size_t sizeTBuffer = static_cast<size_t>(GetWindowTextLength(input) + 1);

    if (sizeTBuffer > 0)
    {
        output.resize(sizeTBuffer);
        sizeTBuffer = static_cast<size_t>(GetWindowTextA(input, &output[0], static_cast<int>(sizeTBuffer)));
        output.resize(sizeTBuffer);
    }

    return output;
}

HWND GetMainWindow()
{
    static DWORD dwCurrentProcessId = GetCurrentProcessId();
    if (!EnumWindows(EnumWindowsProc, (LPARAM)&dwCurrentProcessId))
    {
        
#ifdef WIN_64
        DevelopHelper::IOLog::Instance().Log(std::string("Succeed in finding process id of main window, and the title of main window is ") + HWNDToString((HWND)(__int64)(dwCurrentProcessId)));
        return (HWND)(__int64)(dwCurrentProcessId);
#else
        DevelopHelper::IOLog::Instance().Log(std::string("Succeed in finding process id of main window, and the title of main window is ") + HWNDToString((HWND)(dwCurrentProcessId)));
        return (HWND)(dwCurrentProcessId);
#endif // WIN_64
    }
    DevelopHelper::IOLog::Instance().Warning("Could not find process id of main window, make sure your program have a window. ");
    return NULL;
}

BOOL WINAPI DllMain(
    _In_ HINSTANCE hinstDLL,
    _In_ DWORD     fdwReason,
    _In_ LPVOID    lpvReserved
)
{
    using namespace DevelopHelper;
    switch (fdwReason)
    {
    case DLL_PROCESS_ATTACH:
        IOApplication::dllInstance = hinstDLL;
        
        /** Initialize Paths before initialize IOLog, because IOLog need a correct log path. */
        Paths::Instance().SetModule(IOApplication::dllInstance);

        break;
    case DLL_PROCESS_DETACH:
        IOApplication::UnHookWindow();
        IOLog::Instance().Log("Dll has been detached. ");
        break;
    case DLL_THREAD_ATTACH:
        break;
    case DLL_THREAD_DETACH:
        break;
    default:
        break;
    }
    return TRUE;
}

std::vector<HHOOK> DevelopHelper::IOApplication::hhks;

HWND DevelopHelper::IOApplication::mainWindow;

HINSTANCE DevelopHelper::IOApplication::dllInstance;

int DevelopHelper::IOApplication::Initialize()
{
    int rtCode = IOApplication::SetWindowsHook();
    if (!SuccessResult(rtCode))
    {
        return ErrorCode;
    }

    /** Key mapping */
    StaticKeys::Initialize();
    
    /** Device initializtion */
    rtCode = IODevices::Initialize();
    if (!SuccessResult(rtCode))
    {
        return ErrorCode;
    }
    
    PlayerInput::Instance().Initialize();
    return SuccessCode;
}

void DevelopHelper::IOApplication::InitAfterLoaded()
{
    HWND hwnd = GetMainWindow();
    IOApplication::mainWindow = hwnd;

#ifndef HID_USAGE_PAGE_GENERIC
#define HID_USAGE_PAGE_GENERIC         ((USHORT) 0x01)
#endif
#ifndef HID_USAGE_GENERIC_MOUSE
#define HID_USAGE_GENERIC_MOUSE        ((USHORT) 0x02)
#endif

    RAWINPUTDEVICE Rid[1];
    Rid[0].usUsagePage = HID_USAGE_PAGE_GENERIC;
    Rid[0].usUsage = HID_USAGE_GENERIC_MOUSE;
    Rid[0].dwFlags = RIDEV_INPUTSINK;
    Rid[0].hwndTarget = IOApplication::mainWindow;

    // Joystick for rawinput .
    //Rid[1].usUsagePage = 0x01;
    //Rid[1].usUsage = 0x04;
    //Rid[1].dwFlags = 0;                 // adds joystick
    //Rid[1].hwndTarget = IOApplication::mainWindow;

    if (RegisterRawInputDevices(Rid, 1, sizeof(Rid[0])) == FALSE)
    {
        IOLog::Instance().Warning("Register raw input devcies failed. ");
    }
}

bool DevelopHelper::IOApplication::SuccessResult(int code)
{
    return code == SuccessCode;
}

void DevelopHelper::IOApplication::PreShutdown()
{
	for (auto& device : IODevices::GetDevcies())
	{
		device.second.Destroy();
	}
}

int DevelopHelper::IOApplication::SetWindowsHook()
{
    DWORD threadID = GetCurrentThreadId();
    HHOOK hhk1 = SetWindowsHookEx(WH_GETMESSAGE, IOApplication::OnMessageProc,IOApplication::dllInstance, threadID);
    if (hhk1)
    {
        IOApplication::hhks.push_back(hhk1);
    }else
    {
        IOLog::Instance().Warning("Hook WM_GETMESSAGE Failed ...");
        return ErrorCode;
    }

    HHOOK hhk2 = SetWindowsHookEx(WH_CALLWNDPROCRET, IOApplication::CallWndRetProc, IOApplication::dllInstance, threadID);
    if (hhk2)
    {
        IOApplication::hhks.push_back(hhk2);
    }else
    {
        IOLog::Instance().Warning("Hook WH_CALLWNDPROCRET Failed ...");
        return ErrorCode;
    }

	HHOOK hhk3 = SetWindowsHookEx(WH_CALLWNDPROC, IOApplication::CallWndProc, IOApplication::dllInstance, threadID);
	if (hhk3)
	{
		IOApplication::hhks.push_back(hhk3);
	}
	else
	{
		IOLog::Instance().Warning("Hook WH_CALLWNDPROC Failed ...");
		return ErrorCode;
	}

    return SuccessCode;
}

void DevelopHelper::IOApplication::UnHookWindow()
{
    for (auto& hhk:IOApplication::hhks)
    {
        UnhookWindowsHookEx(hhk);
    }
}

LRESULT CALLBACK DevelopHelper::IOApplication::OnMessageProc(int code, WPARAM wParam, LPARAM lParam)
{
    return StandardIO::OnMessageProc(code, wParam, lParam);
}

LRESULT CALLBACK DevelopHelper::IOApplication::CallWndRetProc(_In_ int nCode, _In_ WPARAM wParam, _In_ LPARAM lParam)
{
    return StandardIO::CallWndRetProc(nCode, wParam, lParam);
}


LRESULT CALLBACK DevelopHelper::IOApplication::CallWndProc(_In_ int nCode, _In_ WPARAM wParam, _In_ LPARAM lParam)
{
	if (nCode != HC_ACTION)
	{
		return CallNextHookEx(IOApplication::hhks[2], nCode, wParam, lParam);
	}
	PCWPSTRUCT data = (PCWPSTRUCT)lParam;
	UINT msg = data->message;
	switch (msg)
	{
	case WM_CLOSE:
		IOApplication::PreShutdown();
		break;
	default:
		break;
	}
	return CallNextHookEx(IOApplication::hhks[2], nCode, wParam, lParam);
}