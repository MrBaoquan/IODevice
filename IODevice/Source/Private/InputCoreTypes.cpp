/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include <string>
#include "InputCoreTypes.h"
#include "IOStatics.h"
using namespace IOToolkit;

IOToolkit::FKey::FKey(const char* InKeyName)
{
    size_t length = strlen(InKeyName);
    keyName = new char[length + 1];
    memcpy(keyName, InKeyName, length + 1);
}

IOToolkit::FKey::~FKey()
{
    if (keyName != nullptr)
    {
        delete[] keyName;
    }
}

const bool IOToolkit::FKey::IsValid() const
{
    return StaticKeys::ValidKey(*this);
}

const bool IOToolkit::FKey::IsModifierKey() const
{
    if (!IsValid()) { return false; }
    std::shared_ptr<FKeyDetails> keyDetail = StaticKeys::InputKeys[*this];
    return keyDetail ? keyDetail->IsModifierKey() : false;
}

bool IOToolkit::FKey::IsFloatAxis() const
{
    if (!IsValid()) 
    {
        return false; 
    }
    std::shared_ptr<FKeyDetails> keyDetail = StaticKeys::InputKeys[*this];
    return keyDetail ? keyDetail->IsFloatAxis() : false;
}

bool IOToolkit::FKey::IsVectorAxis() const
{
    if (!IsValid()) { return false; }
    std::shared_ptr<FKeyDetails> keyDetail = StaticKeys::InputKeys[*this];
    return keyDetail ? keyDetail->IsVectorAxis() : false;
}

void IOToolkit::FKey::operator=(const FKey& rhs)
{
    if (this->keyName != nullptr)
    {
        delete[] keyName;
    }
    size_t length = strlen(rhs.GetName());
    keyName = new char[length + 1];
    memcpy(keyName, rhs.GetName(), length + 1);
}

IOToolkit::FKey::FKey(const FKey& key)
{
    size_t length = strlen(key.GetName());
    keyName = new char[length + 1];
    memcpy(keyName, key.GetName(), length + 1);
}

bool IOToolkit::operator==(const FKey & KeyA, const FKey & KeyB)
{
    return std::string(KeyA.keyName) == std::string(KeyB.keyName);
}

bool IOToolkit::operator!=(const FKey & KeyA, const FKey & KeyB)
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

const IOToolkit::FKey IOToolkit::EKeys::Invalid("NAME_None");

const IOToolkit::FKey IOToolkit::EKeys::Button_00("Button_00");
const IOToolkit::FKey IOToolkit::EKeys::Button_01("Button_01");
const IOToolkit::FKey IOToolkit::EKeys::Button_02("Button_02");
const IOToolkit::FKey IOToolkit::EKeys::Button_03("Button_03");
const IOToolkit::FKey IOToolkit::EKeys::Button_04("Button_04");
const IOToolkit::FKey IOToolkit::EKeys::Button_05("Button_05");
const IOToolkit::FKey IOToolkit::EKeys::Button_06("Button_06");
const IOToolkit::FKey IOToolkit::EKeys::Button_07("Button_07");
const IOToolkit::FKey IOToolkit::EKeys::Button_08("Button_08");
const IOToolkit::FKey IOToolkit::EKeys::Button_09("Button_09");
const IOToolkit::FKey IOToolkit::EKeys::Button_10("Button_10");
const IOToolkit::FKey IOToolkit::EKeys::Button_11("Button_11");
const IOToolkit::FKey IOToolkit::EKeys::Button_12("Button_12");
const IOToolkit::FKey IOToolkit::EKeys::Button_13("Button_13");
const IOToolkit::FKey IOToolkit::EKeys::Button_14("Button_14");
const IOToolkit::FKey IOToolkit::EKeys::Button_15("Button_15");
const IOToolkit::FKey IOToolkit::EKeys::Button_16("Button_16");
const IOToolkit::FKey IOToolkit::EKeys::Button_17("Button_17");
const IOToolkit::FKey IOToolkit::EKeys::Button_18("Button_18");
const IOToolkit::FKey IOToolkit::EKeys::Button_19("Button_19");
const IOToolkit::FKey IOToolkit::EKeys::Button_20("Button_20");
const IOToolkit::FKey IOToolkit::EKeys::Button_21("Button_21");
const IOToolkit::FKey IOToolkit::EKeys::Button_22("Button_22");
const IOToolkit::FKey IOToolkit::EKeys::Button_23("Button_23");
const IOToolkit::FKey IOToolkit::EKeys::Button_24("Button_24");
const IOToolkit::FKey IOToolkit::EKeys::Button_25("Button_25");
const IOToolkit::FKey IOToolkit::EKeys::Button_26("Button_26");
const IOToolkit::FKey IOToolkit::EKeys::Button_27("Button_27");
const IOToolkit::FKey IOToolkit::EKeys::Button_28("Button_28");
const IOToolkit::FKey IOToolkit::EKeys::Button_29("Button_29");
const IOToolkit::FKey IOToolkit::EKeys::Button_30("Button_30");
const IOToolkit::FKey IOToolkit::EKeys::Button_31("Button_31");

