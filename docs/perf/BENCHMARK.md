# The frame-rate benchmark

A fixed tour of the game's busiest places, measured on a real GPU, written as JSON so two runs can be compared. Use it
before and after any change to what is drawn or ticked every frame, on the user's Windows PC (the cloud box has a
software renderer: its numbers say nothing about a real frame).

```
CallOfSlop.exe --bench [SPOTS] [--out FILE.json] [--frames N] [--settle N] [--size WxH] [--shots] [--storm]
CallOfSlop.exe --bench-compare BASE.json NEW.json [--noise PCT]
```

- `--bench` with no file runs `tools/bench/glacier.json` (found by walking up from the exe): the Glacier story world in
  survival with the colony built, the opening skipped, no cold storm (`--storm` keeps it), VSync and every frame cap off,
  a 1600x900 window. For each spot it flies the camera there, waits `settle` frames (900) for streaming and the GPU to
  settle, then measures `frames` frames (900). About 65 s on the 4070 Ti. It opens a window on the user's screen and uses
  a throwaway copy of the saves.
- The result goes to `bench-results/<spots>-<date>.json` in the repo (ignored by git), or `--out`. It is written again
  after every spot, so a crash keeps the spots done. Each spot also prints a `[bench]` line to the log.
- `--bench-compare` prints each spot side by side: median and mean fps, the 99th percentile frame, GPU ms, CPU render and
  game ms, draws, and the five CPU areas or GPU passes that moved most. A spot is **WORSE** when its median fps fell past
  the noise (5%) or its 99th percentile rose past twice that. The exit code is the number of WORSE spots.

## Noise: run alone

Other game windows and builds on the same machine halve the numbers: two runs of the same build, one with two other
game windows open, measured 648 and 286 fps at the colony overview. Every spot records how many cores' worth of CPU other
`CallOfSlop`, `dotnet`, `csc` and `MSBuild` processes used while it was measured (`otherCores`, `others`). The log line
says `NOT ALONE` and `--bench-compare` lists those spots: run again when the machine is free. Even alone, compare medians,
not means or 1% lows (one hitch moves those), and run the base and the change back to back, from separate build folders.

## A change's cost, step by step

1. Build the base (main, or your branch before the change) into its own folder:
   `dotnet build -c Release --no-restore -o ..\bench-base`. Build your change into another.
2. Run `--bench --out base.json` from the first and `--bench --out new.json` from the second, one after the other, nothing
   else running.
3. `--bench-compare base.json new.json`. Report the WORSE lines and the areas that moved. A change that costs frames
   needs the user's OK (frame rate comes first).

## What a run holds

Per spot: frames, `fps` (from the mean), `medianFps`, `low1Fps` (the slowest 1% of frames as fps), `frameMs` (mean, p50,
p95, p99, max), `gpuMs` (every GPU pass added up, per frame) and `gpuGapMs` (GPU time between the timed passes),
`draws`, `triangles`, `instances`, `allocBytesPerFrame` (main thread) and `allocAllThreadsPerFrame`, `gc` (collections
of each generation while measuring), `ticks` (server ticks per frame, mean and worst tick ms: the 8 ms budget),
`world` (constructs, players, parts), `cpu` (every F9 area with its mean and max ms per frame and its depth in the
tree; a parent includes its children), `gpu` (each pass's mean ms) and `worstFrames` (the three slowest frames, each
with the deepest areas that took over 0.1 ms in it: what made the spike). The run's head records the build, the GPU and
GL version, the window and the video settings (`video`): compare runs with the same settings.

## The F9 areas this reads

The benchmark records the F9 profiler (Source/Perf, docs in `Perf.cs`), so anything timed there shows up here. Sandbox's
own rows: *Sandbox tick* (under Match and mode) with *Constructs* (*Links, power, logic*, *Construct physics*,
*Machines, production*), *Spawns, pilots, debris*, *Colony, duties* and *Story*; *Space, travel* and *Terrain
streaming* under the server ticks; *City* and *Constructs (draw)* under Render, with GPU passes of the same names. To
time something new, add a `PerfArea` (and its name and parent in `Perf.cs`) and wrap the code in
`using (Perf.Measure(PerfArea.X))`: closed, a scope is one bool test. A GPU pass is a `PerfGpuPass` in draw order and
`Perf.Pass(area, pass)`.

## Spots files

`tools/bench/*.json`: `map`, `survival`, `calm`, `start` (a story start), `size`, `settle`, `frames`, and `spots`, each a
`name` and a `view` in the window tool's tour form `TARGET | DIST,UP,BEARING` (Program.WindowShotTour.cs: a colony
building's blueprint name, `outpost N`, `geo LAT,LON`, `body N` or `X,Y,Z`; the eye DIST m out on a compass bearing
and UP m above, looking at it), with an optional `settle` and `frames` of its own. Keep names stable, because compare
matches spots by name; add a spot rather than move one. The play-test tour (`tools/playtest/tour.ps1`) uses the same
`perf` action, so its spots print `[bench]` lines too.

## First numbers (2026-11-13, 4070 Ti, 1600x900, calm; other threads' game windows may have been open)

The colony spots run at 650-800 fps, the open ground at about 1,200. The GPU is busy only 0.2-0.45 ms a frame: the frame
is CPU-bound, and most of it is the render pass's CPU side (0.6-1.1 ms), chiefly *Constructs (draw)* (0.3-0.47 ms) and
*Players* (0.25-0.31 ms for 6-8 people). Spikes seen: a 4.7 ms Buildings frame at the colony overview, a 2.1 ms HUD
windows frame at the muster square and a 1.8 ms Machines, production tick at the first outpost. The cold storm costs
10-30% at the colony.
