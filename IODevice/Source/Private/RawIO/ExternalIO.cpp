/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#include "RawIO/ExternalIO.h"
#include <filesystem>
#include "InputSettings.h"
#include "CoreTypes/IOTypes.h"
#include "IOLog.h"

IOToolkit::ExternalIO::ExternalIO(uint8 InID, uint8 InDeviceIndex, std::string InFullDllName):
                                 CustomIOBase(InID,InDeviceIndex,IOType::External)
                                ,externalDll(InFullDllName.data())
                                ,bValid(false)
{
	Constructor();
}

void IOToolkit::ExternalIO::Tick(float DeltaSeconds)
{
    if (!bValid) { return; }

	// Dispatch button input events
    if (inputCount > 0)
    {
        if(GetDeviceDI(DIStatus))
        {
            DispatchButtonEvent(DIStatus, channelsState);
        }
    }
    
	// Dispatch axis input events
    if (axisCount > 0)
    {
        if (externalDll.HasGetDeviceAD_INT())
        {
            // V2: Use new int32_t interface
            if (GetDeviceAD(ADStatusInt)) 
            {
                DispatchAxisEvent(ADStatusInt);
            }
        }
        else
        {
            // V1: Use legacy short interface
            if (GetDeviceAD(ADStatus)) 
            {
                DispatchAxisEvent(ADStatus);
            }
        }
    }

	// Get input channel values
    if (outputCount > 0)
    {
		this->DOImmediate();
        GetDO(DOStatus);
    }
}

/**
 * Note: Only for core main logic, not for user/business layer frame end
 */
void IOToolkit::ExternalIO::OnFrameEnd()
{
   
}

void IOToolkit::ExternalIO::Destroy()
{
	if (bValid)
	{
		externalDll.CloseDevice(deviceIndex);
	}
}

int IOToolkit::ExternalIO::ConvertFKeyToChannel(const FKey& InKey)
{
    std::string keyName = InKey.GetName();
    size_t pos = keyName.find_first_of('_');
    std::string num_str = keyName.substr(pos + 1, keyName.size() - pos - 1);
    return std::atoi(num_str.data());
}

float IOToolkit::ExternalIO::GetDO(const FKey InKey)
{
	if (!bValid) { return 0; }
    int numChannel = ConvertFKeyToChannel(InKey);
    if (!IsValidChannel(numChannel,outputCount))
    {
        return 0;
    }
    return DOStatus[numChannel];
}


int IOToolkit::ExternalIO::RefreshStreamingData(BYTE* StreamingData, unsigned int DataSize)
{
	if (!bValid) { return 0; }
	return externalDll.RefreshStreamingData(deviceIndex, StreamingData, DataSize);
}

float IOToolkit::ExternalIO::GetDO(const char* InOAction)
{
	if (!bValid) { return 0; }
	if (!OActionMappings.count(InOAction)) { return 0; }
	
	if (rawDOStatus.count(InOAction)) {
		return rawDOStatus.at(InOAction);
	}
	return 0;
}


void IOToolkit::ExternalIO::Initialize()
{
	__super::Initialize();
	OActionMappings = UInputSettings::Instance().OActionMappings[deviceID];
}

int IOToolkit::ExternalIO::GetDeviceDI(std::vector<BYTE>& OutDIStatus)
{
    static BYTE exDIStatus[MaxIOCount];
	std::copy(OutDIStatus.begin(), OutDIStatus.end(), exDIStatus);

    int retCode = externalDll.GetDeviceDI(deviceIndex, exDIStatus);
    for (size_t index = 0;index < OutDIStatus.size();index++)
    {
        OutDIStatus[index] = exDIStatus[index];
    }
    return retCode;
}

int IOToolkit::ExternalIO::GetDeviceAD(std::vector<short>& OutADStatus)
{
    static short exADStatus[MaxIOCount];
	std::copy(OutADStatus.begin(), OutADStatus.end(), exADStatus);

    int retCode = externalDll.GetDeviceAD(deviceIndex, exADStatus);
    for (size_t index = 0;index < OutADStatus.size();index++) 
    {
        OutADStatus[index] = exADStatus[index];
    }
    return retCode;
}

