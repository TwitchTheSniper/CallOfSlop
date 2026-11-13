---
name: playtest-tour
description: Run the play-test tour of the Glacier story world on the Windows PC - one window run that flies to each spot, reads the frame rate and takes a picture. Use to see how the game looks and runs after a visual or world change, to compare frame rates before and after, or to get pictures for the look-critic agent.
---

# The play-test tour

`tools\playtest\tour.ps1` starts the real game once, skips the opening, flies the camera to twenty spots round the Glacier colony (the colony, the quarry, the ice, the five outposts, the spaceport), times `-Frames` frames at each with VSync and the caps off, and takes a picture. It also prints the tutorial's pacing estimate. About a minute a pass. It runs on a throwaway copy of the saves.

## Where to run it

On the Windows PC, on the real GPU: the frame rates mean something only there. In a cloud container the same script of actions runs under `xvfb-run` through `--window-shot` (the pictures are usable, the frame rates are llvmpipe's and mean nothing).

On the PC work in a worktree of your own, never by switching the main folder's branch (the guard refuses that):

```
git -C C:\path\to\CallOfSlop worktree add ..\CallOfSlop-NAME -b YOUR-BRANCH origin/main
cd ..\CallOfSlop-NAME
dotnet restore --source "$env:USERPROFILE\.nuget\packages"     # from the package cache alone, no network
dotnet build -c Release --no-restore
```

Close any running game first: a build fails on locked files, and a second window sharing the GPU spoils the numbers.

## Run it

```
powershell -File tools\playtest\tour.ps1 [-Out shots] [-Only colony,outpost] [-Calm] [-Frames 300] [-Size 1920x1080] [-Menus]
```

- Output: `<Out>\tour_<spot>.png` and `<Out>\tour.txt`. The script prints the `[perf]` line of each spot (mean, fps, median, 99th percentile, worst frame) and any `fail` or `unknown action` line.
- `-Only` takes prefixes of spot names (`-Only colony,gate`); `-Calm` leaves the cold storm out, so a site is seen whole. Run with the storm too before judging lamps and outlines: that is how a player meets them.
- `-Menus` takes pictures of the windows a camera tour can't show (the Logbook's Technologies page, the Faction window's ranks, the parts menu with its locked parts, the Work Board) with the induction skipped: pictures only, no frame rates and no pacing.
- A new spot is one line in the script's `$spots` table: `"name" = "TARGET | DIST,UP,BEARING"`. TARGET is a colony building's blueprint name, `outpost N`, `geo LAT,LON`, `body N` or `X,Y,Z`; the `places` action prints the names a world knows (`Source/App/Program.WindowShotTour.cs`).

## Read it

1. **Frame rate.** Compare each spot's median fps and 99th percentile with a pass on `origin/main` made the same day on the same PC, the same `-Size` and `-Frames`, with nothing else running. A spot that fell by more than about 5%, or whose 99th percentile rose by more than about 10%, is a finding: find what was added there (lamps, small detail parts and uncached boxes are the usual cost) before merging. The frame rate comes first in visual work.
2. **The log.** `fail`, `unknown action`, `[gl] error` and `REFUSED` lines in `tour.txt` and `callofslop.log` are findings whatever the pictures show.
3. **The pictures.** Open only the ones your change touches, at full size. For anything about looks, give the paths to the `look-critic` agent (`.claude/agents/look-critic.md`) with what each should show, rather than judging your own work. What to look for yourself: debug text on the HUD, things floating or sunk, people standing in walls, flat or grid-lined ground, bare ground round a building, a storm that hides everything.
4. **Pacing.** The tour prints `[pace]` lines: how long the induction and the first duty take at the least, step by step (reading, the way on foot, the bus, waits), and the distances between their places. After a change to the tutorial, a duty, the colony's layout or where the quarry stands, compare them with main's. For how long real players took, search `callofslop.log` from a play session for `[pace]`: a step several times over its floor is where people get lost or stuck (docs/sandbox/TUTORIAL.md "Pacing").
5. **The benchmark.** For a before-and-after of the frame rate use `--bench` and `--bench-compare BASE NEW` instead of step 1 (docs/perf/BENCHMARK.md): the same Glacier tour (`tools/bench/glacier.json`) with the F9 breakdown per spot as JSON, and the spots that got slower flagged WORSE. Build the base and the change in separate folders and run them back to back, alone on the PC. The tour stays the tool for the pictures and the pacing.

## Afterwards

- Findings go in your report as a short list: spot, what is wrong, how sure. Fix what your change caused; hand the rest to the coordinator.
- Pictures are for checking. Send one to the user only with a question that holds up the work (CLAUDE.md, "No review pictures unless asked"). Put pictures other threads need under the project's shared folder, not in the repo.
- Remove your worktree when the branch has merged: `git worktree remove ..\CallOfSlop-NAME`.
