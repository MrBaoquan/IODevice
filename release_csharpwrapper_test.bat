@echo off

set param1=%1%

if "%param1%" neq "Win64" (
	 exit
)

call %cd%\IOToolkitENV.bat

set src_root_path=%cd%\
set dst_root_path=%cd%\%version%\IOToolkit_Test_%param1%\

set src_binaries_path=%src_root_path%Binaries\%param1%\Release\

set dst_binaries_path=%dst_root_path%

if not exist %dst_binaries_path% md %dst_binaries_path%
copy %src_binaries_path%IODevice_C_CSharpTest.exe %dst_binaries_path% /y

echo IODevice_C_CSharpTest has beed copied to %dst_root_path% successfully.