int IOToolkit::ExternalIO::GetDeviceAD(std::vector<int32_t>& OutADStatus)
{
    static int32_t exADStatus[MaxIOCount];
	std::copy(OutADStatus.begin(), OutADStatus.end(), exADStatus);

    int retCode = externalDll.GetDeviceAD_INT(deviceIndex, exADStatus);
    for (size_t index = 0;index < OutADStatus.size();index++) 
    {
        OutADStatus[index] = exADStatus[index];
    }
    return retCode;
}

int IOToolkit::ExternalIO::GetDO(std::vector<float>& OutDOStatus)
{
    static short exDoStatus[MaxIOCount];
	std::copy(OutDOStatus.begin(), OutDOStatus.end(), exDoStatus);

    int retCode = externalDll.GetDeviceDO(deviceIndex, exDoStatus);
	if (retCode == 1) {
		for (size_t index = 0; index < OutDOStatus.size(); ++index)
		{
			OutDOStatus[index] = exDoStatus[index];
		}
	}
    return retCode;
}

int IOToolkit::ExternalIO::GetDO(float* OutDOStatus)
{
    if (!bValid) { return 0; }
	memcpy(OutDOStatus, DOStatus.data(), sizeof(float)*outputCount);
    return 1;
}

// Set All by raw data
int IOToolkit::ExternalIO::SetDO(float* InDOStatus)
{
    if (!bValid) { return 0; }
	memcpy(DOStatus.data(), InDOStatus, sizeof(float)*outputCount);
	bDOChanged = true;
	return 1;
}


int IOToolkit::ExternalIO::SetDOOn(const char* InOAction)
{
	return  this->SetDO(InOAction, 1.0f);
}


int IOToolkit::ExternalIO::SetDOOff(const char* InOAction)
{
	return this->SetDO(InOAction,0.f);
}

int IOToolkit::ExternalIO::DOImmediate()
{
	if (bDOChanged) {
		int _retCode = this->SetDO(DOStatus);
		bDOChanged = false;
		return _retCode;
	}
	return -1;
}

// Set do by o action
int IOToolkit::ExternalIO::SetDO(const char* InOAction, float val, bool bIgnoreMassage/*=false*/)
{
	if (!bValid) { return 0; }
	if (!OActionMappings.count(InOAction)) { return 0; }
	/**
	 * Set the raw output value for GetDO(OAction) retrieval
	 */
	const std::vector<FOutputActionKey>& _keys = OActionMappings.at(InOAction);
	if (!rawDOStatus.count(InOAction)) {
		rawDOStatus.insert(std::pair<std::string, float>(InOAction, val));
	}
	else {
		rawDOStatus[InOAction] = val;
	}

	/**
	 * Calculate actual output value based on raw input
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
int IOToolkit::ExternalIO::SetDO(const FKey& InKey, float val)
{
	if (!bValid) return 0;
	int numChannel = ConvertFKeyToChannel(InKey);
	if (!IsValidChannel(numChannel, outputCount))
	{
		return 0;
	}
	DOStatus[numChannel] = val;
	bDOChanged = true;
	return 1;
}

int IOToolkit::ExternalIO::SetDO(std::vector<float>& InDOStatus)
{
	if (!bValid) return 0;
    static short tmpDOStatus[MaxIOCount];
    for (size_t index = 0;index < InDOStatus.size();index++)
    {
        tmpDOStatus[index] = static_cast<short>(DOStatus[index]);
    }
    return externalDll.SetDeviceDO(deviceIndex, tmpDOStatus);
}

IOToolkit::ExternalIO::~ExternalIO()
{
    
}

void IOToolkit::ExternalIO::Constructor()
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
    ADStatusInt = std::vector<int32_t>(axisCount, 0);

    channelsState = std::vector<ButtonState>(inputCount);
	// Initialize base class after basic variables are initialized
	__super::Constructor();
    bValid = externalDll.OpenDevice(deviceIndex)==1;
}

bool IOToolkit::ExternalIO::IsValidChannel(int InChannel, int InMaxNumber)
{
    if (InChannel < 0 || InChannel >= InMaxNumber)
    {
        return false;
    }
    return true;
}