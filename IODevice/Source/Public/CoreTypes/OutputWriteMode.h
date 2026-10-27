/** Copyright (c) mrma617@gmail.com
 *  Output write policy shared by C++, C and C# wrappers.
 */
#pragma once

#include <cstdint>

namespace IOToolkit
{
enum class EOutputWriteMode : uint8_t
{
    /** Apply Key.SlewRate when configured (> 0); otherwise write immediately. */
    Configured = 0,
    /** Always bypass SlewRate. Intended for emergency stop, reset and explicit overrides. */
    Immediate = 1,
    /** Write each physical Key to its configured SafeValue using SafeStopPolicy/SafeStopRate. */
    SafeStop = 2
};
}
