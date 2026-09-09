/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#include "IODevice.h"
#include "IOStatics.h"
#include "CoreTypes.inl"
#include "IOApplication.h"
#include "nlohmann/json.hpp"

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <unordered_map>
#include <mutex>
#include <tuple>

namespace
{
    constexpr const char* kPluginChannelCapabilities = "_capabilities";
    constexpr const char* kPluginChannelRpcReq = "_rpc.req";
    constexpr const char* kPluginChannelRpcRes = "_rpc.res";
    constexpr const char* kPluginChannelEvent = "_event";

    std::atomic<unsigned int> gPluginRequestId{ 1 };
    std::atomic<unsigned int> gChannelSubscriptionId{ 1 };
    std::mutex gChannelSubscriptionsMutex;
    // subscriptionKey → (eventHandlerId, channelHandlerId, channelName)
    std::unordered_map<unsigned long long, std::tuple<int, int, std::string>> gChannelSubscriptions;

    // Plugin→host RPC subscription registry: per-device map of (subscriptionId → [name, rpcReqHandlerId])
    std::atomic<unsigned int> gChannelRequestSubscriptionId{ 1 };
    std::mutex gChannelRequestSubscriptionsMutex;
    std::unordered_map<unsigned long long, std::pair<std::string, int>> gChannelRequestSubscriptions;

    std::string NextPluginRequestId()
    {
        return std::string("req-") + std::to_string(gPluginRequestId.fetch_add(1));
    }

    unsigned long long MakeSubscriptionKey(unsigned char deviceId, int handlerId)
    {
        return (static_cast<unsigned long long>(deviceId) << 32) | static_cast<unsigned int>(handlerId);
    }

    bool TryParseJson(const std::string& text, nlohmann::json& outJson)
    {
        try
        {
            outJson = nlohmann::json::parse(text);
            return true;
        }
        catch (...)
        {
            return false;
        }
    }

    nlohmann::json PayloadToJson(const IOToolkit::PluginMessage& message)
    {
        nlohmann::json parsed;
        if (!message.Payload.empty() && TryParseJson(message.Payload, parsed))
        {
            return parsed;
        }
        return message.Payload;
    }

    IOToolkit::PluginMessage JsonToPluginMessage(const nlohmann::json& json)
    {
        IOToolkit::PluginMessage message;
        if (!json.is_object()) return message;

        if (json.contains("requestId") && json["requestId"].is_string()) message.RequestId = json["requestId"].get<std::string>();
        if (json.contains("topic") && json["topic"].is_string()) message.Topic = json["topic"].get<std::string>();
        if (json.contains("contentType") && json["contentType"].is_string()) message.ContentType = json["contentType"].get<std::string>();
        if (json.contains("ok") && json["ok"].is_boolean()) message.Ok = json["ok"].get<bool>();
        if (json.contains("errorCode") && json["errorCode"].is_number_integer()) message.ErrorCode = json["errorCode"].get<int>();
        if (json.contains("errorMessage") && json["errorMessage"].is_string()) message.ErrorMessage = json["errorMessage"].get<std::string>();
        if (json.contains("payload"))
        {
            message.Payload = json["payload"].is_string()
                ? json["payload"].get<std::string>()
                : json["payload"].dump();
        }
        else if (json.contains("data"))
        {
            message.Payload = json["data"].is_string()
                ? json["data"].get<std::string>()
                : json["data"].dump();
        }
        return message;
    }

    std::string BuildRpcRequestJson(const char* topic, const IOToolkit::PluginMessage& request, const std::string& requestId, const std::string& metadataJson)
    {
        nlohmann::json json;
        json["requestId"] = requestId;
        json["topic"] = topic ? topic : request.Topic;
        json["contentType"] = request.ContentType.empty() ? "application/json" : request.ContentType;
        json["payload"] = PayloadToJson(request);
        if (!metadataJson.empty())
        {
            nlohmann::json metaJson;
            if (TryParseJson(metadataJson, metaJson)) json["metadata"] = metaJson;
            else json["metadata"] = metadataJson;
        }
        return json.dump();
    }

