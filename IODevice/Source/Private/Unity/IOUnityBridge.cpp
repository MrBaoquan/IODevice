#if defined(__ANDROID__)

#include <cstdlib>
#include <cstring>
#include <string>

#include "IODeviceController.h"
#include "IODevice.h"
#include "IOSettings.h"
#include "MotionPlayer.h"

namespace dh = IOToolkit;

#define IODEVICE_UNITY_API extern "C" __attribute__((visibility("default")))

using Byte = unsigned char;
using InputActionSignature = void (*)();
using InputActionWithKeySignature = void (*)(const char*);
using InputAxisSignature = void (*)(float);
using PluginChannelCallbackManaged = void (*)(const char* channelName, const Byte* data, unsigned int size);
using ChannelRequestCallbackManaged = void (*)(const char* name, const char* requestId, const Byte* payloadJson, unsigned int payloadSize, const Byte* metadataJson, unsigned int metadataSize);
using MotionEventCallbackManaged = void (*)(const char* slotId, int eventType);
using MotionEventDataCallbackManaged = void (*)(const char* slotId, int eventType, const char* eventData);

static const char* SafeString(const char* value)
{
    return value ? value : "";
}

static char* DuplicateString(const std::string& value)
{
    char* result = static_cast<char*>(std::malloc(value.size() + 1));
    if (!result) return nullptr;
    std::memcpy(result, value.c_str(), value.size() + 1);
    return result;
}

static dh::IODevice& GetIODevice(const char* deviceName)
{
    return dh::IODeviceController::Instance().GetIODevice(SafeString(deviceName));
}

static int CopyBytesToBuffer(const std::string& payload, Byte* outData, unsigned int capacity)
{
    if (!outData || capacity == 0) return static_cast<int>(payload.size());
    unsigned int copySize = static_cast<unsigned int>(payload.size());
    if (copySize > capacity) copySize = capacity;
    if (copySize > 0) std::memcpy(outData, payload.data(), copySize);
    return static_cast<int>(payload.size());
}

IODEVICE_UNITY_API int Load()
{
    return dh::IODeviceController::Instance().Load();
}

IODEVICE_UNITY_API int Unload()
{
    return dh::IODeviceController::Instance().Unload();
}

IODEVICE_UNITY_API int EnterSafeState()
{
    return dh::IODeviceController::Instance().EnterSafeState();
}

IODEVICE_UNITY_API int SetIOConfigPath(const char* filePath)
{
    return dh::IOSettings::Instance().SetIOConfigPath(SafeString(filePath));
}

IODEVICE_UNITY_API int SetIORuntimeRoot(const char* runtimeRoot)
{
    return dh::IOSettings::Instance().SetIORuntimeRoot(SafeString(runtimeRoot));
}

IODEVICE_UNITY_API int SetIOLogDir(const char* logDir)
{
    return dh::IOSettings::Instance().SetIOLogDir(SafeString(logDir));
}

IODEVICE_UNITY_API int BindKeyWithKey(const char* deviceName, const char* keyName, int keyEvent, InputActionWithKeySignature handler)
{
    if (!handler) return 0;
    dh::IODevice& device = GetIODevice(deviceName);
    device.BindKey(SafeString(keyName), static_cast<dh::InputEvent>(keyEvent), [handler](dh::FKey key)
    {
        handler(key.GetName());
    });
    return 1;
}

IODEVICE_UNITY_API int BindKey(const char* deviceName, const char* keyName, int keyEvent, InputActionSignature handler)
{
    if (!handler) return 0;
    dh::IODevice& device = GetIODevice(deviceName);
    device.BindKey(SafeString(keyName), static_cast<dh::InputEvent>(keyEvent), handler);
    return 1;
}

IODEVICE_UNITY_API int BindAxisKey(const char* deviceName, const char* axisName, InputAxisSignature handler)
{
    if (!handler) return 0;
    dh::IODevice& device = GetIODevice(deviceName);
    device.BindAxisKey(SafeString(axisName), handler);
    return 1;
}

IODEVICE_UNITY_API int BindAction(const char* deviceName, const char* actionName, int keyEvent, InputActionWithKeySignature handler)
{
    if (!handler) return 0;
    dh::IODevice& device = GetIODevice(deviceName);
    device.BindAction(SafeString(actionName), static_cast<dh::InputEvent>(keyEvent), [handler](dh::FKey key)
    {
        handler(key.GetName());
    });
    return 1;
}

