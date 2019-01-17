#include "IOStatics.h"
#include <memory>
#include "RawIOFactory.h"
#include "InputSettings.h"
#include "IOLog.h"

void DevelopHelper::IODevices::AddDevice(IODeviceDetails& deviceDetails)
{
    if (HasDevice(deviceDetails.getName())) 
    {
        std::string msg = std::string("There is already a devcie named <") + deviceDetails.getName() + ">, Create device failed.";
        IOLog::Instance().Error(msg);
        return; 
    }
    devices.insert(std::pair<std::string,IODeviceDetails>(deviceDetails.getName(), deviceDetails));
    std::string msg = std::string("Create device <") + deviceDetails.getName() + "> succeed.";
    IOLog::Instance().Log(msg);
}

DevelopHelper::IODevice& DevelopHelper::IODevices::GetDevice(const std::string deviceName)
{
    if (HasDevice(deviceName))
    {
        return devices[deviceName].GetDevice();
    }else
    {
        return Invalid.GetDevice();
    }
}

DevelopHelper::IODeviceDetails& DevelopHelper::IODevices::GetDeviceDetail(const std::string deviceName)
{
    if (HasDevice(deviceName))
    {
        return devices[deviceName];
    }else
    {
        return Invalid;
    }
}

DevelopHelper::IODeviceDetails& DevelopHelper::IODevices::GetDeviceDetail(uint8 deviceID)
{
    for (auto&deviceIt : devices)
    {
        IODeviceDetails& deviceDetails = deviceIt.second;
        if (deviceDetails.GetDevice().GetID() == deviceID)
        {
            return deviceDetails;
        }
    }
    return Invalid;
}

DevelopHelper::IODeviceDetails& DevelopHelper::IODevices::GetDeviceDetail(const IODevice& InDevice)
{
    for (auto&deviceIt : devices)
    {
        IODeviceDetails& deviceDetails = deviceIt.second;
        if (deviceDetails.GetDevice()==InDevice)
        {
            return deviceDetails;
        }
    }
    return Invalid;
}

const DevelopHelper::uint8 DevelopHelper::IODevices::GetDevicesCount()
{
    return static_cast<uint8>(IODevices::devices.size());
}

const DevelopHelper::uint8 DevelopHelper::IODevices::GetDevicesCount(std::string InType)
{
    uint8 count = 0;
    for (auto& device:devices)
    {
        if (device.second.getIOType() == InType)
        {
            count++;
        }
    }
    return count;

}

int DevelopHelper::IODevices::Initialize()
{
    return UInputSettings::Instance().Initialize();
}

bool DevelopHelper::IODevices::HasDevice(const std::string deviceName)
{
    if (devices.count(deviceName))
    {
        return true;
    }
    return false;
}

std::map<std::string, DevelopHelper::IODeviceDetails>& DevelopHelper::IODevices::GetDevcies()
{
    return devices;
}

std::map<std::string, DevelopHelper::IODeviceDetails> DevelopHelper::IODevices::devices;

DevelopHelper::IODeviceDetails DevelopHelper::IODevices::Invalid("Invalid", nullptr);

std::shared_ptr<DevelopHelper::FKeyDetails> DevelopHelper::StaticKeys::GetKeyDetails(const FKey& key)
{
    std::shared_ptr<FKeyDetails>* keyDetails=nullptr;
    if (InputKeys.count(key))
    {
        return InputKeys.at(key);
    }

    return nullptr;
}


std::map<DevelopHelper::FKey, std::shared_ptr<DevelopHelper::FKeyDetails>, DevelopHelper::LessKey> DevelopHelper::StaticKeys::InputKeys;

