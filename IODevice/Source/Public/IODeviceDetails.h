/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include <vector>
#include <set>
#include <string>
#include <memory>
#include <map>
#include <mutex>
#include "IODevice.h"
#include "InputBinding.h"
#include "InputBinding/InputKeyBinding.h"
#include "InputBinding/InputActionBinding.h"
#include "RawIO/RawIO.h"
#include "CoreTypes/IOTypes.h"

/**
 * Device class, posses all raw input and events.
 */

namespace IOToolkit
{

struct IODeviceDetails
{
public:
    IODeviceDetails():
                    name("Invalid")
                    ,rawIO(nullptr)
                    ,device(InvalidDeviceID){}
    IODeviceDetails(DeviceProperties props, std::shared_ptr<RawIO> InRawIO):
                     name(props.Name)
                    ,device(InRawIO?InRawIO->ID(): InvalidDeviceID)
					,props(props)
                    ,rawIO(InRawIO){}
    IODevice& GetDevice();

    void Initialize();
    
    void Tick(float DeltaSeconds);
	void Destroy();
    void ProcessFrameEnd();
    void ClearBinding();

    void BindKey(const FKey& InKey, InputEvent InEvent, InputActionHandlerSignature delegate);
    void BindKey(const FKey& InKey, InputEvent InEvent, InputActionHandlerWithKeySignature delegate);
    void BindAxis(const std::string axisName,FInputAxisHandlerSignature delegate);
    void BindAxisKey(const FKey AxisKey, FInputAxisHandlerSignature delegate);
    void BindAction(std::string ActionName, const InputEvent KeyEvent,InputActionHandlerSignature delegate);
    void BindAction(std::string ActionName, const InputEvent KeyEvent, InputActionHandlerWithKeySignature delegate);

    int SetDO(float* InDOStatus);
    int SetDO(const FKey& InKey, float InValue);
	int SetDO(const char* InOAction, float InValue, bool bIgnoreMassage=false);
	int SetDOOn(const char* InOAction);
	int SetDOOff(const char* InOAction);
	int DOImmediate();

	int SetAKProps(const char* axisName, const char* keyName, float scale);
	int SetOKProps(const char* oactionName, const char* keyName, float scale, bool invertEvent);
	int SetPKProps(const char* keyName, float offset, float scale, float minValue, float maxValue, float deadZone, float sensitivity, float exponent, bool invert, bool invertEvent);

    int GetDO(float* OutDOStatus);
	float GetDO(const FKey& InKey);
	float GetDO(const char* InOAction);

    int RefreshStreamingData(BYTE* StreamingData, unsigned int DataSize);

    /** 通用插件通道 - 宿主→插件 */
    int WritePluginChannel(const char* channelName, const BYTE* data, unsigned int size);

    /** 通用插件通道 - 宿主订阅 */
    int BindPluginChannel(const char* channelName,
                          std::function<void(const char*, const BYTE*, unsigned int)> handler);

    int UnbindPluginChannel(const char* channelName, int handlerId);

    /** 供 RawIO 调用，将插件上行字节派发给所有已注册 handler */
    void DispatchPluginChannel(const char* channelName, const BYTE* data, unsigned int size);

    bool GetKey(const FKey& InKey);
    bool GetKeyDown(const FKey& InKey);
    bool GetKeyUp(const FKey& InKey);
    
    float GetAxis(const char* AxisName);
    float GetAxisKey(const FKey& InKey);
    float GetRawKeyValue(const FKey& InKey);

    float GetKeyDownDuration(const FKey& InKey);
    
    int32 GetNumActionBindings()const { return static_cast<int32>(ActionBindings.size()); }
    FInputActionBinding& GetActionBinding(const int32 BindingIndex);
    const std::string& getName();
    const std::string& getIOType();
    const std::string& getDllName();
    uint8 getIndex();
    bool isValid();
private:
    void AddActionBinding(const FInputActionBinding& Binding);
    bool ValidDevcie(std::string customMsg);
public:
    std::vector<FInputKeyBinding> KeyBindings;
    
    /** The collection of axis bindings. */
    std::vector<FInputAxisBinding> AxisBindings;
   
    std::vector<FInputAxisKeyBinding> AxisKeyBindings;

private:
    std::vector<FInputActionBinding> ActionBindings;

    IODevice device;
    std::string name;
    std::shared_ptr<RawIO> rawIO;
   
    DeviceProperties props;

    /** 插件通道订阅状态 (用 shared_ptr 包裹以保持 IODeviceDetails 可拷贝) */
    struct PluginChannelState
    {
        struct Entry
        {
            int id;
            std::function<void(const char*, const BYTE*, unsigned int)> handler;
        };
        std::map<std::string, std::vector<Entry>> handlers;
        int nextId = 1;
        std::mutex mtx;
    };
    std::shared_ptr<PluginChannelState> pluginChannelState = std::make_shared<PluginChannelState>();
};

};