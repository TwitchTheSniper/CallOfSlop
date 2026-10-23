# CLAUDE.md: Call of Slop codebase map

Call of Slop is a C#/.NET 10 multiplayer arena shooter built directly on the BRUTAL framework (`Ahwoo.BRUTAL.*` NuGet packages: GLFW, OpenGL 4.1, RakNet, ImGui, OpenAL, glTF, meshoptimizer). Everything is drawn from boxes. It is a listen server with clients, running a fixed 60 Hz simulation (`World.TickRate`).

- **Repo:** TwitchTheSniper/CallOfSlop (public).
- **Player docs:** `README.md`, about 1,000 lines, is the player and maintainer manual. Read its "How it works" and "Testing without playing" sections before big changes.

## Build, run, test

| What | Command |
| --- | --- |
| Build | `dotnet build -c Release`, output in `bin/Release/net10.0/` |
| Build and play (Windows) | `Start.bat`: builds, then launches into the lobby |
| Build and self-test (Windows) | `tools\BuildCheck.bat`: writes `bin\buildcheck.done`; runs the smoke test only if `tools\run_smoke.flag` exists |
| Headless self-test (all) | `CallOfSlop.exe --selftest` (Linux: `dotnet bin/Release/net10.0/CallOfSlop.dll --selftest`). Writes `selftest.log`; the exit code is the failure count |
| Some sections only | `--selftest-only "Save folder, Frenzy"`: comma-separated, case-insensitive, prefix match. `--selftest-list` prints the names |
| Window smoke test | `--smoketest`: drives the real window, writes `smoke_*.bmp` and `[smoke]` log lines. Uses a throwaway save copy |
| Quick host or join | `CallOfSlop host [port]`, `CallOfSlop join <ip> [port]`, `--lag=ms --loss=%`, `--no-upnp`, `--save-dir <path>` |
| Modding checks | `--check-map FILE`, `--map-ops`, `--export-maps DIR`, `--check-weapon FILE`, `--check-part FILE`, `--check-parts [DIR]`, `--export-parts DIR [--resolved]`, `--watch-parts` (hot reload) |
| Package a tester zip | `tools\Package.bat` (calls `tools/package.ps1`): self-contained win-x64 zip, with no personal files |
| Verify an update package | `--verify-update <update.json> <zip>` |

**Local build in the agent container (Linux):** `. /home/claude/dotnet-env.sh` (adds the SDK to PATH and sets `XDG_RUNTIME_DIR`, which the game needs even headless), then `dotnet build -c Release --no-restore` (incremental: ~2 s) and `dotnet bin/Release/net10.0/CallOfSlop.dll --selftest-only "..."` (a couple of sections: ~1 s). The run prints nothing; read `bin/Release/net10.0/selftest.log`. If the SDK is missing, install it with the dot.net install script (`--channel 10.0 --install-dir /home/claude/.dotnet`) and restore once; `dot.net`, `builds.dotnet.microsoft.com` and `api.nuget.org` are allowed through the proxy.

**CI** (`.github/workflows/build.yml`):
- Every push builds and runs the self-test on Linux.
- Windows runs only when the commit message contains `[windows ci]`, or on a manual run.
- On non-main branches, `[tests: A, B]` in the head commit message runs only those sections. `main` always runs everything, and fails if the log says `FILTERED`.
- NuGet packages come from the Actions cache.

**Release** (`.github/workflows/release.yml`):
- Run it from Actions → Release → Run workflow (or push a `v*` tag).
- It runs on a Windows runner: full self-test, package, verify, then a GitHub Release with the zip and `update.json`.
- Version scheme: `0.YYYY.MDD.N` (`BuildInfo.cs`).
- The Azure signing step is present but off (`vars.SIGNING_ENABLED`).
- The release notes append `tools/MIGRATING.md`.

## Layout

