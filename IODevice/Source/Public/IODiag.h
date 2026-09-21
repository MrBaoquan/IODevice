#pragma once

#include "IOPlatform.h"

// 临时诊断：把生命周期事件打到 logcat（tag=IOToolkitDiag）。
// IOLog 只写内部存储的日志文件，Release 包无 root 读不到，排查用后即删。
#if IODEVICE_PLATFORM_ANDROID
#include <android/log.h>
#define IODIAG(...) __android_log_print(ANDROID_LOG_ERROR, "IOToolkitDiag", __VA_ARGS__)
#else
#define IODIAG(...) ((void)0)
#endif
