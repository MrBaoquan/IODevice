/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once

#include "spdlog/common.h"

#include "spdlog/details/log_msg.h"
#include "spdlog/sinks/sink.h"

#include <array>
#include <string>
#include <syslog.h>

namespace spdlog {
namespace sinks {
/**
 * Sink that write to syslog using the `syscall()` library call.
 *
 * Locking is not needed, as `syslog()` itself is thread-safe.
 */
class syslog_sink : public sink
{
public:
    //
    syslog_sink(const std::string &ident = "", int syslog_option = 0, int syslog_facility = LOG_USER)
        : _ident(ident)
    {
        _priorities[static_cast<size_t>(level::trace)] = LOG_DEBUG;
        _priorities[static_cast<size_t>(level::debug)] = LOG_DEBUG;
        _priorities[static_cast<size_t>(level::info)] = LOG_INFO;
        _priorities[static_cast<size_t>(level::warn)] = LOG_WARNING;
        _priorities[static_cast<size_t>(level::err)] = LOG_ERR;
        _priorities[static_cast<size_t>(level::critical)] = LOG_CRIT;
        _priorities[static_cast<size_t>(level::off)] = LOG_INFO;

        // set ident to be program name if empty
        ::openlog(_ident.empty() ? nullptr : _ident.c_str(), syslog_option, syslog_facility);
    }

    ~syslog_sink() override
    {
        ::closelog();
    }

    syslog_sink(const syslog_sink &) = delete;
    syslog_sink &operator=(const syslog_sink &) = delete;

    void log(const details::log_msg &msg) override
    {
        ::syslog(syslog_prio_from_level(msg), "%s", msg.raw.str().c_str());
    }

    void flush() override {}

private:
    std::array<int, 7> _priorities;
    // must store the ident because the man says openlog might use the pointer as is and not a string copy
    const std::string _ident;

    //
    // Simply maps spdlog's log level to syslog priority level.
    //
    int syslog_prio_from_level(const details::log_msg &msg) const
    {
        return _priorities[static_cast<size_t>(msg.level)];
    }
};
} // namespace sinks

// Create and register a syslog logger
template<typename Factory = default_factory>
inline std::shared_ptr<logger> syslog_logger(
    const std::string &logger_name, const std::string &syslog_ident = "", int syslog_option = 0, int syslog_facility = (1 << 3))
{
    return Factory::template create<sinks::syslog_sink>(logger_name, syslog_ident, syslog_option, syslog_facility);
}
} // namespace spdlog
