#include "PlayerInput.h"
#include <windows.h>
#include <algorithm>
#include "IOStatics.h"
#include "IODeviceController.h"
#include "Math/Vector.h"
#include "InputSettings.h"

DevelopHelper::PlayerInput& DevelopHelper::PlayerInput::Instance()
{
    static PlayerInput instance;
    return instance;
}

void DevelopHelper::PlayerInput::Initialize()
{
    ForceRebuildingKeyMaps(true);
    const uint8 deviceCount = IODevices::GetDevicesCount();
    for (uint8 deviceIndex=0; deviceIndex < deviceCount; ++deviceIndex)
    {
        std::map<FKey, FKeyState, LessKey> keyStateMap;
        KeyStateMaps.push_back(keyStateMap);
        ActionKeyMaps.push_back(std::map<std::string, FActionKeyDetails>());
        AxisKeyMaps.push_back(std::map<std::string, FAxisKeyDetails>());
    }
}

void DevelopHelper::PlayerInput::Tick(float DeltaSeconds)
{
    ProcessInputStack();
}

void DevelopHelper::PlayerInput::InputKey(FKey& InKey, InputEvent KeyEvent,const uint8 deviceID, float AmountDepressed)
{
    std::map<FKey, FKeyState, LessKey>& KeyStateMap = KeyStateMaps[deviceID];
    KeyStateMap.try_emplace(InKey);
    FKeyState& keyState = KeyStateMap[InKey];
    switch (KeyEvent)
    {
    case IE_Pressed:
    case IE_Repeat:
        keyState.RawValueAccumulator.X = AmountDepressed;
        keyState.EventAccumulator[KeyEvent].push_back(++EventCount);
        if (keyState.bDownPrevious == false)
        {
            // check for doubleclick
            // note, a tripleclick will currently count as a 2nd double click.
            const float WorldRealTimeSeconds = GetTickCount()/1000.f;
            const float deltaTime = WorldRealTimeSeconds - keyState.LastUpDownTransitionTime;
            if (deltaTime < doubleClickTime&&deltaTime>0.01f)
            {
                keyState.EventAccumulator[IE_DoubleClick].push_back(++EventCount);
            }

            // just went down
            keyState.LastUpDownTransitionTime = WorldRealTimeSeconds;
        }
        break;
    case IE_Released:
        {
            const float WorldRealTimeSeconds = GetTickCount() / 1000.f;
            keyState.LastUpDownTransitionTime = WorldRealTimeSeconds;
            keyState.RawValueAccumulator.X = 0.f;
            keyState.EventAccumulator[IE_Released].push_back(++EventCount);
        }        
        break;
    case IE_DoubleClick:
        keyState.RawValueAccumulator.X = AmountDepressed;
        keyState.EventAccumulator[IE_Pressed].push_back(++EventCount);
        keyState.EventAccumulator[IE_DoubleClick].push_back(++EventCount);
        break;
    }
    keyState.SampleCountAccumulator++;
}

void DevelopHelper::PlayerInput::InputAxis(FKey Key, float Delta, float DeltaTime, uint8 deviceID, int32 NumSamples)
{
    if (NumSamples <= 0) { return; }
    std::map<FKey, FKeyState, LessKey>& KeyStateMap = KeyStateMaps[deviceID];
    KeyStateMap.try_emplace(Key);
    FKeyState& keyState = KeyStateMap[Key];
    // look for event edges
    if (keyState.Value.X == 0.f && Delta != 0.f)
    {
        keyState.EventAccumulator[IE_Pressed].push_back(++EventCount);
    }
    else if (keyState.Value.X != 0.f && Delta == 0.f)
    {
        keyState.EventAccumulator[IE_Released].push_back(++EventCount);
    }
    else
    {
        keyState.EventAccumulator[IE_Repeat].push_back(++EventCount);
    }

    // accumulate deltas until processed next
    keyState.SampleCountAccumulator += NumSamples;
    keyState.RawValueAccumulator.X += Delta;

}

