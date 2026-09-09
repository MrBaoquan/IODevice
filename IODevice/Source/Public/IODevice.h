/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include <functional>
#include <string>
#include <vector>
#include "IOExportsAPI.h"
#include "ExportCoreTypes.h"

namespace IOToolkit
{

struct PluginMessage
{
    std::string RequestId;
    std::string Topic;
    std::string ContentType = "application/json";
    std::string Payload;
    bool Ok = true;
    int ErrorCode = 0;
    std::string ErrorMessage;
};

struct PluginChannelInfo
{
    std::string Name;
    std::string Direction;
    std::string ContentType;
    std::string Mode;
    int RateHintHz = 0;
    bool LatestOnly = false;
};

struct PluginCapabilities
{
    std::string Plugin;
    int Version = 0;
    bool Rpc = false;
    std::vector<PluginChannelInfo> Channels;
    std::string Json;
};

enum class ChannelCompletionKind
{
    LocalAccepted = 0,
    RemoteResponded = 1,
    Failed = 2,
    Timeout = 3
};

struct ChannelRequestOptions
{
    bool WaitResponse = true;
    int TimeoutMs = 1000;
    /**
     * 透明 metadata, IODevice 不解析仅原样转发到插件 envelope.
     * 推荐内容由插件协议定义 (NETIO 使用 {"target":"sid:xxx" 或 "ip:port"} 等).
     * 若为空且 Target 非空, IODevice 会合成 {"target":Target}.
     */
    std::string MetadataJson;
    std::string Target;
};

struct ChannelResponse
{
    bool Ok = false;
    ChannelCompletionKind Completion = ChannelCompletionKind::Failed;
    PluginMessage Message;
};

/**
 * 插件转发的对端请求上下文. 由 SubscribeChannelRequest 的 handler 接收;
 * 业务侧调用 RespondChannelRequest(ctx, ...) 完成响应.
 * IODevice 对 metadata 内容透明, 仅作为字符串原样转发回插件.
 */
struct ChannelRequestContext
{
    std::string Name;          ///< 业务 topic, 来自 envelope.topic
    std::string RequestId;     ///< 由插件分配 (推荐 "p:xxx" 前缀), 用于回写 _rpc.res
    std::string PayloadJson;   ///< envelope.payload 序列化后的 JSON
    std::string MetadataJson;  ///< envelope.metadata, 协议特定 (NETIO 含 srcSid 等)
};

/**
 * Device export type
 */
class IOAPI IODevice
{
public:
	/**
	 * 绑定按键回调
	 */
	void BindKey(const FKey& key, InputEvent KeyEvent, std::function<void(FKey)> keyDelegate);
	void BindKey(const FKey& key, InputEvent KeyEvent, std::function<void(void)> keyDelegate);

    template<class UserClass>
    void BindKey(const FKey& Key, InputEvent KeyEvent, UserClass* Object, void(UserClass::*Method)(FKey));

    template<class UserClass>
    void BindKey(const FKey& Key, InputEvent KeyEvent, UserClass* Object, void(UserClass::*Method)());

	/**
	 * 绑定轴回调
	 */
	void BindAxis(const char* axisName, std::function<void(float)> axisDelegate);

    template<class UserClass>
    void BindAxis(const char* axisName, UserClass* Object, void(UserClass::*Method)(float));


	/**
	 * 绑定键轴回调
	 */
	void BindAxisKey(const FKey& key, std::function<void(float)> axisDelegate);

    template<class UserClass>
    void BindAxisKey(const FKey AxisKey, UserClass* Object, void(UserClass::*Method)(float));

	/**
	 * 绑定动作回调
	 */
	void BindAction(const char* actionName, InputEvent KeyEvent, std::function<void(FKey)> actionDelegate);

	void BindAction(const char* actionName, InputEvent KeyEvent, std::function<void(void)> actionDelegate);

	/*template<class... VarTypes>
	void BindAction(const char* actionName, InputEvent KeyEvent, void(* Method)(VarTypes...), VarTypes... args);*/

    template<class UserClass>
    void BindAction(const char* actionName, InputEvent KeyEvent, UserClass* Object, void(UserClass::*Method)());

    template<class UserClass>
    void BindAction(const char* actionName, InputEvent KeyEvent, UserClass* Object, void(UserClass::*Method)(FKey));

    template<class UserClass, class... VarTypes>
    void BindAction(const char* actionName, InputEvent KeyEvent, UserClass* Object, void(UserClass::*Method)(VarTypes...), VarTypes... args);
    
    /**
     * 设置设备输出状态
     * @param InDOStatus: 设备所有通道的值
     * @return: 成功返回1 失败返回0
     */
    int SetDO(float* InDOStatus);