const IOToolkit::FKey IOToolkit::EKeys::Axis_00("Axis_00");
const IOToolkit::FKey IOToolkit::EKeys::Axis_01("Axis_01");
const IOToolkit::FKey IOToolkit::EKeys::Axis_02("Axis_02");
const IOToolkit::FKey IOToolkit::EKeys::Axis_03("Axis_03");
const IOToolkit::FKey IOToolkit::EKeys::Axis_04("Axis_04");
const IOToolkit::FKey IOToolkit::EKeys::Axis_05("Axis_05");
const IOToolkit::FKey IOToolkit::EKeys::Axis_06("Axis_06");
const IOToolkit::FKey IOToolkit::EKeys::Axis_07("Axis_07");
const IOToolkit::FKey IOToolkit::EKeys::Axis_08("Axis_08");
const IOToolkit::FKey IOToolkit::EKeys::Axis_09("Axis_09");
const IOToolkit::FKey IOToolkit::EKeys::Axis_10("Axis_10");
const IOToolkit::FKey IOToolkit::EKeys::Axis_11("Axis_11");
const IOToolkit::FKey IOToolkit::EKeys::Axis_12("Axis_12");
const IOToolkit::FKey IOToolkit::EKeys::Axis_13("Axis_13");
const IOToolkit::FKey IOToolkit::EKeys::Axis_14("Axis_14");
const IOToolkit::FKey IOToolkit::EKeys::Axis_15("Axis_15");
const IOToolkit::FKey IOToolkit::EKeys::Axis_16("Axis_16");
const IOToolkit::FKey IOToolkit::EKeys::Axis_17("Axis_17");
const IOToolkit::FKey IOToolkit::EKeys::Axis_18("Axis_18");
const IOToolkit::FKey IOToolkit::EKeys::Axis_19("Axis_19");
const IOToolkit::FKey IOToolkit::EKeys::Axis_20("Axis_20");
const IOToolkit::FKey IOToolkit::EKeys::Axis_21("Axis_21");
const IOToolkit::FKey IOToolkit::EKeys::Axis_22("Axis_22");
const IOToolkit::FKey IOToolkit::EKeys::Axis_23("Axis_23");
const IOToolkit::FKey IOToolkit::EKeys::Axis_24("Axis_24");
const IOToolkit::FKey IOToolkit::EKeys::Axis_25("Axis_25");
const IOToolkit::FKey IOToolkit::EKeys::Axis_26("Axis_26");
const IOToolkit::FKey IOToolkit::EKeys::Axis_27("Axis_27");
const IOToolkit::FKey IOToolkit::EKeys::Axis_28("Axis_28");
const IOToolkit::FKey IOToolkit::EKeys::Axis_29("Axis_29");
const IOToolkit::FKey IOToolkit::EKeys::Axis_30("Axis_30");
const IOToolkit::FKey IOToolkit::EKeys::Axis_31("Axis_31");

const IOToolkit::FKey IOToolkit::EKeys::OAxis_00("OAxis_00");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_01("OAxis_01");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_02("OAxis_02");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_03("OAxis_03");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_04("OAxis_04");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_05("OAxis_05");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_06("OAxis_06");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_07("OAxis_07");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_08("OAxis_08");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_09("OAxis_09");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_10("OAxis_10");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_11("OAxis_11");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_12("OAxis_12");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_13("OAxis_13");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_14("OAxis_14");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_15("OAxis_15");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_16("OAxis_16");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_17("OAxis_17");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_18("OAxis_18");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_19("OAxis_19");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_20("OAxis_20");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_21("OAxis_21");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_22("OAxis_22");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_23("OAxis_23");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_24("OAxis_24");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_25("OAxis_25");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_26("OAxis_26");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_27("OAxis_27");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_28("OAxis_28");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_29("OAxis_29");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_30("OAxis_30");
const IOToolkit::FKey IOToolkit::EKeys::OAxis_31("OAxis_31");


