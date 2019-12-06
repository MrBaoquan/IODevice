/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include <vector>
#include "CustomIOBase.h"
#include "PlayerInput.h"
#include "PDLL.h"
#include "LessKey.h"


struct __declspec(dllimport) DeviceInfo
{
    /** 输入通道数量 */
    BYTE InputCount = 16;
    /** 输出通道数量 */
    BYTE OutputCount = 16;
    /** 模拟量输入通道数量 */
    BYTE AxisCount = 0;
};

namespace DevelopHelper
{

class IOUIDLL :public PDLL
{
    DECLARE_CLASS(IOUIDLL)

    DECLARE_FUNCTION0(DeviceInfo*,Initialize)
    DECLARE_FUNCTION1(int, OpenDevice, uint8)
    DECLARE_FUNCTION1(int, CloseDevice, uint8)
    DECLARE_FUNCTION2(int, SetDeviceDO, uint8, short*)
    DECLARE_FUNCTION2(int, GetDeviceDO, uint8, short*)
    DECLARE_FUNCTION2(int, GetDeviceDI, uint8, BYTE*)
    DECLARE_FUNCTION2(int, GetDeviceAD, uint8, short*)
};

class ExternalIO : public CustomIOBase
{
public:
    ExternalIO(uint8 InID, uint8 InDeviceIndex, std::string InFullDllName);

    virtual void Tick(float DeltaSeconds) override;

    virtual void OnFrameEnd() override;

	virtual void Destroy()override;

    virtual const bool Valid() const override { return bValid; }

    virtual int SetDO(short* InDOStatus) override;
    virtual int SetDO(const FKey InKey, short val) override;
	virtual int SetDO(const char* InOAction, short val) override;
	virtual int SetDOOn(const char* InOAction) override;
	virtual int SetDOOff(const char* InOAction) override;

    virtual int GetDO(short* OutDOStatus) override;
    virtual short GetDO(const FKey InKey) override;
	virtual void Initialize() override;
    virtual ~ExternalIO() override;

private:
	void Constructor();
    bool IsValidChannel(int InChannel, int InMaxNumber);

    int SetDO(std::vector<short>& InDOStatus);
    int GetDO(std::vector<short>& OutDOStatus);

    int GetDeviceDI(std::vector<BYTE>& OutDIStatus);

    int GetDeviceAD(std::vector<short>& OutADStatus);

    int ConvertFKeyToChannel(const FKey& InKey);
private:
    bool bValid;
    bool bDoDOAtFrameEnd = false;
    IOUIDLL externalDll;
    std::vector<short> DOStatus;
    std::vector<short> ADStatus;
	std::map<std::string, std::vector<FOutputActionKey>> OActionMappings;
};

};