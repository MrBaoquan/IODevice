/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#include "RawIO/ExternalIO.h"
#include <filesystem>
#include "InputSettings.h"
#include "CoreTypes/IOTypes.h"
#include "IOLog.h"

DevelopHelper::ExternalIO::ExternalIO(uint8 InID, uint8 InDeviceIndex, std::string InFullDllName):
                                 CustomIOBase(InID,InDeviceIndex,IOType::External)
                                ,externalDll(InFullDllName.data())
                                ,bValid(false)
{
	Constructor();
}

void DevelopHelper::ExternalIO::Tick(float DeltaSeconds)
{
    if (!bValid) { return; }

	// 分发按键输入事件
    if (inputCount > 0)
    {
        if(GetDeviceDI(DIStatus))
        {
            DispatchButtonEvent(DIStatus, channelsState);
        }
    }
    
	// 分发轴输入事件
    if (axisCount > 0)
    {
        if (GetDeviceAD(ADStatus)) 
        {
            DispatchAxisEvent(ADStatus);
        }
    }

	// 获取输入通道值
    if (outputCount > 0)
    {
        GetDO(DOStatus);
    }
    
}

void DevelopHelper::ExternalIO::OnFrameEnd()
{
    if (bDoDOAtFrameEnd)
    {
        SetDO(DOStatus);
        bDoDOAtFrameEnd = false;
    }
}

void DevelopHelper::ExternalIO::Destroy()
{
	if (bValid)
	{
		externalDll.CloseDevice(deviceIndex);
	}
}

int DevelopHelper::ExternalIO::ConvertFKeyToChannel(const FKey& InKey)
{
    std::string keyName = InKey.GetName();
    size_t pos = keyName.find_first_of('_');
    std::string num_str = keyName.substr(pos + 1, keyName.size() - pos - 1);
    return std::atoi(num_str.data());
}

short DevelopHelper::ExternalIO::GetDO(const FKey InKey)
{
    int numChannel = ConvertFKeyToChannel(InKey);
    if (!IsValidChannel(numChannel,outputCount))
    {
        return 0;
    }
    return DOStatus[numChannel];
}


void DevelopHelper::ExternalIO::Initialize()
{
	__super::Initialize();
	OActionMappings = UInputSettings::Instance().OActionMappings[deviceID];
}

int DevelopHelper::ExternalIO::GetDeviceDI(std::vector<BYTE>& OutDIStatus)
{
    static BYTE exDIStatus[MaxIOCount];
    int retCode = externalDll.GetDeviceDI(deviceIndex, exDIStatus);
    for (size_t index = 0;index < OutDIStatus.size();index++)
    {
        OutDIStatus[index] = exDIStatus[index];
    }
    return retCode;
}

int DevelopHelper::ExternalIO::GetDeviceAD(std::vector<short>& OutADStatus)
{
    static short exADStatus[MaxIOCount];
    int retCode = externalDll.GetDeviceAD(deviceIndex, exADStatus);
    for (size_t index = 0;index < OutADStatus.size();index++) 
    {
        OutADStatus[index] = exADStatus[index];
    }
    return retCode;
}

int DevelopHelper::ExternalIO::GetDO(std::vector<short>& OutDOStatus)
{
    static short exDoStatus[MaxIOCount];
    int retCode = externalDll.GetDeviceDO(deviceIndex, exDoStatus);
	if (retCode == 1) {
		for (size_t index = 0; index < OutDOStatus.size(); ++index)
		{
			OutDOStatus[index] = exDoStatus[index];
		}
	}
    return retCode;
}

int DevelopHelper::ExternalIO::GetDO(short* OutDOStatus)
{
    if (!bValid) { return 0; }
	memcpy(OutDOStatus, DOStatus.data(), sizeof(short)*outputCount);
    return 1;
}

// Set All by raw data
int DevelopHelper::ExternalIO::SetDO(short* InDOStatus)
{
    if (!bValid) { return 0; }
	memcpy(DOStatus.data(), InDOStatus, sizeof(short)*outputCount);
	bDoDOAtFrameEnd = true;
	return 1;
}

int DevelopHelper::ExternalIO::SetDOOn(const char* InOAction)
{
	return this->SetDO(InOAction, 1);
}


int DevelopHelper::ExternalIO::SetDOOff(const char* InOAction)
{
	return this->SetDO(InOAction,0);
}

// Set do by oaction
int DevelopHelper::ExternalIO::SetDO(const char* InOAction, short val)
{
	if (!bValid) { return 0; }
	if (!OActionMappings.count(InOAction)) { return 0; }
	const std::vector<FOutputActionKey>& _keys = OActionMappings.at(InOAction);
	for (auto& _actionKey : _keys)
	{
		int _channel = ConvertFKeyToChannel(_actionKey.Key);
		if(!IsValidChannel(_channel,outputCount)){continue;}
		float _rawValue = static_cast<float>(val);
		if (_actionKey.InvertEvent) {
			if (_rawValue == 0.f) {
				_rawValue = 1.f;
			}
			else {
				_rawValue = 0.f;
			}
		}
		DOStatus[_channel] = static_cast<short>(MassageKeyInput(_actionKey.Key, _rawValue) * _actionKey.Scale);
	}
	bDoDOAtFrameEnd = true;
	return 1;
}

// Set do by singel channel
int DevelopHelper::ExternalIO::SetDO(const FKey InKey, short val)
{
	int numChannel = ConvertFKeyToChannel(InKey);
	if (!IsValidChannel(numChannel, outputCount))
	{
		return 0;
	}
	DOStatus[numChannel] = val;
	bDoDOAtFrameEnd = true;
	return 1;
}

int DevelopHelper::ExternalIO::SetDO(std::vector<short>& InDOStatus)
{
    static short tmpDOStatus[MaxIOCount];
    for (size_t index = 0;index < InDOStatus.size();index++)
    {
        tmpDOStatus[index] = DOStatus[index];
    }
    return externalDll.SetDeviceDO(deviceIndex, tmpDOStatus);
}

DevelopHelper::ExternalIO::~ExternalIO()
{
    
}

void DevelopHelper::ExternalIO::Constructor()
{
    int loadDllErrCode = externalDll.GetErrCode();
    if (loadDllErrCode != 0)
    {
        IOLog::Instance().Error(std::string("Load externall dll ")+externalDll.GetDllName()+" failed. error code:" + std::to_string(loadDllErrCode));
        bValid = false;
        return;
    }
    DeviceInfo* devInfo = externalDll.Initialize();
    inputCount = devInfo->InputCount;
    outputCount = devInfo->OutputCount;
    axisCount = devInfo->AxisCount;

    DIStatus = std::vector<BYTE>(inputCount, 0);
    DOStatus = std::vector<short>(outputCount, 0);
    ADStatus = std::vector<short>(axisCount, 0);

    channelsState = std::vector<ButtonState>(inputCount);
	// 基本变量初始化完成后 再初始化父类
	__super::Constructor();
    bValid = externalDll.OpenDevice(deviceIndex)==1;
}

bool DevelopHelper::ExternalIO::IsValidChannel(int InChannel, int InMaxNumber)
{
    if (InChannel < 0 || InChannel >= InMaxNumber)
    {
        return false;
    }
    return true;
}