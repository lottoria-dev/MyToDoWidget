@echo off
setlocal
rem Run from any working directory. Pass --no-pause for terminal/CI use.
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0build_release.ps1"
set "BUILD_EXIT_CODE=%ERRORLEVEL%"
echo.
if not "%BUILD_EXIT_CODE%"=="0" echo Release build failed. See the error above.
if /I not "%~1"=="--no-pause" pause
exit /b %BUILD_EXIT_CODE%