    std::string BuildRpcResponseJson(const std::string& requestId, bool ok, const IOToolkit::PluginMessage& response)
    {
        nlohmann::json json;
        json["requestId"] = requestId;
        json["ok"] = ok;
        json["contentType"] = response.ContentType.empty() ? "application/json" : response.ContentType;
        json["payload"] = PayloadToJson(response);
        if (!ok && !response.ErrorMessage.empty()) json["errorMessage"] = response.ErrorMessage;
        if (!ok && response.ErrorCode != 0) json["errorCode"] = response.ErrorCode;
        return json.dump();
    }

    std::string SynthesizeMetadataJson(const IOToolkit::ChannelRequestOptions& options)
    {
        if (!options.MetadataJson.empty()) return options.MetadataJson;
        if (!options.Target.empty())
        {
            nlohmann::json meta;
            meta["target"] = options.Target;
            return meta.dump();
        }
        return std::string();
    }

    bool TryParseCapabilities(const std::string& text, IOToolkit::PluginCapabilities& outCaps)
    {
        nlohmann::json json;
        if (!TryParseJson(text, json) || !json.is_object()) return false;

        outCaps = IOToolkit::PluginCapabilities();
        outCaps.Json = text;
        if (json.contains("plugin") && json["plugin"].is_string()) outCaps.Plugin = json["plugin"].get<std::string>();
        if (json.contains("version") && json["version"].is_number_integer()) outCaps.Version = json["version"].get<int>();
        if (json.contains("rpc") && json["rpc"].is_boolean()) outCaps.Rpc = json["rpc"].get<bool>();
        if (json.contains("channels") && json["channels"].is_array())
        {
            for (const auto& item : json["channels"])
            {
                if (!item.is_object()) continue;
                IOToolkit::PluginChannelInfo channel;
                if (item.contains("name") && item["name"].is_string()) channel.Name = item["name"].get<std::string>();
                if (item.contains("direction") && item["direction"].is_string()) channel.Direction = item["direction"].get<std::string>();
                if (item.contains("contentType") && item["contentType"].is_string()) channel.ContentType = item["contentType"].get<std::string>();
                if (item.contains("mode") && item["mode"].is_string()) channel.Mode = item["mode"].get<std::string>();
                if (item.contains("rateHintHz") && item["rateHintHz"].is_number_integer()) channel.RateHintHz = item["rateHintHz"].get<int>();
                if (item.contains("latestOnly") && item["latestOnly"].is_boolean()) channel.LatestOnly = item["latestOnly"].get<bool>();
                outCaps.Channels.push_back(channel);
            }
        }
        return true;
    }
}

void IOToolkit::IODevice::BindKey(const FKey& key, InputEvent KeyEvent, std::function<void(FKey)> keyDelegate)
{
    if (!IOApplication::bLoaded) return;
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    deviceDetails.BindKey(key, KeyEvent, keyDelegate);
}

void IOToolkit::IODevice::BindKey(const FKey& key, InputEvent KeyEvent, std::function<void(void)> keyDelegate)
{
    if (!IOApplication::bLoaded) return;
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    deviceDetails.BindKey(key, KeyEvent, keyDelegate);
}

void IOToolkit::IODevice::BindAxisKey(const FKey& key, std::function<void(float)> axisDelegate)
{
    if (!IOApplication::bLoaded) return;
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    deviceDetails.BindAxisKey(key, axisDelegate);
}


//void IOToolkit::IODevice::BindAction(const char* actionName, InputEvent KeyEvent, void(*Method)(void))
//{
//	IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
//	deviceDetails.BindAction(actionName, KeyEvent, Method);
//}

void IOToolkit::IODevice::BindAxis(const char* axisName, std::function<void(float)> axisDelegate)
{
    if (!IOApplication::bLoaded) return;
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    deviceDetails.BindAxis(axisName, axisDelegate);
}

void IOToolkit::IODevice::BindAction(const char* actionName, InputEvent KeyEvent, std::function<void(void)> actionDelegate)
{
    if (!IOApplication::bLoaded) return;
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    deviceDetails.BindAction(actionName, KeyEvent, actionDelegate);
}


