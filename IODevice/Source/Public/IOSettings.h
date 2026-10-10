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

        /** Set runtime root directory containing Config/, Logs/, and ExternalLibraries/. */
        int SetIORuntimeRoot(const char* InRuntimeRoot);

        /**
         * Set plugin config root only (i.e. <InRoot>/Config), leaving the module
         * directory / ExternalLibraries / Logs untouched. Used by the -project
         * product-pack flow so device-layer config comes from the pack while native
         * libraries stay next to the module.
         */
        int SetPluginConfigRoot(const char* InRoot);

        /**
         * Set Log Directory
         */
        int SetIOLogDir(const char* InLogDir);

    private:
        IOSettings(){}

    };
};