void DevelopHelper::StaticKeys::Initialize()
{
    AddKey(FKeyDetails(EKeys::AnyKey, "AnyKey"));

    AddKey(FKeyDetails(EKeys::MouseX, "MouseX", FKeyDetails::FloatAxis | FKeyDetails::MouseButton | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::MouseY, "MouseY", FKeyDetails::FloatAxis | FKeyDetails::MouseButton | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::MouseWheelAxis, "MouseWheelAxis", FKeyDetails::FloatAxis | FKeyDetails::MouseButton | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::MouseScrollUp, "MouseScrollUp", FKeyDetails::MouseButton));
    AddKey(FKeyDetails(EKeys::MouseScrollDown, "MouseScrollDown", FKeyDetails::MouseButton));

    AddKey(FKeyDetails(EKeys::LeftMouseButton, "LeftMouseButton", FKeyDetails::MouseButton));
    AddKey(FKeyDetails(EKeys::RightMouseButton, "RightMouseButton", FKeyDetails::MouseButton));
    AddKey(FKeyDetails(EKeys::MiddleMouseButton, "MiddleMouseButton", FKeyDetails::MouseButton));
    AddKey(FKeyDetails(EKeys::ThumbMouseButton, "ThumbMouseButton", FKeyDetails::MouseButton));
    AddKey(FKeyDetails(EKeys::ThumbMouseButton2, "ThumbMouseButton2", FKeyDetails::MouseButton));

    AddKey(FKeyDetails(EKeys::Tab, "Tab"));
    AddKey(FKeyDetails(EKeys::Enter, "Enter"));
    AddKey(FKeyDetails(EKeys::Pause, "Pause"));

    AddKey(FKeyDetails(EKeys::CapsLock, "CapsLock"));
    AddKey(FKeyDetails(EKeys::Escape, "Escape"));
    AddKey(FKeyDetails(EKeys::SpaceBar, "SpaceBar"));
    AddKey(FKeyDetails(EKeys::PageUp, "PageUp"));
    AddKey(FKeyDetails(EKeys::PageDown, "PageDown"));
    AddKey(FKeyDetails(EKeys::End, "End"));
    AddKey(FKeyDetails(EKeys::Home, "Home"));

    AddKey(FKeyDetails(EKeys::Left, "Left"));
    AddKey(FKeyDetails(EKeys::Up, "Up"));
    AddKey(FKeyDetails(EKeys::Right, "Right"));
    AddKey(FKeyDetails(EKeys::Down, "Down"));

    AddKey(FKeyDetails(EKeys::Insert, "Insert"));

    AddKey(FKeyDetails(EKeys::Zero, "0"));
    AddKey(FKeyDetails(EKeys::One, "1"));
    AddKey(FKeyDetails(EKeys::Two, "2"));
    AddKey(FKeyDetails(EKeys::Three, "3"));
    AddKey(FKeyDetails(EKeys::Four, "4"));
    AddKey(FKeyDetails(EKeys::Five, "5"));
    AddKey(FKeyDetails(EKeys::Six, "6"));
    AddKey(FKeyDetails(EKeys::Seven, "7"));
    AddKey(FKeyDetails(EKeys::Eight, "8"));
    AddKey(FKeyDetails(EKeys::Nine, "9"));

    AddKey(FKeyDetails(EKeys::A, "A"));
    AddKey(FKeyDetails(EKeys::B, "B"));
    AddKey(FKeyDetails(EKeys::C, "C"));
    AddKey(FKeyDetails(EKeys::D, "D"));
    AddKey(FKeyDetails(EKeys::E, "E"));
    AddKey(FKeyDetails(EKeys::F, "F"));
    AddKey(FKeyDetails(EKeys::G, "G"));
    AddKey(FKeyDetails(EKeys::H, "H"));
    AddKey(FKeyDetails(EKeys::I, "I"));
    AddKey(FKeyDetails(EKeys::J, "J"));
    AddKey(FKeyDetails(EKeys::K, "K"));
    AddKey(FKeyDetails(EKeys::L, "L"));
    AddKey(FKeyDetails(EKeys::M, "M"));
    AddKey(FKeyDetails(EKeys::N, "N"));
    AddKey(FKeyDetails(EKeys::O, "O"));
    AddKey(FKeyDetails(EKeys::P, "P"));
    AddKey(FKeyDetails(EKeys::Q, "Q"));
    AddKey(FKeyDetails(EKeys::R, "R"));
    AddKey(FKeyDetails(EKeys::S, "S"));
    AddKey(FKeyDetails(EKeys::T, "T"));
    AddKey(FKeyDetails(EKeys::U, "U"));
    AddKey(FKeyDetails(EKeys::V, "V"));
    AddKey(FKeyDetails(EKeys::W, "W"));
    AddKey(FKeyDetails(EKeys::X, "X"));
    AddKey(FKeyDetails(EKeys::Y, "Y"));
    AddKey(FKeyDetails(EKeys::Z, "Z"));


    AddKey(FKeyDetails(EKeys::Button_00, "Button_00"));
    AddKey(FKeyDetails(EKeys::Button_01, "Button_01"));
    AddKey(FKeyDetails(EKeys::Button_02, "Button_02"));
    AddKey(FKeyDetails(EKeys::Button_03, "Button_03"));
    AddKey(FKeyDetails(EKeys::Button_04, "Button_04"));
    AddKey(FKeyDetails(EKeys::Button_05, "Button_05"));
    AddKey(FKeyDetails(EKeys::Button_06, "Button_06"));
    AddKey(FKeyDetails(EKeys::Button_07, "Button_07"));
    AddKey(FKeyDetails(EKeys::Button_08, "Button_08"));
    AddKey(FKeyDetails(EKeys::Button_09, "Button_09"));
    AddKey(FKeyDetails(EKeys::Button_10, "Button_10"));
    AddKey(FKeyDetails(EKeys::Button_11, "Button_11"));
    AddKey(FKeyDetails(EKeys::Button_12, "Button_12"));
    AddKey(FKeyDetails(EKeys::Button_13, "Button_13"));
    AddKey(FKeyDetails(EKeys::Button_14, "Button_14"));
    AddKey(FKeyDetails(EKeys::Button_15, "Button_15"));
    AddKey(FKeyDetails(EKeys::Button_16, "Button_16"));
    AddKey(FKeyDetails(EKeys::Button_17, "Button_17"));
    AddKey(FKeyDetails(EKeys::Button_18, "Button_18"));
    AddKey(FKeyDetails(EKeys::Button_19, "Button_19"));
    AddKey(FKeyDetails(EKeys::Button_20, "Button_20"));
    AddKey(FKeyDetails(EKeys::Button_21, "Button_21"));
    AddKey(FKeyDetails(EKeys::Button_22, "Button_22"));
    AddKey(FKeyDetails(EKeys::Button_23, "Button_23"));
    AddKey(FKeyDetails(EKeys::Button_24, "Button_24"));
    AddKey(FKeyDetails(EKeys::Button_25, "Button_25"));
    AddKey(FKeyDetails(EKeys::Button_26, "Button_26"));
    AddKey(FKeyDetails(EKeys::Button_27, "Button_27"));
    AddKey(FKeyDetails(EKeys::Button_28, "Button_28"));
    AddKey(FKeyDetails(EKeys::Button_29, "Button_29"));
    AddKey(FKeyDetails(EKeys::Button_30, "Button_30"));
    AddKey(FKeyDetails(EKeys::Button_31, "Button_31"));

    AddKey(FKeyDetails(EKeys::Axis_00, "Axis_00", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_01, "Axis_01", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_02, "Axis_02", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_03, "Axis_03", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_04, "Axis_04", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_05, "Axis_05", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_06, "Axis_06", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_07, "Axis_07", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_08, "Axis_08", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_09, "Axis_09", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_10, "Axis_10", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_11, "Axis_11", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_12, "Axis_12", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_13, "Axis_13", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_14, "Axis_14", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_15, "Axis_15", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_16, "Axis_16", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_17, "Axis_17", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_18, "Axis_18", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_19, "Axis_19", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_20, "Axis_20", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_21, "Axis_21", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_22, "Axis_22", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_23, "Axis_23", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_24, "Axis_24", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_25, "Axis_25", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_26, "Axis_26", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_27, "Axis_27", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_28, "Axis_28", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_29, "Axis_29", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_30, "Axis_30", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::Axis_31, "Axis_31", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));

    /** Joystick axes */

    AddKey(FKeyDetails(EKeys::JS_X,"JS_X",FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_Y,"JS_Y",FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_Z,"JS_Z",FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_Rx,"JS_Rx",FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_Ry,"JS_Ry",FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_Rz,"JS_Rz",FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_VX,"JS_VX",FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_VY,"JS_VY",FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_VZ,"JS_VZ",FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_VRx,"JS_VRx",FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_VRy,"JS_VRy",FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_VRz,"JS_VRz",FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_AX,"JS_AX",FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_AY,"JS_AY",FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_AZ,"JS_AZ",FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_ARx,"JS_ARx",FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_ARy,"JS_ARy",FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_ARz,"JS_ARz",FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_FX,"JS_FX",FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_FY,"JS_FY",FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_FZ,"JS_FZ",FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_FRx,"JS_FRx",FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_FRy,"JS_FRy",FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_FRz,"JS_FRz",FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));

    AddKey(FKeyDetails(EKeys::JS_Slider_00, "JS_Slider_00", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_Slider_01, "JS_Slider_01", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));

    AddKey(FKeyDetails(EKeys::JS_VSlider_00, "JS_VSlider_00", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_VSlider_01, "JS_VSlider_01", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));

    AddKey(FKeyDetails(EKeys::JS_ASlider_00, "JS_ASlider_00", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_ASlider_01, "JS_ASlider_01", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));

    AddKey(FKeyDetails(EKeys::JS_FSlider_00, "JS_FSlider_00", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_FSlider_01, "JS_FSlider_01", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));

    AddKey(FKeyDetails(EKeys::JS_POV_00, "JS_POV_00", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_POV_01, "JS_POV_01", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_POV_02, "JS_POV_02", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
    AddKey(FKeyDetails(EKeys::JS_POV_03, "JS_POV_03", FKeyDetails::FloatAxis | FKeyDetails::UpdateAxisWithoutSamples));
}

void DevelopHelper::StaticKeys::AddKey(const FKeyDetails& keyDetails)
{
    const FKey& key = keyDetails.GetKey();
    std::shared_ptr<FKeyDetails> sharedKeyDetails = std::make_shared<FKeyDetails>(keyDetails);
    InputKeys.insert(std::pair<FKey, std::shared_ptr<FKeyDetails>>(key, sharedKeyDetails));
}

bool DevelopHelper::StaticKeys::ValidKey(const FKey& key)
{
    if (InputKeys.count(key)
        ||IsExternalKey(key)
        ||IsExternalAxisKey(key))
    {
        return true;
    }

    return false;
}

bool DevelopHelper::StaticKeys::IsExternalKey(const FKey& InKey)
{
    const std::string externalBtnStr = "Button_";

    const std::string KeyName(InKey.GetName());
    if (KeyName.compare(0, externalBtnStr.size(), externalBtnStr) == 0)
    {
        return true;
    }
    return false;
}

bool DevelopHelper::StaticKeys::IsExternalAxisKey(const FKey& InKey)
{
    const std::string externalAxisStr = "Axis_";
    const std::string KeyName(InKey.GetName());
    if(KeyName.compare(0, externalAxisStr.size(), externalAxisStr) == 0)
    {
        return true;
    }
    return false;
}
