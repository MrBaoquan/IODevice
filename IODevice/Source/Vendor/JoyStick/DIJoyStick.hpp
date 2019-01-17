/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#include <windows.h>
#include <stdio.h>
#define DIRECTINPUT_VERSION 0x0800
#include <dinput.h>
#pragma comment(lib, "dinput8.lib")
#pragma comment(lib, "dxguid.lib")


class DIJoyStick {
public:
	struct Entry {
		DIDEVICEINSTANCE        diDeviceInstance;
		DIDEVCAPS               diDevCaps;
		LPDIRECTINPUTDEVICE8    diDevice;
		DIJOYSTATE2             joystate;
        void Update()
        {
            LPDIRECTINPUTDEVICE8 d = diDevice;

            if (FAILED(d->Poll())) {
                HRESULT hr = d->Acquire();
                while (hr == DIERR_INPUTLOST) {
                    hr = d->Acquire();
                }
            }
            else {
                d->GetDeviceState(sizeof(DIJOYSTATE2), &joystate);
            }
        }
	};

	DIJoyStick() : entry(0)
        , maxEntry(0)
        , nEntry(0)
        , di(0)
        ,maxVal(1000){}

	~DIJoyStick() { clear(); }

	void clear() 
    {
		if (entry) {
			delete[] entry;
			entry = 0;
		}
		maxEntry = 0;
		nEntry = 0;
		di = 0;
	}

	void enumerate(LPDIRECTINPUT di, DWORD dwDevType = DI8DEVTYPE_JOYSTICK, LPCDIDATAFORMAT _lpdf = &c_dfDIJoystick2, DWORD dwFlags = DIEDFL_ATTACHEDONLY, int maxEntry = 16) 
    {
		clear();

		entry = new Entry[maxEntry];
		this->di = di;
		this->maxEntry = maxEntry;
		nEntry = 0;
		this->lpdf = _lpdf;

		di->EnumDevices(dwDevType, DIEnumDevicesCallback_static, this, dwFlags);
		this->di = 0;
	}

	int getEntryCount() const {
		return nEntry;
	}
    short getMaxValue()const { return maxVal; }
	Entry* getEntry(int index) const 
    {
		Entry* e = 0;
		if (index >= 0 && index < nEntry) {
			e = &entry[index];
		}
		return e;
	}

    void Update() {
        for (int iEntry = 0; iEntry < nEntry; ++iEntry) {
            Entry& e = entry[iEntry];
            LPDIRECTINPUTDEVICE8 d = e.diDevice;

            if (FAILED(d->Poll())) {
                HRESULT hr = d->Acquire();
                while (hr == DIERR_INPUTLOST) {
                    hr = d->Acquire();
                }
            }
            else {
                d->GetDeviceState(sizeof(DIJOYSTATE2), &e.joystate);
            }
        }
    }

protected:
	static BOOL CALLBACK DIEnumDevicesCallback_static(LPCDIDEVICEINSTANCE lpddi, LPVOID pvRef) 
    {
		return reinterpret_cast<DIJoyStick*>(pvRef)->DIEnumDevicesCallback(lpddi, pvRef);
	}
    static BOOL CALLBACK DIEnumObjectsCallback_static(const DIDEVICEOBJECTINSTANCE* pdidoi, LPVOID pvRef)
    {
        return reinterpret_cast<DIJoyStick*>(pvRef)->DIEnumObjectsCallback(pdidoi, pvRef);
    }

    BOOL DIEnumObjectsCallback(const DIDEVICEOBJECTINSTANCE* pdidoi, LPVOID pvRef)
    {
        if (pdidoi->dwType & DIDFT_AXIS)
        {
            DIJoyStick* diJoystick = reinterpret_cast<DIJoyStick*>(pvRef);
            DIPROPRANGE diprg;
            diprg.diph.dwSize = sizeof(DIPROPRANGE);
            diprg.diph.dwHeaderSize = sizeof(DIPROPHEADER);
            diprg.diph.dwHow = DIPH_BYID;
            diprg.diph.dwObj = pdidoi->dwType; // Specify the enumerated axis
            diprg.lMin = -diJoystick->getMaxValue();
            diprg.lMax = +diJoystick->getMaxValue();
            Entry& curEntry = entry[nEntry - 1];
            // Set the range for the axis
            if (FAILED(curEntry.diDevice->SetProperty(DIPROP_RANGE, &diprg.diph)))
                return DIENUM_STOP;
        }
        return DIENUM_CONTINUE;
    }

	BOOL DIEnumDevicesCallback(LPCDIDEVICEINSTANCE lpddi, LPVOID pvRef) {
		if (nEntry < maxEntry) {
			Entry e = { 0 };
            
			memcpy(&e.diDeviceInstance, lpddi, sizeof(e.diDeviceInstance));
			e.diDevCaps.dwSize = sizeof(e.diDevCaps);

			LPDIRECTINPUTDEVICE8    did = 0;

			if (SUCCEEDED(di->CreateDevice(lpddi->guidInstance, (LPDIRECTINPUTDEVICE*)&did, 0))) 
            {
				if (SUCCEEDED(did->SetDataFormat(lpdf))) {
					if (SUCCEEDED(did->GetCapabilities(&e.diDevCaps))) 
                    {
						e.diDevice = did;
						entry[nEntry++] = e;
                        did->EnumObjects(DIEnumObjectsCallback_static, (void*)this, DIDFT_ALL);
					}
				}
			}
		}
		return DIENUM_CONTINUE;
	}

	//
	Entry*          entry;
	int             maxEntry;
	int             nEntry;
	LPDIRECTINPUT   di;
	LPCDIDATAFORMAT lpdf;
    short           maxVal;
};
