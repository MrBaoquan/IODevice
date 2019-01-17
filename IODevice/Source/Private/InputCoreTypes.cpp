#pragma once
#include <string>
#include "InputCoreTypes.h"
#include "IOStatics.h"
using namespace DevelopHelper;

DevelopHelper::FKey::FKey(const char* InKeyName)
{
    size_t length = strlen(InKeyName);
    keyName = new char[length + 1];
    memcpy(keyName, InKeyName, length + 1);
}

DevelopHelper::FKey::~FKey()
{
    if (keyName != nullptr)
    {
        delete[] keyName;
    }
}

const bool DevelopHelper::FKey::IsValid() const
{
    return StaticKeys::ValidKey(*this);
}

const bool DevelopHelper::FKey::IsModifierKey() const
{
    if (!IsValid()) { return false; }
    std::shared_ptr<FKeyDetails> keyDetail = StaticKeys::InputKeys[*this];
    return keyDetail ? keyDetail->IsModifierKey() : false;
}

bool DevelopHelper::FKey::IsFloatAxis() const
{
    if (!IsValid()) 
    {
        return false; 
    }
    std::shared_ptr<FKeyDetails> keyDetail = StaticKeys::InputKeys[*this];
    return keyDetail ? keyDetail->IsFloatAxis() : false;
}

bool DevelopHelper::FKey::IsVectorAxis() const
{
    if (!IsValid()) { return false; }
    std::shared_ptr<FKeyDetails> keyDetail = StaticKeys::InputKeys[*this];
    return keyDetail ? keyDetail->IsVectorAxis() : false;
}

void DevelopHelper::FKey::operator=(const FKey& rhs)
{
    if (this->keyName != nullptr)
    {
        delete[] keyName;
    }
    size_t length = strlen(rhs.GetName());
    keyName = new char[length + 1];
    memcpy(keyName, rhs.GetName(), length + 1);
}

DevelopHelper::FKey::FKey(const FKey& key)
{
    size_t length = strlen(key.GetName());
    keyName = new char[length + 1];
    memcpy(keyName, key.GetName(), length + 1);
}

bool DevelopHelper::operator==(const FKey & KeyA, const FKey & KeyB)
{
    return std::string(KeyA.keyName) == std::string(KeyB.keyName);
}

bool DevelopHelper::operator!=(const FKey & KeyA, const FKey & KeyB)
{
    return std::string(KeyA.keyName) != std::string(KeyB.keyName);
}


const FKey EKeys::AnyKey("AnyKey");

const FKey EKeys::MouseX("MouseX");
const FKey EKeys::MouseY("MouseY");
const FKey EKeys::MouseScrollUp("MouseScrollUp");
const FKey EKeys::MouseScrollDown("MouseScrollDown");
const FKey EKeys::MouseWheelAxis("MouseWheelAxis");

const FKey EKeys::LeftMouseButton("LeftMouseButton");
const FKey EKeys::RightMouseButton("RightMouseButton");
const FKey EKeys::MiddleMouseButton("MiddleMouseButton");
const FKey EKeys::ThumbMouseButton("ThumbMouseButton");
const FKey EKeys::ThumbMouseButton2("ThumbMouseButton2");

const FKey EKeys::BackSpace("BackSpace");
const FKey EKeys::Tab("Tab");
const FKey EKeys::Enter("Enter");
const FKey EKeys::Pause("Pause");

const FKey EKeys::CapsLock("CapsLock");
const FKey EKeys::Escape("Escape");
const FKey EKeys::SpaceBar("SpaceBar");
const FKey EKeys::PageUp("PageUp");
const FKey EKeys::PageDown("PageDown");
const FKey EKeys::End("End");
const FKey EKeys::Home("Home");

const FKey EKeys::Left("Left");
const FKey EKeys::Up("Up");
const FKey EKeys::Right("Right");
const FKey EKeys::Down("Down");

const FKey EKeys::Insert("Insert");
const FKey EKeys::Delete("Delete");

const FKey EKeys::Zero("Zero");
const FKey EKeys::One("One");
const FKey EKeys::Two("Two");
const FKey EKeys::Three("Three");
const FKey EKeys::Four("Four");
const FKey EKeys::Five("Five");
const FKey EKeys::Six("Six");
const FKey EKeys::Seven("Seven");
const FKey EKeys::Eight("Eight");
const FKey EKeys::Nine("Nine");

