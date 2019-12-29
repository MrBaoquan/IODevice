/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include <vector>
#include <set>
#include <string>
#include <memory>
#include "IODevice.h"
#include "InputBinding.h"
#include "InputBinding/InputKeyBinding.h"
#include "InputBinding/InputActionBinding.h"
#include "RawIO/RawIO.h"

/**
 * Device class, posses all raw input and events.
 */

namespace DevelopHelper
{

struct IODeviceDetails
{
public:
    IODeviceDetails():
                    name("Invalid")
                    ,rawIO(nullptr)
                    ,device(InvalidDeviceID){}
    IODeviceDetails(std::string InName, std::shared_ptr<RawIO> InRawIO):
                     name(InName)
                    ,device(InRawIO?InRawIO->ID(): InvalidDeviceID)
                    ,rawIO(InRawIO){}
    IODevice& GetDevice();

    void Initialize();
    
    void Tick(float DeltaSeconds);
	void Destroy();
    void ProcessFrameEnd();
    void ClearBinding();

    void BindKey(const FKey& InKey, InputEvent InEvent, InputActionHandlerSignature delegate);
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

    int GetDO(float* OutDOStatus);
	float GetDO(const FKey& InKey);
	float GetDO(const char* InOAction);

    bool GetKey(const FKey& InKey);
    bool GetKeyDown(const FKey& InKey);
    bool GetKeyUp(const FKey& InKey);
    
    float GetAxis(const char* AxisName);
    float GetAxisKey(const FKey& InKey);

    float GetKeyDownDuration(const FKey& InKey);
    
    int32 GetNumActionBindings()const { return static_cast<int32>(ActionBindings.size()); }
    FInputActionBinding& GetActionBinding(const int32 BindingIndex);
    std::string getName();
    std::string getIOType();
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

};

};