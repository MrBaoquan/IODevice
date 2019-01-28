/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once

//
// Async logging using global thread pool
// All loggers created here share same global thread pool.
// Each log message is pushed to a queue along withe a shared pointer to the logger.
// If a logger deleted while having pending messages in the queue, it's actual destruction will defer
// until all its messages are processed by the thread pool.
// This is because each message in the queue holds a shared_ptr to the originating logger.

#include "spdlog/async_logger.h"
#include "spdlog/details/registry.h"
#include "spdlog/details/thread_pool.h"

#include <memory>
namespace spdlog {

// async logger factory - creates async loggers backed with thread pool.
// if a global thread pool doesn't already exist, create it with default queue size of 8192 items and single thread.
struct create_async
{
    template<typename Sink, typename... SinkArgs>
    static std::shared_ptr<async_logger> create(const std::string &logger_name, SinkArgs &&... args)
    {
        using details::registry;

        std::lock_guard<registry::MutexT> lock(registry::instance().tp_mutex());
        auto tp = registry::instance().get_thread_pool();
        if (tp == nullptr)
        {
            tp = std::make_shared<details::thread_pool>(8192, 1);
            registry::instance().set_thread_pool(tp);
        }

        auto sink = std::make_shared<Sink>(std::forward<SinkArgs>(args)...);
        auto new_logger = std::make_shared<async_logger>(logger_name, std::move(sink), std::move(tp), async_overflow_policy::block_retry);
        registry::instance().register_and_init(new_logger);
        return new_logger;
    }
};

template<typename Sink, typename... SinkArgs>
inline std::shared_ptr<spdlog::logger> create_async_logger(const std::string &logger_name, SinkArgs &&... sink_args)
{
    return create_async::create<Sink>(logger_name, std::forward<SinkArgs>(sink_args)...);
}

// set global thread pool. q_size must be power of 2
inline void init_thread_pool(size_t q_size, size_t thread_count)
{
    using details::registry;
    using details::thread_pool;
    auto tp = std::make_shared<thread_pool>(q_size, thread_count);
    registry::instance().set_thread_pool(std::move(tp));
}
} // namespace spdlog
