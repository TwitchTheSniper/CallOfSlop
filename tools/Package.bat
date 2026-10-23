@echo off
rem Builds a shareable copy of the game for testers: bin\package\CallOfSlop-<version>.zip
rem Self-contained (testers don't need .NET installed), Windows x64, and with NO personal files:
rem it is published into a fresh folder, so settings.txt, progress.txt, loadout*.txt, workbench.txt, audio mixes,
rem save backups, portable.txt, logs and smoke screenshots never get in (and any that somehow do are deleted before zipping).
rem (Players' saves live in %APPDATA%\CallOfSlop anyway, see SavePaths.cs; a stray portable.txt would pin them to the folder.)
rem Writes bin\package.done when finished ("package-ok <zip>" or "package-failed").
setlocal
cd /d "%~dp0.."
if not exist bin mkdir bin
del /q bin\package.done 2>nul
set OUT=bin\package\CallOfSlop
if exist bin\package rmdir /s /q bin\package
mkdir bin\package

for /f %%i in ('git rev-parse --short HEAD 2^>nul') do set VER=%%i
if "%VER%"=="" set VER=build
for /f "tokens=1-3 delims=/-. " %%a in ('powershell -NoProfile -Command "Get-Date -Format yyyy-MM-dd"') do set DAY=%%a-%%b-%%c

dotnet publish CallOfSlop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=none -o "%OUT%" -nologo > bin\package.log 2>&1
if errorlevel 1 (echo package-failed > bin\package.done & exit /b 1)

rem Belt and braces: no personal or debug files in the shared copy.
del /q "%OUT%\settings.txt" "%OUT%\progress*.txt" "%OUT%\loadout*.txt" "%OUT%\workbench.txt" "%OUT%\audio_mix.json" "%OUT%\portable.txt" "%OUT%\*.bak" "%OUT%\*.tmp" "%OUT%\*.corrupt" "%OUT%\*.log" "%OUT%\smoke_*.bmp" "%OUT%\selftest.log" 2>nul
if exist "%OUT%\AudioMixes" rmdir /s /q "%OUT%\AudioMixes"
copy /y tools\TESTERS.txt "%OUT%\READ ME FIRST.txt" > nul
copy /y tools\RunServerList.bat "%OUT%\RunServerList.bat" > nul
copy /y tools\masters.txt "%OUT%\masters.txt" > nul

set ZIP=bin\package\CallOfSlop-%DAY%-%VER%.zip
powershell -NoProfile -Command "Compress-Archive -Path '%OUT%' -DestinationPath '%ZIP%' -Force" >> bin\package.log 2>&1
if errorlevel 1 (echo package-failed > bin\package.done & exit /b 1)
echo package-ok %ZIP% > bin\package.done
endlocal
