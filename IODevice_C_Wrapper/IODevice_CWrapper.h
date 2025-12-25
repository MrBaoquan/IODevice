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
	IOCAPI int __stdcall Unload();

	IOCAPI int __stdcall SetIOConfigPath(BSTR InFilePath);
	IOCAPI int __stdcall SetIOLogDir(BSTR InLogDir);

	IOCAPI BSTR __stdcall DeviceIOType(BSTR InDeviceName);
	IOCAPI BSTR __stdcall DeviceDllName(BSTR InDeviceName);
	IOCAPI int __stdcall DeviceIndex(BSTR InDeviceName);
	IOCAPI bool __stdcall IsValid(BSTR InDeviceName);
	
	IOCAPI int __stdcall BindKeyWithKey(BSTR InDeviceName, BSTR InKeyName, int InKeyEvent, InputActionWithKeySignature InHandler);
	IOCAPI int __stdcall BindKey(BSTR InDeviceName, BSTR InKeyName, int InKeyEvent, InputActionSignature InHandler);
	IOCAPI int __stdcall BindAxisKey(BSTR InDeviceName, BSTR InAxisName, InputAxisSignature InHandler);
	IOCAPI int __stdcall BindAction(BSTR InDeviceName, BSTR InActionName, int InKeyEvent, InputActionWithKeySignature InHandler);
	IOCAPI int __stdcall BindAxis(BSTR InDeviceName, BSTR InAxisName, InputAxisSignature InHandler);
	
	IOCAPI float __stdcall GetDOSingle(BSTR InDeviceName,BSTR InKeyName);
	IOCAPI float __stdcall GetDOAction(BSTR InDeviceName, BSTR InOAction);
	IOCAPI int __stdcall GetDOAll(BSTR InDeviceName, float* InStatus);
	IOCAPI int RefreshStreamingData(BSTR InDeviceName, BYTE* StreamingData, unsigned int DataSize);

	IOCAPI int __stdcall SetDOSingle(BSTR InDeviceName, BSTR InKeyName, float InVal);
	IOCAPI int __stdcall SetDOAll(BSTR InDeviceName, float* InStatus);
	
	IOCAPI int __stdcall SetDOAction(BSTR InDeviceName, BSTR InOAction, float InVal, bool bIngoreMassage=false);
	IOCAPI int __stdcall SetDOOn(BSTR InDeviceName, BSTR InOAction);
	IOCAPI int __stdcall SetDOOff(BSTR InDeviceName, BSTR InOAction);
	IOCAPI int __stdcall DOImmediate(BSTR InDeviceName);

	/**
	 * utility functions
	 */
	IOCAPI bool __stdcall GetKey(BSTR InDeviceName, BSTR InKey);

	IOCAPI bool __stdcall GetKeyDown(BSTR InDeviceName, BSTR InKey);

	IOCAPI bool __stdcall GetKeyUp(BSTR InDeviceName, BSTR InKey);

	IOCAPI float __stdcall GetAxis(BSTR InDeviceName, BSTR InAxisName);

	IOCAPI float __stdcall GetAxisKey(BSTR InDeviceName, BSTR InKey);

	IOCAPI float __stdcall GetRawKeyValue(BSTR InDeviceName, BSTR InKey);

	IOCAPI float __stdcall GetKeyDownDuration(BSTR InDeviceName, BSTR InKey);

	IOCAPI void __stdcall Query();

	IOCAPI void __stdcall ClearBindings(BSTR InDeviceName);
	IOCAPI void __stdcall ClearAllBindings();

	/**
	 * 设置 Axis Key 的属性 (Scale)
	 * @param InDeviceName: 设备名称
	 * @param InAxisName: Axis 名称
	 * @param InKeyName: Key 名称
	 * @param InScale: 缩放系数
	 * @return: 成功返回1 失败返回0
	 */
	IOCAPI int __stdcall SetAKProps(BSTR InDeviceName, BSTR InAxisName, BSTR InKeyName, float InScale);

	/**
	 * 设置 OAction Key 的属性 (Scale, InvertEvent)
	 * @param InDeviceName: 设备名称
	 * @param InOActionName: OAction 名称
	 * @param InKeyName: Key 名称
	 * @param InScale: 缩放系数
	 * @param InInvertEvent: 是否反转事件
	 * @return: 成功返回1 失败返回0
	 */
	IOCAPI int __stdcall SetOKProps(BSTR InDeviceName, BSTR InOActionName, BSTR InKeyName, float InScale, bool InInvertEvent);

	/**
	 * 设置 Property Key 的属性
	 * @param InDeviceName: 设备名称
	 * @param InKeyName: Key 名称
	 * @param InOffset: 偏移值（校准零点）
	 * @param InScale: 缩放系数（映射输入范围）
	 * @param InMinValue: 最小值
	 * @param InMaxValue: 最大值
	 * @param InDeadZone: 死区
	 * @param InSensitivity: 灵敏度
	 * @param InExponent: 指数曲线
	 * @param InInvert: 是否反转数值
	 * @param InInvertEvent: 是否反转事件
	 * @return: 成功返回1 失败返回0
	 */
	IOCAPI int __stdcall SetPKProps(BSTR InDeviceName, BSTR InKeyName, float InOffset, float InScale, float InMinValue, float InMaxValue, float InDeadZone, float InSensitivity, float InExponent, bool InInvert, bool InInvertEvent);
	
}
