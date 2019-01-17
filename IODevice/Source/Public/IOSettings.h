#pragma once
#include "IOExportsAPI.h"
#include "ExportCoreTypes.h"

namespace DevelopHelper
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
