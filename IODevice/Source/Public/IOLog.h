/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include "spdlog/spdlog.h"

namespace DevelopHelper
{

class IOLog
{
public:
    static IOLog& Instance();
    void Log(std::string msg);
    void Warning(std::string msg);
    void Error(std::string msg);

	void ReleaseLogger();
private:
    IOLog();
    ~IOLog();

	void MakeReference();
    std::shared_ptr<spdlog::logger> IOLogger = nullptr;
};

};