```
Source/
  App/        Program.cs (entry, main loop, smoke test) + Program.*.cs partials per feature; Settings.cs, Keybinds.cs,
              SavePaths.cs (save folder + migration), BuildInfo.cs; SelfTest*.cs (~145 files, the headless test suite)
  Game/       Game.cs (server/client game state) + Game*.cs partials; World.cs (simulation, 60 Hz); Match.cs (phases);
              Bots*.cs / BotNav*.cs (nav graph, A*, movement, stuck watch); Modes/ (one module per game mode);
              Zombies*.cs / ZombieAI.cs (Zombies mode); Pve/ (Co-op mode, spawn director); Frenzy/ (kill-streak frenzy);
              Launchers/; Bots/{Doors,Squads,Vantage}; KillStreaks*, Perks*, Loadouts, Progression, Health, Throwables...
  Net/        Net.cs (messages), Snapshot.cs, ServerBrowser.cs (ProtocolVersion + server info), ListServer.cs (server
              lists), InviteCode.cs, Stun.cs, Upnp.cs
  Zombies/    weapon builder: PartDef/PartFile/PartLibrary (part files), WeaponBuild, BuildRules (what fits), BuildStats
              (stat pipeline), BuildModel (part boxes -> gun mesh), BenchModel (workbench state/filters), StandardBuilds,
              ActionMigration, ZombiesRules (Parts/Tables/ZombiesRules.json), RunTables, PushCart, MeleeStats
  Render/     Renderer.cs, BodyModel.cs, PlayerRender, ZombieAnim.cs, Ragdolls, Arms/IK, SightLens, Terrain/Foliage/Prop/
              Building/Decal renderers, Mesh/, Sky/, FrenzyFx/
  UI/         Ui.cs (ImGui menus) + Ui*.cs per screen (UiWorkbench, UiLoadout, UiAppearance, UiAudioMixer, UiZombies...)
  Audio/      Audio*.cs (OpenAL, buses, EFX), AudioMix* (F8 mixer), Music*/MusicRenderer* (procedural hardbass), Synth*
  Maps/ Physics/ Props/ Buildings/ Decals/ Scene/ Weapons/ Cosmetics/ Updates/
Maps/<Pack>/        map JSON files + pack.json (packs: Classic, Cluttered, CollisionPoint, Dev, HighGround, Space,
                    World [World Tour], Zombies). Maps/README.md documents the map ops.
Weapons/ Attachments/   one JSON per gun / attachment (Weapons/README.md)
Parts/<Category>/       weapon-builder parts (Action, Ammo, Barrel, Feed, Grip, Muzzle, Optic, Receiver, Splitter, Stock,
                        Upgrade, Utility, Multiplier, Hilt, Head, MeleeMod); Parts/Builds (64 standard builds);
                        Parts/Tables (ZombiesRules.json, DropTables.json); _Registry.json, _Bases, _Fragments, _Template
Props/ Buildings/ Models/ Decals/ Materials/ Cosmetics/ Audio/ Branding/   data, copied next to the exe
docs/zombies/       ZOMBIES-MODE.md, WEAPON-BUILDER.md (the design + decisions); docs/pve/DESIGN.md; docs/net/SERVER-DISCOVERY.md
tools/              package.ps1, Package.bat, BuildCheck.bat, TESTERS.txt (the zip's READ ME FIRST), MIGRATING.md,
                    parts/floaters.py (connectivity checker), zombies/make_blackmoor.py and other map/prop generators
```

## Conventions

- **Data first.** Maps, weapons, attachments, parts, props, buildings and decals are JSON files loaded at start-up. Prefer adding data over code. Bad files are refused with a reason.
- **Partial classes.** `Program` and `Game` are split into one partial file per feature (`Program.Zombies.cs`, `GameDestruction.cs`, ...). Follow that pattern.
- **Game modes.** A new mode is a module in `Source/Game/Modes/<Mode>/`, registered in `GameModeRegistry`. Add its id at the END of `enum GameMode`, and never renumber: ids are wire bytes. See `Source/Game/Modes/README.md`.
- **Protocol.**
  - Any wire-format change bumps `ServerBrowser.ProtocolVersion`, which is currently **45**. Add a comment line in the history above it.
  - New messages go at the end of `Msg`.
  - When two branches bump it, the merger renumbers.
