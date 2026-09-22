@echo off
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0BuildAndDeploy.ps1" %*
set "deployExitCode=%ERRORLEVEL%"
pause
exit /b %deployExitCode%
