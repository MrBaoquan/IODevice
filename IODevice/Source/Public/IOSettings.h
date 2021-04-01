/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-8-10 17:00
 */

#pragma once
#include "IOExportsAPI.h"
#include "ExportCoreTypes.h"

namespace IOToolkit
{
    class IOAPI IOSettings
    {
    public:
        static IOSettings& Instance();

        /** Set IODevice.xml file path */
        int SetIOConfigPath(const char* InPath);

    private:
        IOSettings(){}

    };
};
