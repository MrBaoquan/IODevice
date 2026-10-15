@echo off
rem ---- iostudio command entry -------------------------------------------
rem Why a shim instead of putting the install dir itself on PATH:
rem   1) Windows resolves PATHEXT in order (.EXE before .CMD), so a same-named
rem      IOStudio.exe next to this file would always win and the command name
rem      could not be pinned to 'iostudio';
rem   2) the install dir holds ~200 files that have no business on PATH.
rem Only this bin dir goes on PATH, so exactly one command is exposed.
rem
rem '%~dp0' keeps it relocatable if the install dir ever moves.
rem A .cmd also makes the shell WAIT for the GUI app and forwards its exit code,
rem so typing it by hand does not hand the prompt straight back.
rem
rem NOTE: keep this file ASCII-only and CRLF. cmd.exe reads .cmd in the OEM
rem codepage, so UTF-8 comments get mangled into bogus commands.
"%~dp0..\IOStudio.exe" %*
exit /b %ERRORLEVEL%
