#include "..\Public\IOSettings.h"
#include "InputSettings.h"
/** Copyright (c) 2018 Hefei And Technology Co.,Ltd All rights reserved
 *  Author: MrBaoquan
 *  CreateTime: 2018-8-7 15:39
 *  Email: mrma617@gmail.com
 */
using namespace DevelopHelper;

IOSettings & IOSettings::Instance()
{
    static IOSettings instance;
    return instance;
}

int IOSettings::SetIOConfigPath(const char* InPath)
{
    return UInputSettings::Instance().SetConfigPath(InPath);
}
