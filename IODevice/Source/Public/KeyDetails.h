/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include <string>
#include "CoreTypes.inl"
#include "InputCoreTypes.h"

namespace IOToolkit
{

struct FKeyDetails
{
public:
    enum EKeyFlags
    {
        GamepadKey = 0x01,
        MouseButton = 0x02,
        ModifierKey = 0x04,
        NotBlueprintBindableKey = 0x08,
        FloatAxis = 0x10,
        VectorAxis = 0x20,
        UpdateAxisWithoutSamples = 0x40,

        NoFlags = 0,
    };
    FKeyDetails(const FKey InKey, const std::string InDisplayName, const unsigned __int8 inKeyFlags = 0);

    inline bool IsGamepadKey() const { return bIsGamepadKey != 0; }
    inline const FKey& GetKey() const { return Key; }
    inline bool IsModifierKey() const { return bIsModifierKey != 0; }
    bool IsFloatAxis() const;
    bool IsVectorAxis() const;
private:
    enum class EInputAxisType : uint8
    {
        None,
        Float,
        Vector
    };
    FKey Key;
    std::string DisplayName;
    int bIsModifierKey : 1;
    int bIsGamepadKey : 1;
    int bIsMouseButton : 1;
    int bIsBindableInBlueprints : 1;
    int bShouldUpdateAxisWithoutSamples : 1;
    EInputAxisType AxisType;
};


};