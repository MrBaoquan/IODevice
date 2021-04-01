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
        IOToolkit::IOLog::Instance().Log(std::string("Succeed in finding process id of main window, and the title of main window is ") + HWNDToString((HWND)(__int64)(dwCurrentProcessId)));
        return (HWND)(__int64)(dwCurrentProcessId);
#else
        IOToolkit::IOLog::Instance().Log(std::string("Succeed in finding process id of main window, and the title of main window is ") + HWNDToString((HWND)(dwCurrentProcessId)));
        return (HWND)(dwCurrentProcessId);
#endif // WIN_64
    }
    IOToolkit::IOLog::Instance().Warning("Could not find process id of main window, make sure your program have a window. ");
    return NULL;
}

BOOL WINAPI DllMain(
    _In_ HINSTANCE hinstDLL,
    _In_ DWORD     fdwReason,
    _In_ LPVOID    lpvReserved
)
{
    using namespace IOToolkit;
    switch (fdwReason)
    {
    case DLL_PROCESS_ATTACH:
        IOApplication::dllInstance = hinstDLL;
		{
			/** Initialize Paths before initialize IOLog, because IOLog need a correct log path. */
			Paths::Instance().SetModule(IOApplication::dllInstance);
			std::string dllPath = Paths::Instance().GetModuleDir() + "ExternalLibraries\\Core\\";
			std::wstring _wdllPath(dllPath.begin(), dllPath.end());
			AddDllDirectory(_wdllPath.data());
		}
        break;
    case DLL_PROCESS_DETACH:
        IOApplication::UnHookWindow();
		IOLog::Instance().Log(std::string("------------------------------  IOToolkit has been detached, Bye!  ------------------------------"));
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

std::vector<HHOOK> IOToolkit::IOApplication::hhks;

HWND IOToolkit::IOApplication::mainWindow;

HINSTANCE IOToolkit::IOApplication::dllInstance;


bool IOToolkit::IOApplication::bLoaded = false;

int IOToolkit::IOApplication::Constructor()
{
	IOLog::Instance().Log(std::string("\n\n\n------------------------------  (*^_^*) Welcome to use IOToolkit (*^_^*) ------------------------------\n\
		\n\
		Copyright(c) 	mrma617@gmail.com\n\
		Author :		MrBaoquan\n\
		\n\
------------------------------------------------------------------------------------------------------------\
"));
    /** Key mapping */
    StaticKeys::Initialize();
	IOApplication::RegisterRawInput();
    return SuccessCode;
}


int IOToolkit::IOApplication::Destructor()
{
	return 0;
}


int IOToolkit::IOApplication::DyLoad()
{
	if (IOApplication::bLoaded) {
		IOLog::Instance().Warning(std::string("------------------------------  IOToolkit is alreay loaded  ------------------------------\n"));
		return -1;
	}

	IOLog::Instance().Log(std::string("------------------------------  IOToolkit Loading...  ------------------------------"));
	IOApplication::SetWindowsHook();
	/** Device initializtion */
	IODevices::Initialize();
	PlayerInput::Instance().Initialize();
	IOApplication::bLoaded = true;
	IOLog::Instance().Log(std::string("------------------------------  IOToolkit Loaded  ------------------------------"));
	return 0;
}


int IOToolkit::IOApplication::DyUnload()
{
	if (!IOApplication::bLoaded) {
		IOLog::Instance().Warning(std::string("------------------------------  IOToolkit is alreay unloaded  ------------------------------\n"));
		return -1;
	}
	IOApplication::UnHookWindow();
	IODevices::UnInitialize();
	PlayerInput::Instance().UnInitialize();
	IOApplication::bLoaded = false;
	IOLog::Instance().Log(std::string("------------------------------  IOToolkit has been unloaded  ------------------------------\n"));
	IOLog::Instance().ReleaseLogger();
	return 0;
}

void IOToolkit::IOApplication::RegisterRawInput()
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

bool IOToolkit::IOApplication::SuccessResult(int code)
{
    return code == SuccessCode;
}

void IOToolkit::IOApplication::PreShutdown()
{
	IOApplication::DyUnload();
}

int IOToolkit::IOApplication::SetWindowsHook()
{
	DWORD dwProcessId = 0;
	DWORD threadID = GetWindowThreadProcessId(IOApplication::mainWindow, &dwProcessId);
    HHOOK hhk1 = SetWindowsHookEx(WH_GETMESSAGE, IOApplication::OnMessageProc, NULL, threadID);
    if (hhk1)
    {
        IOApplication::hhks.push_back(hhk1);
    }else
    {
        IOLog::Instance().Warning("Hook WM_GETMESSAGE Failed ...");
        return ErrorCode;
    }

    HHOOK hhk2 = SetWindowsHookEx(WH_CALLWNDPROCRET, IOApplication::CallWndRetProc, NULL, threadID);
    if (hhk2)
    {
        IOApplication::hhks.push_back(hhk2);
    }else
    {
        IOLog::Instance().Warning("Hook WH_CALLWNDPROCRET Failed ...");
        return ErrorCode;
    }

	HHOOK hhk3 = SetWindowsHookEx(WH_CALLWNDPROC, IOApplication::CallWndProc, NULL, threadID);
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

void IOToolkit::IOApplication::UnHookWindow()
{
    for (auto& hhk:IOApplication::hhks)
    {
        UnhookWindowsHookEx(hhk);
    }
}

LRESULT CALLBACK IOToolkit::IOApplication::OnMessageProc(int code, WPARAM wParam, LPARAM lParam)
{
    return StandardIO::OnMessageProc(code, wParam, lParam);
}

LRESULT CALLBACK IOToolkit::IOApplication::CallWndRetProc(_In_ int nCode, _In_ WPARAM wParam, _In_ LPARAM lParam)
{
    return StandardIO::CallWndRetProc(nCode, wParam, lParam);
}


LRESULT CALLBACK IOToolkit::IOApplication::CallWndProc(_In_ int nCode, _In_ WPARAM wParam, _In_ LPARAM lParam)
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
		IOLog::Instance().Log("Detected window close event.");
		IOApplication::PreShutdown();
		break;
	case WM_QUERYENDSESSION:
		IOLog::Instance().Log("Detected system shutdown event.");
		IOApplication::PreShutdown();
		break;
	default:
		break;
	}
	return CallNextHookEx(IOApplication::hhks[2], nCode, wParam, lParam);
}