    /**
     * 设置设备单个键的输出状态
     * @param InKey: 需要设置的按键
     * @param InValue: 要设置的值
     * @return: 成功返回1 失败返回0
     */
    int SetDO(const FKey& InKey, float InValue);

	/**
	 * @param InOAction: 输出动作名称
	 * @param InValue: 设置的值
	 * @return: 成功返回1 失败返回0
	 */
	int SetDO(const char* InOAction, float InValue, bool bIngoreMassage=false);
	int SetDOOn(const char* InOAction);
	int SetDOOff(const char* InOAction);
	int DOImmediate();
    /**
     * 获取设备输出状态
     * @param OutDOStatus: 输出参数,设备所有通道的值
     * @return: 成功返回1 失败返回0
     */
    int GetDO(float* OutDOStatus);

    /**
     * 获取设备指定按键输出状态
     * @param InKey: 需要获取的按键
     * @return: 高电平返回1 低电平返回0
     */
	float GetDO(const FKey& InKey);
	float GetDO(const char* InOAction);

    /**
     * 刷新设备自定义数据流
     * @param StreamingData 数据流缓冲区
     * @param DataSize  数据缓冲区大小
     *
     * @deprecated 建议改用 WritePluginChannel("default", ...)，该接口作为兼容层保留。
     */
    int RefreshStreamingData(BYTE* StreamingData, unsigned int DataSize);

    /**
     * 通用插件通道：宿主 → 插件 写入字节流
     * 通道命名由插件自身约定（如 NETIO 的 "netio.event.out"、"netio.ws.out"）
     * @param channelName 通道名称 (ASCII/UTF-8)
     * @param data 字节缓冲区
     * @param size 字节数
     * @return 成功返回 1, 失败返回 0 或负数
     */
    int WritePluginChannel(const char* channelName, const BYTE* data, unsigned int size);

    /**
     * 通用插件通道：宿主侧订阅插件上行字节流
     * @param channelName 通道名称
     * @param handler 接收字节流的回调 (channel, data, size)
     * @return >=0 : 回调 ID (用于 UnbindPluginChannel); <0 : 失败
     */
    int BindPluginChannel(const char* channelName,
                          std::function<void(const char*, const BYTE*, unsigned int)> handler);

    /**
     * 解除通道订阅
     * @param channelName 通道名称
     * @param handlerId BindPluginChannel 返回的 ID
     * @return 1 成功; 0 未找到
     */
    int UnbindPluginChannel(const char* channelName, int handlerId);

    /**
     * 查询插件能力声明. 插件需通过 _capabilities 通道返回 UTF-8 JSON.
     * @param outCaps 解析后的能力信息, Json 字段保留原始 JSON
     * @return 1 成功; 0/负数 失败或超时
     */
    int QueryPluginCapabilities(PluginCapabilities& outCaps, int timeoutMs = 1000);

    /**
     * 通过 _rpc.req/_rpc.res 执行通用请求/响应.
     * @param topic 请求主题
     * @param request 请求消息, Payload 为 UTF-8 JSON 或文本
     * @param response 响应消息
     * @param timeoutMs 超时时间
     * @return 1 成功; 0/负数 失败或超时
     */
    int SendPluginRequest(const char* topic, const PluginMessage& request, PluginMessage& response, int timeoutMs);

    /**
     * 订阅 _event 通道中的结构化事件.
     * @param eventName 事件名; 为空则接收全部事件
     * @param handler 事件回调
        * @return >=0 : 回调 ID, 可用 UnbindPluginEvent(id) 解绑
     */
    int BindPluginEvent(const char* eventName, std::function<void(const PluginMessage&)> handler);

        /**
        * 解除结构化事件订阅.
        * @param handlerId BindPluginEvent 返回的 ID
        * @return 1 成功; 0 未找到
        */
        int UnbindPluginEvent(int handlerId);

    /**
    * 标准 Channel 发送入口. 默认等待响应; WaitResponse=false 时直接走单向通道.
     */
    int RequestChannel(const char* name, const PluginMessage& request, const ChannelRequestOptions& options, ChannelResponse& response);

    /**
     * 标准 Channel 订阅入口. 覆盖 _event 路径 + 命名 channel 路径 (单向消息).
     * 若需处理对端请求并回复, 使用 SubscribeChannelRequest.
     */
    int SubscribeChannel(const char* name, std::function<void(const PluginMessage&)> handler);

    /**
     * 解除标准 Channel 订阅.
     */
    int UnsubscribeChannel(int handlerId);

    /**
     * 标准能力查询入口.
     */
    int QueryChannelCapabilities(PluginCapabilities& outCaps, int timeoutMs = 1000);