IODEVICE_UNITY_API int BindAxis(const char* deviceName, const char* axisName, InputAxisSignature handler)
{
    if (!handler) return 0;
    dh::IODevice& device = GetIODevice(deviceName);
    device.BindAxis(SafeString(axisName), handler);
    return 1;
}

IODEVICE_UNITY_API float GetDOSingle(const char* deviceName, const char* keyName)
{
    return GetIODevice(deviceName).GetDO(dh::FKey(SafeString(keyName)));
}

IODEVICE_UNITY_API float GetDOAction(const char* deviceName, const char* actionName)
{
    return GetIODevice(deviceName).GetDO(SafeString(actionName));
}

IODEVICE_UNITY_API int GetDOAll(const char* deviceName, float* status)
{
    return GetIODevice(deviceName).GetDO(status);
}

IODEVICE_UNITY_API int RefreshStreamingData(const char* deviceName, Byte* streamingData, unsigned int dataSize)
{
    return GetIODevice(deviceName).RefreshStreamingData(streamingData, dataSize);
}

IODEVICE_UNITY_API int WritePluginChannel(const char* deviceName, const char* channelName, const Byte* data, unsigned int size)
{
    if (!channelName) return 0;
    return GetIODevice(deviceName).WritePluginChannel(channelName, data, size);
}

IODEVICE_UNITY_API int BindPluginChannel(const char* deviceName, const char* channelName, PluginChannelCallbackManaged callback)
{
    if (!channelName || !callback) return -1;
    return GetIODevice(deviceName).BindPluginChannel(channelName,
        [callback](const char* channel, const Byte* data, unsigned int size) {
            callback(channel, data, size);
        });
}

IODEVICE_UNITY_API int UnbindPluginChannel(const char* deviceName, const char* channelName, int handlerId)
{
    if (!channelName) return 0;
    return GetIODevice(deviceName).UnbindPluginChannel(channelName, handlerId);
}

IODEVICE_UNITY_API int QueryPluginCapabilities(const char* deviceName, Byte* outJson, unsigned int capacity, unsigned int timeoutMs)
{
    dh::PluginCapabilities caps;
    int result = GetIODevice(deviceName).QueryPluginCapabilities(caps, static_cast<int>(timeoutMs));
    if (result <= 0) return result;
    return CopyBytesToBuffer(caps.Json, outJson, capacity);
}

IODEVICE_UNITY_API int SendPluginRequest(const char* deviceName, const char* topic, const Byte* requestJson, unsigned int requestSize, Byte* responseJson, unsigned int capacity, unsigned int timeoutMs)
{
    if (!topic) return 0;
    dh::PluginMessage request;
    request.Topic = topic;
    request.ContentType = "application/json";
    if (requestJson && requestSize > 0)
        request.Payload.assign(reinterpret_cast<const char*>(requestJson), requestSize);
    dh::PluginMessage response;
    int result = GetIODevice(deviceName).SendPluginRequest(topic, request, response, static_cast<int>(timeoutMs));
    if (result <= 0) return result;
    return CopyBytesToBuffer(response.Payload, responseJson, capacity);
}

IODEVICE_UNITY_API int BindPluginEvent(const char* deviceName, const char* eventName, PluginChannelCallbackManaged callback)
{
    if (!callback) return -1;
    return GetIODevice(deviceName).BindPluginEvent(eventName,
        [callback](const dh::PluginMessage& message) {
            const std::string channelName = message.Topic.empty() ? std::string("_event") : message.Topic;
            callback(channelName.c_str(), reinterpret_cast<const Byte*>(message.Payload.data()), static_cast<unsigned int>(message.Payload.size()));
        });
}

IODEVICE_UNITY_API int UnbindPluginEvent(const char* deviceName, int handlerId)
{
    if (handlerId < 0) return 0;
    return GetIODevice(deviceName).UnbindPluginEvent(handlerId);
}

