# A play-test tour of the Glacier story world: one window run that skips the opening, flies the camera to each spot,
# reads the frame rate (VSync and caps off) and takes a picture. Run from the repo root after a Release build; close
# the game first. Output: <Out>\tour_<spot>.png and <Out>\tour.txt (grep "[perf]" for the frame rates, "[pace]" for how
# long the induction and the first duty take at the least: GamePace.cs).
#   powershell -File tools\playtest\tour.ps1 [-Out shots] [-Calm] [-Frames 300] [-Size 1920x1080]
# Spots are "NAME = TARGET | DIST,UP,BEARING" (Program.WindowShotTour.cs: TARGET is a colony building's blueprint
# name, outpost N, geo LAT,LON, body N or X,Y,Z; the eye is DIST m out on the bearing and UP m above, looking at it).
param([string]$Out = "shots", [switch]$Calm, [int]$Frames = 300, [int]$Settle = 1500, [string]$Size = "1600x900", [string[]]$Only = @(), [switch]$Menus)

$spots = [ordered]@{
    "colony_overview"  = "Penal Colony | 75,40,135"
    "colony_eye"       = "Penal Colony | 30,1.7,60"
    "muster_board"     = "Colony Muster Square | 16,3,90"
    "watchtower"       = "Watchtower | 10,2,0"
    "gate_lamps"       = "Colony Gatehouse | 28,3,100"
    "boiler_smoke"     = "Colony Boiler House | 40,14,135"
    "records_canteen"  = "Ministry Records Office | 22,3,210"
    "barracks"         = "Colony Barracks | 18,3,280"
    "quarry_gangs"     = "body 47 | 45,15,0"
    "spine_ridge"      = "geo 81.5,285 | 500,60,0"
    "ice_cave"         = "geo 82.206,281.942 | 14,1,0"
    "teeth_crevasses"  = "geo 80,150 | 140,45,0"
    "rucks_drifts"     = "geo 79.5,210 | 150,25,0"
    "outpost1_checkpoint" = "outpost 1 | 22,5,0"
    "outpost2_sentry"  = "outpost 2 | 22,5,180"
    "outpost3_weewatch" = "outpost 3 | 22,5,0"
    "outpost4_relay"   = "outpost 4 | 22,5,0"
    "outpost5_cabinet" = "outpost 5 | 22,5,0"
    "spaceport"        = "Spaceport Terminal | 90,30,0"
    "spaceport_pad"    = "Spaceport Landing Pad | 25,4,0"
}

$Only = @($Only | ForEach-Object { $_ -split "," } | Where-Object { $_ })
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$bin = Join-Path $root "bin\Release\net10.0"
$Out = [IO.Path]::GetFullPath((Join-Path $root $Out))
New-Item -ItemType Directory -Force $Out | Out-Null
$do = "skip; wait 120; places; pace"
# -Menus: the windows a camera tour can't show (the Logbook's Technologies page, the Faction window's rank bars, the
# parts menu with its locked parts, the Work Board), after the induction is skipped. Pictures only: no frame rates.
if ($Menus) { $do = "skip induction; wait 300; open logbook Technologies; wait 60; shot menu_technologies; open factions; wait 60; shot menu_ranks; esc; wait 30; key SandboxInventory; wait 60; shot menu_parts; esc; wait 30; near Ministry Work Board 2; wait 60; use; wait 90; shot menu_workboard"; $spots = [ordered]@{} }
foreach ($k in $spots.Keys) { if ($Only.Count -gt 0 -and -not ($Only | Where-Object { $k -like "$_*" })) { continue }; $do += "; view $($spots[$k]); wait $Settle; perf $Frames $k; shot $k" }
$a = @("--window-shot", "`"$Out\tour.png`"", "--map", "Glacier", "--survival", "--uncapped", "--size", $Size, "--frames", "5", "--do", "`"$do`"")
if ($Calm) { $a += "--calm" }
$t = Get-Date
$p = Start-Process (Join-Path $bin "CallOfSlop.exe") -ArgumentList $a -WorkingDirectory $bin -RedirectStandardOutput "$Out\tour.txt" -Wait -PassThru
"exit $($p.ExitCode) after $([int]((Get-Date) - $t).TotalSeconds) s"
Get-Content "$Out\tour.txt" | Select-String "\[perf\]|\[pace\]|fail|unknown action" | Select-Object -Unique | ForEach-Object { $_.Line }
