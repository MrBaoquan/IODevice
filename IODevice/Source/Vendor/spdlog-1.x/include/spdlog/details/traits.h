/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
//
// Copyright(c) 2018 Gabi Melman.
// Distributed under the MIT License (http://opensource.org/licenses/MIT)
//

#include "stdio.h"
namespace spdlog {
namespace details {
struct console_stdout_trait
{
    static FILE *stream()
    {
        return stdout;
    }
#ifdef _WIN32
    static HANDLE handle()
    {
        return ::GetStdHandle(STD_OUTPUT_HANDLE);
    }
#endif
};

struct console_stderr_trait
{
    static FILE *stream()
    {
        return stderr;
    }
#ifdef _WIN32
    static HANDLE handle()
    {
        return ::GetStdHandle(STD_ERROR_HANDLE);
    }
#endif
};

struct console_mutex_trait
{
    using mutex_t = std::mutex;
    static mutex_t &console_mutex()
    {
        static mutex_t mutex;
        return mutex;
    }
};

struct console_null_mutex_trait
{
    using mutex_t = null_mutex;
    static mutex_t &console_mutex()
    {
        static mutex_t mutex;
        return mutex;
    }
};
} // namespace details
} // namespace spdlog