//void IOToolkit::IODevice::BindAction(const char* actionName, InputEvent KeyEvent, void(*Method)(FKey))
//{
//	IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
//	deviceDetails.BindAction(actionName, KeyEvent, Method);
//}

int IOToolkit::IODevice::SetDO(float* InDOStatus)
{
    if (!IOApplication::bLoaded) return 0;
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.SetDO(InDOStatus);
}

int IOToolkit::IODevice::SetDO(const char* InOAction, float InValue, bool bIgnoreMassage/*=false*/)
{
	if (!IOApplication::bLoaded) return 0;
	IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
	return deviceDetails.SetDO(InOAction, InValue, bIgnoreMassage);
}

int IOToolkit::IODevice::SetDOOn(const char* InOAction)
{
	if (!IOApplication::bLoaded) return 0;
	IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
	return deviceDetails.SetDOOn(InOAction);
}


int IOToolkit::IODevice::SetDOOff(const char* InOAction)
{
	if (!IOApplication::bLoaded) return 0;
	IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
	return deviceDetails.SetDOOff(InOAction);
}

int IOToolkit::IODevice::DOImmediate()
{
	if (!IOApplication::bLoaded) return 0;
	IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
	return deviceDetails.DOImmediate();
}

int IOToolkit::IODevice::SetDO(const FKey& InKey, float InValue)
{
    if (!IOApplication::bLoaded) return 0;
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.SetDO(InKey, InValue);
}

int IOToolkit::IODevice::GetDO(float* OutDOStatus)
{
    if (!IOApplication::bLoaded) return 0;
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.GetDO(OutDOStatus);
}


int IOToolkit::IODevice::RefreshStreamingData(BYTE* StreamingData, unsigned int DataSize)
{
    if (!IOApplication::bLoaded) return 0;
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.RefreshStreamingData(StreamingData, DataSize);
}

int IOToolkit::IODevice::WritePluginChannel(const char* channelName, const BYTE* data, unsigned int size)
{
    if (!IOApplication::bLoaded) return 0;
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.WritePluginChannel(channelName, data, size);
}

int IOToolkit::IODevice::BindPluginChannel(const char* channelName,
                                           std::function<void(const char*, const BYTE*, unsigned int)> handler)
{
    if (!IOApplication::bLoaded) return -1;
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.BindPluginChannel(channelName, std::move(handler));
}

int IOToolkit::IODevice::UnbindPluginChannel(const char* channelName, int handlerId)
{
    if (!IOApplication::bLoaded) return 0;
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.UnbindPluginChannel(channelName, handlerId);
}

int IOToolkit::IODevice::QueryPluginCapabilities(PluginCapabilities& outCaps, int timeoutMs)
{
    if (!IOApplication::bLoaded) return 0;

    std::mutex mutex;
    std::condition_variable cv;
    std::string payload;
    bool completed = false;

    int handlerId = BindPluginChannel(kPluginChannelCapabilities,
        [&](const char*, const BYTE* data, unsigned int size)
        {
            std::lock_guard<std::mutex> lock(mutex);
            payload.assign(reinterpret_cast<const char*>(data), size);
            completed = true;
            cv.notify_one();
        });
    if (handlerId < 0) return 0;

    int writeResult = WritePluginChannel(kPluginChannelCapabilities, nullptr, 0);
    if (writeResult <= 0)
    {
        UnbindPluginChannel(kPluginChannelCapabilities, handlerId);
        return 0;
    }

    std::unique_lock<std::mutex> lock(mutex);
    bool ok = cv.wait_for(lock, std::chrono::milliseconds(timeoutMs > 0 ? timeoutMs : 1000), [&] { return completed; });
    lock.unlock();
    UnbindPluginChannel(kPluginChannelCapabilities, handlerId);
    return ok && TryParseCapabilities(payload, outCaps) ? 1 : 0;
}

