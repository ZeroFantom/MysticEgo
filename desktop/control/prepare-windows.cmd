@echo off
setlocal
cd /d "%~dp0" || exit /b 50
powershell.exe -NoProfile -NonInteractive -Command "if ((Get-FileHash -LiteralPath 'payload.tgz' -Algorithm SHA256).Hash -ne '%~1') {exit 51}"
if errorlevel 1 exit /b 51
tar.exe -xzf payload.tgz
if errorlevel 1 exit /b 52
del /q payload.tgz
