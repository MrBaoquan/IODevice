rem 为IODevice_Test  提供最新 IOUI

call IOToolkitENV.bat

set platform=Win64

set env=Debug
set dst_core_path=.\Binaries\%platform%\%env%\ExternalLibraries\Core\

if not exist %dst_core_path% md %dst_core_path%

copy .\..\IOUI\Binaries\%platform%\%env%\IOUI-*.dll .\Binaries\%platform%\%env%\ExternalLibraries\ /Y
copy .\..\IOUI\Binaries\%platform%\%env%\Libraries\*.dll .\Binaries\%platform%\%env%\ExternalLibraries\Core\ /Y

set env=Release
set dst_core_path=.\Binaries\%platform%\%env%\ExternalLibraries\Core\
if not exist %dst_core_path% md %dst_core_path%

copy .\..\IOUI\Binaries\%platform%\%env%\IOUI-*.dll .\Binaries\%platform%\%env%\ExternalLibraries\ /Y
copy .\..\IOUI\Binaries\%platform%\%env%\Libraries\*.dll .\Binaries\%platform%\%env%\ExternalLibraries\Core\ /Y


set platform=Win32

set env=Debug
set dst_core_path=.\Binaries\%platform%\%env%\ExternalLibraries\Core\

if not exist %dst_core_path% md %dst_core_path%

copy .\..\IOUI\Binaries\%platform%\%env%\IOUI-*.dll .\Binaries\%platform%\%env%\ExternalLibraries\ /Y
copy .\..\IOUI\Binaries\%platform%\%env%\Libraries\*.dll .\Binaries\%platform%\%env%\ExternalLibraries\Core\ /Y

set env=Release
set dst_core_path=.\Binaries\%platform%\%env%\ExternalLibraries\Core\
if not exist %dst_core_path% md %dst_core_path%

copy .\..\IOUI\Binaries\%platform%\%env%\IOUI-*.dll .\Binaries\%platform%\%env%\ExternalLibraries\ /Y
copy .\..\IOUI\Binaries\%platform%\%env%\Libraries\*.dll .\Binaries\%platform%\%env%\ExternalLibraries\Core\ /Y

pause