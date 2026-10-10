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
        Paths& SetRuntimeRoot(const std::string& InRuntimeRoot)
        {
            SetDirectoriesFromRoot(InRuntimeRoot);
            return *this;
        }
        /// 只重定向「插件配置根」(即 <InConfigRoot>/Config)，不动模块目录、
        /// ExternalLibraries 与 Logs。-project 产品包模式用它把设备层配置切到包内，
        /// 而引擎/中间件动态库仍留在模块目录（Windows 既有行为）。
        /// 同时把原始根记录下来，供宿主在装载插件时经 SetPluginRuntimeRoot 下发。
        Paths& SetConfigRoot(const std::string& InConfigRoot)
        {
            plugin_config_root = NormalizeBaseDir(InConfigRoot);
            config_dir = AppendDir(plugin_config_root, "Config");
            return *this;
        }
		const std::string& GetModuleDir() const{ return module_dir; }
		const std::string& GetResourceDir() const{ return resource_dir; }
		const std::string& GetConfigDir() const{ return config_dir; }
        const std::string& GetPluginConfigRoot() const { return plugin_config_root; }
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
        std::string plugin_config_root;
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

        static std::string NormalizeBaseDir(std::string base)
        {
            if (!base.empty() && base.back() != '\\' && base.back() != '/')
            {
                base.push_back(DirectorySeparator());
            }
            return base;
        }

        void SetDirectoriesFromRoot(const std::string& root_path)
        {
            module_dir = NormalizeBaseDir(root_path);
            resource_dir = AppendDir(module_dir, "Resources");
            config_dir = AppendDir(module_dir, "Config");
            log_dir = AppendDir(module_dir, "Logs");
            external_libraries_dir = AppendDir(module_dir, "ExternalLibraries");
            external_library_core_dir = AppendDir(external_libraries_dir, "Core");
        }

        void SetDirectoriesFromFullPath(const std::string& full_path)
        {
            size_t pos = full_path.find_last_of("\\/");
            SetDirectoriesFromRoot(pos == std::string::npos ? std::string() : full_path.substr(0, pos + 1));
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
