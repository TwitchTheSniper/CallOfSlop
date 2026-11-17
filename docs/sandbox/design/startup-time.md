# Start-up time: where it goes and how to cut it

Status: phases 0-3 being built (2026-11-17), measured on main d662cd0f77. Maps and their code belong to the maps developer
(CLAUDE.md "Who owns what"), who isn't working on these systems now: the user settled section 6 (below) and asked us to
run the map-side phases too. Phase 0 is on `claude/startup-phase0`, phases 1-3 on `claude/startup-phases123`
(notes: `startup-time-notes.md`).
No game code was changed for this study. The figures came from a throwaway trace in `Maps.LoadFolder` and
`Program.RunLoop` that was never committed.

## 1. How it was measured

- **Machine.** The 2-core cloud container (Release build, headless or under xvfb with Mesa). The Windows PC has 32
  threads. The parallel stages (map build, validation) will be several times faster there. The single-threaded stages
  (light bake of one map, blueprint check, part library) will be roughly 1.5-2x faster. Take the ratios below as the
  guide, not the absolute seconds.
- **Warm or cold.** "Warm" means the caches hit: `Cache/Maps` in the save folder and `Cache/blueprints-checked.txt` next
  to the DLL. "After a build" means the first run after `dotnet build`. Both cache keys hold the assembly's
  `ModuleVersionId` (`NavBake.BuildId`, `BlueprintCheckCache` stamp), so **every build makes both caches miss once**.
  This is the normal agent loop: build, run one section, change, build again.

## 2. Measurements

### 2.1 What a start does with maps

Every run that touches `Maps.All`, `Maps.Find` or `Maps.Packs` builds or reads **all 65 maps** before it goes on. The
first touch comes from different places:

- a self-test section: often `Settings.Load` (`map=` line -> `Maps.Find`);
- the game window: `RenderLobbyBackground` (`Maps.Packs`) in the first menu frame;
- tools: `--window-shot`, `--flight-check`, `--ride-check` and the benches.

`--check-construct-parts`, `--preview-shot`, `--gen-parts` and `--gen-vehicle` never touch maps.

Packs: Dev has 54 maps, Planetoids 7, Zombies 2, Sandbox 2. Sandbox play needs only Proving Ground, Construct Yard,
the planetoids (Glacier for Survival) and the Frostreach system.

### 2.2 Headless self-test, one section that needs no map (`--selftest-only "Keybinds"`)

The self-test builds every map live by design (`MapBake.Enabled = false` in `SelfTest.Run`).

| Stage | Warm | After a build |
| --- | --- | --- |
| Process start to first map touch | 0.3 s | 0.3 s |
| Discover files, inputs key | 0.02 s | 0.02 s |
| Shared libraries (props, decals, buildings, flags; `RunJobs` warm) | 0.7-0.8 s | 0.8-1.2 s |
| `ConstructParts.All` (2,448 part files, 16 MB) | 3.6-4.7 s | 3.3-4.2 s |
| `ConstructBuild.Blueprints` (377 blueprints, build check) | 0.5 s | **21-22 s** |
| Map build, 65 files on 2 workers (wall) | ~2.9 s | ~2.9 s |
| Validation incl. light bake (wall) | 4.6 s | 3.8-4.6 s |
| **Whole run** | **12.8 s** | **32-34 s** |

The construct rows are not map work. Proving Ground's `sandbox` section checks `rackParts` and `constructs` against
`ConstructParts.Find` and `ConstructBuild.FindBlueprint` while the map is read (`SandboxMap.Read`,
`Source/Maps/MapSandbox.cs`). That forces the whole part library and the blueprint check inside the map load. This is
why Proving Ground showed 28.7 s of "build" in the first trace.

Per-map build CPU (warm, summed over workers): Dev 4.3 s, Zombies 0.5 s, Planetoids 0.17 s, Sandbox 0.07 s. The
dearest maps are Building Yard 0.47 s, Ashgrave Cathedral 0.42 s, Finnish Buildings 0.42 s, Commercial Street 0.40 s,
Civic Buildings 0.31 s and Mesh Test 0.29 s. The five planetoids cost 8-40 ms each. Proving Ground costs 70 ms and
Construct Yard 3 ms.