const float DevelopHelper::PlayerInput::GetKeyDownTime(const FKey& InKey, uint8 deviceID)
{
    const std::map<FKey, FKeyState, LessKey>& KeyStateMap = KeyStateMaps[deviceID];
    bool bPressed = IsPressed(InKey, deviceID);
    if (KeyStateMap.count(InKey)&&bPressed)
    {
        const FKeyState& keyState = KeyStateMap.at(InKey);
        float curTime = GetTickCount() / 1000.f;
        return curTime - keyState.LastUpDownTransitionTime;
    }
    return 0.0f;
}

float DevelopHelper::PlayerInput::GetKeyValue(FKey InKey, uint8 deviceID) const
{
    const std::map<FKey, FKeyState, LessKey>& KeyStateMap = KeyStateMaps[deviceID];
    if (InKey == EKeys::AnyKey)
    {
        return 0.f;
    }
    if (!KeyStateMap.count(InKey))
    {
        return 0.f;
    }
    FKeyState const* const KeyState = &KeyStateMap.at(InKey);
    return KeyState ? KeyState->Value.X : 0.f;
}

void DevelopHelper::PlayerInput::ProcessInputStack()
{
    // Copy standard KeyStateMap to Others
    if (KeyStateMaps.size() > 1)
    {
        uint8 deviceCount = IODevices::GetDevicesCount();
        std::map<FKey, FKeyState, LessKey> KeyStateMap = KeyStateMaps[0];
        for (int deviceIndex = 1;deviceIndex < deviceCount;++deviceIndex)
        {
            std::map<FKey, FKeyState, LessKey>& tempKeyStateMap = KeyStateMaps[deviceIndex];
            for (auto& It : KeyStateMap)
            {
                tempKeyStateMap.try_emplace(It.first, It.second);
                tempKeyStateMap[It.first] = KeyStateMap[It.first];
            }
        }
    }

    uint8 deviceID = 0;
    for (auto& keyStateMap : KeyStateMaps)
    {
        for (auto& It : keyStateMap)
        {
            const FKey& key = It.first;
            FKeyState* const KeyState = &It.second;

            for (uint8 EventIndex = 0;EventIndex < IE_MAX;++EventIndex)
            {
                KeyState->EventCounts[EventIndex].clear();
                std::swap(KeyState->EventCounts[EventIndex], KeyState->EventAccumulator[EventIndex]);
            }

            if ((KeyState->SampleCountAccumulator > 0))
            {
                // if we had no samples, we'll assume the state hasn't changed
                // except for some axes, where no samples means the mouse stopped moving
                KeyState->RawValue = KeyState->RawValueAccumulator;
                if (KeyState->SampleCountAccumulator == 0)
                {
                    KeyState->EventCounts[IE_Released].push_back(++EventCount);
                }
            }

            ProcessNonAxesKeys(key, KeyState, deviceID);
            KeyState->RawValueAccumulator = Vector(0.f, 0.f, 0.f);
            KeyState->SampleCountAccumulator = 0;
        }
        deviceID++;
    }

    EventCount = 0;

    struct FAxisDelegateDetails
    {
        FInputAxisUnifiedDelegate Delegate;
        float Value;

        FAxisDelegateDetails(const FInputAxisUnifiedDelegate& InDelegate, const float InValue)
            : Delegate(InDelegate)
            , Value(InValue)
        {
        }
    };

    static std::vector<FAxisDelegateDetails> AxisDelegates;
    static std::vector<FDelegateDispatchDetails> NonAxisDelegates;
    static std::vector<FDelegateDispatchDetails> FoundChords;
    static std::set<FKey, LessKey> KeysToConsume;
    deviceID = 0;
    for (auto& deviceIt : IODevices::GetDevcies())
    {
        
        IODeviceDetails& deviceDetails = deviceIt.second;
        deviceID = deviceDetails.GetDevice().GetID();
        for (int32 ActionIndex = 0;ActionIndex < deviceDetails.GetNumActionBindings();++ActionIndex)
        {
            GetChordsForAction(deviceDetails.GetActionBinding(ActionIndex), deviceID, FoundChords, KeysToConsume);
        }

        //deviceDetails.ConditionalBuildKeyMap(this);

        for (auto& keyBinding : deviceDetails.KeyBindings)
        {
            GetChordForKey(keyBinding,FoundChords,KeysToConsume,deviceID);
        }

        for (uint32 ChordIndex = 0; ChordIndex < FoundChords.size(); ++ChordIndex)
        {
            const FDelegateDispatchDetails& FoundChord = FoundChords[ChordIndex];
            bool bFireDelegate = true;

            // If this is a paired action (implements both pressed and released) then we ensure that only one chord is handling the pairing
            if (FoundChord.SourceAction && FoundChord.SourceAction->IsPaired())
            {
                FActionKeyDetails& KeyDetails = ActionKeyMaps[deviceID].at(FoundChord.SourceAction->ActionName);
                if (!KeyDetails.CapturingChord.Key.IsValid() || KeyDetails.CapturingChord == FoundChord.Chord || !IsPressed(KeyDetails.CapturingChord.Key,deviceID))
                {
                    if (FoundChord.SourceAction->KeyEvent == IE_Pressed)
                    {
                        KeyDetails.CapturingChord = FoundChord.Chord;
                    }
                    else
                    {
                        KeyDetails.CapturingChord.Key = EKeys::Invalid;
                    }
                }
                else
                {
                    bFireDelegate = false;
                }
            }

            if (bFireDelegate && FoundChords[ChordIndex].ActionDelegate.IsBound())
            {
                FoundChords[ChordIndex].FoundIndex = static_cast<uint32>(NonAxisDelegates.size());
                NonAxisDelegates.push_back(FoundChords[ChordIndex]);
            }
        }
        FoundChords.clear();

        // Run though game axis bindings and accumulate axis values
        for (FInputAxisBinding& AB : deviceDetails.AxisBindings)
        {
            AB.AxisValue = DetermineAxisValue(AB, deviceID, KeysToConsume);
           
            if (AB.AxisDelegate.IsBound())
            {
                AxisDelegates.push_back(FAxisDelegateDetails(AB.AxisDelegate, AB.AxisValue));
            }
        }
        for (FInputAxisKeyBinding& AxisKeyBinding : deviceDetails.AxisKeyBindings)
        {
            if (!IsKeyConsumed(AxisKeyBinding.AxisKey,deviceID))
            {
                AxisKeyBinding.AxisValue = GetKeyValue(AxisKeyBinding.AxisKey,deviceID);
             
                if (AxisKeyBinding.bConsumeInput)
                {
                    KeysToConsume.insert(AxisKeyBinding.AxisKey);
                }
            }

            if (AxisKeyBinding.AxisDelegate.IsBound())
            {
                AxisDelegates.push_back(FAxisDelegateDetails(AxisKeyBinding.AxisDelegate, AxisKeyBinding.AxisValue));
            }
        }
    }


    struct FDelegateDispatchDetailsSorter
    {
        bool operator()(const FDelegateDispatchDetails& A, const FDelegateDispatchDetails& B) const
        {
            return (A.EventIndex == B.EventIndex ? A.FoundIndex < B.FoundIndex : A.EventIndex < B.EventIndex);
        }
    };

    if (NonAxisDelegates.size() > 0)
    {
        std::sort(NonAxisDelegates.begin(), NonAxisDelegates.end(), FDelegateDispatchDetailsSorter());
    }
    
    for (const FDelegateDispatchDetails& Details : NonAxisDelegates)
    {
        if (Details.ActionDelegate.IsBound())
        {
            Details.ActionDelegate.Execute(Details.Chord.Key);
        }
    }

    // Now dispatch delegates for summed axes
    for (const FAxisDelegateDetails& Details : AxisDelegates)
    {
        if (Details.Delegate.IsBound())
        {
            Details.Delegate.Execute(Details.Value);
        }
    }

    /** Process end */

    FinishProcessingPlayerInput();
    NonAxisDelegates.clear();
    AxisDelegates.clear();
}

