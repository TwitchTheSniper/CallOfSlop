<#
.SYNOPSIS
  Builds a shareable copy of the game for testers: bin\package\CallOfSlop-<version>.zip plus update.json.

.DESCRIPTION
  Self-contained (testers don't need .NET installed), Windows x64, and with NO personal files: it is published into a
  fresh folder, so settings.txt, progress.txt, loadout*.txt, workbench.txt, audio mixes, save backups, portable.txt,
  logs and smoke screenshots never get in (and any that somehow do are deleted before zipping). Players' saves live in
  %APPDATA%\CallOfSlop anyway (SavePaths.cs).

  The exe is stamped with the version (-p:InformationalVersion, shown in the main menu corner and used by the in-game
  updater, see Source\App\BuildInfo.cs). update.json is the manifest the in-game updater reads from a GitHub release:
  version, build number, git sha, protocol version, the zip's release URL, size and SHA-256, notes, minimum updater.

  Used by tools\Package.bat (local tester zips) and by .github\workflows\release.yml (GitHub releases).
  Works in Windows PowerShell 5.1 and PowerShell 7.

.PARAMETER Version
  0.YYYY.MDD.N (year, month*100+day without a leading zero, build number). Default: today with build 0, which any
  release of the same day counts as newer.
.PARAMETER Step
  all (default) | publish (build the folder only, e.g. to sign the exe) | zip (zip an already published folder).
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\package.ps1
  pwsh tools/package.ps1 -Version 0.2026.1022.17 -Notes "New map" -Tag v0.2026.1022.17
#>
param(
    [string]$Version = "",
    [string]$Sha = "",
    [string]$Tag = "",
    [string]$Notes = "",
    [string]$OutDir = "bin/package",
    [ValidateSet("all", "publish", "zip")][string]$Step = "all",
    [string]$Runtime = "win-x64",
    [int]$MinUpdater = 1
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot
New-Item -ItemType Directory -Force -Path "bin" | Out-Null
$doneFile = Join-Path "bin" "package.done"
Remove-Item -Force -ErrorAction SilentlyContinue $doneFile

function Fail([string]$why) {
    Write-Host "package failed: $why"
    Set-Content -Path $doneFile -Value "package-failed"
    exit 1
}

if ($Version -eq "") {
    $now = Get-Date
    $Version = "0.{0}.{1}.0" -f $now.Year, ($now.Month * 100 + $now.Day)
}
# Canonical form only (e.g. 105, not 0105): the game compares and displays System.Version.
$parsed = $null
if (-not [System.Version]::TryParse($Version, [ref]$parsed) -or $parsed.ToString() -ne $Version -or $parsed.Major -ne 0 -or $parsed.Minor -lt 2020 -or $parsed.Build -lt 101 -or $parsed.Build -gt 1231 -or $parsed.Revision -lt 0) {
    Fail "version '$Version' isn't 0.YYYY.MDD.N (e.g. 0.2026.1022.5; month*100+day without a leading zero)"
}
if ($Sha -eq "") {
    try { $Sha = (git rev-parse HEAD) } catch { $Sha = "" }
    if (-not $Sha -or $LASTEXITCODE -ne 0) { $Sha = "" }
}
$Sha = "$Sha".Trim().ToLowerInvariant()
if ($Tag -eq "") { $Tag = "v$Version" }
$Notes = "$Notes".Trim()

$OutDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutDir))
$folder = Join-Path $OutDir "CallOfSlop"
$zipName = "CallOfSlop-$Version.zip"
$zip = Join-Path $OutDir $zipName
$log = Join-Path $repoRoot "bin/package.log"

