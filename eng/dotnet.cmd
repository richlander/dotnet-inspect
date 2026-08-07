@echo off
where pwsh.exe >nul 2>&1
if errorlevel 1 goto windowsPowerShell

pwsh.exe -NoProfile -File "%~dp0dotnet.ps1" %*
exit /b %ERRORLEVEL%

:windowsPowerShell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0dotnet.ps1" %*
exit /b %ERRORLEVEL%
