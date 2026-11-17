@echo off
rem Builds the game (log in bin\build.log). That is all it does by default.
rem The headless self-test only runs if tools\run_selftest.flag exists (results in bin\Release\net10.0\selftest.log).
cd /d "%~dp0.."
if not exist bin mkdir bin
del /q bin\buildcheck.done 2>nul
dotnet build -c Release -nologo -clp:NoSummary > bin\build.log 2>&1
if errorlevel 1 (echo build-failed > bin\buildcheck.done & exit /b 1)
set ST=skipped
if exist tools\run_selftest.flag (bin\Release\net10.0\CallOfSlop.exe --selftest & call set ST=%%errorlevel%%)
echo selftest-exit %ST% > bin\buildcheck.done
