/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include "IOPlatform.h"
#if IODEVICE_PLATFORM_WINDOWS
#include <windows.h>
#else
#include <cstdint>
#define CALLBACK
#define WINAPI
#define _In_
using HHOOK = void*;
using HWND = void*;
using HINSTANCE = void*;
using WPARAM = std::uintptr_t;
using LPARAM = std::intptr_t;
using LRESULT = std::intptr_t;
#endif
#include <vector>

namespace IOToolkit
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
	static std::vector<HWND> mainWindows;
    static HINSTANCE dllInstance;

	/**
	 * Core IOToolkit �����Ƿ��ѱ�����
	 */
	static bool bLoaded;
	/**
	 * dll ����
	 */
    static int Constructor();
	/**
	 * dll ����
	 */
	static int Destructor();

	/**
	 * ����ʱ����
	 */
	static int DyLoad();
	/**
	 * ����ʱж��
	 */
	static int DyUnload();

    static void RegisterRawInput();
    static void UnregisterRawInput();
    static bool SuccessResult(int code);

	/**
	 * ͳһ������������Դ
	 */
    static void Cleanup();
    static int SetWindowsHook();
    static void UnHookWindow();
    static LRESULT CALLBACK OnMessageProc(int code, WPARAM wParam, LPARAM lParam);
    static LRESULT CALLBACK CallWndRetProc(_In_ int nCode, _In_ WPARAM wParam, _In_ LPARAM lParam);
	static LRESULT CALLBACK CallWndProc(_In_ int nCode, _In_ WPARAM wParam, _In_ LPARAM lParam);
};

};
