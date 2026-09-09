#pragma once

#if defined(_WIN32)
#define IODEVICE_PLATFORM_WINDOWS 1
#else
#define IODEVICE_PLATFORM_WINDOWS 0
#endif

#if defined(__ANDROID__)
#define IODEVICE_PLATFORM_ANDROID 1
#else
#define IODEVICE_PLATFORM_ANDROID 0
#endif

#if !IODEVICE_PLATFORM_WINDOWS && !IODEVICE_PLATFORM_ANDROID
#define IODEVICE_PLATFORM_POSIX 1
#else
#define IODEVICE_PLATFORM_POSIX 0
#endif

#if defined(_WIN64) || defined(__x86_64__) || defined(__aarch64__)
#define IODEVICE_PLATFORM_64BIT 1
#else
#define IODEVICE_PLATFORM_64BIT 0
#endif

#if IODEVICE_PLATFORM_WINDOWS
#define IODEVICE_PLUGIN_CALL __stdcall
#define IODEVICE_EXPORT __declspec(dllexport)
#define IODEVICE_IMPORT __declspec(dllimport)
#define IODEVICE_DLL_IMPORT __declspec(dllimport)
#else
#define IODEVICE_PLUGIN_CALL
#if defined(__GNUC__) || defined(__clang__)
#define IODEVICE_EXPORT __attribute__((visibility("default")))
#else
#define IODEVICE_EXPORT
#endif
#define IODEVICE_IMPORT
#define IODEVICE_DLL_IMPORT
#endif