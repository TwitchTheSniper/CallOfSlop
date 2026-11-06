@echo off
title Call of Slop
cd /d "%~dp0"

rem Build (only recompiles what changed), then launch straight into the lobby.
echo Building Call of Slop...
if not exist bin mkdir bin
dotnet build -c Release -nologo -v q > bin\build.log 2>&1
if errorlevel 1 (
    findstr /c:"error CS" /c:"error NU" /c:"error NETSDK" bin\build.log >nul
    if not errorlevel 1 (
        type bin\build.log
        echo.
        echo Build failed - details above and in bin\build.log
        pause
        exit /b 1
    )
    rem Not a code error: most likely the game is already running and its files are locked.
    if not exist "bin\Release\net10.0\CallOfSlop.exe" (
        type bin\build.log
        pause
        exit /b 1
    )
)
rem Video Screens' browser (tools\browser): about 500 MB the first time, and only a link in the game needs it, so a failure here isn't one for the game.
rem Built again when its source is newer than the built helper: an old helper plays the picture and leaves out whatever came after it.
set BROWSER_OLD=1
if exist "bin\Release\net10.0\Browser\SlopBrowser.dll" powershell -NoProfile -Command "$t = (Get-Item 'bin\Release\net10.0\Browser\SlopBrowser.dll').LastWriteTime; if (Get-ChildItem tools\browser -File | Where-Object { $_.LastWriteTime -gt $t }) { exit 1 } else { exit 0 }" && set BROWSER_OLD=0
if "%BROWSER_OLD%"=="1" (
    echo Building the video browser...
    dotnet publish tools\browser\SlopBrowser.csproj -c Release -o bin\Release\net10.0\Browser -nologo -v q > bin\browser.log 2>&1
)
rem .NET crash dumps beside the log (Source/App/CrashDumps.cs): a native crash leaves crash-PID.dmp to send to the developer.
set DOTNET_DbgEnableMiniDump=1
set DOTNET_DbgMiniDumpType=1
set "DOTNET_DbgMiniDumpName=%~dp0bin\Release\net10.0\crash-%%p.dmp"
set DOTNET_CreateDumpDiagnostics=1
set DOTNET_EnableCrashReport=1
start "" "bin\Release\net10.0\CallOfSlop.exe" %*
