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
rem .NET crash dumps beside the log (Source/App/CrashDumps.cs): a native crash leaves crash-PID.dmp to send to the developer.
set DOTNET_DbgEnableMiniDump=1
set DOTNET_DbgMiniDumpType=1
set "DOTNET_DbgMiniDumpName=%~dp0bin\Release\net10.0\crash-%%p.dmp"
set DOTNET_CreateDumpDiagnostics=1
set DOTNET_EnableCrashReport=1
start "" "bin\Release\net10.0\CallOfSlop.exe" %*