const DevelopHelper::FKeyState DevelopHelper::PlayerInput::GetKeyState(const FKey& InKey, uint8 deviceID) const
{
    const std::map<FKey, FKeyState, LessKey>& KeyStateMap = KeyStateMaps[deviceID];

    if (InKey == EKeys::AnyKey)
    {
        // Is there any key that is down
        for (const std::pair<FKey, FKeyState>& KeyStatePair : KeyStateMap)
        {
            if (!KeyStatePair.first.IsFloatAxis() && !KeyStatePair.first.IsVectorAxis() && KeyStatePair.second.bDown)
            {
                return KeyStatePair.second;
            }
        }
    }
    else if (KeyStateMap.count(InKey))
    {
        return KeyStateMap.at(InKey);
    }
    return FKeyState();
}

void DevelopHelper::PlayerInput::ProcessNonAxesKeys(FKey Inkey, FKeyState* KeyState, uint8 deviceID)
{
    KeyState->Value.X = MassageAxisInput(Inkey, KeyState->RawValue.X,deviceID);
    
    int32 const PressDelta =static_cast<uint32>(KeyState->EventCounts[IE_Pressed].size() - KeyState->EventCounts[IE_Released].size());
    if (PressDelta < 0)
    {
        // If this is negative, we definitely released
        KeyState->bDown = false;
    }
    else if (PressDelta > 0)
    {
        // If this is positive, we defini tely pressed
        KeyState->bDown = true;
    }
    else
    {
        // If this is 0, we maintain state
        KeyState->bDown = KeyState->bDownPrevious;
    }
}

