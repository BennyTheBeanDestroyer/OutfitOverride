@echo off
rem Builds Outfit Override, copies it to .\release and installs it into Skua's plugins folder.
rem Close Skua first so the plugin file isn't locked.
cd /d "%~dp0"
echo === dotnet SDKs === > build.log
dotnet --list-sdks >> build.log 2>&1
echo === build === >> build.log
dotnet build -c Release -nologo >> build.log 2>&1
echo EXITCODE=%ERRORLEVEL% >> build.log
type build.log
echo.
echo Done. You can close this window.
timeout /t 30
