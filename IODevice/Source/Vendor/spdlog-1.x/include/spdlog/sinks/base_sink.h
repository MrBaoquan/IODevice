/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
//
// base sink templated over a mutex (either dummy or real)
// concrete implementation should only override the _sink_it method.
// all locking is taken care of here so no locking needed by the implementers..
//

#include "spdlog/common.h"
#include "spdlog/details/log_msg.h"
#include "spdlog/formatter.h"
#include "spdlog/sinks/sink.h"

namespace spdlog {
namespace sinks {
template<class Mutex>
class base_sink : public sink
{
public:
    base_sink() = default;

    base_sink(const base_sink &) = delete;
    base_sink &operator=(const base_sink &) = delete;

    void log(const details::log_msg &msg) SPDLOG_FINAL override
    {
        std::lock_guard<Mutex> lock(_mutex);
        _sink_it(msg);
    }

    void flush() SPDLOG_FINAL override
    {
        std::lock_guard<Mutex> lock(_mutex);
        _flush();
    }

protected:
    virtual void _sink_it(const details::log_msg &msg) = 0;
    virtual void _flush() = 0;
    Mutex _mutex;
};
} // namespace sinks
} // namespace spdlog