void DevelopHelper::PlayerInput::GetChordForKey(const FInputKeyBinding& KeyBinding, std::vector<struct FDelegateDispatchDetails>& FoundChords, std::set<FKey,LessKey>& KeysToConsume, uint8 deviceID)
{
    bool bConsumeInput = false;
    std::map<FKey, FKeyState, LessKey>& KeyStateMap = KeyStateMaps[deviceID];
    if (KeyBinding.Chord.Key == EKeys::AnyKey)
    {
        for (const std::pair<FKey, FKeyState>& KeyStatePair : KeyStateMap)
        {
            const FKey& Key = KeyStatePair.first;
            if (!Key.IsFloatAxis() && !Key.IsVectorAxis() && !KeyStatePair.second.bConsumed)
            {
                FInputKeyBinding SubKeyBinding(KeyBinding);
                SubKeyBinding.Chord.Key = Key;
                GetChordForKey(SubKeyBinding, FoundChords,KeysToConsume, deviceID);
            }
        }
    }else
    {
        if (!IsKeyConsumed(KeyBinding.Chord.Key,deviceID))
        {
            if ((KeyBinding.Chord.bAlt == false || IsAltPressed())
                && (KeyBinding.Chord.bCtrl == false || IsCtrlPressed())
                && (KeyBinding.Chord.bShift == false || IsShiftPressed())
                && (KeyBinding.Chord.bCmd == false || IsCmdPressed())
                && KeyEventOccurred(KeyBinding.Chord.Key, KeyBinding.KeyEvent, EventIndices, deviceID))
            {
                FDelegateDispatchDetails FoundChord(
                    EventIndices[0],
                    static_cast<uint32>(FoundChords.size()),
                    KeyBinding.Chord,
                    KeyBinding.KeyDelegate,
                    KeyBinding.KeyEvent);
                FoundChords.push_back(FoundChord);

                for (int32 EventsIndex = 1; EventsIndex < static_cast<int32>(EventIndices.size()); ++EventsIndex)
                {
                    FoundChord.EventIndex = EventIndices[EventsIndex];
                    FoundChords.push_back(FoundChord);
                }

                EventIndices.clear();
                bConsumeInput = true;
            }
        }
    }
    if (KeyBinding.bConsumeInput && (bConsumeInput || !(KeyBinding.Chord.bAlt || KeyBinding.Chord.bCtrl || KeyBinding.Chord.bShift || KeyBinding.Chord.bCmd || KeyBinding.KeyEvent == InputEvent::IE_DoubleClick)))
    {
        KeysToConsume.emplace(KeyBinding.Chord.Key);
    }
}

