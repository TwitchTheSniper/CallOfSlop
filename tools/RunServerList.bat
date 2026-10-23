@echo off
rem Call of Slop server list ("master server") for your group of friends.
rem In the release zip this file sits next to CallOfSlop.exe; in the source tree it uses the built game.
rem It opens a console window showing the list; close that window to stop it. Log: serverlist.log.
rem Friends add this PC's public address under Settings > Multiplayer > Server lists (or just join any of your
rem group's games by invite code once: the list is learned automatically). UDP port 27050 must be reachable
rem (the list tries UPnP on your router itself). Use a different port with: RunServerList.bat 27051
cd /d "%~dp0"
set EXE=CallOfSlop.exe
if not exist "%EXE%" set EXE=..\bin\Release\net10.0\CallOfSlop.exe
if not exist "%EXE%" (
    echo CallOfSlop.exe not found. Put this file next to CallOfSlop.exe.
    pause
    exit /b 1
)
start "Call of Slop server list" "%EXE%" --master %*
