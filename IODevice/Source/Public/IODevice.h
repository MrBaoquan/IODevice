/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include "IOExportsAPI.h"
#include "ExportCoreTypes.h"

namespace DevelopHelper
{

/**
 * Device export type
 */
class IOAPI IODevice
{
public:
	/**
	 * �������غ����ݴ˽�������
	 */
	void BindKey(const FKey& key, InputEvent KeyEvent, std::function<void(void)> keyDelegate);

    template<class UserClass>
    void BindKey(const FKey& Key, InputEvent KeyEvent, UserClass* Object, void(UserClass::*Method)());

	/**
	* �������غ����ݴ˽�������
	*/
	void BindAxis(const char* axisName, std::function<void(float)> axisDelegate);

    template<class UserClass>
    void BindAxis(const char* axisName, UserClass* Object, void(UserClass::*Method)(float));

    template<class UserClass>
    void BindAxisKey(const FKey AxisKey, UserClass* Object, void(UserClass::*Method)(float));

	/**
	 * �������غ����ݴ˽�������
	 */
	void BindAction(const char* actionName, InputEvent KeyEvent, std::function<void(FKey)> actionDelegate);

	template<class... VarTypes>
	void BindAction(const char* actionName, InputEvent KeyEvent, void(* Method)(VarTypes...), VarTypes... args);

    template<class UserClass>
    void BindAction(const char* actionName, InputEvent KeyEvent, UserClass* Object, void(UserClass::*Method)());

    template<class UserClass>
    void BindAction(const char* actionName, InputEvent KeyEvent, UserClass* Object, void(UserClass::*Method)(FKey));

    template<class UserClass, class... VarTypes>
    void BindAction(const char* actionName, InputEvent KeyEvent, UserClass* Object, void(UserClass::*Method)(VarTypes...), VarTypes... args);
    
    /**
     * �����豸���״̬
     * @param InDOStatus: �豸����ͨ����ֵ
     * @return: �ɹ�����1 ʧ�ܷ���0
     */
    int SetDeviceDO(BYTE* InDOStatus);

    /**
     * �����豸�����������״̬
     * @param InKey: ��Ҫ���õİ���
     * @param InValue: Ҫ���õ�ֵ
     * @return: �ɹ�����1 ʧ�ܷ���0
     */
    int SetDeviceDO(const FKey& InKey, BYTE InValue);

    /**
     * ��ȡ�豸���״̬
     * @param OutDOStatus: �������,�豸����ͨ����ֵ
     * @return: �ɹ�����1 ʧ�ܷ���0
     */
    int GetDeviceDO(BYTE* OutDOStatus);

    /**
     * ��ȡ�豸ָ���������״̬
     * @param InKey: ��Ҫ��ȡ�İ���
     * @return: �ߵ�ƽ����1 �͵�ƽ����0
     */
    BYTE GetDeviceDO(const FKey& InKey);
  
    /**
     * ��ȡ�豸ָ������״̬
     * @param InKey: ָ��Ҫ��ѯ�İ���
     * @return: ���������·���true, ���򷵻�false
     */
    bool GetKey(const FKey& InKey);

    /**
     * ��ȡ�豸ָ������״̬
     * @param InKey: ָ��Ҫ����ѯ�İ���
     * @return: �������ͷ�״̬������ʱ����֡����true, ���򷵻�false
     */
    bool GetKeyDown(const FKey& InKey);

    /**
    * ��ȡ�豸ָ������״̬
    * @param InKey: ָ��Ҫ����ѯ�İ���
    * @return: �����ɰ���״̬���ͷ�ʱ����֡����true, ���򷵻�false
    */
    bool GetKeyUp(const FKey& InKey);

    /**
     * ��ȡ�豸ָ��Axis��ֵ
     * @param AxisName: �����ļ���Axis�ڵ������
     * @return: ���ظ�Axis�ڵ㾭��������ֵ
     */
    float GetAxis(const char* AxisName);

    /**
    * ��ȡ�豸ָ��AxisKey��ֵ
    * @param InKey: ָ��Ҫ����ѯ�İ���
    * @return: ���ظ�Axis�ڵ㾭��������ֵ
    */
    float GetAxisKey(const FKey& InKey);

    /**
     * ��ȡָ���������µĳ���ʱ��
     * @return: ����ð����������򷵻ذ��µĳ���ʱ�䣬û�б������򷵻�0.0
     */
    float GetKeyDownDuration(const FKey& InKey);

    /** ������豸�󶨵����лص����� */
    void ClearBindings();

    const bool IsValid() const;
    const uint8 GetID() const;
    const bool operator==(const IODevice& rhs);

private:
    IODevice() :deviceID(0) {}
    ~IODevice() = default;
    IODevice(uint8 InID) :deviceID(InID) {}

    void BindAxisKey(const FKey& key, std::function<void(float)> axisDelegate);
    void BindAction(const char* actionName, InputEvent KeyEvent, std::function<void(void)> actionDelegate);

    uint8 deviceID;

    friend class IODevices;
    friend struct IODeviceDetails;
};

};

#include "IODeviceImpl.hpp"