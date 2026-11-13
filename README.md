# Call of Slop

A multiplayer survival sandbox built directly on the [BRUTAL framework](https://github.com/ahwootech/BRUTAL): build
vehicles and bases from modular parts, gather resources, and fight, with every part damaged and destroyed on its own.
The prime focus is the **Sandbox** mode ([Sandbox and constructs](docs/MANUAL.md#sandbox-and-constructs-in-development)). The game
began as an arena shooter, and those modes (Free-for-all, Team Deathmatch and more, plus Zombies and Co-op) are still in it.

| BRUTAL library | Used for |
| --- | --- |
| `Ahwoo.BRUTAL.Glfw` | Window, OpenGL context, keyboard and mouse |
| `Ahwoo.BRUTAL.OpenGl` | Rendering (OpenGL 4.6 core, or 4.1 core where that is all there is; everything is a box) |
| `Ahwoo.BRUTAL.RakNet` | UDP networking (listen-server + clients) |
| `Ahwoo.BRUTAL.ImGui` + `.Backends` | Lobby, menus, loadout screen, HUD text, scoreboard |
| `Ahwoo.BRUTAL.OpenAl` | 3D positional weapon audio, streamed procedural music |

## Requirements

- .NET 10 SDK
- Windows x64 or Linux x64, a GPU with OpenGL 4.1 (4.6 is used where the driver has it), and speakers or headphones
- macOS: Apple Silicon (M1 or newer) with macOS 14 or newer. The Mac download is built and tested but **not shipped yet**; see [Mac (Apple Silicon)](docs/MANUAL.md#mac-apple-silicon-not-yet-shipped)
- Internet access the first time you build (NuGet restores the BRUTAL packages)

## Play

Double-click **`Start.bat`**: it builds the game if anything changed and opens the lobby.

The player manual is [docs/MANUAL.md](docs/MANUAL.md). Its [Sandbox and constructs](docs/MANUAL.md#sandbox-and-constructs-in-development) section is the game as it is now; most of the rest (matches, loadouts, perks, kill streaks, bots, Zombies, Co-op) describes the archived arena modes and is kept as history. It also covers updates and [where your saves live](docs/MANUAL.md#where-your-saves-live).

## Development and version control

The repository holds only the source: `Source/`, `Maps/`, `Weapons/`, `Attachments/`, `tools/`, the project file and the docs. Build output (`bin/`, `obj/`) is ignored. Your personal game files live in the save folder (see [Where your saves live](docs/MANUAL.md#where-your-saves-live)), not in the repo; if you run in portable mode from the project folder, `.gitignore` keeps them (and their `.bak` copies) out of commits.

- **Get started:** clone the repo, then run `Start.bat` (or `dotnet build -c Release`). NuGet fetches the BRUTAL packages on the first build.
- **Automatic checks:** with `.github/workflows/build.yml` in the repo, every push runs **Build and self-test** on GitHub Actions. Pushes to `main` build on both Linux and Windows; branch pushes build on Linux only (on Windows when the commit message has `[windows ci]`, both for a manual run). Each job runs the self-test and attaches the logs to the run. A red X on a commit means it didn't build or a check failed; open the run to see which. On branches, a `[tests: ...]` tag runs only some sections (see [Focused self-test runs](#focused-self-test-runs) below); `main` always runs the full suite.
- **Line endings:** `.gitattributes` keeps text files LF in the repo and batch files CRLF, so diffs stay clean whichever editor or OS touched a file.

## Testing without playing

The rule (CLAUDE.md, "Tests only when asked"): a change must build and be tried by hand in a throwaway run; the self-test runs only when asked, and the suite doesn't grow. The old, longer description of every section is in [docs/archive/README-ARENA.md](docs/archive/README-ARENA.md).

- **`tools\BuildCheck.bat`** builds the game (output in `bin\build.log`), runs the self-test and writes `bin\buildcheck.done` when finished. The window smoke test runs too only if a file called `tools\run_smoke.flag` exists.
- **`CallOfSlop.exe --selftest`** runs the suite headless (no window) and prints nothing: results go to `selftest.log` next to the exe (`bin\Release\net10.0\`), and the exit code is the failure count. Every section logs its time (`-- Name: 1.23 s`) and the log ends with a `SLOWEST` line (the ten slowest sections), which CI shows as a notice.
  - **Limits:** the full suite holds at most `SelfTest.MaxSections` sections, and a section may take at most `SelfTest.MaxSectionSeconds`; the run fails past either (`Source/App/SelfTest.cs`). Sections in `SelfTest.Archived` (the arena modes, Zombies, Co-op, the older map packs and their content) are skipped by the full suite, and the log says how many with an `ARCHIVED` line.
  - <a id="focused-self-test-runs"></a>**Focused runs:** `CallOfSlop.exe --selftest-only "Save folder, Mods"` runs only those sections, in suite order, archived ones included. Names are comma-separated and ignore case; a whole section name picks that section, otherwise the text picks every section whose name starts with it. A name that matches nothing fails the run and lists the valid names, so a typo never passes; `CallOfSlop.exe --selftest-list` prints them (to the terminal and `callofslop.log`). A filtered run says so in `selftest.log` (a `FILTERED run` line at the top) and in its `DONE` line. A few sections use what an earlier one left behind and pull it in by themselves.
  - **Shards:** `--selftest-shard K/N` runs one part of the full suite (the long sections are dealt out by cost, `SelfTest.ShardHeavy`); CI runs three parts side by side.
  - **On CI:** on a push to any branch except `main`, put `[tests: Section A, Section B]` in the head commit's message to run only those sections (a manual **Run workflow** takes the same list in its `tests` input; empty means everything). With no tag the full suite runs. `main` (pushes and manual runs) and the Release workflow always run the full suite and ignore the tag; a `main` run whose log says `FILTERED` fails, so a filtered run can never make `main` look green.
- **`CallOfSlop.exe --smoketest`** opens the real window and runs one short script (about 30 s): the BRUTAL splash, the lobby's menus, then a Free-for-all match on Proto Box, a Zombies start (Blackmoor Asylum), a Sandbox start on Construct Yard (with the texture paint, fuse and material tools) and one on the Planetoid. It logs `[smoke]` lines (session ok, fps, worst frame, GL errors) to `callofslop.log` and then quits. It works on a throwaway copy of your saves and never saves your settings or progress. It works without a GPU under `xvfb-run` with Mesa's software GL.
- **Window pictures for your own checking** (`Program.WindowShot.cs`): `--window-shot OUT.png|bmp [--map NAME] [--blueprint NAME] [--spawn-at X,Y,Z] [--at X,Y,Z] [--look-at X,Y,Z | --yaw D --pitch D] [--frames N] [--every N] [--size WxH] [--net host[:PORT]|join:IP[:PORT]] [--survival] [--calm] [--do "action; ..."]` starts an offline creative Sandbox world (default Proving Ground; `--survival` a survival one, so on Glacier the story's colony is built; `--calm` leaves a map's cold storm out so a site is seen whole, and `--at` flies the camera there too) in the real window on a throwaway copy of the saves, spawns the blueprint in front of the player through the parts menu's own call, flies the player (noclip) to the camera, runs the actions, waits `--frames` frames and saves the picture (`--every N`: a numbered series over them), prints the paths and quits (exit 1 with the reason when the window, the world or an action fails). Actions go through the real input: `aim PART[#n]`, `near PART[#n] [m]` (stand in front of its screen), `use` (F), `key ACTION` (any keybind action, e.g. `key Fire`), `touch LABEL[@PART#n]` (a screen's button, with Hands, through the crosshair; it moves the player to the screen), `from PART[#n] SIDE,UP,OUT` (the eye that far beside, above and in front of the part's screen, OUT negative for behind it, looking at it), `person NAME X,UP,Z` (the eye that far from a story person's body along the world's axes, looking at them), `wheel N`, `wait N`, `tool NAME`, `mains off|on`, `click X,Y` (the left button at a window pixel, for an ImGui panel), `drag X1,Y1 X2,Y2` (the button held between two pixels over twelve frames: a slider), `esc`, `ui` (what is open, who has the mouse), `bring PART` (host: the other players to that part), `disown` (host: the blueprint is nobody's, so a client may use it), `craft PART` (a Crafting Terminal's orders, tray and reserved parts; on the host a save and load of its body), `design [preset N|length N|width N|crown N|rake N|thick N|angle N|at N|radius N|nose N|mirror 0/1|profile H,CM|name TEXT|drag N|await|held|bring N|open|save|order|parts]` (the Panel Designer's panel: its key, preview hash, designs and held panels; sets a slider as a drag would; waits on the other window), `saveload` (host: the spawned body written as a world save writes it and read back, its parts' names before and after), `shot [NAME]`; and for checks, `panel PART[#n]` (prints its screens as this peer fills them, its store's line state and what is open), `feed PRESS[#n] BAY KEY` (host: an extruded piece into a press's bay by the bays' own rule, taken or refused with the reason), `saveworld NAME` and `loadworld NAME` (host: the world through the real save, copied out, and loaded in a second run; then each body's run-time parts by name), `kit PRESS[#n] N PART|MATERIAL` (host: a kit into a press's bay 2 by the hopper's rule), `poke BUTTON[#n]` (host: a button pressed as F presses it), `timed` (host: machines take their time and need power, as in survival), `stock PART[#n]` (what a store or rack holds by name as this peer has it, and the loose one-part bodies within 6 m), `fill PART[#n] N MATERIAL` (a bin, on the host; a material may be two words: `iron ore`), `deposit [TIER]` (the nearest ground a drill taps, for `--spawn-at`). For a play-test pass: `view TARGET | DIST,UP,BEARING` (the eye hovers DIST m out on a compass bearing and UP m above a colony building by blueprint name, `outpost N`, `geo LAT,LON`, `body N` or `X,Y,Z`, looking at it), `perf N [LABEL]` (frame times over N frames, with the lamps lit), `places` (where the colony's buildings and the outposts are) and `skip` (the survival opening), with `--uncapped` for VSync and frame caps off; `tools/playtest/tour.ps1 [-Calm] [-Only a,b]` runs twenty Glacier spots in one window (about a minute on the Windows PC). `--calm` leaves out the storm and with it the polar dusk, which real play always has. With `--net`, a host and a client in two windows over RakNet (add `--lag`, `--loss`, `--save-dir`) each print their own dumps (the joined window to `callofslop-join.log`, beside the host's `callofslop.log`). Under xvfb: `xvfb-run -a -s "-screen 0 1280x720x24"` with `MESA_GL_VERSION_OVERRIDE=4.6 MESA_GLSL_VERSION_OVERRIDE=460`. Example: `--window-shot /tmp/bill.png --blueprint "Production Showcase" --do "touch Bill @ Line Console#2; wait 20; near Line Console#2 1.0"`.
- **Data checks** load one kind of data file and report what is wrong in seconds, without a match: `--check-construct-parts`, `--check-part FILE`, `--check-parts [DIR]` and `--watch-parts` (hot reload while playing) for construct parts, `--check-vehicle FILE` for any blueprint, `--look-report` for blueprints' colours, `--check-mods [DIR]`, `--check-system FILE`, `--check-planetoid FILE`, `--check-story [DIR]`, `--check-weapon FILE` for a weapon or attachment file, and for map makers `--check-map FILE` / `--check-maps DIR` (a map file's `test` block: `Maps/README.md`, "Testing a map"). CLAUDE.md's command table lists them all with their options.
- **A headless second player:** `CallOfSlop.exe --net-client <ip> <port>` (or `--net-client <invite code>`), with `--lag=ms`, `--loss=%`, `--name NAME`, `--seconds S`, joins a host with no window and prints a `[netclient]` line whenever what it sees changes (connected, phase, map, mode, players, gun, and the reason if it loses the host). It reads `team N`, `gun ID`, `ready`, `state` and `quit` on its standard input.
- **Bad connections on one PC:** add `--lag=150` (extra milliseconds on everything that side sends, plus some jitter) and/or `--loss=5` (percent of packets dropped), e.g. `Start.bat --lag=150 --loss=3`. On clients, the window title shows ping, the current interpolation delay, and the size of the last prediction correction ("fix ... cm").
- **Benches and pictures** (only when a change needs them; see CLAUDE.md on pictures):
  - `--bench [SPOTS] [--out FILE.json]` and `--bench-compare BASE NEW`: the frame-rate benchmark on a real GPU, a tour of fixed spots with the F9 breakdown per spot as JSON, and two runs side by side (docs/perf/BENCHMARK.md).
  - `--bot-bench [50,100,192] [--seconds S] [--map NAME] [--net]` runs headless a Sandbox fight of spawned bots at each size and writes `botbench.log` (tick cost, the F9 profiler's areas, allocations, snapshot size; `--net` over the real transport). See docs/sandbox/constructs/network-and-scale.md.
  - `--gl-bench ["Map" ...] [--frames N] [--bench-shots] [--bench-bp "A,B" ...]` times the OpenGL 4.6 experiments with each on and off (docs/render/OPENGL46.md); `--gl41` forces the 4.1 path in any run, `--no-merge` draws constructs box by box (docs/render/MERGED-MESHES.md).
  - `--light-shots "Map Name" [--at x,y,z,yaw,pitch ...]` draws a map with baked lighting on and off and saves the pictures next to the log (`--light-debug` adds what the bake sees; docs/render/LIGHTING.md). `--ragdoll-shots` and `--perf-shot` are the other picture commands.

## How it works

**Netcode: prediction, interpolation and lag compensation**

Both sides run the same fixed **60 Hz** simulation (`World.Simulate`, `World.TickRate`), independent of frame rate. Rendering blends between ticks, so motion is smooth at any FPS. The server sends a snapshot every 2 ticks (30 Hz).

| Problem | What the game does |
| --- | --- |
| Your own movement lagging behind your keys | **Client-side prediction.** Your input is simulated on your PC immediately, every tick. |
| Server disagreeing with your prediction | **Reconciliation.** Each snapshot tells you the last input the server ran. Your client resets to the server's state, replays the inputs the server hasn't seen yet, and fades any leftover error out over about 100 ms instead of snapping. |
| Other players jittering or teleporting | **Entity interpolation.** Remote players are drawn a little in the past (about 70 to 300 ms, adapting to measured network jitter), smoothly between buffered snapshots. If packets drop, they're briefly extrapolated along their last velocity. |
| Having to lead targets by your ping | **Lag compensation.** The server keeps 2 s of position history per player and rewinds targets to the exact tick you were seeing when you fired (up to 400 ms back, `World.MaxRewindTicks`). If it looked like a hit, it is a hit. |
| Clients stuttering on the host's screen | **Server jitter buffer.** The server holds about 2 ticks of each client's input and consumes one per tick. If a packet is late, it assumes you're still holding the same keys, and the late packet is dropped when it arrives. |
| Packet loss | Every input packet repeats your last 4 commands, so a lost packet costs nothing. |
| Many players in one packet | **Compact snapshots.** Positions are sent to 1/256 m and angles to 0.1 mrad (24 bytes a player); your own state comes exact, so prediction never chases rounding. Slow-changing player state (scores, loadouts, perks) goes out only when it changes (repeated for half a second) plus a slow refresh. Players more than 40 m away, and the dead, update at 15 Hz instead of 30. A snapshot that still doesn't fit 1200 bytes is split into self-contained parts. |
| Standing on a moving platform | **Stamped mover clock.** A platform's pose is a pure function of the server tick. Each input you send carries the tick the server will run it at, and the server runs it at exactly that tick, so prediction, replays and the server all see the platform in the same place. |

Movement is server-authoritative, so clients can't teleport. Shots are resolved only on the server. Constructs (vehicles, bases, loose parts) have their own stream of bodies and poses (`ConstructNet`), drawn on the server's tick stamps: see docs/sandbox/constructs/network-and-scale.md. Any wire-format change bumps `ServerBrowser.ProtocolVersion` (CLAUDE.md, "Protocol").

**Where a shot is seen to start.** The hit ray starts at your eye, so the crosshair tells the truth (a built gun's barrels move their rays sideways by their offset). The tracer and the muzzle flash leave the muzzle of the barrel that fired: the end of that barrel, past any muzzle device (`Source/Weapons/GunMuzzles.cs`). Your own tracers start where your view model's muzzle is drawn on screen; another player's start at the gun as drawn in their hands. `Msg.Shot` carries each end's barrel for guns with several barrels, so every screen agrees. Code: `Source/Game/GameMuzzle.cs`. Hit markers appear instantly from your own prediction, and the server confirms them.

**Physics**

All collision of the world and the players goes through one module, `Source/Physics` (constructs add their own compound collision, `ConstructCollider`):
- `PhysicsWorld` is the current map's collision world (`World.Physics`): one BVH (`Bvh.cs`, surface-area-heuristic build) over every static collider (map boxes, prop collider boxes, oriented boxes, triangle meshes via `IMeshCollider`), plus the terrain heightfield and the movers posed at a tick. Broken props switch their boxes off.
- `IWorldQuery` (`World.Query`) is the one query interface: `Raycast`, `SphereCast`, `CapsuleCast`, `OverlapBox`, `OverlapCapsule`, `OverlapBody` (the classic player box), `PointInside`, all with a `QueryFilter` (layers, the mover tick, shapes to ignore).
- **Movement** goes through `World.Mover` (`IMover`). `ClassicMover` is the original movement; on terrain both movers follow `SlopeRules` (the footprint's slope, 45 degree limit, slower uphill, grip and terrain steps, capped steerable slides; see [Slopes](docs/MANUAL.md#slopes)). `SweptMover` is a capsule character controller: sweep and slide (it can't tunnel), step-up (0.55 m steps climb), ground snap, 42 degree slope limit on boxes and meshes, crouch and prone capsules, mantling, riding and being pushed by movers. It reads "up" and gravity from `PhysicsWorld.Gravity`, so gravity can change per zone (docs/space/GRAVITY.md). The host picks the movement with `physics=classic` (default) or `physics=swept` in `settings.txt`, and clients predict with the host's choice.
- `LegacyCollision` is the old collision code, kept as a reference.

**Arms, legs and the scene graph**

Players and guns are drawn from boxes, with no animation files:
- **Arms:** `Render/ArmAnchors.cs` knows where the hands go on each gun and picks the reload style; `Render/ViewModelAnimator.cs` moves the gun (hip and aim, sway, bob, sprint, equip, melee, reload, recoil) and works out where both hands are; `Render/Arms.cs` solves each arm with two-bone IK; `Render/ThirdPersonArms.cs` gives other players the same arms. `Render/HeadBob.cs` is the camera head bob.
- **Legs:** `Render/LegAnimator.cs` poses each player's legs every frame from the drawn velocity (walk and run blended by speed, crouch, prone, jump, landing, mantling), with foot IK on the ground for players within 40 m. Legs are presentation only: hit boxes, collision and movement don't use them. Set `CALLOFSLOP_FPLEGS=0` to hide your own legs in first person.
- **HUD text in the world:** `UI/WorldLabels.cs` (`Hud.Project`, `Hud.WorldLabel`) puts text at 3D positions; the name tags use it.
- **Scene graph:** things built from parts are held as a tree of parented nodes (`Source/Scene`, see [Source/Scene/README.md](Source/Scene/README.md)), each with a local transform, a cached world matrix, named sockets (`muzzle`, `grip`, `hand_l`, `seat_0`, ...) and a box or mesh to draw. glTF models load straight into node trees. `CALLOFSLOP_SCENEGRAPH=0` falls back to the old immediate drawing (`PlayerRender`, `MoverRender`).

Constructs are drawn by `Render/ConstructRender*.cs`, with merged meshes (docs/render/MERGED-MESHES.md) and shared materials (docs/render/MATERIALS.md).

**Project layout**

CLAUDE.md's "Layout" is the up-to-date map of `Source/` and the data folders, Sandbox first. In short: `Source/Constructs` is the building system (docs/sandbox/CONSTRUCTS.md is its index), `Source/Game` the server and client state (`GameSandbox*.cs` is the mode), `Source/App` the entry point, settings and self-tests, `Source/UI` the screens, `Source/Render` the drawing, `Source/Physics` the collision above, `Source/Net` the messages. The data the game loads at start-up sits in folders next to the exe: `Constructs/`, `Parts/`, `Maps/`, `Props/`, `Buildings/`, `Models/`, `Weapons/`, `Attachments/`, `Materials/`, `Systems/`, `Audio/`. Each has its own guide (a README.md in the folder or a file under `docs/`).

**Command-line options** for playing: `host [port]`, `join <ip> [port]`, `join CODE` or a `callofslop://join/CODE` link (join from an invite), `--lag=MS`, `--loss=PCT`, `--no-upnp`, `--save-dir <path>`, `--no-splash` (a dev build skips the BRUTAL logo, for test launches; window-tool runs skip it by themselves and a release always shows it), `--master [port]` (run a server list; `tools\RunServerList.bat`). The checks and tools are in "Testing without playing" above and CLAUDE.md's command table; map makers also have `--map-ops` (every op and its parameters) and `--export-maps DIR`, weapon makers `--export-weapons DIR`, part makers `--export-parts DIR [--resolved]`.

Player tuning values (speeds, step height, slope limit) live at the top of `World.cs`; construct parts' values live in their part files (`Constructs/Parts`).

## Licence note

The `Browser` folder holds Chromium Embedded Framework and CefSharp, both BSD licensed: `NOTICE.txt` lists them.

BRUTAL is free to use, but section 2 of the BRUTAL License asks shipped products to credit BRUTAL, show the BRUTAL logo before gameplay and ship `NOTICE.txt`.
This prototype prints the credit and ships `NOTICE.txt`; add the logo splash before you distribute it.

The recorded gunshots in `Audio/Samples/Guns` come from **The Free Firearm Sound Library** (CC0 1.0, no rights reserved; credit is not required but given with thanks): recorded by Ben Jaszczak, Brian Nelson, Kevin Heras and Matthew Nanney. See `NOTICE.txt` and `Audio/Samples/Guns/README.md`.
