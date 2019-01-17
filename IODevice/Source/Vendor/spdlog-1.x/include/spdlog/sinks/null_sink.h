/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once

#include "spdlog/details/null_mutex.h"
#include "spdlog/sinks/base_sink.h"

#include <mutex>

namespace spdlog {
namespace sinks {

template<class Mutex>
class null_sink : public base_sink<Mutex>
{
protected:
    void _sink_it(const details::log_msg &) override {}

    void _flush() override {}
};

using null_sink_mt = null_sink<details::null_mutex>;
using null_sink_st = null_sink<details::null_mutex>;

} // namespace sinks
} // namespace spdlog