int IOToolkit::IODevice::SendPluginRequest(const char* topic, const PluginMessage& request, PluginMessage& response, int timeoutMs)
{
    if (!IOApplication::bLoaded || !topic) return 0;

    const std::string requestId = request.RequestId.empty() ? NextPluginRequestId() : request.RequestId;
    std::mutex mutex;
    std::condition_variable cv;
    bool completed = false;

    int handlerId = BindPluginChannel(kPluginChannelRpcRes,
        [&](const char*, const BYTE* data, unsigned int size)
        {
            const std::string payload(reinterpret_cast<const char*>(data), size);
            nlohmann::json json;
            if (!TryParseJson(payload, json) || !json.is_object()) return;
            if (!json.contains("requestId") || !json["requestId"].is_string()) return;
            if (json["requestId"].get<std::string>() != requestId) return;

            std::lock_guard<std::mutex> lock(mutex);
            response = JsonToPluginMessage(json);
            completed = true;
            cv.notify_one();
        });
    if (handlerId < 0) return 0;

    const std::string body = BuildRpcRequestJson(topic, request, requestId, std::string());
    int writeResult = WritePluginChannel(kPluginChannelRpcReq, reinterpret_cast<const BYTE*>(body.data()), static_cast<unsigned int>(body.size()));
    if (writeResult <= 0)
    {
        UnbindPluginChannel(kPluginChannelRpcRes, handlerId);
        return 0;
    }

    std::unique_lock<std::mutex> lock(mutex);
    bool ok = cv.wait_for(lock, std::chrono::milliseconds(timeoutMs > 0 ? timeoutMs : 1000), [&] { return completed; });
    lock.unlock();
    UnbindPluginChannel(kPluginChannelRpcRes, handlerId);
    return ok ? 1 : 0;
}

int IOToolkit::IODevice::BindPluginEvent(const char* eventName, std::function<void(const PluginMessage&)> handler)
{
    if (!IOApplication::bLoaded || !handler) return -1;
    const std::string expectedName = eventName ? eventName : "";
    return BindPluginChannel(kPluginChannelEvent,
        [expectedName, handler](const char*, const BYTE* data, unsigned int size)
        {
            const std::string payload(reinterpret_cast<const char*>(data), size);
            nlohmann::json json;
            if (!TryParseJson(payload, json) || !json.is_object()) return;

            std::string actualName;
            if (json.contains("evt") && json["evt"].is_string()) actualName = json["evt"].get<std::string>();
            else if (json.contains("event") && json["event"].is_string()) actualName = json["event"].get<std::string>();
            else if (json.contains("name") && json["name"].is_string()) actualName = json["name"].get<std::string>();
            if (!expectedName.empty() && actualName != expectedName) return;

            PluginMessage message = JsonToPluginMessage(json);
            if (message.Topic.empty()) message.Topic = actualName;
            if (message.Payload.empty() && json.contains("data"))
            {
                message.Payload = json["data"].is_string() ? json["data"].get<std::string>() : json["data"].dump();
            }
            handler(message);
        });
}

int IOToolkit::IODevice::UnbindPluginEvent(int handlerId)
{
    return UnbindPluginChannel(kPluginChannelEvent, handlerId);
}

