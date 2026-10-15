rem Version: single source of truth is the repo root version.txt.
rem (This was previously hardcoded in three places -- here, build_IOToolkit_v*.bat
rem  and IOUIENV.bat -- two of which differed only in letter case.)
set "version="
set /p version=<"%~dp0..\version.txt"
if not defined version (
    echo [IOToolkitENV] version.txt missing or empty: %~dp0..\version.txt
    exit /b 1
)