Light bake: only two maps are lit. **Ashgrave Cathedral takes 3.7-4.5 s** and Light Test 0.9 s. Ashgrave is the
critical path of the whole validation stage: the other core is idle while it bakes, because `LightBaker.Bake` runs on
one thread inside that map's `Validate`. All other validation together is about 0.3 s.

### 2.3 Game window to main menu (`--no-splash`, xvfb)

| | Warm | After a build |
| --- | --- | --- |
| Settings read | 0.4 s | 0.4 s |
| Window and GL up (first frame, maps not loaded yet) | 1.6 s | 1.6 s |
| Maps in the first lobby frame | 1.7 s (65 cached, 1.0 s of it library warm) | 14.1 s (65 built: ~4 s parts, 0.5 s blueprints, ~5 s build, 4.5 s light bake) |
| **First menu frame with maps** | **3.6 s** | **16 s** (+21 s when the blueprint cache is cold too) |

The menu draws one map behind it (the picked map, or a tour of the pack). It needs one built map, not 65.

### 2.4 Hosting a Sandbox world (`--window-shot`, offline creative)

- Maps: 1.5-1.6 s warm. After a build, 15.7 s.
- Session start (first frame to first in-game frame): **7-8 s**. This is mostly construct-side work:
  - `ConstructParts.All`: ~4 s, if nothing loaded it earlier;
  - vehicle kit warm-up: 1.8 s (`[vehicles] warmed the kit`);
  - detail textures waited at the first scene shader: 0.9 s;
  - Glacier's caves: 0.2 s.
- Whole run: Proving Ground 13.7 s warm (24 s after a build), Glacier 15.6-16.5 s.

**Release builds:** `BlueprintCheckCache.Enabled` is false when `BuildInfo.IsRelease`. A player's game therefore runs
the full blueprint check (21 s on this box, one thread) the first time a session reads the blueprints, **on every
launch**. The baked maps (`Baked/Maps`) skip the sandbox-section check, so the cost moves to hosting. This comes from
reading the code. It should be confirmed with one timed release run on the PC.

### 2.5 Tool runs (warm)

| Tool | Wall | Maps |
| --- | --- | --- |
| `--check-construct-parts` | 14.0 s | none |
| `--preview-shot Buggy` | 4.9 s | none |
| `--flight-check FILE` | 5.6 s | 1.6 s (cached) |
| `--ride-check` | 8.5 s | 1.7 s (cached) |
| `--window-shot`, Proving Ground | 13.7 s (24 s after a build) | 1.6 s (15.7 s built) |

Window tools use the throwaway save copy (`CallOfSlop-smoke-saves`) and its own `Cache/Maps` (40 MB for 65 maps). Its
first run after any build rebuilds all 65 maps.

Nav graphs are not built at start-up. They are built (or read from `Cache`) when a map is hosted, and were not measured
here.

## 3. Options, ranked by seconds saved against risk

Savings are from the 2-core measurements above.

| # | Option | Saves | Risk | Owner |
| --- | --- | --- | --- | --- |
| A | Blueprint check: parallel over files, and a pass list shipped with the release | 21 s after each dev build -> ~11 s or less; up to 21 s per player launch | Low | Constructs (ours) |
| B | Move the sandbox section's part and blueprint name checks out of map reading | 4-5 s (warm) to 25 s (cold) from every run that builds Proving Ground but uses no construct | Low | `MapSandbox.cs` (maps dev to agree) |
| C | Lazy light bake on first read; light bake cells in parallel | ~4.5 s wall of each full map load today; Ashgrave's bake / cores when it is used | Low | Maps dev |
| D | Map index, each map built on first use | Self-test without maps: ~8-12 s. Menu: 1.7 s -> ~0.3 s warm, 14 s -> ~1-2 s after a build | Medium (199 files read `Maps.All` / `Find` / `Get`) | Maps dev |
| E | Archived packs indexed, never built until asked | Included in D. Alone: ~0.5 s build + 4.5 s light (Zombies) | Low-medium | Maps dev |
| F | Part library: parse the 2,448 files in parallel; start loading on a worker when the menu opens | 3.6-4.7 s -> ~1-2 s; hidden behind the menu when the user takes longer to click Host | Low | Constructs (ours) |
| G | Pre-load the picked map and the Sandbox maps on a worker after the menu shows | Hosting waits on nothing already done | Low once D is in | Maps dev |
| - | Parallel map building | Already done (`RunJobs`, up to 8 workers) | - | - |
| - | Cache built maps across runs | Already done (`MapBake`, `Cache/Maps`). It misses after every build (key has the module id) | - | - |
| x | Key the dev map cache by a hash of map code instead of the module id | Up to 14 s after a build | High: a stale map nobody notices. Rejected; D makes it small | - |
| x | Let the self-test read the map cache | ~0 in the agent loop (misses after each build); hides build errors | Rejected | - |