int IOToolkit::IODevice::RequestChannel(const char* name, const PluginMessage& request, const ChannelRequestOptions& options, ChannelResponse& response)
{
    response = ChannelResponse();
    if (!IOApplication::bLoaded || !name || name[0] == '\0') return 0;

    const std::string metadataJson = SynthesizeMetadataJson(options);

    if (!options.WaitResponse)
    {
        // 单向发送: 直接写 raw payload 到命名通道, 插件如需 metadata 可从 options.MetadataJson 传递
        // (现有 NETIO/Simulator 插件均能处理 raw payload 不需 envelope).
        const std::string& body = request.Payload;
        int writeResult = WritePluginChannel(name, reinterpret_cast<const BYTE*>(body.data()), static_cast<unsigned int>(body.size()));
        response.Ok = writeResult > 0;
        response.Completion = response.Ok ? ChannelCompletionKind::LocalAccepted : ChannelCompletionKind::Failed;
        response.Message = request;
        if (response.Message.Topic.empty()) response.Message.Topic = name;
        return writeResult;
    }

    // 等响应: 手动走 _rpc.req/_rpc.res, 所以能带 metadata. 逻辑与 SendPluginRequest 一致但额外携 metadata.
    const std::string requestId = request.RequestId.empty() ? NextPluginRequestId() : request.RequestId;
    std::mutex mutex;
    std::condition_variable cv;
    bool completed = false;
    PluginMessage rpcResponse;

    int handlerId = BindPluginChannel(kPluginChannelRpcRes,
        [&](const char*, const BYTE* data, unsigned int size)
        {
            const std::string payload(reinterpret_cast<const char*>(data), size);
            nlohmann::json json;
            if (!TryParseJson(payload, json) || !json.is_object()) return;
            if (!json.contains("requestId") || !json["requestId"].is_string()) return;
            if (json["requestId"].get<std::string>() != requestId) return;

            std::lock_guard<std::mutex> lock(mutex);
            rpcResponse = JsonToPluginMessage(json);
            completed = true;
            cv.notify_one();
        });
    if (handlerId < 0)
    {
        response.Completion = ChannelCompletionKind::Failed;
        return 0;
    }

    const std::string body = BuildRpcRequestJson(name, request, requestId, metadataJson);
    int writeResult = WritePluginChannel(kPluginChannelRpcReq,
        reinterpret_cast<const BYTE*>(body.data()),
        static_cast<unsigned int>(body.size()));
    if (writeResult <= 0)
    {
        UnbindPluginChannel(kPluginChannelRpcRes, handlerId);
        response.Completion = ChannelCompletionKind::Failed;
        return 0;
    }

    std::unique_lock<std::mutex> lock(mutex);
    bool ok = cv.wait_for(lock, std::chrono::milliseconds(options.TimeoutMs > 0 ? options.TimeoutMs : 1000), [&] { return completed; });
    lock.unlock();
    UnbindPluginChannel(kPluginChannelRpcRes, handlerId);

    if (!ok)
    {
        response.Completion = ChannelCompletionKind::Timeout;
        response.Ok = false;
        return 0;
    }

    response.Ok = rpcResponse.Ok;
    response.Completion = ChannelCompletionKind::RemoteResponded;
    response.Message = rpcResponse;
    if (response.Message.Topic.empty()) response.Message.Topic = name;
    return 1;
}

int IOToolkit::IODevice::SubscribeChannel(const char* name, std::function<void(const PluginMessage&)> handler)
{
    if (!IOApplication::bLoaded || !name || name[0] == '\0' || !handler) return -1;

    const std::string expectedName = name;

    // 路径 1: _event 通道上按事件名过滤 (代码/含义与原 BindPluginEvent 一致).
    int eventHandlerId = BindPluginEvent(name, handler);

    // 路径 2: 同名命名通道的单向消息. 接受 raw JSON 或 envelope (都能解出 Payload).
    int channelHandlerId = BindPluginChannel(name,
        [expectedName, handler](const char*, const BYTE* data, unsigned int size)
        {
            if (!data || size == 0) return;
            const std::string body(reinterpret_cast<const char*>(data), size);
            nlohmann::json json;
            PluginMessage message;
            if (TryParseJson(body, json) && json.is_object() && (json.contains("payload") || json.contains("topic")))
            {
                message = JsonToPluginMessage(json);
                if (message.Topic.empty()) message.Topic = expectedName;
            }
            else
            {
                message.Topic = expectedName;
                message.ContentType = "application/json";
                message.Payload = body;
            }
            handler(message);
        });

    if (eventHandlerId < 0 && channelHandlerId < 0) return -1;

    int subscriptionId = static_cast<int>(gChannelSubscriptionId.fetch_add(1));
    std::lock_guard<std::mutex> lock(gChannelSubscriptionsMutex);
    gChannelSubscriptions[MakeSubscriptionKey(deviceID, subscriptionId)] = std::make_tuple(eventHandlerId, channelHandlerId, expectedName);
    return subscriptionId;
}