bool DevelopHelper::PlayerInput::KeyEventOccurred(FKey Key, InputEvent Event, std::vector<uint32>& InEventIndices, uint8 deviceID) const
{
    const std::map<FKey, FKeyState, LessKey>& KeyStateMap = KeyStateMaps[deviceID];
    if(KeyStateMap.size()>0)
    {
        if (!KeyStateMap.count(Key))
        {
            return false;
        } 
        const FKeyState& keyState = KeyStateMap.at(Key);
        if (keyState.EventCounts[Event].size() > 0)
        {
            InEventIndices = keyState.EventCounts[Event];
            return true;
        }
    }
   
    return false;
}

bool DevelopHelper::PlayerInput::IsKeyConsumed(FKey InKey, uint8 deviceID) const
{
    const std::map<FKey, FKeyState, LessKey>& KeyStateMap = KeyStateMaps[deviceID];
    if (InKey == EKeys::AnyKey)
    {
        // Is there any key that is consumed
        for (const std::pair<FKey, FKeyState>& KeyStatePair : KeyStateMap)
        {
            if (KeyStatePair.second.bConsumed)
            {
                return true;
            }
        }
    }
    else
    {
        if (!KeyStateMap.count(InKey))
        {
            return false;
        }
        const FKeyState& KeyState = KeyStateMap.at(InKey);
        return KeyState.bConsumed;
    }

    return false;
}

void DevelopHelper::PlayerInput::FinishProcessingPlayerInput()
{
    for (auto& KeyStateMap:KeyStateMaps)
    {
        // finished processing input for this frame, clean up for next update
        for (auto& It : KeyStateMap)
        {
            FKeyState& keyState = It.second;
            keyState.IsKeyDown = false;
            keyState.IsKeyUp = false;
            if (keyState.bDown != keyState.bDownPrevious)
            {
                if (keyState.bDown)
                {
                    keyState.IsKeyDown = true;
                }else
                {
                    keyState.IsKeyUp = true;
                }
            }
            keyState.bDownPrevious = keyState.bDown;
           
        }
    }
}

float DevelopHelper::PlayerInput::MassageAxisInput(FKey Key, float RawValue, uint8 deviceID)
{
    float NewVal = RawValue;
    std::map<FKey, FKeyState, LessKey>& KeyStateMap = KeyStateMaps[deviceID];
    
    std::map<FKey, FInputKeyProperties, LessKey>& KeyProperties = KeysProperties[deviceID];

    if (KeyProperties.count(Key))
    {
        FInputKeyProperties const* const KeyProps = &KeyProperties.at(Key);
        if (NewVal > 0)
        {
            NewVal = max(0.f, NewVal - KeyProps->DeadZone) / (1.f - KeyProps->DeadZone);
        }else
        {
            NewVal = -max(0.f, -NewVal - KeyProps->DeadZone) / (1.f - KeyProps->DeadZone);
        }
        if (KeyProps->Exponent != 1.f)
        {
            NewVal = std::sin(NewVal)*std::powf(std::abs(NewVal), KeyProps->Exponent);
        }
        NewVal *= KeyProps->Sensitivity;
        if (KeyProps->bInvert)
        {
            NewVal *= -1.f;
        }
    }
    
    
    // special handling for mouse input
    if ((Key == EKeys::MouseX) || (Key == EKeys::MouseY))
    {
        if (KeyStateMap.count(Key))
        {
            FKeyState* const KeyState = &KeyStateMap.at(Key);
            if (KeyState->SampleCountAccumulator == 0)
            {
                NewVal = 0.f;
            }else
            {
                NewVal /= KeyState->SampleCountAccumulator;
            }
        }
    }

    return NewVal;
}

