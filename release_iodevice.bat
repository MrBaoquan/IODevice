@echo off

set param1=%1%

if "%param1%" neq "Win64" (
	if "%param1%" neq "Win32" exit
)

call %cd%\..\IOToolkitENV.bat

set src_root_path=%cd%\..\
set dst_root_path=%cd%\..\%version%\IOToolkit\

set src_include_path=%src_root_path%IODevice\Source\Public\
set src_binaries_path=%src_root_path%Binaries\%param1%\Release\
set src_config_path=%src_root_path%Config\

set dst_include_path=%dst_root_path%include\
set dst_binaries_path=%dst_root_path%binaries\%param1%\
set dst_config_path=%dst_binaries_path%Config\

if not exist %dst_include_path% md %dst_include_path%
copy %src_include_path%IOSettings.h %dst_include_path% /y
copy %src_include_path%CoreTypes.inl %dst_include_path% /y
copy %src_include_path%ExportCoreTypes.h %dst_include_path% /y
copy %src_include_path%InputCoreTypes.h %dst_include_path% /y
copy %src_include_path%IODevice.h %dst_include_path% /y
copy %src_include_path%IODeviceController.h %dst_include_path% /y
copy %src_include_path%IODeviceImpl.hpp %dst_include_path% /y
copy %src_include_path%IOExportsAPI.h %dst_include_path% /y

if not exist %dst_binaries_path% md %dst_binaries_path%
copy %src_binaries_path%IODevice.dll %dst_binaries_path% /y
copy %src_binaries_path%IODevice.lib %dst_binaries_path% /y

if not exist %dst_config_path% md %dst_config_path%
copy %src_config_path%* %dst_config_path% /y
echo IODevice has beed copied to %dst_root_path% successfully.
