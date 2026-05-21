#pragma once

#include <chrono>
#include <cstdint>

namespace IOToolkit
{
class IOClock
{
public:
    static std::uint64_t GetMilliseconds()
    {
        using namespace std::chrono;
        return static_cast<std::uint64_t>(duration_cast<milliseconds>(steady_clock::now().time_since_epoch()).count());
    }

    static double GetSeconds()
    {
        return static_cast<double>(GetMilliseconds()) / 1000.0;
    }
};
}