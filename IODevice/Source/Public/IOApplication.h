/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include <windows.h>
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
    static HWND mainWindow;
    static HINSTANCE dllInstance;

	/**
	 * Core IOToolkit 工具是否已被加载
	 */
	static bool bLoaded;
	/**
	 * dll 构造
	 */
    static int Constructor();
	/**
	 * dll 析构
	 */
	static int Destructor();

	/**
	 * 运行时加载
	 */
	static int DyLoad();
	/**
	 * 运行时卸载
	 */
	static int DyUnload();

    static void RegisterRawInput();
    static bool SuccessResult(int code);

	/**
	 * 程序退出时执行
	 */
    static void PreShutdown();
    static int SetWindowsHook();
    static void UnHookWindow();
    static LRESULT CALLBACK OnMessageProc(int code, WPARAM wParam, LPARAM lParam);
    static LRESULT CALLBACK CallWndRetProc(_In_ int nCode, _In_ WPARAM wParam, _In_ LPARAM lParam);
	static LRESULT CALLBACK CallWndProc(_In_ int nCode, _In_ WPARAM wParam, _In_ LPARAM lParam);
};

};
