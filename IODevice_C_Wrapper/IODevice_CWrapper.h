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

	/**
	 * 通用插件通道 (Socket.IO 化改造)
	 * 上层（托管语言）通过此接口和插件（NETIO 等）交换字节流，不关心底层协议。
	 * channelName 采用 ASCII (const char*) 而非 BSTR，降低互操作开销。
	 */
	typedef void(__stdcall *PluginChannelCallbackManaged)(const char* channelName, const BYTE* data, unsigned int size);

	/**
	 * 对端请求回调 (插件 → 宿主 RPC). requestId/payload/metadata 均为 UTF-8 字节串.
	 * 业务侧需保存 requestId 并调用 RespondChannelRequest 完成响应.
	 */
	typedef void(__stdcall *ChannelRequestCallbackManaged)(
		const char* name,
		const char* requestId,
		const BYTE* payloadJson, unsigned int payloadSize,
		const BYTE* metadataJson, unsigned int metadataSize);

	IOCAPI int  __stdcall WritePluginChannel(BSTR InDeviceName, const char* channelName, const BYTE* data, unsigned int size);
	IOCAPI int  __stdcall BindPluginChannel(BSTR InDeviceName, const char* channelName, PluginChannelCallbackManaged InCallback);
	IOCAPI int  __stdcall UnbindPluginChannel(BSTR InDeviceName, const char* channelName, int handlerId);
	IOCAPI int  __stdcall QueryPluginCapabilities(BSTR InDeviceName, BYTE* outJson, unsigned int capacity, unsigned int timeoutMs);
	IOCAPI int  __stdcall SendPluginRequest(BSTR InDeviceName, const char* topic, const BYTE* requestJson, unsigned int requestSize, BYTE* responseJson, unsigned int capacity, unsigned int timeoutMs);
	IOCAPI int  __stdcall BindPluginEvent(BSTR InDeviceName, const char* eventName, PluginChannelCallbackManaged InCallback);
	IOCAPI int  __stdcall UnbindPluginEvent(BSTR InDeviceName, int handlerId);
	/**
	 * 标准 Channel 请求入口. metadataJson 为透传 JSON 字符串 (如需要路由可传 {"target":"sid:xxx"}, 可为 nullptr/0).
	 * 若 waitResponse=0, 则单向投递不等响应 (responseJson 不会被填充, 返回 1=投递成功 / 0=失败).
	 */
	IOCAPI int  __stdcall RequestChannel(BSTR InDeviceName, const char* name, const BYTE* requestJson, unsigned int requestSize, int waitResponse, const BYTE* metadataJson, unsigned int metadataSize, BYTE* responseJson, unsigned int capacity, unsigned int timeoutMs);
	IOCAPI int  __stdcall SubscribeChannel(BSTR InDeviceName, const char* name, PluginChannelCallbackManaged InCallback);
	IOCAPI int  __stdcall UnsubscribeChannel(BSTR InDeviceName, int handlerId);
	/**
	 * 订阅插件转发的对端请求 (plugin → host RPC).
	 * 插件获取 NetFrame.req 后包装为 _rpc.req envelope, IODevice 按 topic 分派。
	 */
	IOCAPI int  __stdcall SubscribeChannelRequest(BSTR InDeviceName, const char* name, ChannelRequestCallbackManaged InCallback);
	IOCAPI int  __stdcall UnsubscribeChannelRequest(BSTR InDeviceName, int handlerId);
	/**
	 * 响应上一步 SubscribeChannelRequest handler 收到的请求. requestId 必须与 ctx.RequestId 一致.
	 */
	IOCAPI int  __stdcall RespondChannelRequest(BSTR InDeviceName, const char* requestId, const BYTE* payloadJson, unsigned int payloadSize, int ok, const char* errorMessage);
	IOCAPI int  __stdcall QueryChannelCapabilities(BSTR InDeviceName, BYTE* outJson, unsigned int capacity, unsigned int timeoutMs);

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

	// ── MotionPlayer (多 Slot 动作文件播放) ─────────────

	IOCAPI int   __stdcall MotionLoadSlot(BSTR InSlotId, BSTR InFilePath, int InPriority, int InMixPolicy);
	IOCAPI void  __stdcall MotionUnloadSlot(BSTR InSlotId);
	IOCAPI void  __stdcall MotionUnloadAll();
	IOCAPI int   __stdcall MotionPlaySlot(BSTR InSlotId);
	IOCAPI int   __stdcall MotionPlaySlotFrom(BSTR InSlotId, float InTimeMs);
	IOCAPI int   __stdcall MotionPauseSlot(BSTR InSlotId);
	IOCAPI int   __stdcall MotionResumeSlot(BSTR InSlotId);
	IOCAPI int   __stdcall MotionStopSlot(BSTR InSlotId);
	IOCAPI int   __stdcall MotionSeekSlot(BSTR InSlotId, float InTimeMs);
	IOCAPI void  __stdcall MotionSetSlotSpeed(BSTR InSlotId, float InSpeed);
	IOCAPI void  __stdcall MotionSetSlotLoop(BSTR InSlotId, bool InLoop);
	IOCAPI void  __stdcall MotionSetSlotClockMode(BSTR InSlotId, int InMode);
	IOCAPI void  __stdcall MotionSetSlotExternalTime(BSTR InSlotId, float InTimeMs);
	IOCAPI int   __stdcall MotionGetSlotState(BSTR InSlotId);
	IOCAPI float __stdcall MotionGetSlotCurrentTime(BSTR InSlotId);
	IOCAPI float __stdcall MotionGetSlotDuration(BSTR InSlotId);
	IOCAPI int   __stdcall MotionGetSlotCount();
	IOCAPI void  __stdcall MotionPlayAll();
	IOCAPI void  __stdcall MotionPauseAll();
	IOCAPI void  __stdcall MotionStopAll();

	// ── Phase 3 新增 API ────────────────────────────────
	IOCAPI int   __stdcall MotionLoadSlotFromJson(BSTR InSlotId, BSTR InJsonContent, int InPriority, int InMixPolicy);
	IOCAPI int   __stdcall MotionEvaluateSlotAt(BSTR InSlotId, float InTimeMs, float* OutValues, int InMaxChannels);
	IOCAPI void  __stdcall MotionSetSafetyConfig(float InMaxRatePerSecond);
}

// typedef outside extern "C" for C++ usage
typedef void (__stdcall *MotionEventCallbackManaged)(BSTR InSlotId, int InEventType);
typedef void (__stdcall *MotionEventDataCallbackManaged)(BSTR InSlotId, int InEventType, BSTR InEventData);

extern "C" {
	IOCAPI void  __stdcall MotionSetEventCallback(MotionEventCallbackManaged InCallback);
	IOCAPI void  __stdcall MotionSetEventDataCallback(MotionEventDataCallbackManaged InCallback);
}
