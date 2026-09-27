@echo off
setlocal
cd /d "%~dp0"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
dotnet run --project "tests\MyToDoWidget.Checks" -c Release -- "templates\MyToDo_Template.csv"
set "MYTODO_CHECK_EXIT=%ERRORLEVEL%"
pause
exit /b %MYTODO_CHECK_EXIT%
