# World saves

A Sandbox world survives quitting and restarting: one host-side save per world. This is the first piece of the persistent
world. The code is in `Source/Worlds/` (the file, the snapshot, reading and writing), `Source/Game/GameWorldSave.cs` (the game's
side) and `Source/UI/UiWorlds.cs` (the menus).

## Using it

- **Lobby room, Sandbox chosen (host):** type a name and press **New world** (it takes the lobby's map and Creative or
  Survival), or **Continue** a saved world (each line shows name, map, mode, last played), or **Delete** one (asks first).
  Without a world the game plays as before and isn't saved; the lobby says so.
- **Single player:** the same panel on the Single player page, with Sandbox chosen.
- A world **locks its map and mode**; **Leave this world** in the lobby frees them. Clients see the world's name.
- **SAVE NOW** in the host's pause menu. **Autosave** (Off, 1, 5, 10, 15 or 30 minutes; default 5) is in the panel
  (`world_autosave` in settings.txt).
- It also saves on Back to lobby, on quitting (the host's window, Leave, Close server), on a mode or map change away from it.
  A new world saves as play starts, so the list has it. Nothing is written for a world nobody played.

## On disk

`Worlds/<name>/world.sav` under the save folder (so `--save-dir` and portable mode apply), `world.sav.bak` beside it.
`SavePaths.SaveFolders` lists `Worlds`, so a new build's migration copies it. A write goes to `.tmp`, is flushed, then
replaces the file, the previous good one becoming `.bak`: a crash mid-save leaves the last good save. A file that won't read
is copied aside as `.corrupt` and the backup is used. If neither reads, the world doesn't load and nothing is written over.

The file (`WorldFile.cs`): magic `COSWORLD`, format (int, now 1), the meta in the clear (name, map, mode, times, counts: the
lobby reads only this), raw and packed lengths, a hash, then the body packed with Brotli. A file with a higher format is
refused with a clear line in the list and is never written over.

The body is tagged sections with lengths, so a later format can add one and an older reader skips it. **Stable ids:** the
first section lists part names, setting keys and material names; everything else uses numbers into those lists. An extrusion's
name is its whole key (`Extrusion i-beam 25x25x400 metal`), never its run-time id. Sockets, ports and setting choices stay
indices into the part's file.

## What is saved

- **Constructs:** parts (type, position, turn, segment, health, broken, setting, state, signals, tag, rounds, reload, fire,
  store, spawner counts, paint, settings by key), socket links, joints and their angles, freeform attachments, routed links with
  node health, poses and speeds, fuel, charge, gear, hover hold, wreck, damaged, pinned, frozen and by whom, owner, what a cargo
  volume carries, wrecks and loose bodies. Couplings, wires between constructs, world links and hoses.
- **Stores:** bins, Storage Volumes, machines' inputs and mixes, Assembler, Recycler, drill and refining jobs in progress.
- **The world:** the ground's op log (replayed on load), felled trees, regrowth due times, part-cut trees, deposit amounts, the
  gather clock; the space clock, warp and the ships that are constructs.
- **People:** position, health, tool, part in hand, inventory, backpack, built weapons; what they built stays theirs.

Not saved, and why:

- Bots and spawned zombies (a spawner part starts again at none alive; its total stays). Bullets, grenades in flight.
- Who sits where: everyone stands up. Hoses in someone's hands go back on the reel. Physics gun holds let go.
- What follows from running: engine spin, spool, plant output, shift timers, contacts. They settle in the first seconds.
- Breakable props, building doors and windows, destructible chunks: they come back whole (their own per-match state, each
  with its own message). The weather (its simulation has no snapshot yet) and any day and night outside a space bubble.
- The look direction: it is restored on the server, but a player's own view may overwrite it.
- Bench stock counted on a player, short of a part.

## People

A person is known by a Steam id if they have one, and always by a saved key (`world_key` in settings.txt, random, made once,
sent at the end of Msg.Hello, protocol 128). Names don't identify anyone, so a name change keeps your things. A newcomer gets a
new record and starts as a new player does.

Someone who leaves a saved world keeps their record: nothing drops, and their constructs stay theirs. Player ids change from
session to session, so while they're away a construct's `Owner` (and `FrozenBy`) holds a byte from `WorldSession.OfflineBase`
(64) up, one a person; on return it turns into their new id. Past 190 people a record has no byte and what they own becomes
nobody's. A person who was alive comes back where they stood, unless something now blocks that spot, then at a spawn.

## How a save stays off the tick

A save is `Game.WorldCapture`: on a finished tick it copies what the worker needs (`WorldSnapshot`): every construct's
arrays are plain structs and go with `Array.Copy` into kept buffers, stores into kept copies, the ground log, the tree lists and
people's records. Then a task turns that into bytes, packs and writes it. A save asked for while one runs is queued. Nothing
runs on a tick without a save, and a save allocates only for what grew, the tree lists and the records.

Measured in a Release build on the 2-core container (other agents running), five 8,000-part hulls, 2,000 small bodies, 107 stores:

| | |
| --- | --- |
| Parts, bodies | 42,173, 2,025 |
| Tick copy | 1.1 to 1.9 ms usual, 3.9 to 8.5 ms now and then (first save with JIT: up to 12.6 ms) |
| Tick around a save | worst 3.2 ms in a 240-tick run |
| Worker: bytes, pack, write | 33 to 70 ms, 8 ms, 1 ms |
| File | 2,629 KB of bytes, 105 KB packed (grid-built hulls pack very well: a real build is bigger) |
| Load | read 4 ms, built 39 ms |
| Join on that world | 374 ms on loopback |

20,000 ground ops: 243 KB in the file, 73 ms to replay.

The cap on stores is `ConstructWorld.MaxStores` (1,024). At the 250,000-part safety cap the copy would be about 45 MB: expect
about 10 ms on the save's tick.

## Loading

`ResetSandbox` (every match start) doesn't build the map's set for a world with a save, and marks it not applied. On the first
tick after that outside the lobby (`Game.WorldTick`) the world is read and built: the map is restored by then, and everyone
reset. A new world builds the map's set as ever. So the map's own constructs never double up. Connected clients then get the
state as joiners do (`SendTerrainDigState`, `SendResourceState`, `SendSandboxState`); a joiner gets it the normal way.

A part this build no longer has is logged (`[worlds] ...: left out n x Name`) and its construct loads with a plain block in
its place, which then goes by `RemoveParts`, the remover's path: what it held on falls off as wrecks, the rest stands. A store
slot, an inventory count, a material likewise. A body that can't be read is skipped alone, with a line in the log. The host's
caps rise to fit what the world holds.

## Rules

- Tests and smoke runs never touch real saves: they use a temp save folder, the smoke sandbox doesn't copy `Worlds`, and no
  world is attached unless the host picks one.
- Other modes never run any of this: every hook checks `WorldSession` or runs under `ServerTickSandbox`.
- When a new per-part or per-construct state matters across a restart, add it to `WriteBody` / `ReadBody`
  (`WorldSaveBodies.cs`), and to `BodySnap` / `CaptureInto` if it isn't in `CPart`. Changing a section's layout bumps
  `WorldFile.Format` and keeps reading the old layout (no such branch exists yet: format 1 is the first).

## Choices made (the developer may change them)

- A Sandbox game without a world still plays, unsaved (tests and quick tries keep working); it could become a default world.
- One world a folder, a whole file each save, no history past the one `.bak`.
- New world takes the map and mode from the lobby's own pickers rather than a dialog of its own.
- A save holds a person's position even on a moving construct, as a world position.
- Who may take what: an away player's construct counts as someone else's, salvageable once damaged, as ever.