    /**
     * 订阅插件转发的对端请求 (plugin → host RPC).
     * 插件通过约定通道 _rpc.req 把对端请求 envelope 推给宿主, IODevice 解析 envelope.topic
     * 并仅在等于 name 时回调 handler. handler 必须使用 ctx.RequestId 调用 RespondChannelRequest 完成响应.
     * @return >=0: 订阅 ID 用于 UnsubscribeChannelRequest; <0: 失败
     */
    int SubscribeChannelRequest(const char* name, std::function<void(const ChannelRequestContext&)> handler);

    /**
     * 解除 SubscribeChannelRequest 订阅.
     */
    int UnsubscribeChannelRequest(int handlerId);

    /**
     * 把响应写回 _rpc.res, 让插件路由回原 peer.
     * @param ctx SubscribeChannelRequest handler 收到的上下文 (RequestId 不能为空)
     * @param response 响应消息, Payload 为 UTF-8 JSON 或文本
     * @param ok 是否业务成功; 失败时 response.ErrorMessage 作为 errorMessage 字段
     * @return 1 成功; 0 失败
     */
    int RespondChannelRequest(const ChannelRequestContext& ctx, const PluginMessage& response, bool ok = true);
  
    /**
     * 获取设备指定按键状态
     * @param InKey: 指定要查询的按键
     * @return: 按键被按下返回true, 否则返回false
     */
    bool GetKey(const FKey& InKey);

    /**
     * 获取设备指定按键状态
     * @param InKey: 指定要被查询的按键
     * @return: 按键由释放状态被按下时，该帧返回true, 否则返回false
     */
    bool GetKeyDown(const FKey& InKey);

    /**
    * 获取设备指定按键状态
    * @param InKey: 指定要被查询的按键
    * @return: 按键由按下状态被释放时，该帧返回true, 否则返回false
    */
    bool GetKeyUp(const FKey& InKey);

    /**
     * 获取设备指定Axis的值
     * @param AxisName: 配置文件中Axis节点的名称
     * @return: 返回该Axis节点经过计算后的值
     */
    float GetAxis(const char* AxisName);

    /**
    * 获取设备指定AxisKey的值
    * @param InKey: 指定要被查询的按键
    * @return: 返回该Axis节点经过计算后的值
    */
    float GetAxisKey(const FKey& InKey);

    /**
    * 获取设备指定Key的原始值（未经处理）
    * @param InKey: 指定要进行查询的按键
    * @return: 返回该Key的原始输入值
    */
    float GetRawKeyValue(const FKey& InKey);

    /**
     * 获取指定按键按下的持续时间
     * @return: 如果该按键被按下则返回按下的持续时间，没有被按下则返回0.0
     */
    float GetKeyDownDuration(const FKey& InKey);

    /**
     * 设置AxisKey属性
     * @param axisName: Axis名称
     * @param keyName: Key名称
     * @param scale: 缩放值
     * @return: 成功返回1，失败返回0
     */
    int SetAKProps(const char* axisName, const char* keyName, float scale);

    /**
     * 设置OutputAction属性
     * @param oactionName: OutputAction名称
     * @param keyName: Key名称
     * @param scale: 缩放值
     * @param invertEvent: 是否反转事件
     * @return: 成功返回1，失败返回0
     */
    int SetOKProps(const char* oactionName, const char* keyName, float scale, bool invertEvent);

    /**
     * 设置PropertyKey属性
     * @param keyName: Key名称
     * @param offset: 偏移值（校准零点）
     * @param scale: 缩放系数（映射输入范围）
     * @param minValue: 最小值
     * @param maxValue: 最大值
     * @param deadZone: 死区
     * @param sensitivity: 灵敏度
     * @param exponent: 指数曲线
     * @param invert: 是否反转数值
     * @param invertEvent: 是否反转事件
     * @return: 成功返回1，失败返回0
     */
    int SetPKProps(const char* keyName, float offset, float scale, float minValue, float maxValue, float deadZone, float sensitivity, float exponent, bool invert, bool invertEvent);

    /** 清除该设备绑定的所有回调函数 */
    void ClearBindings();

    const char* Name();
    const char* DllName();
    const char* IOType();
    const uint8 Index();

    const bool IsValid() const;
    const uint8 GetID() const;
    const bool operator==(const IODevice& rhs);

private:
    IODevice() :deviceID(0) {}
    ~IODevice() = default;
    IODevice(uint8 InID) :deviceID(InID) {}

    uint8 deviceID;

    friend class IODevices;
    friend struct IODeviceDetails;
};

};

#include "IODeviceImpl.hpp"