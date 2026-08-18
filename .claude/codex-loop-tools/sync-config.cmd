@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0sync-config.ps1" %*
exit /b %ERRORLEVEL%
