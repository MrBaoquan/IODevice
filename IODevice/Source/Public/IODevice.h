/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include "IOExportsAPI.h"
#include "ExportCoreTypes.h"

namespace IOToolkit
{

/**
 * Device export type
 */
class IOAPI IODevice
{
public:
	/**
	 * 绑定按键回调
	 */
	void BindKey(const FKey& key, InputEvent KeyEvent, std::function<void(void)> keyDelegate);

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
     * 获取指定按键按下的持续时间
     * @return: 如果该按键被按下则返回按下的持续时间，没有被按下则返回0.0
     */
    float GetKeyDownDuration(const FKey& InKey);

    /** 清除该设备绑定的所有回调函数 */
    void ClearBindings();

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