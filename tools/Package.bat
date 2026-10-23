@echo off
rem Builds a shareable copy of the game for testers: bin\package\CallOfSlop-<version>.zip (+ update.json).
rem The work is done by tools\package.ps1 (also used by the Release workflow, .github\workflows\release.yml):
rem self-contained (testers don't need .NET installed), Windows x64, and with NO personal files (settings.txt,
rem progress.txt, loadout*.txt, workbench.txt, audio mixes, save backups, portable.txt, logs and smoke screenshots).
rem The exe is stamped with a version (0.YYYY.MDD.0 by default) shown in the main menu corner.
rem Arguments are passed on, e.g.  tools\Package.bat -Version 0.2026.1022.3 -Notes "Fixes"
rem Writes bin\package.done when finished ("package-ok <zip>" or "package-failed").
setlocal
cd /d "%~dp0.."
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0package.ps1" %*
exit /b %errorlevel%
