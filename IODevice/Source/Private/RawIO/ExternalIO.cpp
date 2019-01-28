/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#include "RawIO/ExternalIO.h"
#include <filesystem>
#include "PlayerInput.h"
#include "CoreTypes/IOTypes.h"
#include "IOLog.h"

DevelopHelper::ExternalIO::ExternalIO(uint8 InID, uint8 InDeviceIndex, std::string InFullDllName):
                                 CustomIOBase(InID,InDeviceIndex,IOType::External)
                                ,externalDll(InFullDllName.data())
                                ,bValid(false)
{
    Initialize();
}

void DevelopHelper::ExternalIO::Tick(float DeltaSeconds)
{
    if (!bValid) { return; }

    if (inputCount > 0)
    {
        if(GetDeviceDI(DIStatus))
        {
            DispatchButtonEvent(DIStatus, channelsState);
        }
    }
    
 
    if (axisCount > 0)
    {
        if (GetDeviceAD(ADStatus)) 
        {
            DispatchAxisEvent(ADStatus);
        }
    }

    if (outputCount > 0)
    {
        GetDeviceDO(DOStatus);
    }
    

}

void DevelopHelper::ExternalIO::OnFrameEnd()
{
    if (bExeDoAtFrameEnd)
    {
        SetDeviceDO(DOStatus);
        bExeDoAtFrameEnd = false;
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

BYTE DevelopHelper::ExternalIO::GetDeviceDO(const FKey InKey)
{
    int numChannel = ConvertFKeyToChannel(InKey);
    if (!IsValidChannel(numChannel,outputCount))
    {
        return 0;
    }
    return DOStatus[numChannel];
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

int DevelopHelper::ExternalIO::GetDeviceDO(std::vector<BYTE>& OutDOStatus)
{
    static BYTE exDoStatus[MaxIOCount];
    int retCode = externalDll.GetDeviceDO(deviceIndex, exDoStatus);
    for (size_t index = 0;index < OutDOStatus.size();++index)
    {
        OutDOStatus[index] = exDoStatus[index];
    }
    return retCode;
}

int DevelopHelper::ExternalIO::GetDeviceDO(BYTE* OutDOStatus)
{
    if (!bValid) { return 0; }
    for (int index = 0;index < outputCount;index++)
    {
        OutDOStatus[index] = DOStatus[index];
    }
    return 1;
}

// Set Device Output
int DevelopHelper::ExternalIO::SetDeviceDO(BYTE* InDOStatus)
{
    if (!bValid) { return 0; }
    return externalDll.SetDeviceDO(deviceIndex, InDOStatus);
}

int DevelopHelper::ExternalIO::SetDeviceDO(std::vector<BYTE>& InDOStatus)
{
    static BYTE tmpDOStatus[MaxIOCount];
    for (size_t index = 0;index < InDOStatus.size();index++)
    {
        tmpDOStatus[index] = DOStatus[index];
    }
    return externalDll.SetDeviceDO(deviceIndex, tmpDOStatus);
}


int DevelopHelper::ExternalIO::SetDeviceDO(const FKey InKey, BYTE val)
{
    int numChannel = ConvertFKeyToChannel(InKey);
    if (!IsValidChannel(numChannel, outputCount))
    {
        return 0;
    }
    DOStatus[numChannel] = val;
    bExeDoAtFrameEnd = true;
    return 1;
}

DevelopHelper::ExternalIO::~ExternalIO()
{
    
}

void DevelopHelper::ExternalIO::Initialize()
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
    DOStatus = std::vector<BYTE>(outputCount, 0);
    ADStatus = std::vector<short>(axisCount, 0);

    channelsState = std::vector<ButtonState>(inputCount);
    CustomIOBase::Initlialize();

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