- **Saves.**
  - Saves live in `%APPDATA%\CallOfSlop`, or next to the exe with `portable.txt`, or wherever `--save-dir` points.
  - Every save file is listed in `SavePaths.SaveFiles`, written atomically, and keeps a `.bak`.
  - A new save file must also be added to the `tools/package.ps1` personal-file list, `tools/MIGRATING.md` and `.gitignore`.
- **Performance is the top priority.**
  - No server tick may do more than 8 ms of CPU work (one-off JIT excluded).
  - Avoid per-frame and per-tick allocations; the self-tests measure allocations.
  - Music CPU must stay ≤1.5x the baseline.
- **Self-tests.**
  - Every feature adds a section in `SelfTest.cs` (`Section("Name", CheckX)`).
  - Use short, seeded, scripted scenarios. No long bot matches.
  - Remove temporary or diagnostic test code before finishing.
- **Text.** Player-visible text is ASCII. Docs and comments use British spelling, as the existing code does.
- **Commits.**
  - Commit as TwitchTheSniper <twitchthesniper@gmail.com>.
  - End each message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` and a `Claude-Session:` line.
  - Use `[skip ci]` only when the tree is identical to one that already passed.

## Rules the user set (keep them)

- **Be gentle to Ahwoo (the BRUTAL author).** Never contact Ahwoo's servers, sites or GitHub from scripts or tools. The NuGet API may be used by `dotnet restore` alone, once per machine to fill the package cache (user, 2026-10-23); after that build with `--no-restore`, and never poll, script or re-download the packages. Don't change the csproj `PackageReference` lines, because that changes the CI cache key.
- **No long bot matches or multi-minute simulations** without the user's OK. Don't run Insane vs Hard bot matches.
- **No DHT** for server listing. Use invite codes plus server lists, which a host starts automatically.
- **Frenzy buffs stay strong.**
- **Smoke tests never touch real saves.** Deletions are batched for the user to approve. Ask the user one question at a time.
- **Focused tests on branches** (the `[tests: ...]` tag). The full suite runs on main at merge.

## Gotchas

- **Seeded digests.**
  - `SelfTestModeModules.cs` holds the seeded match digests per mode. They change whenever nav, bot or gameplay timing changes, and are then re-recorded from the failure message.
  - `SelfTestPackAssets.cs` pins map and prop hashes, including some Windows-only pins. Editing a prop or map changes them.
- **Windows-only checks.** CPU-time tick checks only measure on Windows (`SelfTestTiming.Available`), so some failures only show up with `[windows ci]`. That includes GC pauses on big maps, stuck-spot counts and tick work.
- **Nav graph building must stay deterministic.** It builds from an as-built copy of the map. Destruction patches are reset when a map is restored. "Nav determinism" tests this, and "Nav edge walks" walks risky edges with the real mover.
- **Prop part rotation.** Prop parts rotate about their own centre (`"rotation": [x, y, z]` in degrees: x = pitch, y = yaw, z = roll; `PropDef.Rotation`). To tilt an assembly, compute each part's offset around the pivot yourself; see `Props/Countryside/TippedFarmCart.json`.
- **Weapon-builder parts.**
  - Parts must form one connected piece, which the "Model connectivity" section and `tools/parts/floaters.py` check.
  - Box tags: `"irons"` draws a box only with a receiver fitted and no optic; `"with:SOCKET"` draws a box only while that socket holds a part.
  - Each receiver has a required `action` socket. The fire type comes from the Action part, and the receiver sets the calibre.
- **The map list order matters to tests.** Many self-tests use the first map. New packs and dev maps get a high `order`.
- **`[tests: ...]` in a commit message is parsed literally**, so don't quote it in prose on a branch.
- **Known flaky checks** are listed in `HANDOFF.md`. Re-run once before chasing them.
