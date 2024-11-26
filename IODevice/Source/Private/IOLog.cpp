/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#include "IOLog.h"
#include <iostream>
#include <fstream>
#include <filesystem>
#include <algorithm>
#include <chrono>
#include "Paths.hpp"
#include "spdlog/sinks/simple_file_sink.h"

namespace fs = std::filesystem;
namespace spd = spdlog;

IOToolkit::IOLog& IOToolkit::IOLog::Instance()
{
    static IOLog instance;
    return instance;
}


int IOToolkit::IOLog::SetLogDir(std::string InLogDir)
{
	if (!InLogDir.empty() && InLogDir.back() != '\\' && InLogDir.back() != '/') {
		InLogDir.push_back('\\');
	}
    logDir = InLogDir;
	MakeReference(true);
	return 1;
}

void IOToolkit::IOLog::Log(std::string msg)
{
    try
    {
		if (!IOLogger) {
			this->MakeReference();
		}
        if (IOLogger)
        {
            IOLogger->log(spd::level::info, msg);
        }
    }
    catch (const spd::spdlog_ex&)
    {
        return;
    }
}

void IOToolkit::IOLog::Warning(std::string msg)
{
    try
    {
		if (!IOLogger) {
			this->MakeReference();
		}
        if (IOLogger)
        {
            IOLogger->warn(msg);
        }
    }
    catch (const spd::spdlog_ex&)
    {
        return;
    }
}

void IOToolkit::IOLog::Error(std::string msg)
{
    try
    {
		if (!IOLogger) {
			this->MakeReference();
		}
        if (IOLogger)
        {
            IOLogger->error(msg);
        }
    }
    catch (const spd::spdlog_ex&)
    {
        return;
    }
}


void IOToolkit::IOLog::ReleaseLogger()
{
	spdlog::drop("IO_Logger");
	IOLogger = nullptr;
}

template <typename TP>
std::time_t to_time_t(TP tp)
{
    using namespace std::chrono;
    auto sctp = time_point_cast<system_clock::duration>(tp - TP::clock::now()
        + system_clock::now());
    return system_clock::to_time_t(sctp);
}


const std::string GetDateTimeString(std::string filePath)
{
    auto time = fs::last_write_time(filePath);
    //std::chrono::time_point<std::chrono::system_clock> now();

    std::time_t start_time = to_time_t(time);
    char timedisplay[100];
    struct tm buf;
    errno_t err = localtime_s(&buf, &start_time);
    std::strftime(timedisplay, sizeof(timedisplay), "%Y.%m.%d-%H.%M.%S", &buf);
    return std::string(timedisplay);
}

void FilterFiles(const std::string& DirPath, int maxNum = 10)
{
	if (DirPath == "Invalid") return;

	std::vector<fs::path> files;

	// 收集符合条件的文件
	for (const auto& dirIt : fs::directory_iterator(DirPath))
	{
		if (dirIt.is_regular_file() && dirIt.path().filename().string().rfind("IODevice-", 0) == 0)
		{
			files.push_back(dirIt.path());
		}
	}

	int num = files.size();

	// 如果文件数超过 maxNum，则按时间顺序删除多余的文件
	if (num > maxNum)
	{
		std::sort(files.begin(), files.end(), [](const fs::path& a, const fs::path& b) {
			return fs::last_write_time(a) < fs::last_write_time(b);
			});

		for (int i = 0; i < num - maxNum; ++i)
		{
			fs::remove(files[i]);
		}
	}
}

IOToolkit::IOLog::IOLog() {}

IOToolkit::IOLog::~IOLog(){}


void IOToolkit::IOLog::RenameIODeviceLogName()
{
  try
    {
        std::string logFilePath = logDir + "IODevice.log";
        std::fstream fp;
        fp.open(logFilePath);
        if(fp)
        {
            fp.close();
            std::string nowTime = GetDateTimeString(logFilePath);
            std::string newName = logDir + "IODevice-"+nowTime+".log";
            rename(logFilePath.data(), newName.data());
        }
    }
    catch (const spd::spdlog_ex&)
    {

    }
}

void IOToolkit::IOLog::MakeReference(bool bForce)
{
	if (IOLogger && !bForce) { return; }
    if (IOLogger) ReleaseLogger();
	try
	{
        if (logDir == "Invalid") {
            logDir = Paths::Instance().GetLogDir();
        }
        
        if (!fs::exists(logDir))
		{
			fs::create_directory(logDir);
		}

        RenameIODeviceLogName();
        FilterFiles(logDir);
        
		std::string logFilePath = logDir + "IODevice.log";
		IOLogger = spd::basic_logger_mt("IO_Logger", logFilePath);
		IOLogger->flush_on(spdlog::level::info);
	}
	catch (const spd::spdlog_ex&)
	{
        
	}
}
