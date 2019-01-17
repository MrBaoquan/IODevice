#pragma once
#include <windows.h>
#include <vector>

namespace DevelopHelper
{

    const int ErrorCode = -1;
    const int SuccessCode = 0;
/**
 * Dll application entry
 */
class IOApplication
{
public:
    static std::vector<HHOOK> hhks;
    static HWND mainWindow;
    static HINSTANCE dllInstance;
    static int Initialize();
    static void InitAfterLoaded();
    static bool SuccessResult(int code);
    static void PreShutdown();
    static int SetWindowsHook();
    static void UnHookWindow();
    static LRESULT CALLBACK OnMessageProc(int code, WPARAM wParam, LPARAM lParam);
    static LRESULT CALLBACK CallWndRetProc(_In_ int nCode, _In_ WPARAM wParam, _In_ LPARAM lParam);
	static LRESULT CALLBACK CallWndProc(_In_ int nCode, _In_ WPARAM wParam, _In_ LPARAM lParam);
};

};