IODEVICE_UNITY_API int RequestChannel(const char* deviceName, const char* name, const Byte* requestJson, unsigned int requestSize, int waitResponse, const Byte* metadataJson, unsigned int metadataSize, Byte* responseJson, unsigned int capacity, unsigned int timeoutMs)
{
    if (!name) return 0;
    dh::PluginMessage request;
    request.Topic = name;
    request.ContentType = "application/json";
    if (requestJson && requestSize > 0)
        request.Payload.assign(reinterpret_cast<const char*>(requestJson), requestSize);

    dh::ChannelRequestOptions options;
    options.WaitResponse = waitResponse != 0;
    options.TimeoutMs = static_cast<int>(timeoutMs);
    if (metadataJson && metadataSize > 0)
        options.MetadataJson.assign(reinterpret_cast<const char*>(metadataJson), metadataSize);

    dh::ChannelResponse response;
    int result = GetIODevice(deviceName).RequestChannel(name, request, options, response);
    if (result <= 0) return result;
    return CopyBytesToBuffer(response.Message.Payload, responseJson, capacity);
}

IODEVICE_UNITY_API int SubscribeChannelRequest(const char* deviceName, const char* name, ChannelRequestCallbackManaged callback)
{
    if (!name || !callback) return -1;
    return GetIODevice(deviceName).SubscribeChannelRequest(name,
        [callback](const dh::ChannelRequestContext& ctx) {
            callback(ctx.Name.c_str(), ctx.RequestId.c_str(), reinterpret_cast<const Byte*>(ctx.PayloadJson.data()), static_cast<unsigned int>(ctx.PayloadJson.size()), reinterpret_cast<const Byte*>(ctx.MetadataJson.data()), static_cast<unsigned int>(ctx.MetadataJson.size()));
        });
}

IODEVICE_UNITY_API int UnsubscribeChannelRequest(const char* deviceName, int handlerId)
{
    if (handlerId < 0) return 0;
    return GetIODevice(deviceName).UnsubscribeChannelRequest(handlerId);
}

IODEVICE_UNITY_API int RespondChannelRequest(const char* deviceName, const char* requestId, const Byte* payloadJson, unsigned int payloadSize, int ok, const char* errorMessage)
{
    if (!requestId || requestId[0] == '\0') return 0;
    dh::ChannelRequestContext ctx;
    ctx.RequestId = requestId;
    dh::PluginMessage response;
    response.ContentType = "application/json";
    if (payloadJson && payloadSize > 0)
        response.Payload.assign(reinterpret_cast<const char*>(payloadJson), payloadSize);
    if (errorMessage && errorMessage[0] != '\0') response.ErrorMessage = errorMessage;
    return GetIODevice(deviceName).RespondChannelRequest(ctx, response, ok != 0);
}

IODEVICE_UNITY_API int SubscribeChannel(const char* deviceName, const char* name, PluginChannelCallbackManaged callback)
{
    if (!name || !callback) return -1;
    return GetIODevice(deviceName).SubscribeChannel(name,
        [callback](const dh::PluginMessage& message) {
            const std::string channelName = message.Topic.empty() ? std::string() : message.Topic;
            callback(channelName.c_str(), reinterpret_cast<const Byte*>(message.Payload.data()), static_cast<unsigned int>(message.Payload.size()));
        });
}

IODEVICE_UNITY_API int UnsubscribeChannel(const char* deviceName, int handlerId)
{
    if (handlerId < 0) return 0;
    return GetIODevice(deviceName).UnsubscribeChannel(handlerId);
}

IODEVICE_UNITY_API int QueryChannelCapabilities(const char* deviceName, Byte* outJson, unsigned int capacity, unsigned int timeoutMs)
{
    dh::PluginCapabilities caps;
    int result = GetIODevice(deviceName).QueryChannelCapabilities(caps, static_cast<int>(timeoutMs));
    if (result <= 0) return result;
    return CopyBytesToBuffer(caps.Json, outJson, capacity);
}

IODEVICE_UNITY_API int SetDOSingle(const char* deviceName, const char* keyName, float value)
{
    return GetIODevice(deviceName).SetDO(dh::FKey(SafeString(keyName)), value);
}

IODEVICE_UNITY_API int SetDOAll(const char* deviceName, float* status)
{
    return GetIODevice(deviceName).SetDO(status);
}

IODEVICE_UNITY_API int SetDOAction(const char* deviceName, const char* actionName, float value, bool ignoreMessage)
{
    return GetIODevice(deviceName).SetDO(SafeString(actionName), value, ignoreMessage);
}

