#pragma once
#include <iostream>
#ifndef IOCAPI
#define IOCAPI __declspec(dllexport)
#endif // !IOCAPI

typedef void(*InputActionSignature)();
typedef void(*InputActionWithKeySignature)(BSTR);
typedef void(*InputAxisSignature)(float);

extern "C" 
{

	IOCAPI int __stdcall Load();
	IOCAPI int __stdcall UnLoad();
	
	IOCAPI int __stdcall BindKey(BSTR InDeviceName, BSTR InKeyName, int InKeyEvent, InputActionSignature InHandler);
	IOCAPI int __stdcall BindAction(BSTR InDeviceName, BSTR InActionName, int InKeyEvent, InputActionWithKeySignature InHandler);
	IOCAPI int __stdcall BindAxis(BSTR InDeviceName, BSTR InAxisName, InputAxisSignature InHandler);
	
	IOCAPI BYTE __stdcall GetDOSingle(BSTR InDeviceName,BSTR InKeyName);
	IOCAPI int __stdcall GetDOAll(BSTR InDeviceName, BYTE* InStatus);

	IOCAPI int __stdcall SetDOSingle(BSTR InDeviceName, BSTR InKeyName, BYTE InVal);
	IOCAPI int __stdcall SetDOAll(BSTR InDeviceName, BYTE* InStatus);

	/**
	 * utility functions
	 */
	IOCAPI bool __stdcall GetKey(BSTR InDeviceName, BSTR InKey);

	IOCAPI bool __stdcall GetKeyDown(BSTR InDeviceName, BSTR InKey);

	IOCAPI bool __stdcall GetKeyUp(BSTR InDeviceName, BSTR InKey);

	IOCAPI float __stdcall GetAxis(BSTR InDeviceName, BSTR InAxisName);

	IOCAPI float __stdcall GetAxisKey(BSTR InDeviceName, BSTR InKey);

	IOCAPI float __stdcall GetKeyDownDuration(BSTR InDeviceName, BSTR InKey);

	IOCAPI void __stdcall Query();

	IOCAPI void __stdcall ClearAllBindings();
	
}
