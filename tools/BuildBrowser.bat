@echo off
rem The browser helper for Video Screens, Music Players and Browser Screens (tools\browser, docs/sandbox/MEDIA.md), published to
rem bin\Release\net10.0\Browser beside the game. "dotnet build" never builds it, so after a change under tools\browser (or a new worktree) run this,
rem or Start.bat, which calls it. It publishes only when the helper is missing or older than its source; "tools\BuildBrowser.bat force" always does.
rem About 500 MB of CEF from NuGet the first time on a PC, from the package cache after that. The game refuses a helper from other source and says so on the screens.
setlocal
cd /d "%~dp0.."
set "OUT=bin\Release\net10.0\Browser"
set BROWSER_OLD=1
if /i not "%~1"=="force" if exist "%OUT%\SlopBrowser.dll" powershell -NoProfile -Command "$t = (Get-Item '%OUT%\SlopBrowser.dll').LastWriteTime; if ((Get-ChildItem tools\browser -File) + (Get-Item Source\Media\WebScrub.cs) | Where-Object { $_.LastWriteTime -gt $t }) { exit 1 } else { exit 0 }" && set BROWSER_OLD=0
if "%BROWSER_OLD%"=="0" (
    echo The video browser is up to date.
    exit /b 0
)
rem (a helper still running holds its files: the game quits it 20 s after the last screen, and closing the game ends it)
tasklist /fi "imagename eq SlopBrowser.exe" 2>nul | find /i "SlopBrowser.exe" >nul && (
    echo The video browser is running: close the game first.
    exit /b 1
)
echo Building the video browser...
if not exist bin mkdir bin
dotnet publish tools\browser\SlopBrowser.csproj -c Release -o "%OUT%" -nologo -v q > bin\browser.log 2>&1
if errorlevel 1 (
    echo The video browser didn't build: see bin\browser.log. Game tracks still play; links and web pages won't.
    findstr /c:"error" bin\browser.log
    exit /b 1
)
echo The video browser is built: %OUT%
exit /b 0
