/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once

#include <atomic>
// null, no cost dummy "mutex" and dummy "atomic" int

namespace spdlog {
namespace details {
struct null_mutex
{
    void lock() {}
    void unlock() {}
    bool try_lock()
    {
        return true;
    }
};

struct null_atomic_int
{
    int value;
    null_atomic_int() = default;

    explicit null_atomic_int(int val)
        : value(val)
    {
    }

    int load(std::memory_order) const
    {
        return value;
    }

    void store(int val)
    {
        value = val;
    }
};

} // namespace details
} // namespace spdlog