IODEVICE_UNITY_API int SetDOOn(const char* deviceName, const char* actionName)
{
    return GetIODevice(deviceName).SetDOOn(SafeString(actionName));
}

IODEVICE_UNITY_API int SetDOOff(const char* deviceName, const char* actionName)
{
    return GetIODevice(deviceName).SetDOOff(SafeString(actionName));
}

IODEVICE_UNITY_API int DOImmediate(const char* deviceName)
{
    return GetIODevice(deviceName).DOImmediate();
}

IODEVICE_UNITY_API void Query()
{
    dh::IODeviceController::Instance().Update();
}

IODEVICE_UNITY_API void ClearAllBindings()
{
    dh::IODeviceController::Instance().ClearBindings();
}

IODEVICE_UNITY_API bool GetKey(const char* deviceName, const char* key)
{
    return GetIODevice(deviceName).GetKey(SafeString(key));
}

IODEVICE_UNITY_API bool GetKeyDown(const char* deviceName, const char* key)
{
    return GetIODevice(deviceName).GetKeyDown(SafeString(key));
}

IODEVICE_UNITY_API bool GetKeyUp(const char* deviceName, const char* key)
{
    return GetIODevice(deviceName).GetKeyUp(SafeString(key));
}

IODEVICE_UNITY_API float GetAxis(const char* deviceName, const char* axisName)
{
    return GetIODevice(deviceName).GetAxis(SafeString(axisName));
}

IODEVICE_UNITY_API float GetAxisKey(const char* deviceName, const char* key)
{
    return GetIODevice(deviceName).GetAxisKey(SafeString(key));
}

IODEVICE_UNITY_API float GetRawKeyValue(const char* deviceName, const char* key)
{
    return GetIODevice(deviceName).GetRawKeyValue(SafeString(key));
}

IODEVICE_UNITY_API float GetKeyDownDuration(const char* deviceName, const char* key)
{
    return GetIODevice(deviceName).GetKeyDownDuration(SafeString(key));
}

IODEVICE_UNITY_API void ClearBindings(const char* deviceName)
{
    GetIODevice(deviceName).ClearBindings();
}

IODEVICE_UNITY_API char* DeviceIOType(const char* deviceName)
{
    return DuplicateString(GetIODevice(deviceName).IOType());
}

IODEVICE_UNITY_API char* DeviceDllName(const char* deviceName)
{
    return DuplicateString(GetIODevice(deviceName).DllName());
}

IODEVICE_UNITY_API int DeviceIndex(const char* deviceName)
{
    return GetIODevice(deviceName).Index();
}

IODEVICE_UNITY_API bool IsValid(const char* deviceName)
{
    return GetIODevice(deviceName).IsValid();
}

IODEVICE_UNITY_API int SetAKProps(const char* deviceName, const char* axisName, const char* keyName, float scale)
{
    return GetIODevice(deviceName).SetAKProps(SafeString(axisName), SafeString(keyName), scale);
}

IODEVICE_UNITY_API int SetOKProps(const char* deviceName, const char* actionName, const char* keyName, float scale, bool invertEvent)
{
    return GetIODevice(deviceName).SetOKProps(SafeString(actionName), SafeString(keyName), scale, invertEvent);
}

IODEVICE_UNITY_API int SetPKProps(const char* deviceName, const char* keyName, float offset, float scale, float minValue, float maxValue, float deadZone, float sensitivity, float exponent, bool invert, bool invertEvent)
{
    return GetIODevice(deviceName).SetPKProps(SafeString(keyName), offset, scale, minValue, maxValue, deadZone, sensitivity, exponent, invert, invertEvent);
}

IODEVICE_UNITY_API int MotionLoadSlot(const char* slotId, const char* filePath, int priority, int mixPolicy)
{
    return dh::MotionPlayer::Instance().LoadSlot(SafeString(slotId), SafeString(filePath), priority, static_cast<dh::MixPolicy>(mixPolicy));
}

IODEVICE_UNITY_API void MotionUnloadSlot(const char* slotId)
{
    dh::MotionPlayer::Instance().UnloadSlot(SafeString(slotId));
}

IODEVICE_UNITY_API void MotionUnloadAll()
{
    dh::MotionPlayer::Instance().UnloadAll();
}

