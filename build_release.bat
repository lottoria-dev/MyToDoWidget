@echo off
setlocal
cd /d "%~dp0"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "MYTODO_OUT=%~dp0dist\MyToDoWidget-1.1.2-win-x64"
set "MYTODO_ZIP=%~dp0dist\MyToDoWidget-1.1.2-win-x64.zip"
where dotnet >nul 2>&1
if errorlevel 1 goto missing_sdk
echo Building MyToDoWidget 1.1.2 for Windows x64...
dotnet publish "src\MyToDoWidget\MyToDoWidget.csproj" -c Release -m:1 -r win-x64 --self-contained true -p:MyToDoTestBuild=false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o "%MYTODO_OUT%"
if errorlevel 1 goto build_failed
if not exist "%MYTODO_OUT%\templates" mkdir "%MYTODO_OUT%\templates"
copy /y "templates\MyToDo_Template.csv" "%MYTODO_OUT%\templates\" >nul
copy /y "docs\QUICK_START_KO.txt" "%MYTODO_OUT%\QUICK_START_KO.txt" >nul
for %%F in (LICENSE.md THIRD_PARTY_NOTICES.md VALIDATION.md ROADMAP.md) do copy /y "%%F" "%MYTODO_OUT%\" >nul
if not exist "%MYTODO_OUT%\docs" mkdir "%MYTODO_OUT%\docs"
copy /y "docs\DATA_HANDLING.md" "%MYTODO_OUT%\docs\DATA_HANDLING.md" >nul
copy /y "docs\RELEASE_1.1.2.md" "%MYTODO_OUT%\docs\RELEASE_1.1.2.md" >nul
copy /y "docs\TESTING_KO.md" "%MYTODO_OUT%\docs\TESTING_KO.md" >nul
if exist "licenses" xcopy /e /i /y "licenses" "%MYTODO_OUT%\licenses" >nul
powershell -NoProfile -Command "Compress-Archive -LiteralPath $env:MYTODO_OUT -DestinationPath $env:MYTODO_ZIP -Force"
if errorlevel 1 goto zip_failed
powershell -NoProfile -Command "(Get-FileHash -LiteralPath $env:MYTODO_ZIP -Algorithm SHA256).Hash | Set-Content -LiteralPath ($env:MYTODO_ZIP + '.sha256') -Encoding ASCII"
if errorlevel 1 goto hash_failed
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
:hash_failed
echo.
echo The ZIP was created, but checksum generation failed. Check write permissions and try again.
pause
exit /b 1
