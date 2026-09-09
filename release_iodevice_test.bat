@echo off

if "%param1%" neq "Win64" (
	if "%param1%" neq "Win32" exit
)

call %cd%\..\IOToolkitENV.bat

set src_root_path=%cd%\..\
set dst_root_path=%cd%\..\%version%\IOToolkit_Test_%param1%\

set src_binaries_path=%src_root_path%Binaries\%param1%\Release\IOStudio\
set src_config_path=%src_binaries_path%Config\

set dst_binaries_path=%dst_root_path%
echo %dst_binaries_path%
set dst_config_path=%dst_binaries_path%Config\

if not exist %dst_binaries_path% md %dst_binaries_path%
copy %src_binaries_path%IOStudio.exe %dst_binaries_path% /y

if not exist %dst_config_path% md %dst_config_path%
copy %src_config_path%\* %dst_config_path% /y

echo IOStudio has been copied to %dst_root_path% successfully.