const FKey EKeys::A("A");
const FKey EKeys::B("B");
const FKey EKeys::C("C");
const FKey EKeys::D("D");
const FKey EKeys::E("E");
const FKey EKeys::F("F");
const FKey EKeys::G("G");
const FKey EKeys::H("H");
const FKey EKeys::I("I");
const FKey EKeys::J("J");
const FKey EKeys::K("K");
const FKey EKeys::L("L");
const FKey EKeys::M("M");
const FKey EKeys::N("N");
const FKey EKeys::O("O");
const FKey EKeys::P("P");
const FKey EKeys::Q("Q");
const FKey EKeys::R("R");
const FKey EKeys::S("S");
const FKey EKeys::T("T");
const FKey EKeys::U("U");
const FKey EKeys::V("V");
const FKey EKeys::W("W");
const FKey EKeys::X("X");
const FKey EKeys::Y("Y");
const FKey EKeys::Z("Z");

const FKey EKeys::NumPadZero("NumPadZero");
const FKey EKeys::NumPadOne("NumPadOne");
const FKey EKeys::NumPadTwo("NumPadTwo");
const FKey EKeys::NumPadThree("NumPadThree");
const FKey EKeys::NumPadFour("NumPadFour");
const FKey EKeys::NumPadFive("NumPadFive");
const FKey EKeys::NumPadSix("NumPadSix");
const FKey EKeys::NumPadSeven("NumPadSeven");
const FKey EKeys::NumPadEight("NumPadEight");
const FKey EKeys::NumPadNine("NumPadNine");

const FKey EKeys::Multiply("Multiply");
const FKey EKeys::Add("Add");
const FKey EKeys::Subtract("Subtract");
const FKey EKeys::Decimal("Decimal");
const FKey EKeys::Divide("Divide");

const FKey EKeys::F1("F1");
const FKey EKeys::F2("F2");
const FKey EKeys::F3("F3");
const FKey EKeys::F4("F4");
const FKey EKeys::F5("F5");
const FKey EKeys::F6("F6");
const FKey EKeys::F7("F7");
const FKey EKeys::F8("F8");
const FKey EKeys::F9("F9");
const FKey EKeys::F10("F10");
const FKey EKeys::F11("F11");
const FKey EKeys::F12("F12");

const FKey EKeys::NumLock("NumLock");

const FKey EKeys::ScrollLock("ScrollLock");

const FKey EKeys::LeftShift("LeftShift");
const FKey EKeys::RightShift("RightShift");
const FKey EKeys::LeftControl("LeftControl");
const FKey EKeys::RightControl("RightControl");
const FKey EKeys::LeftAlt("LeftAlt");
const FKey EKeys::RightAlt("RightAlt");
const FKey EKeys::LeftCommand("LeftCommand");
const FKey EKeys::RightCommand("RightCommand");

const DevelopHelper::FKey DevelopHelper::EKeys::Invalid("NAME_None");

const DevelopHelper::FKey DevelopHelper::EKeys::Button_00("Button_00");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_01("Button_01");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_02("Button_02");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_03("Button_03");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_04("Button_04");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_05("Button_05");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_06("Button_06");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_07("Button_07");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_08("Button_08");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_09("Button_09");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_10("Button_10");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_11("Button_11");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_12("Button_12");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_13("Button_13");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_14("Button_14");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_15("Button_15");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_16("Button_16");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_17("Button_17");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_18("Button_18");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_19("Button_19");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_20("Button_20");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_21("Button_21");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_22("Button_22");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_23("Button_23");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_24("Button_24");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_25("Button_25");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_26("Button_26");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_27("Button_27");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_28("Button_28");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_29("Button_29");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_30("Button_30");
const DevelopHelper::FKey DevelopHelper::EKeys::Button_31("Button_31");

const DevelopHelper::FKey DevelopHelper::EKeys::Axis_00("Axis_00");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_01("Axis_01");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_02("Axis_02");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_03("Axis_03");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_04("Axis_04");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_05("Axis_05");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_06("Axis_06");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_07("Axis_07");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_08("Axis_08");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_09("Axis_09");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_10("Axis_10");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_11("Axis_11");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_12("Axis_12");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_13("Axis_13");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_14("Axis_14");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_15("Axis_15");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_16("Axis_16");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_17("Axis_17");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_18("Axis_18");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_19("Axis_19");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_20("Axis_20");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_21("Axis_21");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_22("Axis_22");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_23("Axis_23");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_24("Axis_24");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_25("Axis_25");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_26("Axis_26");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_27("Axis_27");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_28("Axis_28");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_29("Axis_29");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_30("Axis_30");
const DevelopHelper::FKey DevelopHelper::EKeys::Axis_31("Axis_31");