int IOToolkit::IODevice::UnsubscribeChannel(int handlerId)
{
    if (handlerId < 0) return 0;

    int eventHandlerId = -1;
    int channelHandlerId = -1;
    std::string channelName;
    {
        std::lock_guard<std::mutex> lock(gChannelSubscriptionsMutex);
        auto it = gChannelSubscriptions.find(MakeSubscriptionKey(deviceID, handlerId));
        if (it == gChannelSubscriptions.end()) return 0;
        eventHandlerId = std::get<0>(it->second);
        channelHandlerId = std::get<1>(it->second);
        channelName = std::get<2>(it->second);
        gChannelSubscriptions.erase(it);
    }

    int result = 0;
    if (eventHandlerId >= 0) result |= UnbindPluginEvent(eventHandlerId);
    if (channelHandlerId >= 0 && !channelName.empty())
        result |= UnbindPluginChannel(channelName.c_str(), channelHandlerId);
    return result;
}

int IOToolkit::IODevice::QueryChannelCapabilities(PluginCapabilities& outCaps, int timeoutMs)
{
    return QueryPluginCapabilities(outCaps, timeoutMs);
}

int IOToolkit::IODevice::SubscribeChannelRequest(const char* name, std::function<void(const ChannelRequestContext&)> handler)
{
    if (!IOApplication::bLoaded || !name || name[0] == '\0' || !handler) return -1;
    const std::string expectedTopic = name;
    const unsigned char capturedDeviceId = deviceID;

    int rpcReqHandlerId = BindPluginChannel(kPluginChannelRpcReq,
        [expectedTopic, handler](const char*, const BYTE* data, unsigned int size)
        {
            if (!data || size == 0) return;
            const std::string payload(reinterpret_cast<const char*>(data), size);
            nlohmann::json json;
            if (!TryParseJson(payload, json) || !json.is_object()) return;
            // \u5fc5\u987b\u662f\u5c1a\u672a\u54cd\u5e94\u7684\u8bf7\u6c42 (\u5305\u542b requestId+topic), \u4e14 topic \u5339\u914d
            if (!json.contains("requestId") || !json["requestId"].is_string()) return;
            if (!json.contains("topic") || !json["topic"].is_string()) return;
            const std::string topic = json["topic"].get<std::string>();
            if (topic != expectedTopic) return;

            ChannelRequestContext ctx;
            ctx.Name = topic;
            ctx.RequestId = json["requestId"].get<std::string>();
            if (json.contains("payload"))
            {
                ctx.PayloadJson = json["payload"].is_string()
                    ? json["payload"].get<std::string>()
                    : json["payload"].dump();
            }
            if (json.contains("metadata"))
            {
                ctx.MetadataJson = json["metadata"].is_string()
                    ? json["metadata"].get<std::string>()
                    : json["metadata"].dump();
            }
            handler(ctx);
        });
    if (rpcReqHandlerId < 0) return -1;

    const unsigned int subId = gChannelRequestSubscriptionId.fetch_add(1);
    {
        std::lock_guard<std::mutex> lock(gChannelRequestSubscriptionsMutex);
        gChannelRequestSubscriptions[(static_cast<unsigned long long>(capturedDeviceId) << 32) | subId]
            = std::make_pair(expectedTopic, rpcReqHandlerId);
    }
    return static_cast<int>(subId);
}

int IOToolkit::IODevice::UnsubscribeChannelRequest(int handlerId)
{
    if (handlerId < 0) return 0;
    const unsigned long long key = (static_cast<unsigned long long>(deviceID) << 32) | static_cast<unsigned int>(handlerId);
    std::pair<std::string, int> entry;
    {
        std::lock_guard<std::mutex> lock(gChannelRequestSubscriptionsMutex);
        auto it = gChannelRequestSubscriptions.find(key);
        if (it == gChannelRequestSubscriptions.end()) return 0;
        entry = it->second;
        gChannelRequestSubscriptions.erase(it);
    }
    return UnbindPluginChannel(kPluginChannelRpcReq, entry.second);
}

int IOToolkit::IODevice::RespondChannelRequest(const ChannelRequestContext& ctx, const PluginMessage& response, bool ok)
{
    if (!IOApplication::bLoaded || ctx.RequestId.empty()) return 0;
    const std::string body = BuildRpcResponseJson(ctx.RequestId, ok, response);
    return WritePluginChannel(kPluginChannelRpcRes,
        reinterpret_cast<const BYTE*>(body.data()),
        static_cast<unsigned int>(body.size()));
}

