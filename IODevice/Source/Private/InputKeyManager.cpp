/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#include "InputKeyManager.h"
#include <windows.h>
#include <map>
#include "IOStatics.h"

IOToolkit::uint32 IOToolkit::InputKeyManager::GetKeyMap(uint32* KeyCodes, std::string* KeyNames, uint32 MaxMappings)
{
#define ADDKEYMAP(KeyCode, KeyName)		if (NumMappings<MaxMappings) { KeyCodes[NumMappings]=KeyCode; KeyNames[NumMappings]=KeyName; ++NumMappings; };

    uint32 NumMappings = 0;

    if (KeyCodes && KeyNames && (MaxMappings > 0))
    {
        ADDKEYMAP(VK_LBUTTON, "LeftMouseButton");
        ADDKEYMAP(VK_RBUTTON, "RightMouseButton");
        ADDKEYMAP(VK_MBUTTON, "MiddleMouseButton");

        ADDKEYMAP(VK_XBUTTON1, "ThumbMouseButton");
        ADDKEYMAP(VK_XBUTTON2, "ThumbMouseButton2");

        ADDKEYMAP(VK_BACK, "BackSpace");
        ADDKEYMAP(VK_TAB, "Tab");
        ADDKEYMAP(VK_RETURN, "Enter");
        ADDKEYMAP(VK_PAUSE, "Pause");

        ADDKEYMAP(VK_CAPITAL, "CapsLock");
        ADDKEYMAP(VK_ESCAPE, "Escape");
        ADDKEYMAP(VK_SPACE, "SpaceBar");
        ADDKEYMAP(VK_PRIOR, "PageUp");
        ADDKEYMAP(VK_NEXT, "PageDown");
        ADDKEYMAP(VK_END, "End");
        ADDKEYMAP(VK_HOME, "Home");

        ADDKEYMAP(VK_LEFT, "Left");
        ADDKEYMAP(VK_UP, "Up");
        ADDKEYMAP(VK_RIGHT, "Right");
        ADDKEYMAP(VK_DOWN, "Down");

        ADDKEYMAP(VK_INSERT, "Insert");
        ADDKEYMAP(VK_DELETE, "Delete");

        ADDKEYMAP(VK_NUMPAD0, "NumPadZero");
        ADDKEYMAP(VK_NUMPAD1, "NumPadOne");
        ADDKEYMAP(VK_NUMPAD2, "NumPadTwo");
        ADDKEYMAP(VK_NUMPAD3, "NumPadThree");
        ADDKEYMAP(VK_NUMPAD4, "NumPadFour");
        ADDKEYMAP(VK_NUMPAD5, "NumPadFive");
        ADDKEYMAP(VK_NUMPAD6, "NumPadSix");
        ADDKEYMAP(VK_NUMPAD7, "NumPadSeven");
        ADDKEYMAP(VK_NUMPAD8, "NumPadEight");
        ADDKEYMAP(VK_NUMPAD9, "NumPadNine");

        ADDKEYMAP(VK_MULTIPLY, "Multiply");
        ADDKEYMAP(VK_ADD, "Add");
        ADDKEYMAP(VK_SUBTRACT, "Subtract");
        ADDKEYMAP(VK_DECIMAL, "Decimal");
        ADDKEYMAP(VK_DIVIDE, "Divide");

        ADDKEYMAP(VK_F1, "F1");
        ADDKEYMAP(VK_F2, "F2");
        ADDKEYMAP(VK_F3, "F3");
        ADDKEYMAP(VK_F4, "F4");
        ADDKEYMAP(VK_F5, "F5");
        ADDKEYMAP(VK_F6, "F6");
        ADDKEYMAP(VK_F7, "F7");
        ADDKEYMAP(VK_F8, "F8");
        ADDKEYMAP(VK_F9, "F9");
        ADDKEYMAP(VK_F10, "F10");
        ADDKEYMAP(VK_F11, "F11");
        ADDKEYMAP(VK_F12, "F12");

        ADDKEYMAP(VK_NUMLOCK, "NumLock");

        ADDKEYMAP(VK_SCROLL, "ScrollLock");

        ADDKEYMAP(VK_LSHIFT, "LeftShift");
        ADDKEYMAP(VK_RSHIFT, "RightShift");
        ADDKEYMAP(VK_LCONTROL, "LeftControl");
        ADDKEYMAP(VK_RCONTROL, "RightControl");
        ADDKEYMAP(VK_LMENU, "LeftAlt");
        ADDKEYMAP(VK_RMENU, "RightAlt");
        ADDKEYMAP(VK_LWIN, "LeftCommand");
        ADDKEYMAP(VK_RWIN, "RightCommand");

        std::map<uint32, uint32> ScanToVKMap;
#define MAP_OEM_VK_TO_SCAN(KeyCode) { const uint32 CharCode = MapVirtualKey(KeyCode,2); if (CharCode != 0) { ScanToVKMap.insert(std::pair<uint32,uint32>(CharCode,KeyCode)); } }
        MAP_OEM_VK_TO_SCAN(VK_OEM_1);
        MAP_OEM_VK_TO_SCAN(VK_OEM_2);
        MAP_OEM_VK_TO_SCAN(VK_OEM_3);
        MAP_OEM_VK_TO_SCAN(VK_OEM_4);
        MAP_OEM_VK_TO_SCAN(VK_OEM_5);
        MAP_OEM_VK_TO_SCAN(VK_OEM_6);
        MAP_OEM_VK_TO_SCAN(VK_OEM_7);
        MAP_OEM_VK_TO_SCAN(VK_OEM_8);
        MAP_OEM_VK_TO_SCAN(VK_OEM_PLUS);
        MAP_OEM_VK_TO_SCAN(VK_OEM_COMMA);
        MAP_OEM_VK_TO_SCAN(VK_OEM_MINUS);
        MAP_OEM_VK_TO_SCAN(VK_OEM_PERIOD);
        MAP_OEM_VK_TO_SCAN(VK_OEM_102);
#undef  MAP_OEM_VK_TO_SCAN

        static const uint32 MAX_KEY_MAPPINGS(256);
        uint32 CharCodes[MAX_KEY_MAPPINGS];
        std::string CharKeyNames[MAX_KEY_MAPPINGS];
        const int CharMappings = GetCharKeyMap(CharCodes, CharKeyNames, MAX_KEY_MAPPINGS);

        for (int MappingIndex = 0; MappingIndex < CharMappings; ++MappingIndex)
        {
            ScanToVKMap.erase(CharCodes[MappingIndex]);
        }

        for (auto It=ScanToVKMap.begin(); It!=ScanToVKMap.end(); ++It)
        {
            ADDKEYMAP(It->second, std::to_string(It->first));
        }
    }

    return NumMappings;

#undef ADDKEYMAP
}

