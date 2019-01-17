/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include <vector>
#include <set>
#include <string>
#include "IODeviceDetails.h"
#include "KeyState.h"
#include "LessKey.h"
#include "InputBinding/InputActionBinding.h"
#include "InputChord.h"
#include "CoreTypes/InputKeyProperties.h"

namespace DevelopHelper
{

struct FInputActionKeyMapping
{
    std::string ActionName;

    /** true if one of the Shift keys must be down when the KeyEvent is received to be acknowledged */
    uint8 bShift : 1;

    /** true if one of the Ctrl keys must be down when the KeyEvent is received to be acknowledged */
    uint8 bCtrl : 1;

    /** true if one of the Alt keys must be down when the KeyEvent is received to be acknowledged */
    uint8 bAlt : 1;

    /** true if one of the Cmd keys must be down when the KeyEvent is received to be acknowledged */
    uint8 bCmd : 1;

    /** Key to bind it to. */
    FKey Key;

    bool operator==(const FInputActionKeyMapping& Other) const
    {
        return (ActionName == Other.ActionName
            && Key == Other.Key
            && bShift == Other.bShift
            && bCtrl == Other.bCtrl
            && bAlt == Other.bAlt
            && bCmd == Other.bCmd);
    }

    bool operator<(const FInputActionKeyMapping& Other) const
    {
        bool bResult = false;
        if (ActionName < Other.ActionName)
        {
            bResult = true;
        }
        return bResult;
    }

    FInputActionKeyMapping(const std::string InActionName = "NAME_None", const FKey InKey = EKeys::Invalid, const bool bInShift = false, const bool bInCtrl = false, const bool bInAlt = false, const bool bInCmd = false)
        : ActionName(InActionName)
        , bShift(bInShift)
        , bCtrl(bInCtrl)
        , bAlt(bInAlt)
        , bCmd(bInCmd)
        , Key(InKey)
    {}
};

struct FInputAxisKeyMapping
{

    /** Friendly name of axis, e.g "MoveForward" */
    std::string AxisName;

    /** Multiplier to use for the mapping when accumulating the axis value */
    float Scale;

    /** Key to bind it to. */
    FKey Key;

    bool operator==(const FInputAxisKeyMapping& Other) const
    {
        return (AxisName == Other.AxisName
            && Key == Other.Key
            && Scale == Other.Scale);
    }

    bool operator<(const FInputAxisKeyMapping& Other) const
    {
        bool bResult = false;
        if (AxisName < Other.AxisName)
        {
            bResult = true;
        }
        else if (AxisName == Other.AxisName)
        {
            if (Key == Other.Key)
            {
                bResult = (Scale < Other.Scale);

            }
        }
        return bResult;
    }

    FInputAxisKeyMapping(const std::string InAxisName = "NAME_None", const FKey InKey = EKeys::Invalid, const float InScale = 1.f)
        : AxisName(InAxisName)
        , Scale(InScale)
        , Key(InKey)
    {}
};

class PlayerInput
{
public:
    static PlayerInput& Instance();
    
    void Initialize();

    void Tick(float DeltaSeconds);

    void InputKey(FKey& InKey, InputEvent KeyEvent,const uint8 deviceID, float AmountDepressed = 1.0f);

    void InputAxis(FKey Key, float Delta, float DeltaTime, uint8 deviceID, int32 NumSamples);

    const float GetKeyDownTime(const FKey& InKey, uint8 deviceID);

    /** @return current state of the InKey */
    float GetKeyValue(FKey InKey,uint8 deviceID) const;

    /** Flushes the current key state. */
    void FlushPressedKeys();

    /** Clear the current cached key maps and rebuild from the source arrays. */
    void ForceRebuildingKeyMaps(const bool bRestoreDefaults = false);


    bool GetKey(const FKey& InKey, uint8 deviceID);
    bool GetKeyDown(const FKey& InKey, uint8 deviceID);
    bool GetKeyUp(const FKey& InKey, uint8 deviceID);


    float GetAxis(const char* AxisName, uint8 deviceID);
    float GetAxisKey(const FKey& InKey, uint8 deviceID);

    /** @return true if InKey is currently held */
    bool IsPressed(const FKey& InKey, uint8 deviceID) const;

    /** @return true if alt key is pressed */
    bool IsAltPressed() const;

    /** @return true if ctrl key is pressed */
    bool IsCtrlPressed() const;

    /** @return true if shift key is pressed */
    bool IsShiftPressed() const;

    /** @return true if cmd key is pressed */
    bool IsCmdPressed() const;

public:
    /** This player's version of the Action Mappings */
    std::vector<std::vector<struct FInputActionKeyMapping>> ActionMappings;

    /** This player's version of Axis Mappings */
    std::vector<std::vector<struct FInputAxisKeyMapping>> AxisMappings;

    /** Static empty array to be able to return from GetKeysFromAction when there are no keys mapped to the requested action name */
    static const std::vector<FInputActionKeyMapping> NoKeyMappings;

    /** List of Axis Mappings that have been inverted */
    std::vector<std::string> InvertedAxis;

private:

    struct FDelegateDispatchDetails
    {
        uint32 EventIndex;
        uint32 FoundIndex;

        FInputChord Chord;
        InputActionUnifiedDelegate ActionDelegate;
        const FInputActionBinding* SourceAction;
        InputEvent KeyEvent;
        
        FDelegateDispatchDetails(const uint32 InEventIndex, const uint32 InFoundIndex, const FInputChord& InChord, const InputActionUnifiedDelegate& InDelegate, const InputEvent InKeyEvent, const FInputActionBinding* InSourceAction = NULL)
            : EventIndex(InEventIndex)
            , FoundIndex(InFoundIndex)
            , ActionDelegate(InDelegate)
            , SourceAction(InSourceAction)
            , Chord(InChord)
            , KeyEvent(InKeyEvent)
        {}

