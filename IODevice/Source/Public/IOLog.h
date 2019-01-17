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

private:
    IOLog();
    ~IOLog();
    std::shared_ptr<spdlog::logger> IOLogger = nullptr;
};

};