float IOToolkit::IODevice::GetDO(const char* InOAction)
{
	if (!IOApplication::bLoaded) return 0.0f;
	IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
	return deviceDetails.GetDO(InOAction);
}

float IOToolkit::IODevice::GetDO(const FKey& InKey)
{
	if (!IOApplication::bLoaded) return 0.0f;
	IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
	return deviceDetails.GetDO(InKey);
}

bool IOToolkit::IODevice::GetKey(const FKey& InKey)
{
    if (!IOApplication::bLoaded) return false;
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.GetKey(InKey);
}

bool IOToolkit::IODevice::GetKeyDown(const FKey& InKey)
{
    if (!IOApplication::bLoaded) return false;
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.GetKeyDown(InKey);
}

bool IOToolkit::IODevice::GetKeyUp(const FKey& InKey)
{
    if (!IOApplication::bLoaded) return false;
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.GetKeyUp(InKey);
}

float IOToolkit::IODevice::GetAxis(const char* AxisName)
{
    if (!IOApplication::bLoaded) return 0.0f;
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.GetAxis(AxisName);
}

float IOToolkit::IODevice::GetAxisKey(const FKey& InKey)
{
    if (!IOApplication::bLoaded) return 0.0f;
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.GetAxisKey(InKey);
}

float IOToolkit::IODevice::GetRawKeyValue(const FKey& InKey)
{
    if (!IOApplication::bLoaded) return 0.0f;
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.GetRawKeyValue(InKey);
}

float IOToolkit::IODevice::GetKeyDownDuration(const FKey& InKey)
{
    if (!IOApplication::bLoaded) return 0.0f;
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.GetKeyDownDuration(InKey);
}

void IOToolkit::IODevice::ClearBindings()
{
    if (!IOApplication::bLoaded) return;
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    deviceDetails.ClearBinding();
}


const char* IOToolkit::IODevice::Name()
{
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.getName().data();
}

const char* IOToolkit::IODevice::DllName()
{
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    auto dllName = deviceDetails.getDllName().data();
    return deviceDetails.getDllName().data();
}


const char* IOToolkit::IODevice::IOType()
{
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.getIOType().data();
}


const IOToolkit::uint8 IOToolkit::IODevice::Index()
{
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.getIndex();
}

const bool IOToolkit::IODevice::IsValid() const
{
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceID != InvalidDeviceID && deviceDetails.isValid();
}

const IOToolkit::uint8 IOToolkit::IODevice::GetID() const
{
    return deviceID;
}

const bool IOToolkit::IODevice::operator==(const IODevice& rhs)
{
    return deviceID == rhs.deviceID;
}

int IOToolkit::IODevice::SetAKProps(const char* axisName, const char* keyName, float scale)
{
    if (!IOApplication::bLoaded) return 0;
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.SetAKProps(axisName, keyName, scale);
}

int IOToolkit::IODevice::SetOKProps(const char* oactionName, const char* keyName, float scale, bool invertEvent)
{
    if (!IOApplication::bLoaded) return 0;
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.SetOKProps(oactionName, keyName, scale, invertEvent);
}

int IOToolkit::IODevice::SetPKProps(const char* keyName, float offset, float scale, float minValue, float maxValue, float deadZone, float sensitivity, float exponent, bool invert, bool invertEvent)
{
    if (!IOApplication::bLoaded) return 0;
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    return deviceDetails.SetPKProps(keyName, offset, scale, minValue, maxValue, deadZone, sensitivity, exponent, invert, invertEvent);
}

void IOToolkit::IODevice::BindAction(const char* actionName, InputEvent KeyEvent, std::function<void(FKey)> actionDelegate)
{
    if (!IOApplication::bLoaded) return;
    IODeviceDetails& deviceDetails = IODevices::GetDeviceDetail(deviceID);
    deviceDetails.BindAction(actionName, KeyEvent, actionDelegate);
}