void DevelopHelper::PlayerInput::GetChordsForAction(const FInputActionBinding& ActionBinding, uint8 deviceID, std::vector<struct FDelegateDispatchDetails>& FoundChords, std::set<FKey,LessKey>& KeysToConsume)
{
    ConditionalBuildKeyMappings();

    std::map<FKey, FKeyState, LessKey>& KeyStateMap = KeyStateMaps[deviceID];

    if (!ActionKeyMaps[deviceID].count(ActionBinding.ActionName)) { return; }
    FActionKeyDetails* KeyDetails = &ActionKeyMaps[deviceID].at(ActionBinding.ActionName);
    if (KeyDetails)
    {
        for (const FInputActionKeyMapping& KeyMapping : KeyDetails->Actions)
        {
            if (KeyMapping.Key == EKeys::AnyKey)
            {
                for (const std::pair<FKey, FKeyState>& KeyStatePair : KeyStateMap)
                {
                    const FKey& Key = KeyStatePair.first;
                    if (!Key.IsFloatAxis() && !Key.IsVectorAxis() && !KeyStatePair.second.bConsumed)
                    {
                        FInputActionKeyMapping SubKeyMapping(KeyMapping);
                        SubKeyMapping.Key = Key;
                        GetChordsForKeyMapping(SubKeyMapping, ActionBinding, deviceID, FoundChords, KeysToConsume);
                    }
                }
            }
            else
            {
                if (!IsKeyConsumed(KeyMapping.Key, deviceID))
                {
                    GetChordsForKeyMapping(KeyMapping, ActionBinding, deviceID, FoundChords, KeysToConsume);
                }
            }
        }
    }
}

void DevelopHelper::PlayerInput::GetChordsForKeyMapping(const FInputActionKeyMapping& KeyMapping, const FInputActionBinding& ActionBinding, uint8 deviceID, std::vector<FDelegateDispatchDetails>& FoundChords, std::set<FKey,LessKey>& KeysToConsume)
{
    bool bConsumeInput = false;
    if (KeyEventOccurred(KeyMapping.Key, ActionBinding.KeyEvent, EventIndices, deviceID))
    {
        const FInputChord Chord(KeyMapping.Key, KeyMapping.bShift, KeyMapping.bCtrl, KeyMapping.bAlt, KeyMapping.bCmd);
        FDelegateDispatchDetails FoundChord(
            EventIndices[0]
            ,static_cast<uint32>(FoundChords.size())
            ,Chord
            ,ActionBinding.ActionDelegate
            ,ActionBinding.KeyEvent
            ,&ActionBinding);

            FoundChords.push_back(FoundChord);
            //for (int32 EventsIndex = 1; EventsIndex < static_cast<int32>(EventIndices.size()); ++EventsIndex)
            //{
            //    FoundChord.EventIndex = EventIndices[EventsIndex];
            //    FoundChords.push_back(FoundChord);
            //}
            bConsumeInput = true;
    }
    if ((bConsumeInput || ActionBinding.KeyEvent == InputEvent::IE_DoubleClick))
    {
        KeysToConsume.insert(KeyMapping.Key);
    }

    EventIndices.clear();
}