### A. Blueprint check (ours)

- `ConstructBuild.LoadBlueprints` builds each of 377 blueprints on a scratch `ConstructWorld`, one after another.
  - Build them with `Parallel.For` into per-file results, then take them in file order (the same pattern as
    `Maps.RunJobs`).
  - Check the statics first: `ConstructConduit.BeginLoad/EndLoad` and `Remember`, `BlueprintCheckCache.Add`, and the
    sub-blueprint flattening that reads `list`.
- The Release workflow already checks every blueprint. It could write the pass list with the release's stamp into
  `Baked/`, and `BlueprintCheckCache.Enabled` would accept that list in a release with no mods. A mod or a changed file
  still gets checked.

### B. Sandbox section checks (`Source/Maps/MapSandbox.cs`, `SandboxMap.Read`)

- Keep the names as read. Check them where the section is first used, at a Sandbox session start (`GameSandbox*`).
- Also check them in `--check-map` and the "Map contract" section, so a bad name still fails the same checks with the
  same message.
- A map then builds without the construct library.

### C. Lazy light bake (`Source/Maps/MapLights.cs`, `LightBaker.cs`, `MapBake*.cs`)

- `Map.LightBake` becomes a property that bakes on first read (thread-safe `Lazy`), from the map as validated.
- **Risk:** destruction edits geometry later. The first read must come before any destruction, or the bake must keep
  using the validated solids. Today `LightBaker.Bake` reads `Map.Solid` and the props, so take a snapshot at
  `Validate`, or force the bake in `World.SetMap` before the first tick.
- Readers to keep working:
  - `LightGrid` (render);
  - `Program.Lighting` (`--light-shots`);
  - the "Baked lighting", "Light flicker", "Light leaks", "Big maps" and "Map contract" sections;
  - `MapBake.Write`, which must force the bake so `--bake` and `Cache/Maps` still hold the grid (`FormatVersion` stays,
    because the file content is the same).
- Parallel cells: `LightBaker.Bake` runs `Cell(...)` per cell with a shared tracer. Give each worker its own `Tracer`,
  and write cells by index so the result is byte-identical. `LightBaker.HashOf` checks that.

### D and E. Map index, built on first use (`Source/Maps/Maps.cs`, `MapPack.cs`, `MapFile.cs`)

- **Index at start-up.** Walk the folder as `LoadFolder` does. For each file, read only the header: `name`, `order`,
  `description`, `maxPlayers`, `halfX/Z`, `test`, and whether it has `sandbox`, `planetoid` or zombies sections. The
  1.7 MB of map text takes well under 0.1 s.
  - Sort by order, then name, as today, so **ids stay list positions**.
  - A file whose build later fails keeps its slot as a stub. Ids are local only: the wire uses name and fingerprint.
  - Name clashes are found from headers, as now.
- **`Maps.Index`** (a new `MapEntry[]`) carries the header and `Map Map` (built or read from the bake on first access,
  under a lock). `MapPack` gets `Entries`.
