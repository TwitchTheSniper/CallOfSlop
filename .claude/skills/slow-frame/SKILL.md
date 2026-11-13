---
name: slow-frame
description: Find where a slow frame or a slow server tick goes in Call of Slop, and show that a fix worked. Use when the frame rate fell at a place or after a change, when a tick passes its 8 ms budget, when the game hitches or stutters, when a steady frame allocates, or before merging any change to what is drawn or ticked every frame.
---

# Finding where a slow frame or tick goes

Measure before guessing: the usual suspect is seldom the one. The limits are in `.claude/rules/performance.md` (no server tick over 8 ms of CPU, nothing allocated in a steady frame or tick, the frame rate first in visual work). The tools are in `docs/perf/BENCHMARK.md`; this is the order to use them in.

## Which machine

- **The Windows PC** (a real GPU) for anything about frames: `--bench`, the play-test tour, F9 in the game. Work in a worktree of your own, close every other game window and build first: two other game windows halved the numbers (648 against 286 fps at the same spot). A run that says `NOT ALONE` is run again.
- **The cloud box** for tick cost and allocations only: `--bot-bench` (headless: tick cost, the F9 areas, allocations, to `botbench.log`), `--outpost-check` (it prints the outposts' own cost), and the self-test sections that measure allocations, run by name. Its frame rates are a software renderer's and mean nothing; its wall-clock tick times are noisy, so compare runs made back to back.

## 1. Say what is slow, in a number

```
CallOfSlop.exe --bench --out base.json                 # the Glacier tour, about 65 s, a window on the screen
```

Per spot: median fps, the 99th percentile frame, GPU ms, CPU render and game ms, draws, allocations a frame, the worst tick against the 8 ms budget, and the three worst frames with the areas that made them. Read medians, not means or 1% lows (one hitch moves those). If the slow place has no spot, add one to `tools/bench/glacier.json` (a name and a view `TARGET | DIST,UP,BEARING`) rather than move one: compare matches spots by name.

Decide from that which kind it is: **GPU** (gpu ms high, a pass stands out), **CPU render** (draws, the `cr:` and `sb:` sections), **the tick** (worst tick ms), **a hitch** (worst frames far over the median) or **allocation** (bytes a frame above zero, `gc` counts).

## 2. Narrow it

| Kind | Tool | What it gives |
| --- | --- | --- |
| Where a frame goes | `--bench --trace-frame` | every marked section as ms a frame: `cr: ...` (the construct renderer: culling, body parts, merged chunks, parts drawn one by one), `sb: ...`, `people: ...`, and the tick's sections |
| A slow tick | `--bench --trace-ticks 2` | each tick of 2 ms or more with its six slowest sections (`[bench] SPOT slow tick N, 8.33 ms: ...`) |
| An area over time | F9 in the game (the full window, the corner overlay, off) | the same areas live, per frame |
| An allocation | the self-test's `AllocProfile` (`AllocProfile.Top`) in the section that measures it, by name | the bytes charged to each marked section |

Both traces slow the frame while they run: use them to find the place, never for the numbers you compare. When the slowest section is still too wide, add an `AllocProfile.Mark("...")` inside it (a bool test when off) and run again; time it down to the per-part call before deciding (on 2026-11-11 a collider rebuild was billed to "Player moves"). For something timed for good, add a `PerfArea` with its name and parent in `Perf.cs` and wrap the code in `using (Perf.Measure(PerfArea.X))`.

## 3. The usual causes here

- Per-tick or per-frame work that grows with a body's size: bound it per body whatever its size, and measure with the biggest blueprint lying on a slope.
- Lamps, small detail parts and boxes that miss the merged chunks, added to a place (the tour's `[perf]` line gives the bodies, the lamps lit and the free lights at each spot).
- A closure or a rare path inside a hot method: C# makes the closure when the local's scope opens, before the early return. Move it to a method of its own.
- A list fed by the network that grows after a hitch: start it at its full size.
- A check or a rebuild that loops over every part of a giant.
- An optimisation that skips idle bodies and never wakes them: the frame is fast and the state is wrong.

## 4. Show the fix

1. Build the base and the change into separate folders (`dotnet build -c Release --no-restore -o ..\bench-base`), and run `--bench --out` from each, back to back, alone on the PC.
2. `--bench-compare base.json new.json`: a spot is WORSE when its median fps fell past the noise (5%) or its 99th percentile rose past twice that; the exit code is the number of WORSE spots.
3. Report the spots that moved, in numbers, and what you could not measure. A change that costs frames needs the user's OK before it merges; a fix that wins at one spot and loses at another is reported as both.
4. If the cause was one seen before, add its line to `.claude/rules/performance.md`.

No new self-test section for it (CLAUDE.md, "Tests only when asked"); the benchmark is the check, and it stays out of CI.