if ($Step -ne "zip") {
    if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    Write-Host "Publishing v$Version ($Sha) for $Runtime..."
    $publishArgs = @(
        "publish", "CallOfSlop.csproj", "-c", "Release", "-r", $Runtime, "--self-contained", "true",
        "-p:PublishSingleFile=false", "-p:DebugType=none",
        "-p:Version=$Version", "-p:InformationalVersion=$Version",
        "-p:IncludeSourceRevisionInInformationalVersion=true",
        "-o", $folder, "-nologo"
    )
    if ($Sha -ne "") { $publishArgs += "-p:SourceRevisionId=$Sha" }
    $ErrorActionPreference = "Continue" # dotnet may write warnings to stderr; its exit code decides
    & dotnet @publishArgs *> $log
    $publishExit = $LASTEXITCODE
    $ErrorActionPreference = "Stop"
    if ($publishExit -ne 0) {
        Get-Content $log -Tail 40 | Write-Host
        Fail "dotnet publish exited with $publishExit (see bin\package.log)"
    }

    # Belt and braces: no personal or debug files in the shared copy.
    $personal = @("settings.txt", "progress*.txt", "loadout*.txt", "workbench.txt", "audio_mix.json", "portable.txt", "update_check.txt",
                  "*.bak", "*.tmp", "*.corrupt", "*.log", "smoke_*.bmp", "selftest.log")
    foreach ($p in $personal) { Get-ChildItem -Path $folder -Filter $p -File -ErrorAction SilentlyContinue | Remove-Item -Force }
    if (Test-Path (Join-Path $folder "AudioMixes")) { Remove-Item -Recurse -Force (Join-Path $folder "AudioMixes") }
    Copy-Item -Force "tools/TESTERS.txt" (Join-Path $folder "READ ME FIRST.txt")
    Copy-Item -Force "tools/RunServerList.bat" (Join-Path $folder "RunServerList.bat") # a server list for friends (README, Playing with friends)
    Copy-Item -Force "tools/masters.txt" (Join-Path $folder "masters.txt") # server lists shipped with the zip (Settings.ShippedMasters)
    if (-not (Test-Path (Join-Path $folder "CallOfSlop.exe")) -and $Runtime -like "win-*") { Fail "CallOfSlop.exe wasn't published" }
}

if ($Step -eq "publish") {
    Set-Content -Path $doneFile -Value "package-published $folder"
    Write-Host "Published to $folder"
    exit 0
}

if (-not (Test-Path $folder)) { Fail "nothing published in $folder (run with -Step publish or all first)" }

# Zip with .NET (forward-slash entry names in both PowerShell versions; Compress-Archive in 5.1 writes backslashes).
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path $zip) { Remove-Item -Force $zip }
[System.IO.Compression.ZipFile]::CreateFromDirectory($folder, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true)

$size = (Get-Item $zip).Length
$sha256 = (Get-FileHash -Algorithm SHA256 -Path $zip).Hash.ToLowerInvariant()
$protocolLine = Select-String -Path "Source/Net/ServerBrowser.cs" -Pattern "ProtocolVersion = (\d+);" | Select-Object -First 1
if (-not $protocolLine) { Fail "couldn't read ServerInfo.ProtocolVersion from Source/Net/ServerBrowser.cs" }
$protocol = [int]$protocolLine.Matches[0].Groups[1].Value
if ($Notes -eq "") { $Notes = "Build $Version" + $(if ($Sha.Length -ge 7) { " ($($Sha.Substring(0, 7)))" } else { "" }) }

$manifest = [ordered]@{
    format     = 1
    version    = $Version
    build      = $parsed.Revision
    sha        = $Sha
    protocol   = $protocol
    url        = "https://github.com/TwitchTheSniper/CallOfSlop/releases/download/$Tag/$zipName"
    size       = $size
    sha256     = $sha256
    notes      = $Notes
    minUpdater = $MinUpdater
    published  = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
}
$json = $manifest | ConvertTo-Json -Depth 3
[System.IO.File]::WriteAllText((Join-Path $OutDir "update.json"), $json, (New-Object System.Text.UTF8Encoding $false))

Set-Content -Path $doneFile -Value "package-ok $zip"
Write-Host "Packaged $zip ($size bytes, sha256 $sha256, protocol $protocol)"
Write-Host "Manifest $(Join-Path $OutDir 'update.json')"
exit 0
