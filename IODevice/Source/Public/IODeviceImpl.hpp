/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include "IODevice.h"

namespace IOToolkit
{

template<class... Args>
class DynamicClassFuncDelegate
{
public:
    template<class UserClass>
    DynamicClassFuncDelegate& Bind(UserClass* Object, void(UserClass::*Method)(Args...), Args... args)
    {
        NoParamDelegate = [Object, Method, args...]() {(*Object.*Method)(args...);};
        return *this;
    }
    
    void operator()(FKey key)
    {
        if (NoParamDelegate)
        {
            NoParamDelegate();
        }
    }

private:
    std::function<void(void)> NoParamDelegate;
};

template<class... Args>
class DynamicFuncDelegate
{
public:
	DynamicFuncDelegate& Bind(void(*Method)(Args...), Args... args)
	{
		NoParamDelegate = [Method, args...]() {Method(args...); };
		return *this;
	}

	void operator()(FKey key)
	{
		if (NoParamDelegate)
		{
			NoParamDelegate();
		}
	}

private:
	std::function<void(void)> NoParamDelegate;
};

};

template<class UserClass>
void IOToolkit::IODevice::BindKey(const FKey& Key, InputEvent KeyEvent, UserClass* Object, void(UserClass::*Method)())
{
    this->BindKey(Key,KeyEvent,std::bind(Method, Object));
}


//template<class... VarTypes>
//void IOToolkit::IODevice::BindAction(const char* actionName, InputEvent KeyEvent, void(*Method)(VarTypes...), VarTypes... args)
//{
//	std:function<void(FKey)> FuncRef = std::bind(DynamicFuncDelegate<VarTypes...>().Bind(Method, args...),std::placeholders::_1);
//	this->BindAction(actionName, KeyEvent, FuncRef);
//}


template<class UserClass>
void IOToolkit::IODevice::BindAction(const char* actionName, InputEvent KeyEvent, UserClass* Object, void(UserClass::*Method)())
{
    std::function<void(void)> FuncRef = std::bind(Method, Object);
    this->BindAction(actionName, KeyEvent, FuncRef);
}


template<class UserClass>
void IOToolkit::IODevice::BindAction(const char* actionName, InputEvent KeyEvent, UserClass* Object, void(UserClass::*Method)(FKey))
{
    std::function<void(FKey)> FuncRef = std::bind(Method, Object, std::placeholders::_1);
    this->BindAction(actionName, KeyEvent, FuncRef);
}


template<class UserClass, class... VarTypes>
void IOToolkit::IODevice::BindAction(const char* actionName, InputEvent KeyEvent, UserClass* Object, void(UserClass::*Method)(VarTypes...), VarTypes... args)
{
    std::function<void(FKey)> FuncRef = std::bind(DynamicClassFuncDelegate<VarTypes...>().Bind(Object, Method, args...),std::placeholders::_1);
    this->BindAction(actionName, KeyEvent, FuncRef);
}

template<class UserClass>
void IOToolkit::IODevice::BindAxis(const char* axisName, UserClass* Object, void(UserClass::*Method)(float))
{
    this->BindAxis(axisName, std::bind(Method, Object, std::placeholders::_1));
}


template<class UserClass>
void IOToolkit::IODevice::BindAxisKey(const FKey AxisKey, UserClass* Object, void(UserClass::*Method)(float))
{
    this->BindAxisKey(AxisKey, std::bind(Method, Object, std::placeholders::_1));
}