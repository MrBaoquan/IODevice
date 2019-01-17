#pragma once

#if defined(_WIN32)

#include "spdlog/details/null_mutex.h"
#include "spdlog/sinks/base_sink.h"

#include <winbase.h>

#include <mutex>
#include <string>

namespace spdlog {
namespace sinks {
/*
 * MSVC sink (logging using OutputDebugStringA)
 */
template<class Mutex>
class msvc_sink : public base_sink<Mutex>
{
public:
    explicit msvc_sink() {}

protected:
    void _sink_it(const details::log_msg &msg) override
    {
        OutputDebugStringA(msg.formatted.c_str());
    }

    void _flush() override {}
};

using msvc_sink_mt = msvc_sink<std::mutex>;
using msvc_sink_st = msvc_sink<details::null_mutex>;

} // namespace sinks
} // namespace spdlog

#endif
