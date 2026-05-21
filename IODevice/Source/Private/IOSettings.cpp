/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-8-10 17:00
 */

#include "..\Public\IOSettings.h"
#include "InputSettings.h"
#include "IOLog.h"
#include "Paths.hpp"

/** Copyright (c) 2018 Hefei And Technology Co.,Ltd All rights reserved
 *  Author: MrBaoquan
 *  CreateTime: 2018-8-7 15:39
 *  Email: mrma617@gmail.com
 */
using namespace IOToolkit;

IOSettings & IOSettings::Instance()
{
    static IOSettings instance;
    return instance;
}

int IOSettings::SetIOConfigPath(const char* InPath)
{
    return UInputSettings::Instance().SetConfigPath(InPath);
}

int IOSettings::SetIORuntimeRoot(const char* InRuntimeRoot)
{
    if (!InRuntimeRoot || InRuntimeRoot[0] == '\0') return 0;
    Paths::Instance().SetRuntimeRoot(InRuntimeRoot);
    return 1;
}

int IOToolkit::IOSettings::SetIOLogDir(const char* InLogDir)
{
    return IOLog::Instance().SetLogDir(InLogDir);
}
