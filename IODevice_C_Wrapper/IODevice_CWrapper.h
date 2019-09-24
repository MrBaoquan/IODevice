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
	IOCAPI int __stdcall BindKey(BSTR InDeviceName, BSTR InKeyName, int InKeyEvent, InputActionSignature InHandler);
	IOCAPI int __stdcall BindAction(BSTR InDeviceName, BSTR InActionName, int InKeyEvent, InputActionWithKeySignature InHandler);
	IOCAPI int __stdcall BindAxis(BSTR InDeviceName, BSTR InAxisName, InputAxisSignature InHandler);
	IOCAPI void __stdcall Query();
	


	IOCAPI void __stdcall ClearAllBindings();
	
}
