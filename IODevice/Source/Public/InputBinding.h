#pragma once

#include <functional>
#include "InputCoreTypes.h"

namespace DevelopHelper
{


/** Utility delegate class to allow binding to either a C++ function */
template<class DelegateType>
struct TInputUnifiedDelegate
{
    TInputUnifiedDelegate() {};
    TInputUnifiedDelegate(DelegateType const& D) : FuncDelegate(D) {};
   // TInputUnifiedDelegate(DynamicDelegateType const& D) : FuncDynDelegate(D) {};

    /** Returns if either the native or dynamic delegate is bound */
    inline bool IsBound() const
    {
        return (FuncDelegate?true:false);
    }

    ///** Binds a native delegate and unbinds any bound dynamic delegate */
    inline void BindDelegate(DelegateType& delegate)
    {
        FuncDelegate = delegate;
    }


protected:
    /** Holds the delegate to call. */
    DelegateType FuncDelegate;
};

typedef std::function<void(float)> FInputAxisHandlerSignature;

/** Unified delegate specialization for float axis events. */
struct FInputAxisUnifiedDelegate : public TInputUnifiedDelegate<FInputAxisHandlerSignature>
{
    /** Execute function for the axis unified delegate. */
    inline void Execute(const float AxisValue) const
    {
        if (FuncDelegate)
        {
            FuncDelegate(AxisValue);
        }
       /* else if (FuncDynDelegate.IsBound())
        {
            FuncDynDelegate.Execute(AxisValue);
        }*/
    }
};



typedef std::function<void(void)> InputActionHandlerSignature;
typedef std::function<void(FKey)> InputActionHandleerWithKeySignature;

struct InputActionUnifiedDelegate
{
public:
    InputActionUnifiedDelegate():boundDelegateType(EBoundDelegate::Unbound){}
    InputActionUnifiedDelegate(InputActionHandlerSignature const& D) :FuncDelegate(D), boundDelegateType(EBoundDelegate::Delegate){}
    InputActionUnifiedDelegate(InputActionHandleerWithKeySignature const& D):FuncDelegateWithKey(D),boundDelegateType(EBoundDelegate::DelegateWithKey){}

    inline void BindDelegate(InputActionHandlerSignature& delegate)
    {
        boundDelegateType = EBoundDelegate::Delegate;
        FuncDelegate = delegate;
    }
    inline void BindDelegate(InputActionHandleerWithKeySignature& delegate)
    {
        boundDelegateType = EBoundDelegate::DelegateWithKey;
        FuncDelegateWithKey = delegate;
    }

    /** Returns if either the native or dynamic delegate is bound */
    inline bool IsBound() const
    {
        switch (boundDelegateType)
        {
        case EBoundDelegate::Delegate:
            return FuncDelegate ? true : false;

        case EBoundDelegate::DelegateWithKey:
            return FuncDelegateWithKey ? true : false;
        }

        return false;
    }

    inline void Execute(const FKey Key) const
    {
        switch (boundDelegateType)
        {
        case EBoundDelegate::Delegate:
            if (FuncDelegate)
            {
                FuncDelegate();
            }
            break;

        case EBoundDelegate::DelegateWithKey:
            if (FuncDelegateWithKey)
            {
                FuncDelegateWithKey(Key);
            }
            break;

        case EBoundDelegate::DynamicDelegate:
       /*     if (FuncDynDelegate.IsBound())
            {
                FuncDynDelegate.Execute(Key);
            }*/
            break;
        }
    }


private:
    /**
     * Holds the delegate to call. 
     */
    InputActionHandlerSignature FuncDelegate;

    /**
     * Holds the delegate that wants to know the key to call
     */
    InputActionHandleerWithKeySignature FuncDelegateWithKey;


    enum class EBoundDelegate:unsigned __int8
    {
        Unbound,
        Delegate,
        DelegateWithKey,
        DynamicDelegate
    };

    EBoundDelegate boundDelegateType;

};

/**
    * 输入绑定基类
    */

struct FInputBinding
{
    /** Whether the binding should consume the input or allow it to pass to another component */
    uint8 bConsumeInput : 1;

    FInputBinding()
        : bConsumeInput(true)
    {}

};

/** Binds a delegate to an axis mapping. */
struct FInputAxisBinding : public FInputBinding
{
    /** The axis mapping being bound to. */
    std::string AxisName;

    /**
    * The delegate bound to the axis.
    * It will be called each frame that the input component is in the input stack
    * regardless of whether the value is non-zero or has changed.
    */
    FInputAxisUnifiedDelegate AxisDelegate;

    /**
    * The value of the axis as calculated during the most recent UPlayerInput::ProcessInputStack
    * if the InputComponent was in the stack, otherwise all values should be 0.
    */
    float AxisValue;

    FInputAxisBinding()
        : FInputBinding()
        , AxisName("NAME_None")
        , AxisValue(0.f)
    { }

    FInputAxisBinding(const std::string InAxisName)
        : FInputBinding()
        , AxisName(InAxisName)
        , AxisValue(0.f)
    { }
};


/** Binds a delegate to a raw float axis mapping. */
struct FInputAxisKeyBinding : public FInputBinding
{
    /**
    * The value of the axis as calculated during the most recent UPlayerInput::ProcessInputStack
    * if the InputComponent containing the binding was in the stack, otherwise the value will be 0.
    */
    float AxisValue;

    /** The axis being bound to. */
    FKey AxisKey;

    /**
    * The delegate bound to the axis.
    * It will be called each frame that the input component is in the input stack
    * regardless of whether the value is non-zero or has changed.
    */
    FInputAxisUnifiedDelegate AxisDelegate;

    FInputAxisKeyBinding()
        : FInputBinding()
        , AxisValue(0.f)
    { }

    FInputAxisKeyBinding(const FKey InAxisKey)
        : FInputBinding()
        , AxisValue(0.f)
        , AxisKey(InAxisKey)
    {
        
    }
};



};
