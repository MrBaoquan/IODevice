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
		this->DOImmediate();
        GetDO(DOStatus);
    }
    
}

/**
 * Note: 仅针对核心主逻辑  并非用户层/业务层尾帧
 */
void DevelopHelper::ExternalIO::OnFrameEnd()
{
   
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

float DevelopHelper::ExternalIO::GetDO(const FKey InKey)
{
    int numChannel = ConvertFKeyToChannel(InKey);
    if (!IsValidChannel(numChannel,outputCount))
    {
        return 0;
    }
    return DOStatus[numChannel];
}

float DevelopHelper::ExternalIO::GetDO(const char* InOAction)
{
	if (!bValid) { return 0; }
	if (!OActionMappings.count(InOAction)) { return 0; }
	
	if (rawDOStatus.count(InOAction)) {
		return rawDOStatus.at(InOAction);
	}
	return 0;
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

int DevelopHelper::ExternalIO::GetDO(std::vector<float>& OutDOStatus)
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

int DevelopHelper::ExternalIO::GetDO(float* OutDOStatus)
{
    if (!bValid) { return 0; }
	memcpy(OutDOStatus, DOStatus.data(), sizeof(float)*outputCount);
    return 1;
}

// Set All by raw data
int DevelopHelper::ExternalIO::SetDO(float* InDOStatus)
{
    if (!bValid) { return 0; }
	memcpy(DOStatus.data(), InDOStatus, sizeof(float)*outputCount);
	bDOChanged = true;
	return 1;
}


int DevelopHelper::ExternalIO::SetDOOn(const char* InOAction)
{
	return  this->SetDO(InOAction, 1.0f);
}


int DevelopHelper::ExternalIO::SetDOOff(const char* InOAction)
{
	return this->SetDO(InOAction,0.f);
}

int DevelopHelper::ExternalIO::DOImmediate()
{
	if (bDOChanged) {
		int _retCode = this->SetDO(DOStatus);
		bDOChanged = false;
		return _retCode;
	}
	return -1;
}

// Set do by o action
int DevelopHelper::ExternalIO::SetDO(const char* InOAction, float val, bool bIgnoreMassage/*=false*/)
{
	if (!bValid) { return 0; }
	if (!OActionMappings.count(InOAction)) { return 0; }
	/**
	 * 设置输出的原始值 用于GetDO(OAction) 取值
	 */
	const std::vector<FOutputActionKey>& _keys = OActionMappings.at(InOAction);
	if (!rawDOStatus.count(InOAction)) {
		rawDOStatus.insert(std::pair<std::string, float>(InOAction, val));
	}
	else {
		rawDOStatus[InOAction] = val;
	}

	/**
	 * 根据原始值输入计算实际输出值
	 */
	for (auto& _actionKey : _keys)
	{
		int _channel = ConvertFKeyToChannel(_actionKey.Key);
		if(!IsValidChannel(_channel,outputCount)){continue;}
		float _rawValue = val;
		if (!bIgnoreMassage) {
			if (_actionKey.InvertEvent) {
				if (_rawValue == 0.f) {
					_rawValue = 1.f;
				}
				else {
					_rawValue = 0.f;
				}
			}
			_rawValue = MassageKeyInput(_actionKey.Key, _rawValue) * _actionKey.Scale;
		}
		DOStatus[_channel] = _rawValue;
	}
	bDOChanged = true;
	return 1;
}

// Set do by single channel
int DevelopHelper::ExternalIO::SetDO(const FKey& InKey, float val)
{
	int numChannel = ConvertFKeyToChannel(InKey);
	if (!IsValidChannel(numChannel, outputCount))
	{
		return 0;
	}
	DOStatus[numChannel] = val;
	bDOChanged = true;
	return 1;
}

int DevelopHelper::ExternalIO::SetDO(std::vector<float>& InDOStatus)
{
    static short tmpDOStatus[MaxIOCount];
    for (size_t index = 0;index < InDOStatus.size();index++)
    {
        tmpDOStatus[index] = static_cast<short>(DOStatus[index]);
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
        IOLog::Instance().Error(std::string("Load external dll ")+externalDll.GetDllName()+" failed. error code:" + std::to_string(loadDllErrCode));
        bValid = false;
        return;
    }
    DeviceInfo* devInfo = externalDll.Initialize();
    inputCount = devInfo->InputCount;
    outputCount = devInfo->OutputCount;
    axisCount = devInfo->AxisCount;

    DIStatus = std::vector<BYTE>(inputCount, 0);
	DOStatus = std::vector<float>(outputCount, 0);
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