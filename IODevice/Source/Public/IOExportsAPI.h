/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#ifdef IODEVICEEXPORTS
#define IOAPI __declspec(dllexport)
#else
#define IOAPI __declspec(dllimport)
#endif // DEVICEEXPORTS