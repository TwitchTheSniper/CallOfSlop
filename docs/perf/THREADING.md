# Threading: what runs where, what it costs, and a staged plan

Board item dev-perf-threading-mt01 (2026-10-30). An investigation only: no game code changed. Each stage in
section 6 becomes its own board item once the developer approves it.

**Short version.** The frame and the server tick run on the main thread; background work (loading, baking, music,
the crowd's per-frame passes) already runs on workers. Measured headless, the host's server tick is small next to
the 8 ms budget: 0.2-0.45 ms of CPU a tick for a 16-player bot match on a big map, about 0.9 ms with a 20,000 or
100,000 zombie Horde Stress, 0.1-0.6 ms for 8-20 Sandbox constructs. The UI is about 0.12 ms. On the user's PC the
Cluttered maps already run at about 1,700 fps. So moving the whole simulation to its own thread (the biggest and
riskiest change) has little to win in ordinary play. The real costs found are:

1. **The live nav graph build.** It runs in budgeted slices inside the server tick, about 2.5-3 ms a tick for the
   first 17-19 s of a match on a big map, whenever no baked or cached graph is used.
2. **One-off hitches.** The first tick builds the collision world (`World.Physics` is lazy). Render data is also
   built on the main thread at a map switch.
3. **Render CPU in heavy scenes**, which has not been measured on a real GPU yet.

The plan therefore starts with a measuring tool and cheap wins, and keeps the sim thread for last, behind numbers.

## 1. What runs on which thread today

### 1.1 The main thread (`Program.RunLoop`, `Source/App/Program.cs`)

One pass per frame, in this order (the F9 areas, `Source/Perf/Perf.cs`, are in brackets):

| Step | Area | Notes |
| --- | --- | --- |
| `Glfw.PollEvents`, Steam callbacks | Events | GLFW requires this on the thread that made the window (always on macOS) |
| Local input, or the lobby room | Input | `UpdateLocalPlayer` writes `Game.LookYaw/HeldButtons` |
| `Game.Update` | Game | Net receive (`Net.Poll` and every handler), the frame's weapon and effect state, then **0-8 fixed 60 Hz ticks** (`ServerTickStep` on a host, `ClientTickStep` on a client), then interpolation and render states (Visuals) |
| Voice, part watch, server lists, Steam lobby, music mood, frenzy | Audio | music *mixing* is on its own thread (1.2) |
| `Render(now)` | Render (+ 13 sub-areas) | every GL call: culling, instance buffer fills, uploads, draws; thumbnails pumped here |
| ImGui | Ui (+ 6 sub-areas) | new frame, HUD windows, menus, F9, ImGui's GL draw, gun thumbnails |
| `SwapBuffers`, frame limiter | Swap, Limiter | vsync or GPU wait; the Max FPS wait |

A host is one `Game` whose `Net.IsServer` is true. The host's own player is a server-side player, not a client: the
same `Players` dictionary is ticked by `ServerTickStep` and read by `UpdateRenderStates` and the renderers, with no
copy in between. A client's `Game` predicts its own player and interpolates the others from snapshots.

The server tick (`ServerTickStep`) holds, in order: match phases and the mode (Modes, with Zombies, Horde and Co-op
under it), projectiles, props, mines, map logic and falling pieces (Destruction), people's moves (Players), bots,
their nav and the **live nav graph build slices** (Bots), constructs and their physics (in the Sandbox mode's tick),
then snapshots and the per-tick sends (NetSend).

### 1.2 Already off the main thread

| Work | Where | How |
| --- | --- | --- |
| Crowd per-frame passes: horizon, cells, flow, agents (`CrowdAgents`), particle instances (`CrowdInstances`) | `Source/Game/Horde/CrowdJobs.cs` | fixed pool of up to 8 stripes (ProcessorCount - 1 above 2 cores), stripe k always on worker k, no allocation, `CrowdJobs.Serial` for the determinism check |
| Music mixing and streaming | `Source/Audio/Music.cs` | own thread (`Music`, above normal priority) |
| Gun samples, frenzy gun sound, mixer renders | `Audio.cs`, `GunSamples.cs`, `FrenzyGunSound.cs`, `Audio.Mixer.cs` | `Parallel.For` at start-up or on change |
| Map loading and validation, map bakes | `Maps.cs` (`Parallel.ForEach`, at most 8), `MapBake.cs` | at launch and on bake |
| Nav graph bake/cache read and cache write | `BotNav.cs` (`Task.Run`) | the read, never the live build |
| Destruction graph | `MapDestruction.cs` | `Task.Run` at map start |
| Foliage cover, vantage points | `FoliageCover.cs`, `VantageMap.cs` | `Task.Run`; bots use them once ready |
| Decal bake and atlas | `DecalRenderer.cs` | `Task.Run`; the GL upload stays on the main thread |
| Light baker | `LightBaker.cs` | inside the (parallel) map validation |
| Server lists, the updater, update clean-up | `ListServer.cs` (own thread), `Updater.cs` | network and disk |
| Stuck-spot and nav cache writes | `StuckSpots.cs`, `BotNav.cs` | `Task.Run` fire-and-forget |

`[ThreadStatic]` is already used where workers share code with the main thread: `World._navBuildPhysics` (a nav slice
sees the map as built), `TerrainData` scratch, `TextCache` buffers, `Maps` per-map log writers.

### 1.3 Shared state (the audit's starting numbers)

Counted with a regex over `Source/` (static fields that are not `readonly`, `const` or expression-bodied), and
static `readonly` collections that serve as scratch:

| Folder | Mutable statics | Static collections | Folder | Mutable statics | Static collections |
| --- | --- | --- | --- | --- | --- |
| Game | 74 | 19 | Render | 248 | 57 |
| Constructs | 19 | 24 | UI | 27 | 9 |
| Physics | 15 | 5 | Zombies | 22 | 13 |
| Maps | 17 | 12 | Weapons | 25 | 14 |
| Net | 6 | 5 | Props | 17 | 10 |
| App (mostly self-tests) | 242 | 85 | Perf | 18 | 16 |

The ones that matter for the simulation: `World` (static: `Map`, the lazy `Physics`, `Mover`, `PhysicsMode`,
`GravityScale`), `ConstructPhysics` (11), `BotNav` (10) and `BotNavBake` (5), `BotGear` (9), `MaterialPenetration` and
`PenetrationTrace` (10), `Movers`, `Ragdoll`, `LegacyCollision`, `GravityFrames`. The sim also reads the wall clock
directly: there are 51 `Glfw.Time` reads under `Source/Game`. Several server paths call render or audio side
effects inline, for example `ImpactDecals.OnTracer` in the shot code and the `Audio` calls in the tick.

## 2. Measurements

### 2.1 How they were taken

- **Headless sim, per F9 area:** a scratch console probe, kept in the agent's scratch folder and not committed. It
  references the Release `CallOfSlop.dll` and runs the real `Game` exactly as the self-tests do. It turns on
  `Perf.SetOn(true)`, calls `Perf.EndFrame()` after each `Game.Update(now, 1/60)` and sums every area. A scene runs a
  10 s warm-up, then waits for the nav graph, then measures 10 s (600 ticks). Thread CPU time
  (`CLOCK_THREAD_CPUTIME_ID`) is taken around each update. The cloud box had a load average of about 16 on 2 cores
  while it ran, so the Perf areas (wall clock) were scaled to CPU by the update's CPU/wall ratio ("cpu~" below).
  Stage 0 turns this into a real command.
- **Self-test sections that print their costs**, run on the user's PC runner: Mass horde, Crowd agents, Constructs,
  Construct drivetrain, UI frame spikes, Matches. Note that **the PC runner is the desktop app's Linux VM (2 cores,
  3 GB), not Windows**.
- **Windowed:** `--perf-shot` under `xvfb-run` (Mesa llvmpipe) for the default bot match and for Horde Stress at
  25,000, plus `--crowd-bench 25000,100000` headless.
- **Windows:** the same sections on a manual CI run (GitHub's Windows runner; see 2.6). The developer's own PC (the
  numbers that matter) is the first job of stage 0.

### 2.2 Server tick, bot matches (cloud box, thread CPU)

| Scene | Tick mean | p50 | p99 | max | Biggest areas (cpu~) |
| --- | --- | --- | --- | --- | --- |
| Vuorikoski, 8 players (7 bots), offline host | 0.195 ms | 0.135 | 1.09 | 6.85 | Bots + nav 0.107 |
| Vuorikoski, 16 players (15 bots), offline host | 0.347 ms | 0.225 | 2.77 | 6.30 | Bots + nav 0.155, Props/logic/destruction 0.094 |
| Vuorikoski, 15 bots + 1 client over loopback RakNet, host | 0.443 ms | 0.349 | 2.46 | 5.71 | Bots + nav 0.262, Projectiles 0.053, Destruction 0.048, Net send 0.004 |
| the same, the client's update | 0.112 ms | | | | Client prediction ~0.08, Frame state ~0.03, Net receive ~0.005 |
| Vuorikoski, 8 players, **while the live nav graph builds** (first ~17 s) | 2.86 ms (wall, loaded box) | 3.18 | 9.87 | 19.7 | Bots + nav 2.68 (the build slices) |

The build's slices are capped at 3 ms a tick (`BotMove.BotNavBuildMs`), which is what the last row shows. HANDOFF.md
also notes that the as-built physics copy (`PhysicsWorld.AsBuilt`) costs 60-110 ms in one tick on Paris, London and
Vuorikoski, the tick after the map loads.

Short bot matches on every shipped map (the Matches section, PC runner VM, 8 players, 90 s each) had a worst tick
under 4 ms on almost every map. The exceptions were Gravel Pit (9.2 ms), Beachhead (7.6 ms) and the first map run
(Proto Box, 29.7 ms, which is JIT). Snapshot sending costs microseconds per client.

### 2.3 Zombies and the mass horde

| Scene | Server tick | Horde | Notes |
| --- | --- | --- | --- |
| Horde Stress 20,000, offline host, descending (cloud, thread CPU) | 0.855 ms mean, p99 1.24, max 1.54 | 0.55 | main thread only |
| Horde Stress 100,000, the same | 0.917 ms mean, p99 1.23, max 1.47 | 0.59 | mass tier is counts per cell, so it barely grows |
| Mass horde section, Blackmoor, 5,000 / 10,000 / 20,000 (PC VM, wall) | 0.29 / 0.41 / 0.75 ms mean | 0.29 / 0.26 / 0.51 | 8 flow fields, 0.9-1.35 ms each, spread over ticks |
| Crowd preparation for drawing, Horde Stress 25,000, host, one thread (PC VM) | 0.17 ms | | Crowd agents section |
| `--crowd-bench`, 25,000 staging / descending, host (cloud) | 0.31 / 0.65 ms main-thread CPU (0.85 / 1.60 wall) | | the rest is on the CrowdJobs workers |
| `--crowd-bench`, 100,000 staging / descending, host | 0.64 / 0.96 ms main CPU (2.47 / 2.32 wall) | | client staging 7.04 ms wall, worst 22 (2 busy cores) |

### 2.4 Sandbox constructs

| Scene | Cost |
| --- | --- |
| 20 constructs (5 awake, 2 joints moving, 18 wires), 15 contacts, 566 rays a tick (Constructs section) | 0.251 ms a tick (PC VM), 0.612 (cloud) |
| 8 vehicles (207 parts) driving (Construct drivetrain section) | 0.11 ms a tick, 0 bytes allocated |

### 2.5 The frame: render and UI

| Scene | Frame | Render (CPU) | Game | UI | Swap |
| --- | --- | --- | --- | --- | --- |
| Default bot match (Proto Box, 5 bots), xvfb llvmpipe | median 56 ms | 26-34 ms | 0.1-1.8 | 1.2-3.1 | 16-34 |
| Horde Stress 25,000, xvfb llvmpipe | median 108 ms | 74-84 ms | 2.0-2.2 | 0.8-1.3 | ~23 |
| HUD + F9 window, headless ImGui (UI frame spikes section, PC VM) | | | | median 0.123, p99 0.195 | |
| Scene graph, 10,000 nodes all moving (Scene section) | | 0.21 ms update + flatten | | | |
| User's PC, Cluttered maps (Maps/Cluttered/DESIGN.md) | ~1,700 fps (~0.6 ms) | | | | |

Under llvmpipe, "render CPU" is the software rasteriser being fed, so those figures say nothing about a real driver.
The same was found in docs/render/OPENGL46.md. What they do show is the split: on a software renderer the game is
2-3% of the frame and the UI 2-5%. On the user's PC an ordinary map's whole frame is under a millisecond.
**Main-thread milliseconds per render sub-area in a heavy scene on a real GPU are the missing number.** Stage 0
collects them.

### 2.6 Windows

A manual CI run of this branch (run 41157255815: Linux and Windows, sections Text, Matches, Mass horde, Crowd agents,
Constructs, Construct drivetrain, UI frame spikes) gave the Windows runner's figures in section 8. GitHub's Windows
runner is slow and shared, so treat its numbers as an upper bound.

**For the developer (5 minutes, the numbers the plan most needs).** Open F9 and press **Log** (it writes a text
snapshot of every area) in each of these four scenes:

1. A full bot match on Vuorikoski or Silo (as host, 16 players).
2. Horde Stress at 25,000 and at 100,000, from the Overlook while the horde descends.
3. Construct Yard with several driving constructs.
4. One of those scenes again as a client joined to a host.

The log lines are what stages 3, 5 and 6 are judged on.

## 3. Candidates for moving off the main thread

| Candidate | Cost now | Can it move? | Verdict |
| --- | --- | --- | --- |
| **Live nav graph build** (`BotNav.PumpGraph`, sliced in the tick) | 2.5-3 ms a tick for ~17-19 s on a big map without a bake or cache: dev builds, custom and edited maps, map reload, `--check-map`, generators | Yes. It already reads only the as-built physics world through a `[ThreadStatic]` and builds deterministically | **Stage 2: best value for effort** |
| First-tick collision world (`World.Physics`, lazy) and per-map render data (`PropBatches.Build`, `FoliageBatches.Build`, `BuildingRenderer.BuildStatic`, terrain arrays) | 100-1,000 ms hitch at a map start or switch (docs/perf/STREAMING.md) | Yes. They are pure functions with no GL; the decal renderer already does this | **Stage 4** (STREAMING stage 4) |
| Render prep: culling and instance fills per renderer (props, foliage, buildings and box cells, constructs, decals, bodies) | Unknown on a real GPU; on the CPU the props and foliage are O(chunks), 0.03-0.4 ms; the crowd is already parallel | Yes for the CPU part; GL calls stay on the GL thread | **Stage 3, after stage 0's numbers** |
| Horde tick (`Zombies` > Horde) | 0.5-0.6 ms at 20,000-100,000 | Partly: cell, flow and separation passes are data-parallel; the individuals (300) touch shared state | Stage 5a, only if Windows shows > 1 ms |
| Construct physics | 0.1-0.6 ms for 8-20 constructs | Per island (a construct or a touching group), solved apart and applied in id order | Stage 5b, when Sandbox scenes grow |
| Bots: perception rays, think | 0.1-0.26 ms for 15 bots | Ray casts are reads of `PhysicsWorld`; think writes shared state, uses `Game.NextRng` and nav scratch statics | Stage 5c; little to win at 16 players |
| Projectiles, hitscan, penetration | 0.01-0.05 ms | Could, but `PenetrationTrace` has statics | Not worth it |
| Snapshots (NetSend) | microseconds per client | Per-client encode is independent | Not worth it below about 16 clients |
| ImGui | 0.12 ms median | No: ImGui's context is single-threaded and its draw is GL | Stays |
| Voice, music mood, frenzy | small; mixing is already threaded | | Stays |
| **The whole host sim on its own thread** | the tick: 0.2-0.9 ms of CPU a tick in heavy scenes; its spikes (p99 1-2.8 ms, max 5-7 ms on the cloud box, up to 8 catch-up ticks in one frame after a hitch) land on frames | Yes, with a large audit (1.3, 4, 5) | **Stage 6, last, only if stage 0 shows the sim limiting real host frames** |

## 4. Constraints

### 4.1 Determinism (the self-tests and multiplayer rely on it)

- The seeded match digests per mode (`SelfTestModeModules.cs`), "Nav determinism" (the graph built whole, a step per
  slice, and in 50 ms slices must match) and the map bake and nav bake checks ("read back exactly as built") must not
  change because of threading. The rule is that **the same result comes out serial or parallel, on any core count**.
- **Split work into fixed-size chunks**, for example 64 items, never into "one stripe per core": the result must not
  depend on `ProcessorCount`. CrowdJobs splits into `Stripes` that depend on the core count. That is fine for drawing
  but not for the sim. Per-chunk outputs are merged in item order. Float reductions (sums, minima with ties) are
  never done across chunks in a varying order.
- Parallel passes are read-only on shared state and write only their own items' slots. Anything with side effects
  (damage, kills, sounds, decals, packets, RNG draws) is recorded per item and applied serially in id order after
  the pass. `Game.NextRng` and every shared `Random` stay serial.
- Every pool keeps a `Serial` switch (like `CrowdJobs.Serial`). The determinism sections run both and compare.

### 4.2 Budgets and the test machines

- **8 ms per tick.** Once part of a tick runs on workers, "CPU work" means the main thread's plus the workers' CPU for
  that tick. The Windows timing checks (`SelfTestTiming`, thread cycles) measure the calling thread only, so they
  must add the workers' cycles, or a regression would hide behind the pool.
- **No per-tick or per-frame allocations.** Use cached delegates (CrowdJobs' pattern), never `Task.Run`,
  `Parallel.For` or closures in a frame. Per-worker scratch is made once. The allocation sections count every thread
  ("Frame allocations" reads main and all threads).
- **2-core CI and slow Windows runners.** The pool is sized from `ProcessorCount` (on 2 cores: one worker plus the
  caller) and falls back to inline on 1 core. Wall-clock timing checks flake under load (CLAUDE.md), so new checks use
  thread CPU.
- **One pool.** The crowd workers, render prep and the sim must share one pool, or a 4-core PC gets 3 crowd workers
  plus 3 render workers plus the main thread, all fighting over the cores. Loading's `Parallel.For` uses the .NET
  thread pool and only runs outside play.
- **F9 visibility.** `Perf` scopes on other threads are ignored by design, so worker time is invisible today. The
  pool must report per-job worker milliseconds as counters, or the window will show the main thread getting faster
  while the machine does not.

### 4.3 Networking

- The `NetPeer` (RakNet) is polled inside `Game.Update` on the main thread, and packets are handled in arrival order
  between ticks. Whoever owns the tick owns the server's `NetPeer`.
- Stages 0-5 change no wire format (`ProtocolVersion` stays as is).
- For stage 6, a host joining its own sim needs a client `Game` and a transport. RakNet over 127.0.0.1 already works
  in-process (many self-tests run a host and a client in one process, one after the other, on one thread). An
  in-process loopback transport would avoid the socket and real-time dependency but is new code. Neither changes the
  protocol.
- Lag compensation, prediction and interpolation (`JitterBufferTarget` = 2 snapshots) would then apply to the host's
  own player too. That is a **feel change for hosts**, who today see no prediction or interpolation at all.

### 4.4 OpenGL

- **One GL context**, made by GLFW on the main thread. There are no shared contexts. GLFW's event polling and window
  calls must stay on that thread (strictly so on macOS, where 4.1 is the only path). So render stays where the window
  is. A "render thread" would mean moving the context and keeping events on the main thread, which is more risk for
  nothing the sim thread wouldn't give.
- Workers never call GL. On the 4.6 path the `StreamRing` (`Source/Render/StreamRing.cs`, a persistently mapped
  buffer with fences) is plain memory, so workers may write instance data straight into a segment the main thread
  has reserved for the frame. On the 4.1 path (macOS, `--gl41`, llvmpipe by default) workers fill CPU arrays and the
  main thread uploads them (`glBufferSubData`), as it does now. Both paths stay, as docs/render/OPENGL46.md requires.
- Texture uploads (decal atlas, thumbnails, the ImGui font atlas) stay on the main thread. KHR_debug callbacks arrive
  on the GL thread.

## 5. Risks

| Risk | Where | Mitigation |
| --- | --- | --- |
| Data races on statics (1.3) | stage 6 above all; stages 2 and 5 in the code they call | an audit list per stage; a debug-build thread-owner guard on `World` setters and the sim statics (throws when the wrong thread writes); `[ThreadStatic]` scratch |
| Digests change | stages 2 and 5 | fixed chunks, serial apply, Serial switch; the digest and determinism sections in each stage's tag |
| Hidden cost (worker time not seen) | all | pool counters in F9; timing checks add worker cycles |
| Oversubscription on 2-4 cores | stages 1, 3 and 5 | one shared pool sized from `ProcessorCount`; inline under 3 cores where measured slower |
| GC: workers allocating | all | per-worker scratch made once; the all-threads allocation counters |
| Host feel change | stage 6 | measure first; keep the current single-thread host as a setting until testers agree |
| Flaky timing checks | all | thread CPU, not wall; Windows CI for the tick checks (`[windows ci]`) |
| Frame pacing worse, not better | stages 3 and 6 | a sync point per frame (the main thread waits for the prep jobs before submitting); the tick hands over a finished state, never a half-written one |

## 6. The staged plan

Effort is in agent sessions (one agent, one sitting, about 2-4 hours). Each stage is its own branch and board item,
and must pass its sections plus the full suite on main.

### Stage 0: measure for real (Sonnet, 1 session)

- **What:** a `--perf-bench [scene,...]` command. It runs headless, short scripted scenes (the probe used here: a bot
  match on a big map, the bots plus a loopback client, Horde Stress at 25,000 and 100,000, Construct Yard with
  driving constructs). It logs every F9 area (sub-areas too) per tick, with thread CPU (Windows thread cycles, Linux
  `CLOCK_THREAD_CPUTIME_ID`), p50, p99 and the maximum. A `--window` flag runs the same scenes in the real window and
  adds the render and UI sub-areas and GPU passes. Also add pool and worker counters to F9 (CrowdJobs' worker
  milliseconds per frame).
- **Gain:** none by itself. It turns this study's estimates into Windows numbers and is the yardstick for every later
  stage.
- **Tests:** a "Perf bench" section runs a 1-second version (areas nest, 0 allocations per tick, scenes start). The
  developer runs the full bench once on their PC.

### Stage 1: one shared job pool (Sonnet, 1 session)

- **What:** grow `CrowdJobs` into `Jobs`: one pool for the crowd, render prep and sim passes. Keep the striped API
  for drawing (stripe count may follow the cores) and add a chunked `For(count, chunk, body)` whose split never
  depends on the core count (for the sim). Keep `Serial`, inline below 2 cores, worker CPU counters and exception
  forwarding. Move the crowd onto it, with no behaviour change.
- **Gain:** none directly. It prevents oversubscription and gives later stages a safe primitive.
- **Tests:** a "Job pool" section (serial = parallel on a seeded workload at 1, 2 and 8 workers, 0 bytes per run, an
  exception reaches the caller, nested runs refused); Crowd agents, Mass horde, Frame allocations.

### Stage 2: the live nav graph build on a worker (Opus for the audit, 1-2 sessions)

- **What:** `PumpGraph` starts the build on a worker against the as-built `PhysicsWorld` (it already reads that one
  through `World.UseNavBuildPhysics`, a `[ThreadStatic]`) and takes the finished graph at a tick boundary. Bots steer
  straight meanwhile, as they do during a sliced build now. Destruction patches follow if they build sliced too. The
  audit covers what the link walks touch: `World.Mover`, `GravityFrames`, `LegacyCollision`, `PenetrationTrace`, the
  `BotNav` statics and the terrain scratch.
- **Gain:** 2.5-3 ms off every tick for the first 17-19 s of a match on a big map wherever no bake or cache is used:
  dev builds, edited and custom maps, reloads, `--check-map`, the generators, the self-tests. The graph is also ready
  sooner (no 1 ms slice budget). Release builds with baked graphs gain nothing in steady play.
- **Tests:** Nav determinism (worker = whole = sliced), Nav edge walks, Nav bake, Bots, Mode modules (digests
  unchanged), Bot stuck watch; Windows CI for the tick timing.

### Stage 3: render prep on workers (Sonnet; Opus if the 4.6 ring is involved, 2-3 sessions; after stage 0)

- **What:** per renderer, culling and instance buffer fills split by chunk over the pool, written into per-chunk
  ranges and merged in a fixed order. This covers props, foliage, buildings and the map's box cells, constructs,
  decals and bodies (the crowd already does it). On 4.6 the workers write into the frame's `StreamRing` segment; on
  4.1 into CPU arrays the main thread uploads. All GL calls stay on the main thread. Order: the renderer stage 0
  shows as biggest first.
- **Gain:** estimated 0.2-1 ms a frame on heavy maps and Horde Stress on a 4+ core PC; near zero on ordinary maps (the
  whole frame is ~0.6 ms there). Confirm with stage 0 before starting.
- **Tests:** a digest of each renderer's instance buffers, serial = parallel (like "Box batches"); GL 4.6; Frame
  allocations (all threads); UI frame spikes; a `--smoketest` and `--gl-bench` run under xvfb on both GL paths.

### Stage 4: loading off the main thread (Sonnet, 1-2 sessions)

- **What:** docs/perf/STREAMING.md stage 4. `World.SetMap` starts the collision world (BVH) and the render data
  builds on workers. The first query waits for the BVH; the renderers draw nothing new until their data is ready,
  then upload (the decal renderer's pattern). Thumbnails wait their turn over a few frames.
- **Gain:** the 100-1,000 ms first-tick and map-switch hitches go, and so do the lobby tour hitches.
- **Tests:** Map bake, Big maps, Box batches, Frame allocations, Map thumbnails, Nav determinism (the as-built copy),
  a `--smoketest`.

### Stage 5: parallel passes inside the tick (Opus, 1-2 sessions each; each only if stage 0 shows the need)

- **5a, horde:** the crowd's cell, flow-field and separation passes on the pool in fixed chunks; the individuals stay
  serial. Gain: about half of the 0.5-0.6 ms Horde time on 4+ cores, more at Endless waves.
- **5b, construct physics per island:** find islands (constructs and touching groups) each tick, solve islands in
  parallel with their own scratch, and apply them in id order. Gain: scales with independent islands (a big Sandbox
  server); about 0 with one big construct.
- **5c, bots:** perception and sight rays for all bots in one parallel pass (reads only), then think serially in id
  order. Gain: small at 16 players; it matters only for bigger lobbies or Insane squads.
- **Tests:** per subsystem, its sections plus Mode modules (digests unchanged), a serial = parallel digest, Windows
  CI for the tick checks (with worker cycles added).

### Stage 6: the host's sim on its own thread (Opus, 6-10 sessions; last; only if stage 0 shows host frames limited by the sim)

- **What:** a host runs its server `Game` on a sim thread as a dedicated server would. The local player joins it as
  a client (`Game` with prediction) through a loopback transport, either RakNet on 127.0.0.1 or a new in-process
  queue. Before that it needs:
  - an audit of every static the server and the client both touch (1.3), with a rule written down in this file and
    enforced by a debug thread-owner guard: the sim thread owns `World` and the sim statics; the main thread owns
    GL, ImGui, audio and input;
  - the 51 `Glfw.Time` reads in `Source/Game` passed in as the tick's `now`;
  - the render and audio side effects called from server code turned into the client's own reactions to messages,
    as they already are for real clients.

  The alternative of one `Game` with a double-buffered render view is less code to run but touches every renderer
  and every entity type. The loopback design reuses the client path, which is already tested over the real network.
- **Gain:** render and sim stop sharing one core on a host. Tick spikes and catch-up ticks no longer stall frames.
  Measured here the sim is 0.2-0.9 ms of CPU a tick, so the average gain is at most about that per tick; the pacing
  gain is the real one.
- **Costs:** the host's feel changes (prediction and interpolation like any client). Memory roughly doubles for the
  per-player state. The total CPU goes up (the client's prediction about 0.1 ms, plus encode and decode).
- **Tests:** the full suite with the sim thread on and off; a "Sim thread" section (a host and its loopback client:
  same digests as a remote client, thread guard clean); the networking sections with separate peers (Lessons,
  2026-10-28); Windows CI.

### Recommended order

0, 1, 2 and 4 first: cheap, low risk and certain gains. Stage 3 follows once stage 0's Windows numbers name the
renderer worth it. Stage 5 comes per subsystem when a scene needs it. Stage 6 only if, after all that, a real host is
limited by its sim.

## 7. Open questions for the developer

1. Does a host's feel matter more than the frame gain? Stage 6 gives hosts client-style prediction and interpolation.
2. What are your PC's core count and the F9 Log snapshots of the four scenes (2.6)? Stage 3's and stage 6's go or
   no-go depend on them.
3. Should the sim keep the "8 ms per tick" rule as total CPU (main plus workers), or as main-thread CPU only? This
   study recommends total CPU.

## 8. Windows CI figures (GitHub runner, run 41157255815, main b4d914d1 code)

| Mass horde, Blackmoor | Server tick CPU mean | Worst (Windows thread cycles) | Crowd | Flow field step |
| --- | --- | --- | --- | --- |
| 5,000 | 0.66 ms | 3.39 ms | 0.61 ms | 2.49 ms (most in one tick 2.85) |
| 10,000 | 0.87 ms | 1.76 ms | 0.58 ms | 1.62 ms (0.50) |
| 20,000 | 1.30 ms | 2.28 ms | 0.96 ms | 0.82 ms (0.23) |

That is about 1.7-2.3x the PC VM's figures, as expected from a shared runner. The horde stays well inside 8 ms, so
stage 5a is not needed for the budget.

The Windows job failed one check, and the failure is itself a data point. In "Matches", Proto Box (the first map
run) reported "a server tick did 20.4 ms of work, JIT compiling left out (wall 57.6 ms, JIT 54.6 ms, 311
methods) ... t=5.03 s, Playing match 1, nav ready, last feed line: GO!". That is the first tick of live play doing
first-time work that the JIT filter does not fully remove. It is code on main, not this branch (docs only), and it
is not yet listed in HANDOFF.md. It is the kind of spike stage 6 would hide from the frame. Warming those paths up
at match start (or ReadyToRun, STREAMING.md 6.3) would remove it more cheaply.

The Linux job of the same run passed all seven sections.
