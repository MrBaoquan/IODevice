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
    if (std::strftime(timedisplay, sizeof(timedisplay), "%Y.%m.%d-%H.%M.%S", &buf))
    {

    }
    return std::string(timedisplay);
}

void FilterFiles(std::string DirPath, int maxNum = 10)
{
    struct FileInfo
    {
        FileInfo(fs::path& p,std::time_t t):fp(p),time(t){}
        fs::path fp;
        std::time_t time;
    };

    std::vector<FileInfo> files;

    int num = 0;
    std::time_t minval = 0;
    for (auto & dirIt : fs::directory_iterator(DirPath))
    {
        num++;
        fs::path p = dirIt.path();
        auto ftime = fs::last_write_time(p);
        std::time_t cftime = to_time_t(ftime);// decltype(ftime)::clock::to_time_t(ftime); // assuming system_clock
        files.push_back(FileInfo(p, cftime));
    }
    if (num > maxNum)
    {
        struct {
            bool operator()(FileInfo a, FileInfo b) const
            {
                return a.time < b.time;
            }
        } customLess;
        std::sort(files.begin(), files.end(),customLess);
        for (int index = 0;index < num-maxNum;++index)
        {
            fs::remove(files[index].fp);
        }
    }
}

IOToolkit::IOLog::IOLog()
{
    try
    {
        std::string logDir = Paths::Instance().GetLogDir();
        if (!fs::exists(logDir))
        {
            fs::create_directory(logDir);
        }
        std::string logFilePath = logDir + "IODevice.log";
        std::fstream fp;
        fp.open(logFilePath);
        if(fp)
        {
            fp.close();
           
            std::string nowTime = GetDateTimeString(logFilePath);
            std::string newName = logDir + "IODevice-backup-"+nowTime+".log";
            rename(logFilePath.data(), newName.data());
        }
        IOLogger = spd::basic_logger_mt("IO_Logger", logFilePath);
		IOLogger->flush_on(spdlog::level::warn);
    }
    catch (const spd::spdlog_ex&)
    {

    }
}

IOToolkit::IOLog::~IOLog()
{
    FilterFiles(Paths::Instance().GetLogDir());
}

void IOToolkit::IOLog::MakeReference()
{
	if (IOLogger) { return; }
	try
	{
		std::string logDir = Paths::Instance().GetLogDir();
		if (!fs::exists(logDir))
		{
			fs::create_directory(logDir);
		}
		std::string logFilePath = logDir + "IODevice.log";
		IOLogger = spd::basic_logger_mt("IO_Logger", logFilePath);
		IOLogger->flush_on(spdlog::level::warn); 
	}
	catch (const spd::spdlog_ex&)
	{
	}
}
