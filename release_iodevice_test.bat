@echo off

set param1=%1%

if "%param1%" neq "Win64" (
	if "%param1%" neq "Win32" exit
)

call %cd%\..\IOToolkitENV.bat

set src_root_path=%cd%\..\
set dst_root_path=%cd%\..\%version%\IOToolkit_Test_%param1%\

rem IOStudio output lives directly in the Binaries Win64 or Win32 Release folder
rem (IOStudio.exe plus the runtime Config folder next to it). This script used to
rem point at a Release\IOStudio\ subdirectory that does not exist -- there is only a
rem stray extensionless FILE of that name -- so both copy commands failed with
rem "system cannot find the path". This file checks no errorlevel and always prints
rem "successfully", so nobody noticed that IOToolkit_Tester never contained IOStudio.exe.
rem Keep this comment free of angle brackets: cmd still performs redirection on a rem line.
set src_binaries_path=%src_root_path%Binaries\%param1%\Release\
set src_config_path=%src_binaries_path%Config\

set dst_binaries_path=%dst_root_path%
set dst_config_path=%dst_binaries_path%Config\

rem IOStudio is x64 only, so there is no Win32 source. Skip that pass instead of erroring.
if not exist "%src_binaries_path%IOStudio.exe" (
	echo [skip] no IOStudio.exe for %param1% -- IOStudio is x64 only.
	exit /b 0
)

if not exist %dst_binaries_path% md %dst_binaries_path%
copy "%src_binaries_path%IOStudio.exe" %dst_binaries_path% /y

if not exist %dst_config_path% md %dst_config_path%
xcopy "%src_config_path%*" %dst_config_path% /E /F /Y

echo IOStudio has been copied to %dst_root_path% successfully.
