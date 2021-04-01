/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include <string>
#include <memory>
#include <map>
#include "CoreTypes.inl"
#include "InputCoreTypes.h"

namespace IOToolkit
{
/**
 * Manager standard input key mapping
 */
struct InputKeyManager
{
public:
    static uint32 GetKeyMap(uint32* KeyCodes, std::string* KeyNames, uint32 MaxMappings);
    static uint32 GetCharKeyMap(uint32* KeyCodes, std::string* KeyNames, uint32 MaxMappings);

    static uint32 GetStandardPrintableKeyMap(uint32* KeyCodes, std::string* KeyNames, uint32 MaxMappings, bool bMapUppercaseKeys, bool bMapLowercaseKeys);

    static InputKeyManager& Instance();

    void GetCodesFromKey(const FKey,const uint32*& KeyCode,const uint32*& CharCode)const;

    FKey GetKeyFromCodes(const uint32 KeyCode, const uint32 CharCode) const;
    void InitKeyMappings();
    const std::map<uint32, FKey>& GetKeyMapVirtualToEnum() const;
    const std::map<uint32, FKey>& GetKeyMapCharToEnum() const;
private:
    InputKeyManager()
    {
        InitKeyMappings();
    }
    std::map<uint32, FKey> KeyMapVirtualToEnum;
    std::map<uint32, FKey> KeyMapCharToEnum;

};


};
