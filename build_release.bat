@echo off
setlocal
cd /d "%~dp0"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "MYTODO_OUT=%~dp0dist\MyToDoWidget-0.3.0-win-x64"
set "MYTODO_ZIP=%~dp0dist\MyToDoWidget-0.3.0-win-x64.zip"
where dotnet >nul 2>&1
if errorlevel 1 goto missing_sdk
echo Building MyToDoWidget 0.3.0 for Windows x64...
dotnet publish "src\MyToDoWidget\MyToDoWidget.csproj" -c Release -m:1 -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o "%MYTODO_OUT%"
if errorlevel 1 goto build_failed
if not exist "%MYTODO_OUT%\templates" mkdir "%MYTODO_OUT%\templates"
copy /y "templates\MyToDo_Template.csv" "%MYTODO_OUT%\templates\" >nul
copy /y "docs\QUICK_START_KO.txt" "%MYTODO_OUT%\QUICK_START_KO.txt" >nul
copy /y "LICENSE" "%MYTODO_OUT%\LICENSE.txt" >nul
if exist "licenses" xcopy /e /i /y "licenses" "%MYTODO_OUT%\licenses" >nul
powershell -NoProfile -Command "Compress-Archive -LiteralPath $env:MYTODO_OUT -DestinationPath $env:MYTODO_ZIP -Force"
if errorlevel 1 goto zip_failed
powershell -NoProfile -Command "(Get-FileHash -LiteralPath $env:MYTODO_ZIP -Algorithm SHA256).Hash | Set-Content -LiteralPath ($env:MYTODO_ZIP + '.sha256') -Encoding ASCII"
echo.
echo Build complete: %MYTODO_OUT%\MyToDoWidget.exe
echo ZIP: %MYTODO_ZIP%
pause
exit /b 0
:missing_sdk
echo .NET 10 SDK was not found. Install the SDK or Visual Studio 2026 with .NET desktop development.
pause
exit /b 1
:build_failed
echo.
echo Build failed. Read the error above. Check the SDK version and internet connection.
pause
exit /b 1
:zip_failed
echo.
echo The executable was built, but ZIP creation failed. Close programs using the ZIP and try again.
pause
exit /b 1
