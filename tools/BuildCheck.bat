@echo off
rem Builds the game and runs the headless self-test (results in bin\Release\net10.0\selftest.log).
rem The window smoke test only runs if tools\run_smoke.flag exists (it takes over the screen for ~80 s).
cd /d "%~dp0.."
if not exist bin mkdir bin
del /q bin\buildcheck.done 2>nul
dotnet build -c Release -nologo -clp:NoSummary > bin\build.log 2>&1
if errorlevel 1 (echo build-failed > bin\buildcheck.done & exit /b 1)
bin\Release\net10.0\CallOfSlop.exe --selftest
set ST=%errorlevel%
set SM=skipped
if exist tools\run_smoke.flag (bin\Release\net10.0\CallOfSlop.exe --smoketest & set SM=done)
echo selftest-exit %ST% smoketest %SM% > bin\buildcheck.done
