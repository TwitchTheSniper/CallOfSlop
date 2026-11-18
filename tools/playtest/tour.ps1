# A play-test tour of the Glacier story world: one window run that skips the opening, flies the camera to each spot,
# reads the frame rate (VSync and caps off) and takes a picture. Run from the repo root after a Release build; close
# the game first. Output: <Out>\tour_<spot>.png and <Out>\tour.txt (grep "[perf]" for the frame rates). The penal
# colony's, the outposts' and the spaceport's spots went with them (2026-11-17).
#   powershell -File tools\playtest\tour.ps1 [-Out shots] [-Calm] [-Frames 300] [-Size 1920x1080]
# Spots are "NAME = TARGET | DIST,UP,BEARING" (Program.WindowShotTour.cs: TARGET is geo LAT,LON,
# body N or X,Y,Z; the eye is DIST m out on the bearing and UP m above, looking at it).
param([string]$Out = "shots", [switch]$Calm, [int]$Frames = 300, [int]$Settle = 1500, [string]$Size = "1600x900", [string[]]$Only = @(), [switch]$Menus)

$spots = [ordered]@{
    "spine_ridge"      = "geo 81.5,285 | 500,60,0"
    "ice_cave"         = "geo 82.206,281.942 | 14,1,0"
    "teeth_crevasses"  = "geo 80,150 | 140,45,0"
    "rucks_drifts"     = "geo 79.5,210 | 150,25,0"
}

$Only = @($Only | ForEach-Object { $_ -split "," } | Where-Object { $_ })
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$bin = Join-Path $root "bin\Release\net10.0"
$Out = [IO.Path]::GetFullPath((Join-Path $root $Out))
New-Item -ItemType Directory -Force $Out | Out-Null
$do = "skip; wait 120; places"
# -Menus: the windows a camera tour can't show (the Logbook's Technologies page, the Faction window's rank bars, the
# parts menu with its locked parts). Pictures only: no frame rates.
if ($Menus) { $do = "skip; wait 300; open logbook Technologies; wait 60; shot menu_technologies; open factions; wait 60; shot menu_ranks; esc; wait 30; key SandboxInventory; wait 60; shot menu_parts; esc"; $spots = [ordered]@{} }
foreach ($k in $spots.Keys) { if ($Only.Count -gt 0 -and -not ($Only | Where-Object { $k -like "$_*" })) { continue }; $do += "; view $($spots[$k]); wait $Settle; perf $Frames $k; shot $k" }
$a = @("--window-shot", "`"$Out\tour.png`"", "--map", "Glacier", "--survival", "--uncapped", "--size", $Size, "--frames", "5", "--do", "`"$do`"")
if ($Calm) { $a += "--calm" }
$t = Get-Date
$p = Start-Process (Join-Path $bin "CallOfSlop.exe") -ArgumentList $a -WorkingDirectory $bin -RedirectStandardOutput "$Out\tour.txt" -Wait -PassThru
"exit $($p.ExitCode) after $([int]((Get-Date) - $t).TotalSeconds) s"
Get-Content "$Out\tour.txt" | Select-String "\[perf\]|fail|unknown action" | Select-Object -Unique | ForEach-Object { $_.Line }