IOToolkit::uint32 IOToolkit::InputKeyManager::GetCharKeyMap(uint32* KeyCodes, std::string* KeyNames, uint32 MaxMappings)
{
    return IOToolkit::InputKeyManager::GetStandardPrintableKeyMap(KeyCodes, KeyNames, MaxMappings, true, false);
}

IOToolkit::uint32 IOToolkit::InputKeyManager::GetStandardPrintableKeyMap(uint32* KeyCodes, std::string* KeyNames, uint32 MaxMappings, bool bMapUppercaseKeys, bool bMapLowercaseKeys)
{
    uint32 NumMappings = 0;

#define ADDKEYMAP(KeyCode, KeyName)		if (NumMappings<MaxMappings) { KeyCodes[NumMappings]=KeyCode; KeyNames[NumMappings]=KeyName; ++NumMappings; };

    ADDKEYMAP('0', "Zero");
    ADDKEYMAP('1', "One");
    ADDKEYMAP('2', "Two");
    ADDKEYMAP('3', "Three");
    ADDKEYMAP('4', "Four");
    ADDKEYMAP('5', "Five");
    ADDKEYMAP('6', "Six");
    ADDKEYMAP('7', "Seven");
    ADDKEYMAP('8', "Eight");
    ADDKEYMAP('9', "Nine");

    // we map both upper and lower
    if (bMapUppercaseKeys)
    {
        ADDKEYMAP('A', "A");
        ADDKEYMAP('B', "B");
        ADDKEYMAP('C', "C");
        ADDKEYMAP('D', "D");
        ADDKEYMAP('E', "E");
        ADDKEYMAP('F', "F");
        ADDKEYMAP('G', "G");
        ADDKEYMAP('H', "H");
        ADDKEYMAP('I', "I");
        ADDKEYMAP('J', "J");
        ADDKEYMAP('K', "K");
        ADDKEYMAP('L', "L");
        ADDKEYMAP('M', "M");
        ADDKEYMAP('N', "N");
        ADDKEYMAP('O', "O");
        ADDKEYMAP('P', "P");
        ADDKEYMAP('Q', "Q");
        ADDKEYMAP('R', "R");
        ADDKEYMAP('S', "S");
        ADDKEYMAP('T', "T");
        ADDKEYMAP('U', "U");
        ADDKEYMAP('V', "V");
        ADDKEYMAP('W', "W");
        ADDKEYMAP('X', "X");
        ADDKEYMAP('Y', "Y");
        ADDKEYMAP('Z', "Z");
    }

    if (bMapLowercaseKeys)
    {
        ADDKEYMAP('a', "A");
        ADDKEYMAP('b', "B");
        ADDKEYMAP('c', "C");
        ADDKEYMAP('d', "D");
        ADDKEYMAP('e', "E");
        ADDKEYMAP('f', "F");
        ADDKEYMAP('g', "G");
        ADDKEYMAP('h', "H");
        ADDKEYMAP('i', "I");
        ADDKEYMAP('j', "J");
        ADDKEYMAP('k', "K");
        ADDKEYMAP('l', "L");
        ADDKEYMAP('m', "M");
        ADDKEYMAP('n', "N");
        ADDKEYMAP('o', "O");
        ADDKEYMAP('p', "P");
        ADDKEYMAP('q', "Q");
        ADDKEYMAP('r', "R");
        ADDKEYMAP('s', "S");
        ADDKEYMAP('t', "T");
        ADDKEYMAP('u', "U");
        ADDKEYMAP('v', "V");
        ADDKEYMAP('w', "W");
        ADDKEYMAP('x', "X");
        ADDKEYMAP('y', "Y");
        ADDKEYMAP('z', "Z");
    }

    ADDKEYMAP(';', "Semicolon");
    ADDKEYMAP('=', "Equals");
    ADDKEYMAP(',', "Comma");
    ADDKEYMAP('-', "Hyphen");
    ADDKEYMAP('.', "Period");
    ADDKEYMAP('/', "Slash");
    ADDKEYMAP('`', "Tilde");
    ADDKEYMAP('[', "LeftBracket");
    ADDKEYMAP('\\', "Backslash");
    ADDKEYMAP(']', "RightBracket");
    ADDKEYMAP('\'', "Apostrophe");
    ADDKEYMAP(' ', "SpaceBar");

    // AZERTY Keys
    ADDKEYMAP('&', "Ampersand");
    ADDKEYMAP('*', "Asterix");
    ADDKEYMAP('^', "Caret");
    ADDKEYMAP(':', "Colon");
    ADDKEYMAP('$', "Dollar");
    ADDKEYMAP('!', "Exclamation");
    ADDKEYMAP('(', "LeftParantheses");
    ADDKEYMAP(')', "RightParantheses");
    ADDKEYMAP('"', "Quote");
    ADDKEYMAP('_', "Underscore");
    ADDKEYMAP(224, "A_AccentGrave");
    ADDKEYMAP(231, "C_Cedille");
    ADDKEYMAP(233, "E_AccentAigu");
    ADDKEYMAP(232, "E_AccentGrave");
    ADDKEYMAP(167, "Section");

#undef ADDKEYMAP

    return NumMappings;
}

