#pragma once
#include <string>
#include <windows.h>

namespace DevelopHelper
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
            char* module_full_path = new char[MAX_PATH];
            GetModuleFileNameA(InModule, module_full_path, MAX_PATH);

            std::string full_path(module_full_path);
            if (module_full_path)
                delete[] module_full_path;

            size_t pos = full_path.rfind("\\");
            module_dir = full_path.substr(0, pos + 1);
            resource_dir = module_dir + "Resources\\";
            config_dir = module_dir + "Config\\";
            log_dir = module_dir + "Logs\\";
            return *this;
        }
		const std::string& GetModuleDir() const{ return module_dir; }
		const std::string& GetResourceDir() const{ return resource_dir; }
		const std::string& GetConfigDir() const{ return config_dir; }
        const std::string& GetLogDir() const { return log_dir; }
	private:
		Paths()
        {
            char* module_full_path = new char[MAX_PATH];
            GetModuleFileNameA(NULL, module_full_path, MAX_PATH);

            std::string full_path(module_full_path);
            if (module_full_path)
                delete[] module_full_path;

            size_t pos = full_path.rfind("\\");
            module_dir = full_path.substr(0, pos + 1);
            resource_dir = module_dir + "Resources\\";
            config_dir = module_dir + "Config\\";
            log_dir = module_dir + "Logs\\";
        }

        ~Paths() {};

        HMODULE hModule = nullptr;
		std::string module_dir;
		std::string resource_dir;
		std::string config_dir;
        std::string log_dir;
};

}