IODEVICE_UNITY_API int MotionPlaySlot(const char* slotId)
{
    return dh::MotionPlayer::Instance().PlaySlot(SafeString(slotId));
}

IODEVICE_UNITY_API int MotionPlaySlotFrom(const char* slotId, float timeMs)
{
    return dh::MotionPlayer::Instance().PlaySlotFrom(SafeString(slotId), timeMs);
}

IODEVICE_UNITY_API int MotionPauseSlot(const char* slotId)
{
    return dh::MotionPlayer::Instance().PauseSlot(SafeString(slotId));
}

IODEVICE_UNITY_API int MotionResumeSlot(const char* slotId)
{
    return dh::MotionPlayer::Instance().ResumeSlot(SafeString(slotId));
}

IODEVICE_UNITY_API int MotionStopSlot(const char* slotId)
{
    return dh::MotionPlayer::Instance().StopSlot(SafeString(slotId));
}

IODEVICE_UNITY_API int MotionSeekSlot(const char* slotId, float timeMs)
{
    return dh::MotionPlayer::Instance().SeekSlot(SafeString(slotId), timeMs);
}

IODEVICE_UNITY_API void MotionSetSlotSpeed(const char* slotId, float speed)
{
    dh::MotionPlayer::Instance().SetSlotSpeed(SafeString(slotId), speed);
}

IODEVICE_UNITY_API void MotionSetSlotLoop(const char* slotId, bool loop)
{
    dh::MotionPlayer::Instance().SetSlotLoop(SafeString(slotId), loop);
}

IODEVICE_UNITY_API void MotionSetSlotClockMode(const char* slotId, int mode)
{
    dh::MotionPlayer::Instance().SetSlotClockMode(SafeString(slotId), static_cast<dh::ClockMode>(mode));
}

IODEVICE_UNITY_API void MotionSetSlotExternalTime(const char* slotId, float timeMs)
{
    dh::MotionPlayer::Instance().SetSlotExternalTime(SafeString(slotId), timeMs);
}

IODEVICE_UNITY_API int MotionGetSlotState(const char* slotId)
{
    return static_cast<int>(dh::MotionPlayer::Instance().GetSlotState(SafeString(slotId)));
}

IODEVICE_UNITY_API float MotionGetSlotCurrentTime(const char* slotId)
{
    return dh::MotionPlayer::Instance().GetSlotCurrentTime(SafeString(slotId));
}

IODEVICE_UNITY_API float MotionGetSlotDuration(const char* slotId)
{
    return dh::MotionPlayer::Instance().GetSlotDuration(SafeString(slotId));
}

IODEVICE_UNITY_API int MotionGetSlotCount()
{
    return dh::MotionPlayer::Instance().GetSlotCount();
}

IODEVICE_UNITY_API void MotionPlayAll()
{
    dh::MotionPlayer::Instance().PlayAll();
}

IODEVICE_UNITY_API void MotionPauseAll()
{
    dh::MotionPlayer::Instance().PauseAll();
}

IODEVICE_UNITY_API void MotionStopAll()
{
    dh::MotionPlayer::Instance().StopAll();
}

IODEVICE_UNITY_API int MotionLoadSlotFromJson(const char* slotId, const char* jsonContent, int priority, int mixPolicy)
{
    return dh::MotionPlayer::Instance().LoadSlotFromJson(SafeString(slotId), SafeString(jsonContent), priority, static_cast<dh::MixPolicy>(mixPolicy));
}

IODEVICE_UNITY_API int MotionEvaluateSlotAt(const char* slotId, float timeMs, float* outValues, int maxChannels)
{
    return dh::MotionPlayer::Instance().EvaluateSlotAt(SafeString(slotId), timeMs, outValues, maxChannels);
}

IODEVICE_UNITY_API void MotionSetEventCallback(MotionEventCallbackManaged callback)
{
    dh::MotionPlayer::Instance().SetEventCallback(callback);
}

IODEVICE_UNITY_API void MotionSetEventDataCallback(MotionEventDataCallbackManaged callback)
{
    dh::MotionPlayer::Instance().SetEventDataCallback(callback);
}

IODEVICE_UNITY_API void MotionSetSafetyConfig(float maxRatePerSecond)
{
    dh::MotionPlayer::Instance().SetSafetyConfig(maxRatePerSecond);
}

#endif