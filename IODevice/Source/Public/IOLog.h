/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include "spdlog/spdlog.h"

namespace IOToolkit
{

class IOLog
{
public:
    static IOLog& Instance();

    int SetLogDir(std::string InLogDir);

    void Log(std::string msg);
    void Warning(std::string msg);
    void Error(std::string msg);

	void ReleaseLogger();

private:
    IOLog();
    ~IOLog();

    std::string logDir = "Invalid";
    void RenameIODeviceLogName();
	void MakeReference(bool bForce = false);
    std::shared_ptr<spdlog::logger> IOLogger = nullptr;
};

};