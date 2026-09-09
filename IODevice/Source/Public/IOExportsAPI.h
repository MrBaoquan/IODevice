/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#include "IOPlatform.h"

#ifdef IODEVICEEXPORTS
#define IOAPI IODEVICE_EXPORT
#else
#define IOAPI IODEVICE_IMPORT
#endif // DEVICEEXPORTS