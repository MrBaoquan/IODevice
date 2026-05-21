/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include <vector>
#include <mutex>
#include "CustomIOBase.h"
#include "PlayerInput.h"
#include "PDLL.h"
#include "LessKey.h"


struct __declspec(dllimport) DeviceInfo
{
    /** Digital input channel count */
    BYTE InputCount = 16;
    /** Digital output channel count */
    BYTE OutputCount = 16;
    /** Analog input channel count */
    BYTE AxisCount = 0;
};

namespace IOToolkit
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
    DECLARE_FUNCTION2(int, GetDeviceAD, uint8, short*)          // V1: Legacy interface (short)
    DECLARE_FUNCTION2(int, GetDeviceAD_INT, uint8, int32_t*)    // V2: New interface (int32_t)
    DECLARE_FUNCTION3(int, RefreshStreamingData, uint8, BYTE*, unsigned int)

    // —— 通用插件通道 —— //
    // 宿主 → 插件 下行写入
    DECLARE_FUNCTION4(int, WritePluginChannel, uint8, const char*, const BYTE*, unsigned int)
    // 注册派发回调 (插件 → 宿主 上行)
    // fn 签名: void(__stdcall*)(uint8 devIdx, const char* channel, const BYTE* data, unsigned int size, void* user)
    DECLARE_FUNCTION3(int, SetPluginChannelDispatcher, uint8, void*, void*)

public:
    // Check if new version GetDeviceAD_INT is supported
    bool HasGetDeviceAD_INT()
    {
        if (0 == m_isGetDeviceAD_INT && m_dllHandle)
        {
            m_GetDeviceAD_INT = (TYPE_GetDeviceAD_INT)GetProcAddress(m_dllHandle, "GetDeviceAD_INT");
            m_isGetDeviceAD_INT = (m_GetDeviceAD_INT != NULL) ? FUNC_LOADED : -1;
        }
        return m_isGetDeviceAD_INT == FUNC_LOADED && m_GetDeviceAD_INT != NULL;
    }
};

class ExternalIO : public CustomIOBase
{
public:
    ExternalIO(uint8 InID, uint8 InDeviceIndex, std::string InFullDllName);

    virtual void Tick(float DeltaSeconds) override;

    virtual void OnFrameEnd() override;

	virtual void Destroy()override;

    virtual const bool Valid() const override { return bValid; }

    virtual int SetDO(float* InDOStatus) override;
    virtual int SetDO(const FKey& InKey, float val) override;
	virtual int SetDO(const char* InOAction, float val, bool bIgnoreMassage=false);
	virtual int SetDOOn(const char* InOAction) override;
	virtual int SetDOOff(const char* InOAction) override;
	virtual int DOImmediate() override;

	/**
	 * 设置 OAction Key 的属性 (Scale, InvertEvent)
	 */
	virtual int SetOKProps(const char* oactionName, const char* keyName, float scale, bool invertEvent) override;

    virtual int GetDO(float* OutDOStatus) override;
    virtual float GetDO(const FKey InKey) override;
	virtual float GetDO(const char* InOAction) override;

    virtual int RefreshStreamingData(BYTE* StreamingData, unsigned int DataSize) override;

    // —— 通用插件通道 —— //
    virtual int WritePluginChannel(const char* channelName, const BYTE* data, unsigned int size) override;
    virtual void EnsurePluginChannelDispatcher(void* details) override;
    
	virtual void Initialize() override;
    virtual ~ExternalIO() override;

private:
	void Constructor();
    bool IsValidChannel(int InChannel, int InMaxNumber);

    int SetDO(std::vector<float>& InDOStatus);
    int GetDO(std::vector<float>& OutDOStatus);

    int GetDeviceDI(std::vector<BYTE>& OutDIStatus);

    int GetDeviceAD(std::vector<short>& OutADStatus);      // V1: Legacy implementation
    int GetDeviceAD(std::vector<int32_t>& OutADStatus);    // V2: New implementation

    int ConvertFKeyToChannel(const FKey& InKey);
private:
    bool bValid;
    bool bDOChanged = false;
    mutable std::mutex tickMutex;  // Protects concurrent access between Tick() and Destroy()
    IOUIDLL externalDll;
    std::vector<float> DOStatus;
	/**
	 *	Used to store raw output values not set by user
	 */
	std::map<std::string, float> rawDOStatus;
    std::vector<short> ADStatus;        // V1: Legacy version uses this
    std::vector<int32_t> ADStatusInt;   // V2: New version uses this
	std::map<std::string, std::vector<FOutputActionKey>> OActionMappings;

	// 插件通道派发器：仅注册一次, 指向 IODeviceDetails*
	void* pluginChannelDetails = nullptr;
};

};