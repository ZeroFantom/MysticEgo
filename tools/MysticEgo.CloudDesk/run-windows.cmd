@echo off
setlocal
cd /d "%~dp0"
set "CLOUDDESK_DOTNET=%~dp0..\..\runtime\win-x64\dotnet.exe"
set "CLOUDDESK_ELECTRON=%~dp0..\..\runtime\electron\electron.exe"
if not exist "%CLOUDDESK_DOTNET%" goto missing
if not exist "%CLOUDDESK_ELECTRON%" goto missing
if /i "%~1"=="--check" goto check
rem Public DNS is used normally. For a private test override, run ensure-hosts.ps1 explicitly.
start "" /wait "%CLOUDDESK_ELECTRON%" .
if errorlevel 1 goto failed
exit /b 0

:check
"%CLOUDDESK_DOTNET%" out\CloudDesk.dll <nul
if errorlevel 1 exit /b 1
start "" /wait "%CLOUDDESK_ELECTRON%" --version
exit /b %errorlevel%

:missing
echo Extract the complete MysticEgo R3.1 archive before starting CloudDesk.
goto stop

:failed
echo CloudDesk could not start.
:stop
pause
exit /b 1
