/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#include "KeyDetails.h"
#include "IOStatics.h"

using namespace IOToolkit;

IOToolkit::FKeyDetails::FKeyDetails(const FKey InKey, const std::string InDisplayName, const unsigned __int8 InKeyFlags /*= 0*/)
    : Key(InKey)
    , DisplayName(InDisplayName)
    , bIsModifierKey((InKeyFlags & EKeyFlags::ModifierKey) != 0)
    , bIsGamepadKey((InKeyFlags & EKeyFlags::GamepadKey) != 0)
    , bIsMouseButton((InKeyFlags & EKeyFlags::MouseButton) != 0)
    , bShouldUpdateAxisWithoutSamples((InKeyFlags & EKeyFlags::UpdateAxisWithoutSamples) != 0)
    , AxisType(EInputAxisType::None)
{
    if ((InKeyFlags & EKeyFlags::FloatAxis) != 0)
    {
        if ((InKeyFlags & EKeyFlags::VectorAxis) == 0)
        {
            AxisType = EInputAxisType::Float;
        }
    }
    else if ((InKeyFlags & EKeyFlags::VectorAxis) != 0)
    {
        AxisType = EInputAxisType::Vector;
    }
}

bool IOToolkit::FKeyDetails::IsFloatAxis() const
{
    return AxisType == EInputAxisType::Float;
}

bool IOToolkit::FKeyDetails::IsVectorAxis() const
{
    return AxisType == EInputAxisType::Vector;
}
 