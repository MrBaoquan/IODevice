#pragma once

#include "spdlog/spdlog.h"
#ifdef _WIN32
#include "spdlog/sinks/wincolor_sink.h"
#else
#include "spdlog/sinks/ansicolor_sink.h"
#endif

namespace {
using namespace spdlog::sinks;
#ifdef _WIN32
using stdout_color_sink_mt = wincolor_stdout_sink_mt;
using stdout_color_sink_st = wincolor_stdout_sink_st;
using stderr_color_sink_mt = wincolor_stderr_sink_mt;
using stderr_color_sink_st = wincolor_stderr_sink_st;
#else
using stdout_color_sink_mt = ansicolor_stdout_sink_mt;
using stdout_color_sink_st = ansicolor_stdout_sink_st;
using stderr_color_sink_mt = ansicolor_stderr_sink_mt;
using stderr_color_sink_st = ansicolor_stderr_sink_st;

#endif
} // namespace

namespace spdlog {
template<typename Factory = default_factory>
inline std::shared_ptr<logger> stdout_color_mt(const std::string &logger_name)
{
    return Factory::template create<stdout_color_sink_mt>(logger_name);
}
template<typename Factory = default_factory>
inline std::shared_ptr<logger> stdout_color_st(const std::string &logger_name)
{
    return Factory::template create<stdout_color_sink_st>(logger_name);
}

template<typename Factory = default_factory>
inline std::shared_ptr<logger> stderr_color_mt(const std::string &logger_name)
{
    return Factory::template create<stderr_color_sink_mt>(logger_name);
}

template<typename Factory = default_factory>
inline std::shared_ptr<logger> stderr_color_st(const std::string &logger_name)
{
    return Factory::template create<stderr_color_sink_mt>(logger_name);
}
} // namespace spdlog