IOToolkit::InputKeyManager& IOToolkit::InputKeyManager::Instance()
{
    static InputKeyManager instance;
    return instance;
}

void IOToolkit::InputKeyManager::GetCodesFromKey(const FKey, const uint32*& KeyCode, const uint32*& CharCode) const
{
    //CharCode = KeyMapCharToEnum.FindKey(Key);
    //KeyCode = KeyMapVirtualToEnum.FindKey(Key);
}

IOToolkit::FKey IOToolkit::InputKeyManager::GetKeyFromCodes(const uint32 KeyCode, const uint32 CharCode) const
{
    FKey key = EKeys::Invalid;

    if (KeyMapVirtualToEnum.count(KeyCode))
    {
        key = KeyMapVirtualToEnum.at(KeyCode);
    }
    else if (KeyMapCharToEnum.count(CharCode))
    {
        key = KeyMapCharToEnum.at(CharCode);
    }else
    {
        return EKeys::Invalid;
    }
  
    return key;
}

void IOToolkit::InputKeyManager::InitKeyMappings()
{
    static const uint32 MAX_KEY_MAPPINGS(256);
    uint32 KeyCodes[MAX_KEY_MAPPINGS], CharCodes[MAX_KEY_MAPPINGS];
    std::string KeyNames[MAX_KEY_MAPPINGS], CharKeyNames[MAX_KEY_MAPPINGS];

    uint32 const CharKeyMapSize(InputKeyManager::GetCharKeyMap(CharCodes, CharKeyNames, MAX_KEY_MAPPINGS));
    uint32 const KeyMapSize(InputKeyManager::GetKeyMap(KeyCodes, KeyNames, MAX_KEY_MAPPINGS));

    for (uint32 Idx = 0; Idx < KeyMapSize; ++Idx)
    {
        FKey Key(KeyNames[Idx].c_str());

        if (StaticKeys::ValidKey(Key))
        {
            StaticKeys::AddKey(FKeyDetails(Key, Key.GetName()));
        }

        KeyMapVirtualToEnum.insert(std::pair<uint32,FKey>(KeyCodes[Idx], Key));
    }
    for (uint32 Idx = 0; Idx < CharKeyMapSize; ++Idx)
    {
        // repeated linear search here isn't ideal, but it's just once at startup
        const FKey Key(CharKeyNames[Idx].c_str());

        if (StaticKeys::ValidKey(Key))
        {
            KeyMapCharToEnum.insert(std::pair<uint32,FKey>(CharCodes[Idx], Key));
        }
    }
}

const std::map<IOToolkit::uint32, IOToolkit::FKey>& IOToolkit::InputKeyManager::GetKeyMapVirtualToEnum() const
{
    return KeyMapVirtualToEnum;
}

const std::map<IOToolkit::uint32, IOToolkit::FKey>& IOToolkit::InputKeyManager::GetKeyMapCharToEnum() const
{
    return KeyMapCharToEnum;
}

