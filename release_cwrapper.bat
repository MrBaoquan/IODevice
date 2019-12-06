@echo off

set param1=%1%

if "%param1%" neq "Win64" (
	if "%param1%" neq "Win32" exit
)

call %cd%\..\IOToolkitENV.bat

set src_root_path=%cd%\..\
set dst_root_path=%cd%\..\%version%\IOToolkit\

set src_binaries_path=%src_root_path%Binaries\%param1%\Release\

set dst_binaries_path=%dst_root_path%binaries\%param1%\

echo %src_binaries_path%
if not exist %dst_binaries_path% md %dst_binaries_path%
copy %src_binaries_path%IODevice_C_Wrapper.dll %dst_binaries_path% /y