float DevelopHelper::PlayerInput::DetermineAxisValue(const FInputAxisBinding& AxisBinding, uint8 deviceID, std::set<FKey,LessKey>& KeysToConsume)
{
    std::map<std::string, FAxisKeyDetails>& AxisKeyMap = AxisKeyMaps[deviceID];
    ConditionalBuildKeyMappings();

    float AxisValue = 0.f;

    if (!AxisKeyMap.count(AxisBinding.AxisName)) { return AxisValue; }
    FAxisKeyDetails* KeyDetails = &AxisKeyMap.at(AxisBinding.AxisName);
    if (KeyDetails)
    {
        for (int32 AxisIndex = 0; AxisIndex <static_cast<int32>(KeyDetails->KeyMappings.size()); ++AxisIndex)
        {
            const FInputAxisKeyMapping& KeyMapping = (KeyDetails->KeyMappings)[AxisIndex];
            if (!IsKeyConsumed(KeyMapping.Key,deviceID))
            {
               
                AxisValue += GetKeyValue(KeyMapping.Key,deviceID) * KeyMapping.Scale;
                
                if (AxisBinding.bConsumeInput)
                {
                    KeysToConsume.insert(KeyMapping.Key);
                }
            }
        }

        if (KeyDetails->bInverted)
        {
            AxisValue *= -1.f;
        }
    }
    
    return AxisValue;
}

void DevelopHelper::PlayerInput::ConditionalBuildKeyMappings_Internal()
{
    struct
    {
        void Build(const std::vector<std::vector<FInputActionKeyMapping>>& InMappings, std::vector<std::map<std::string, FActionKeyDetails>>& InKeyMap)
        {
            int deviceIndex = 0;
            for (const std::vector<FInputActionKeyMapping>& Mappings : InMappings)
            {
                std::map<std::string, FActionKeyDetails>& KeyMap = InKeyMap[deviceIndex++];
                for (const FInputActionKeyMapping& ActionMapping : Mappings)
                {
                    KeyMap.try_emplace(ActionMapping.ActionName);
                    FActionKeyDetails& keyDetails = KeyMap.at(ActionMapping.ActionName);
                    std::vector<FInputActionKeyMapping>& KeyMappings = keyDetails.Actions;
                    KeyMappings.push_back(ActionMapping);
                }
            }
        }
    } ActionMappingsUtility;

    ActionMappingsUtility.Build(ActionMappings, ActionKeyMaps);


    struct
    {
        void Build(const std::vector<std::vector<FInputAxisKeyMapping>>& InMappings, std::vector<std::map<std::string, FAxisKeyDetails>>& InAxisMap)
        {
            int deviceIndex = 0;
            for (const std::vector<FInputAxisKeyMapping>& Mappings : InMappings)
            {
                std::map<std::string, FAxisKeyDetails>& AxisMap = InAxisMap[deviceIndex++];
                for (const FInputAxisKeyMapping& AxisMapping : Mappings)
                {
                    bool bAdd = true;
                    AxisMap.try_emplace(AxisMapping.AxisName);
                    FAxisKeyDetails& KeyDetails = AxisMap.at(AxisMapping.AxisName);
                    for (const FInputAxisKeyMapping& KeyMapping : KeyDetails.KeyMappings)
                    {
                        if (KeyMapping.Key == AxisMapping.Key)
                        {
                            //UE_LOG(LogInput, Error, TEXT("Duplicate mapping of key %s for axis %s"), *KeyMapping.Key.ToString(), *AxisMapping.AxisName.ToString());
                            bAdd = false;
                            break;
                        }
                    }
                    if (bAdd)
                    {
                        KeyDetails.KeyMappings.push_back(AxisMapping);
                    }
                }
            }
        }
    } AxisMappingsUtility;

    AxisMappingsUtility.Build(AxisMappings, AxisKeyMaps);

    bKeyMapsBuilt = true;
}

void DevelopHelper::PlayerInput::FlushPressedKeys()
{
    std::map<FKey,int,LessKey> PressedKeys;
    int deviceIndex = -1;
    for (auto& KeyStateMap:KeyStateMaps)
    {
        deviceIndex++;
        for (auto& It:KeyStateMap)
        {
            const FKeyState& KeyState = It.second;
            if (KeyState.bDown)
            {
                PressedKeys.insert(std::pair<FKey, int>(It.first,deviceIndex));
            }
        }
    }
    

    // we may have gotten here as a result of executing an input bind.  in order to ensure that the simulated IE_Released events
    // we're about to fire are actually propagated to the game, we need to clear the bExecutingBindCommand flag
    if (PressedKeys.size() > 0)
    {
        for (auto& It:PressedKeys)
        {
            FKey key = It.first;
            //InputKey(key, IE_Released, It.second );
        }
    }

    float TimeSeconds = GetTickCount() / 1000.f;
    for (auto& KeyStateMap : KeyStateMaps)
    {
        for (auto& It:KeyStateMap)
        {
            FKeyState& KeyState = It.second;
            KeyState.RawValue = Vector(0.f, 0.f, 0.f);
            KeyState.bDown = false;
            KeyState.bDownPrevious = false;
            KeyState.LastUpDownTransitionTime = TimeSeconds;
        }
    }
}

