@echo off

call release_csharpwrapper_test.bat Win64

cd IODevice
call ../release_iodevice.bat Win64
call ../release_iodevice.bat Win32

call ../release_cwrapper.bat Win64
call ../release_cwrapper.bat Win32

call ../release_csharpwrapper.bat Win32
call ../release_csharpwrapper.bat Win64

call ../release_iodevice_test.bat Win64
call ../release_iodevice_test.bat Win32

cd ../