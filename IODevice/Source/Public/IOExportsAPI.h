#ifdef IODEVICEEXPORTS
#define IOAPI __declspec(dllexport)
#else
#define IOAPI __declspec(dllimport)
#endif // DEVICEEXPORTS