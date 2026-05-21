/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include <string>
#include "IOPlatform.h"
#if IODEVICE_PLATFORM_WINDOWS
#include <windows.h>
#else
#include <unistd.h>
#ifndef MAX_PATH
#define MAX_PATH 4096
#endif
using HMODULE = void*;
#endif

namespace IOToolkit
{

class Paths
{
	public:
        static Paths& Instance() 
        {
            static Paths instance;
            return instance;
        }

        Paths& SetModule(HMODULE InModule)
        {
            SetDirectoriesFromModule(InModule);
            return *this;
        }
		const std::string& GetModuleDir() const{ return module_dir; }
		const std::string& GetResourceDir() const{ return resource_dir; }
		const std::string& GetConfigDir() const{ return config_dir; }
        const std::string& GetLogDir() const { return log_dir; }
		const std::string& GetExternalLibrariesDir() const { return external_libraries_dir; }
		const std::string& GetExternalLibraryCoreDir() const { return external_library_core_dir; }
	private:
		Paths()
        {
            SetDirectoriesFromModule(nullptr);
        }

        ~Paths() {};

        HMODULE hModule = nullptr;
		std::string module_dir;
		std::string resource_dir;
		std::string config_dir;
        std::string log_dir;
        std::string external_libraries_dir;
        std::string external_library_core_dir;

        static char DirectorySeparator()
        {
    #if IODEVICE_PLATFORM_WINDOWS
            return '\\';
#else
            return '/';
#endif
        }

        static std::string AppendDir(const std::string& base, const char* child)
        {
            return base + child + DirectorySeparator();
        }

        void SetDirectoriesFromFullPath(const std::string& full_path)
        {
            size_t pos = full_path.find_last_of("\\/");
            module_dir = pos == std::string::npos ? std::string() : full_path.substr(0, pos + 1);
            resource_dir = AppendDir(module_dir, "Resources");
            config_dir = AppendDir(module_dir, "Config");
            log_dir = AppendDir(module_dir, "Logs");
            external_libraries_dir = AppendDir(module_dir, "ExternalLibraries");
            external_library_core_dir = AppendDir(external_libraries_dir, "Core");
        }

        void SetDirectoriesFromModule(HMODULE module)
        {
            char module_full_path[MAX_PATH] = { 0 };
#if IODEVICE_PLATFORM_WINDOWS
            GetModuleFileNameA(module, module_full_path, MAX_PATH);
#else
            ssize_t length = readlink("/proc/self/exe", module_full_path, MAX_PATH - 1);
            if (length > 0)
            {
                module_full_path[length] = '\0';
            }
#endif
            SetDirectoriesFromFullPath(module_full_path);
        }
};

}