- **`Maps.Find(name)` and `Maps.Get(id)`** return the built map for that one entry.
- **`Maps.All`** keeps its meaning: it builds every map, in parallel as now, on first touch. The 199 readers and the
  archived sections by name keep working unchanged. Only hot paths move to `Index`:
  - `Settings.Load` (name check);
  - `RenderLobbyBackground` and `MapPack.RandomPool`;
  - the map select screen and `MapThumbnails` (build a thumbnail's map when it is shown);
  - `MapVote`, the lobby and the server info.
- **E** falls out of D: no map is built until something asks for it, archived packs included. No pack list or flag is
  needed. Optionally `pack.json` gets `"archived": true`, which hides the pack from the menus' default lists.
- **`Maps.Reload`** rebuilds the index. It rebuilds only the entries that were built and whose text or inputs changed.
  `Generation` still increments.

## 4. What must keep working

- `--bake DIR` and the Release workflow: build every map and nav graph through `Maps.All` (unchanged), and write
  `Baked/Maps` with the light grids.
- CI full suite and shards. They touch `Maps.All`, so they pay what they pay today. A shard with no map section saves
  the whole load.
- `--selftest-only "Name"` for archived sections. They use `Maps.All` or `Find` and get their map built.
- Menus list every pack and map with name, size, players and description from the index. The background, thumbnails
  and the map vote build only what they show.
- Hosting any map: `Find` or `Get` builds it, and `World.SetMap` gets a full `Map`. Joining: the client builds the
  host's map by name on Welcome, then compares `ContentHash` as now.
- Map packs, pack-local props and decals, and mods' map folders: the same folder walk, with libraries warmed only when
  the first map of that pack builds.
- `Maps.Reload` ("Refresh maps"), `--check-map`, `--check-maps`, `--export-maps` (builds all), `--gen-map`.
- `MapBake` keys and files: unchanged, per map. A lazy map reads its own cache file.

## 5. Recommended plan

| Phase | Work | Owner | Self-test, one section (warm / after build) | Menu (warm / after build) | Sandbox host |
| --- | --- | --- | --- | --- | --- |
| Today | - | - | 12.8 s / 33 s | 3.6 s / 16 s | 7-8 s session start |
| 0 | A (parallel check, release pass list), F (parallel part library, worker pre-load at the menu) | Ours | ~10 s / ~20 s | unchanged | -3 to -4 s; release -21 s |
| 1 | B, C | Maps dev agrees, we can do it | ~3-4 s / ~4-5 s | ~3 s / ~7 s | - |
| 2 | D (+E) | Maps dev | ~0.5 s for a section with no map; Sandbox sections build 1-3 maps (≤0.1 s each) | ~2.2 s / ~2.5 s | maps ready at once |
| 3 | G, and move the remaining hot `Maps.All` readers to `Index` | Maps dev | - | - | hidden behind the menu |

Phase 1 is the best value for the least change: two small edits in the maps code take about 8-9 s off every headless run
on this box. Phase 2 is the structural fix. It also stops the dev map cache from mattering, because after a build only
the maps used are rebuilt.

## 6. Decided (user, 2026-11-17)

The maps developer isn't working on these systems now, so the user settled every point below on the doc's
recommendation:

1. The sandbox section's part and blueprint names are checked at session start, in `--check-map` and in the map
   contract check, not in `SandboxMap.Read` (B).
2. `Map.LightBake` becomes lazy (forced before the first tick and before a bake is written), byte-identical; the
   bake's cells run in parallel (C).
3. A header index with build on first use (D): `Maps.All` keeps building everything, `Find` and `Get` build one map,
   menus read `Maps.Index`.
4. Ids stay list positions sorted by header order and name; a file that fails to build keeps its slot.
5. `name` and `order` stay plain top-level fields of a map file, never set by an op or a generator at build time.
6. `pack.json` may carry `"archived": true`, which hides the pack from the menus' default lists.
7. One of our agents does phases 1-3.

The questions as first asked:


1. May the sandbox section's part and blueprint names be checked at session start and in the map contract check,
   instead of in `SandboxMap.Read` (B)?
2. May `Map.LightBake` become lazy (forced before the first tick and before a bake is written), with the bake's cells
   run in parallel, byte-identical (C)?
3. Is a header index with build on first use acceptable (D)? Specifically:
   - `Maps.All` keeps building everything;
   - `Find` and `Get` build one map;
   - menus read `Maps.Index`.
   This answers open question 2 in docs/perf/STREAMING.md.
4. Ids: keep positions sorted by header order and name, with a stub slot for a file that fails to build (today a broken
   file shifts later ids).
5. Which header fields may the index rely on? Must `name` and `order` stay top-level literals, never set by an op or a
   generator at build time?
6. Should archived packs get `"archived": true` in `pack.json`, to hide them from the default menu lists?
7. Who does phase 1: the maps developer, or one of our agents under their review?