/** Joystick axes */

const IOToolkit::FKey IOToolkit::EKeys::JS_X("JS_X");/* x-axis position              */
const IOToolkit::FKey IOToolkit::EKeys::JS_Y("JS_Y");/* y-axis position              */
const IOToolkit::FKey IOToolkit::EKeys::JS_Z("JS_Z");/* z-axis position              */
const IOToolkit::FKey IOToolkit::EKeys::JS_Rx("JS_Rx");/* x-axis rotation              */
const IOToolkit::FKey IOToolkit::EKeys::JS_Ry("JS_Ry");/* y-axis rotation              */
const IOToolkit::FKey IOToolkit::EKeys::JS_Rz("JS_Rz");/* z-axis rotation              */
const IOToolkit::FKey IOToolkit::EKeys::JS_VX("JS_VX");/* x-axis velocity              */
const IOToolkit::FKey IOToolkit::EKeys::JS_VY("JS_VY");/* y-axis velocity              */
const IOToolkit::FKey IOToolkit::EKeys::JS_VZ("JS_VZ");/* z-axis velocity              */
const IOToolkit::FKey IOToolkit::EKeys::JS_VRx("JS_VRx");/* x-axis angular velocity      */
const IOToolkit::FKey IOToolkit::EKeys::JS_VRy("JS_VRy");/* y-axis angular velocity      */
const IOToolkit::FKey IOToolkit::EKeys::JS_VRz("JS_VRz");/* z-axis angular velocity      */
const IOToolkit::FKey IOToolkit::EKeys::JS_AX("JS_AX");/* x-axis acceleration          */
const IOToolkit::FKey IOToolkit::EKeys::JS_AY("JS_AY");/* y-axis acceleration          */
const IOToolkit::FKey IOToolkit::EKeys::JS_AZ("JS_AZ");/* z-axis acceleration          */
const IOToolkit::FKey IOToolkit::EKeys::JS_ARx("JS_ARx");/* x-axis angular acceleration  */
const IOToolkit::FKey IOToolkit::EKeys::JS_ARy("JS_ARy");/* y-axis angular acceleration  */
const IOToolkit::FKey IOToolkit::EKeys::JS_ARz("JS_ARz");/* z-axis angular acceleration  */
const IOToolkit::FKey IOToolkit::EKeys::JS_FX("JS_FX");/* x-axis force                 */
const IOToolkit::FKey IOToolkit::EKeys::JS_FY("JS_FY");/* y-axis force                 */
const IOToolkit::FKey IOToolkit::EKeys::JS_FZ("JS_FZ");/* z-axis force                 */
const IOToolkit::FKey IOToolkit::EKeys::JS_FRx("JS_FRx");/* x-axis torque                */
const IOToolkit::FKey IOToolkit::EKeys::JS_FRy("JS_FRy");/* y-axis torque                */
const IOToolkit::FKey IOToolkit::EKeys::JS_FRz("JS_FRz");/* z-axis torque                */

const IOToolkit::FKey IOToolkit::EKeys::JS_Slider_00("JS_Slider_00");
const IOToolkit::FKey IOToolkit::EKeys::JS_Slider_01("JS_Slider_01");

const IOToolkit::FKey IOToolkit::EKeys::JS_VSlider_00("JS_VSlider_00");
const IOToolkit::FKey IOToolkit::EKeys::JS_VSlider_01("JS_VSlider_01");

const IOToolkit::FKey IOToolkit::EKeys::JS_ASlider_00("JS_ASlider_00");
const IOToolkit::FKey IOToolkit::EKeys::JS_ASlider_01("JS_ASlider_01");

const IOToolkit::FKey IOToolkit::EKeys::JS_FSlider_00("JS_FSlider_00");
const IOToolkit::FKey IOToolkit::EKeys::JS_FSlider_01("JS_FSlider_01");

const IOToolkit::FKey IOToolkit::EKeys::JS_POV_00("JS_POV_00");
const IOToolkit::FKey IOToolkit::EKeys::JS_POV_01("JS_POV_01");
const IOToolkit::FKey IOToolkit::EKeys::JS_POV_02("JS_POV_02");
const IOToolkit::FKey IOToolkit::EKeys::JS_POV_03("JS_POV_03");