void DevelopHelper::PlayerInput::ForceRebuildingKeyMaps(const bool bRestoreDefaults /*= false*/)
{
    if (bRestoreDefaults)
    {
        KeysProperties = UInputSettings::Instance().KeyProperties;
        ActionMappings = UInputSettings::Instance().ActionMappings;
        AxisMappings = UInputSettings::Instance().AxisMappings;
    }

    ActionKeyMaps.clear();
    bKeyMapsBuilt = false;
}

bool DevelopHelper::PlayerInput::GetKey(const FKey& InKey, uint8 deviceID)
{
    return IsPressed(InKey, deviceID);
}

bool DevelopHelper::PlayerInput::GetKeyDown(const FKey& InKey, uint8 deviceID)
{
    const FKeyState keyState = GetKeyState(InKey, deviceID);
    return keyState.IsKeyDown;
}

bool DevelopHelper::PlayerInput::GetKeyUp(const FKey& InKey, uint8 deviceID)
{
    const FKeyState keyState = GetKeyState(InKey, deviceID);
    return keyState.IsKeyUp;
}

float DevelopHelper::PlayerInput::GetAxis(const char* AxisName, uint8 deviceID)
{
    std::map<std::string, FAxisKeyDetails>& AxisKeyMap = AxisKeyMaps[deviceID];
    ConditionalBuildKeyMappings();

    float AxisValue = 0.f;

    if (!AxisKeyMap.count(AxisName)) { return AxisValue; }
    FAxisKeyDetails* KeyDetails = &AxisKeyMap.at(AxisName);
    if (KeyDetails)
    {
        for (int32 AxisIndex = 0; AxisIndex < static_cast<int32>(KeyDetails->KeyMappings.size()); ++AxisIndex)
        {
            const FInputAxisKeyMapping& KeyMapping = (KeyDetails->KeyMappings)[AxisIndex];
            if (!IsKeyConsumed(KeyMapping.Key, deviceID))
            {

                AxisValue += GetKeyValue(KeyMapping.Key, deviceID) * KeyMapping.Scale;
            }
        }

        if (KeyDetails->bInverted)
        {
            AxisValue *= -1.f;
        }
    }

    return AxisValue;
}

float DevelopHelper::PlayerInput::GetAxisKey(const FKey& InKey, uint8 deviceID)
{
    return GetKeyValue(InKey, deviceID);
}

bool DevelopHelper::PlayerInput::IsPressed(const FKey& InKey, uint8 deviceID) const
{
    const FKeyState keyState = GetKeyState(InKey, deviceID);
    return keyState.bDown;
}

bool DevelopHelper::PlayerInput::IsAltPressed() const
{
    return IsPressed(EKeys::LeftAlt,0) || IsPressed(EKeys::RightAlt,0);
}

bool DevelopHelper::PlayerInput::IsCtrlPressed() const
{
    return IsPressed(EKeys::LeftControl,0) || IsPressed(EKeys::RightControl,0);
}

bool DevelopHelper::PlayerInput::IsShiftPressed() const
{
    return IsPressed(EKeys::LeftShift,0) || IsPressed(EKeys::RightShift,0);
}

bool DevelopHelper::PlayerInput::IsCmdPressed() const
{
    return IsPressed(EKeys::LeftCommand,0) || IsPressed(EKeys::RightCommand,0);
}