        /*FDelegateDispatchDetails(const uint32 InEventIndex, const uint32 InFoundIndex, const FInputTouchUnifiedDelegate& InDelegate, const FVector InLocation, const ETouchIndex::Type InFingerIndex)
        : EventIndex(InEventIndex)
        , FoundIndex(InFoundIndex)
        , TouchDelegate(InDelegate)
        , TouchLocation(InLocation)
        , FingerIndex(InFingerIndex)
        {}

        FDelegateDispatchDetails(const uint32 InEventIndex, const uint32 InFoundIndex, const FInputGestureUnifiedDelegate& InDelegate, const float InValue)
        : EventIndex(InEventIndex)
        , FoundIndex(InFoundIndex)
        , GestureDelegate(InDelegate)
        , GestureValue(InValue)
        {}*/
    };

    /** Runtime struct that caches the list of mappings for a given Action Name and the capturing chord if applicable */
    struct FActionKeyDetails
    {
        /** List of all action key mappings that correspond to the action name in the containing map */
        std::vector<FInputActionKeyMapping> Actions;

        /** For paired actions only, this represents the chord that is currently held and when it is released will represent the release event */
        FInputChord CapturingChord;
    };

    /** Internal structure for storing axis config data. */
    std::vector<std::map<FKey, FInputKeyProperties,LessKey>> KeysProperties;

    /** Runtime struct that caches the list of mappings for a given Axis Name and whether that axis is currently inverted */
    struct FAxisKeyDetails
    {
        /** List of all axis key mappings that correspond to the axis name in the containing map */
        std::vector<FInputAxisKeyMapping> KeyMappings;

        /** Whether this axis should invert its outputs */
        uint8 bInverted : 1;

        FAxisKeyDetails()
            : bInverted(false)
        {
        }
    };

    std::vector<std::map<FKey, FKeyState, LessKey>> KeyStateMaps;
    const float doubleClickTime = 0.15f;
    
    /** A counter used to track the order in which events occurred since the last time the input stack was processed */
    uint32 EventCount;

    //static int deviceID;

    // Temporary array used as part of input processing
    std::vector<uint32> EventIndices;

    /** Map of Action Name to details about the keys mapped to that action */
    std::vector<std::map<std::string, FActionKeyDetails>> ActionKeyMaps;

    /** Map of Axis Name to details about the keys mapped to that axis */
    std::vector<std::map<std::string, FAxisKeyDetails>> AxisKeyMaps;

    uint8 bKeyMapsBuilt : 1;

private:
    PlayerInput() {};
    ~PlayerInput() {}

    void ProcessInputStack();
    
    const FKeyState GetKeyState(const FKey& InKey, uint8 deviceID) const;

    /** Process non-axes keystates */
    void ProcessNonAxesKeys(FKey Inkey, FKeyState* KeyState,uint8 deviceID);

    /** Add a chord if key event occurred. */
    void GetChordForKey(const FInputKeyBinding& KeyBinding, std::vector<struct FDelegateDispatchDetails>& FoundChords, std::set<FKey,LessKey>& KeysToConsume, uint8 deviceID);

    /** Detect whether a key event occurrable */
    bool KeyEventOccurred(FKey Key, InputEvent Event, std::vector<uint32>& EventIndices, uint8 deviceID) const;

    bool IsKeyConsumed(FKey InKey, uint8 deviceID) const;

    /** Finish process input */
    void FinishProcessingPlayerInput();

   /**
    * Given raw keystate value, returns the "massaged" value. Override for any custom behavior,
    * such as input changes dependent on a particular game state.
    */
    virtual float MassageAxisInput(FKey Key, float RawValue, uint8 deviceID);

   /** Collects the chords and the delegates they invoke for an action binding
    * @param ActionBinding - the action to determine whether it occurred
    * @param FoundChords - the list of chord/delegate pairs to add to
    * @param KeysToConsume - array to collect the keys associated with this binding that should be consumed
    */
    void GetChordsForAction(const FInputActionBinding& ActionBinding, uint8 deviceID, std::vector<struct FDelegateDispatchDetails>& FoundChords, std::set<FKey,LessKey>& KeysToConsume);


   /** Helper function for GetChordsForAction to examine each keymapping that belongs to the ActionBinding
    * @param KeyMapping - the key mapping to determine whether it occured
    * @param ActionBinding - the action to determine whether it occurred
    * @param FoundChords - the list of chord/delegate pairs to add to
    * @param KeysToConsume - array to collect the keys associated with this binding that should be consumed
    */
    void GetChordsForKeyMapping(const FInputActionKeyMapping& KeyMapping, const FInputActionBinding& ActionBinding, uint8 deviceID, std::vector<FDelegateDispatchDetails>& FoundChords, std::set<FKey,LessKey>& KeysToConsume);


   /** Returns the summed values of all the components of this axis this frame
    * @param AxisBinding - the action to determine if it ocurred
    * @param KeysToConsume - array to collect the keys associated with this binding that should be consumed
    */
    float DetermineAxisValue(const FInputAxisBinding& AxisBinding, uint8 deviceID, std::set<FKey,LessKey>& KeysToConsume);

    void ConditionalBuildKeyMappings()
    {
        if (!bKeyMapsBuilt)
        {
            ConditionalBuildKeyMappings_Internal();
        }
    }

    void ConditionalBuildKeyMappings_Internal();

};


};