/** Joystick axes */

const DevelopHelper::FKey DevelopHelper::EKeys::JS_X("JS_X");/* x-axis position              */
const DevelopHelper::FKey DevelopHelper::EKeys::JS_Y("JS_Y");/* y-axis position              */
const DevelopHelper::FKey DevelopHelper::EKeys::JS_Z("JS_Z");/* z-axis position              */
const DevelopHelper::FKey DevelopHelper::EKeys::JS_Rx("JS_Rx");/* x-axis rotation              */
const DevelopHelper::FKey DevelopHelper::EKeys::JS_Ry("JS_Ry");/* y-axis rotation              */
const DevelopHelper::FKey DevelopHelper::EKeys::JS_Rz("JS_Rz");/* z-axis rotation              */
const DevelopHelper::FKey DevelopHelper::EKeys::JS_VX("JS_VX");/* x-axis velocity              */
const DevelopHelper::FKey DevelopHelper::EKeys::JS_VY("JS_VY");/* y-axis velocity              */
const DevelopHelper::FKey DevelopHelper::EKeys::JS_VZ("JS_VZ");/* z-axis velocity              */
const DevelopHelper::FKey DevelopHelper::EKeys::JS_VRx("JS_VRx");/* x-axis angular velocity      */
const DevelopHelper::FKey DevelopHelper::EKeys::JS_VRy("JS_VRy");/* y-axis angular velocity      */
const DevelopHelper::FKey DevelopHelper::EKeys::JS_VRz("JS_VRz");/* z-axis angular velocity      */
const DevelopHelper::FKey DevelopHelper::EKeys::JS_AX("JS_AX");/* x-axis acceleration          */
const DevelopHelper::FKey DevelopHelper::EKeys::JS_AY("JS_AY");/* y-axis acceleration          */
const DevelopHelper::FKey DevelopHelper::EKeys::JS_AZ("JS_AZ");/* z-axis acceleration          */
const DevelopHelper::FKey DevelopHelper::EKeys::JS_ARx("JS_ARx");/* x-axis angular acceleration  */
const DevelopHelper::FKey DevelopHelper::EKeys::JS_ARy("JS_ARy");/* y-axis angular acceleration  */
const DevelopHelper::FKey DevelopHelper::EKeys::JS_ARz("JS_ARz");/* z-axis angular acceleration  */
const DevelopHelper::FKey DevelopHelper::EKeys::JS_FX("JS_FX");/* x-axis force                 */
const DevelopHelper::FKey DevelopHelper::EKeys::JS_FY("JS_FY");/* y-axis force                 */
const DevelopHelper::FKey DevelopHelper::EKeys::JS_FZ("JS_FZ");/* z-axis force                 */
const DevelopHelper::FKey DevelopHelper::EKeys::JS_FRx("JS_FRx");/* x-axis torque                */
const DevelopHelper::FKey DevelopHelper::EKeys::JS_FRy("JS_FRy");/* y-axis torque                */
const DevelopHelper::FKey DevelopHelper::EKeys::JS_FRz("JS_FRz");/* z-axis torque                */

const DevelopHelper::FKey DevelopHelper::EKeys::JS_Slider_00("JS_Slider_00");
const DevelopHelper::FKey DevelopHelper::EKeys::JS_Slider_01("JS_Slider_01");

const DevelopHelper::FKey DevelopHelper::EKeys::JS_VSlider_00("JS_VSlider_00");
const DevelopHelper::FKey DevelopHelper::EKeys::JS_VSlider_01("JS_VSlider_01");

const DevelopHelper::FKey DevelopHelper::EKeys::JS_ASlider_00("JS_ASlider_00");
const DevelopHelper::FKey DevelopHelper::EKeys::JS_ASlider_01("JS_ASlider_01");

const DevelopHelper::FKey DevelopHelper::EKeys::JS_FSlider_00("JS_FSlider_00");
const DevelopHelper::FKey DevelopHelper::EKeys::JS_FSlider_01("JS_FSlider_01");

const DevelopHelper::FKey DevelopHelper::EKeys::JS_POV_00("JS_POV_00");
const DevelopHelper::FKey DevelopHelper::EKeys::JS_POV_01("JS_POV_01");
const DevelopHelper::FKey DevelopHelper::EKeys::JS_POV_02("JS_POV_02");
const DevelopHelper::FKey DevelopHelper::EKeys::JS_POV_03("JS_POV_03");

